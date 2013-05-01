using System;
using System.Collections.Generic;
using System.Linq;
using TeamCityBuildChanges.ExternalApi.TeamCity;
using TeamCityBuildChanges.IssueDetailResolvers;
using TeamCityBuildChanges.NuGetPackage;
using TeamCityBuildChanges.Output;

namespace TeamCityBuildChanges.Commands
{
    /// <summary>
    /// Calculates ChangeManifest objects based on TeamCity builds.
    /// </summary>
    public class AggregateBuildDeltaResolver
    {
        private readonly ITeamCityApi _api;
        private readonly IEnumerable<IExternalIssueResolver> _externalIssueResolvers;
        private readonly IPackageChangeComparator _packageChangeComparator;
        private readonly PackageBuildMappingCache _packageBuildMappingCache;

        /// <summary>
        /// Provides the ability to generate delta change manifests between arbitrary build versions.
        /// </summary>
        /// <param name="api">A TeamCityApi.</param>
        /// <param name="externalIssueResolvers">A list of IExternalIssueResolver objects.</param>
        /// <param name="packageChangeComparator">Provides package dependency comparison capability.</param>
        /// <param name="packageBuildMappingCache">Provides the ability to map from a Nuget package to the build that created the package.</param>
        public AggregateBuildDeltaResolver(ITeamCityApi api, IEnumerable<IExternalIssueResolver> externalIssueResolvers, IPackageChangeComparator packageChangeComparator, PackageBuildMappingCache packageBuildMappingCache)
        {
            _api = api;
            _externalIssueResolvers = externalIssueResolvers;
            _packageChangeComparator = packageChangeComparator;
            _packageBuildMappingCache = packageBuildMappingCache;
        }

        /// <summary>
        /// Creates a change manifest based on a build name and a project.
        /// </summary>
        /// <param name="projectName">The project name</param>
        /// <param name="buildName">The build type name to use.</param>
        /// <param name="referenceBuild">Any reference build that provides the actual build information.</param>
        /// <param name="from">The From build number</param>
        /// <param name="to">The To build number</param>
        /// <param name="useBuildSystemIssueResolution">Uses the issues resolved by the build system at time of build, rather than getting them directly from the version control system.</param>
        /// <param name="recurse">Recurses down through any detected package dependency changes.</param>
        /// <returns>The calculated ChangeManifest object.</returns>
        public ChangeManifest CreateChangeManifestFromBuildTypeName(string projectName, string buildName, string referenceBuild = null, string @from = null, string to = null, bool useBuildSystemIssueResolution = true, bool recurse = false)
        {
            return CreateChangeManifest(buildName, null, referenceBuild, from, to, projectName, useBuildSystemIssueResolution, recurse);
        }

        /// <summary>
        /// Creates a change manifest based on a build name and a project.
        /// </summary>
        /// <param name="buildType">The Build Type ID to work on.</param>
        /// <param name="referenceBuild">Any reference build that provides the actual build information.</param>
        /// <param name="from">The From build number</param>
        /// <param name="to">The To build number</param>
        /// <param name="useBuildSystemIssueResolution">Uses the issues resolved by the build system at time of build, rather than getting them directly from the version control system.</param>
        /// <param name="recurse">Recurses down through any detected package dependency changes.</param>
        /// <returns>The calculated ChangeManifest object.</returns>
        public ChangeManifest CreateChangeManifestFromBuildTypeId(string buildType, string referenceBuild = null, string from = null, string to = null, bool useBuildSystemIssueResolution = true, bool recurse = false)
        {
            return CreateChangeManifest(null, buildType, referenceBuild, from, to, null, useBuildSystemIssueResolution, recurse);
        }

        private ChangeManifest CreateChangeManifest(string buildName, string buildType, string referenceBuild = null, string from = null, string to = null, string projectName = null, bool useBuildSystemIssueResolution = true, bool recurse = false)
        {
            var changeManifest = new ChangeManifest();
            if (recurse && _packageBuildMappingCache == null)
            {
                changeManifest.GenerationLog.Add(new LogEntry(DateTime.Now,Status.Warning,"Recurse option provided with no PackageBuildMappingCache, we will not be honoring the Recurse option."));
                changeManifest.GenerationStatus = Status.Warning;
            }

            buildType = buildType ?? ResolveBuildTypeId(projectName, buildName);

            if (String.IsNullOrEmpty(from))
            {
                changeManifest.GenerationLog.Add(new LogEntry(DateTime.Now, Status.Warning, "Resolving FROM version based on the provided BuildType (FROM was not provided)."));
                from = ResolveFromVersion(buildType);
            }

            if (String.IsNullOrEmpty(to))
            {
                changeManifest.GenerationLog.Add(new LogEntry(DateTime.Now, Status.Warning, "Resolving TO version based on the provided BuildType (TO was not provided)."));
                to = ResolveToVersion(buildType);
            }

            var buildWithCommitData = referenceBuild ?? buildType;
            var buildTypeDetails = _api.GetBuildTypeDetailsById(buildType);
            var referenceBuildTypeDetails = !String.IsNullOrEmpty(referenceBuild) ? _api.GetBuildTypeDetailsById(referenceBuild) : null;

            if (!String.IsNullOrEmpty(from) && !String.IsNullOrEmpty(to) && !String.IsNullOrEmpty(buildWithCommitData))
            {
                changeManifest.GenerationLog.Add(new LogEntry(DateTime.Now, Status.Ok, "Getting builds based on BuildType"));
                var builds = _api.GetBuildsByBuildType(buildWithCommitData);
                if (builds != null)
                {
                    var buildList = builds as List<Build> ?? builds.ToList();
                    changeManifest.GenerationLog.Add(new LogEntry(DateTime.Now,Status.Ok, string.Format("Got {0} builds for BuildType {1}.",buildList.Count(), buildType)));
                    var changeDetails =_api.GetChangeDetailsByBuildTypeAndBuildNumber(buildWithCommitData, @from, to, buildList).ToList();
                    var issueDetailResolver = new IssueDetailResolver(_externalIssueResolvers);

                    //Rather than use TeamCity to resolve the issue to commit details (via TeamCity plugins) use the issue resolvers directly...
                    var issues = useBuildSystemIssueResolution
                                     ? _api.GetIssuesByBuildTypeAndBuildRange(buildWithCommitData, @from, to, buildList).ToList()
                                     : issueDetailResolver.GetAssociatedIssues(changeDetails).ToList();

                    changeManifest.GenerationLog.Add(new LogEntry(DateTime.Now,Status.Ok, string.Format("Got {0} issues for BuildType {1}.", issues.Count(),buildType)));

                    changeManifest.GenerationLog.Add(new LogEntry(DateTime.Now, Status.Ok, "Checking package dependencies."));
                    var buildFrom = buildList.FirstOrDefault(b => b.Number == @from);
                    var buildTo = buildList.FirstOrDefault(b => b.Number == to);
                    var initialPackages = new List<TeamCityApi.PackageDetails>();
                    var finalPackages = new List<TeamCityApi.PackageDetails>();
                    if (buildFrom != null)
                        initialPackages = _api.GetNuGetDependenciesByBuildTypeAndBuildId(buildType,buildFrom.Id).ToList();
                    if (buildTo != null)
                        finalPackages = _api.GetNuGetDependenciesByBuildTypeAndBuildId(buildType, buildTo.Id).ToList();

                    var packageChanges = _packageChangeComparator.GetPackageChanges(initialPackages, finalPackages).ToList();

                    var issueDetails = issueDetailResolver.GetExternalIssueDetails(issues);

                    changeManifest.NuGetPackageChanges = packageChanges;
                    changeManifest.ChangeDetails.AddRange(changeDetails);
                    changeManifest.IssueDetails.AddRange(issueDetails);
                    changeManifest.Generated = DateTime.Now;
                    changeManifest.FromVersion = @from;
                    changeManifest.ToVersion = to;
                    changeManifest.BuildConfiguration = buildTypeDetails;
                    changeManifest.ReferenceBuildConfiguration = referenceBuildTypeDetails ?? new BuildTypeDetails();
                }
                else
                {
                    changeManifest.GenerationLog.Add(new LogEntry(DateTime.Now, Status.Warning, string.Format("No builds returned for BuildType {0}.", buildType)));
                }
            }
            //Now we need to see if we need to recurse, and whether we have been given a cache file....
            if (changeManifest.NuGetPackageChanges.Any() && recurse && _packageBuildMappingCache != null)
            {
                //We can have multiple packages from the same build, they will all tick together...we only want to recurse the one build.
                //First, get the list of package changes, and the set of builds that they are associated with....
                var changedpackageMapping =  GetPackageChangeToBuildMapping(changeManifest);
                
                //Now sort out which PackageBuildMappings are associated with which builds
                var buildPackageMappings = GetBuildToPackageChangeMapping(changeManifest, buildTypeDetails, changedpackageMapping);
                
                //This should end up being a  List<KeyValuePair<PackageBuildMapping, List<NuGetPackageChange>>> where the NuGetPackageChange associated with a PackageBuildMapping
                //are all the same OldVersion and NewVersion.
                //This is so when we check a PackageBuildMapping, we can use the OldVersion/NewVersion from the associated list and ALL the NuGetPackageChange objects can get the 
                //same ChangeManifest.  We will be querying the PackagBuildMapping multiple times, so we will need caching to speed this up.

                //var test2 = new List<KeyValuePair<PackageBuildMapping, List<NuGetPackageChange>>>();
                    

                //Now we need to get a mapping of 
                var packageBuildChangeManifestMapping = new List<Tuple<PackageBuildMapping, string, string, ChangeManifest>>();
                foreach (var build in buildPackageMappings)
                {
                    if (build.Key.BuildConfigurationId == buildType)
                        continue;
                    foreach (var specificPackageChange in build.Value)
                    {
                        var versionMin = specificPackageChange.OldVersion;
                        var versionMax = specificPackageChange.NewVersion;

                        var instanceTeamCityApi = _api.TeamCityServer.Equals(build.Key.ServerUrl, StringComparison.OrdinalIgnoreCase)
                                                              ? _api
                                                              : new TeamCityApi(build.Key.ServerUrl);

                        var resolver = new AggregateBuildDeltaResolver(instanceTeamCityApi, _externalIssueResolvers, _packageChangeComparator, _packageBuildMappingCache);
                        var dependencyManifest = resolver.CreateChangeManifest(null, build.Key.BuildConfigurationId, null, versionMin, versionMax, null, true, true);
                        packageBuildChangeManifestMapping.Add(Tuple.Create(build.Key, versionMin, versionMin, dependencyManifest));
                    }
                }
            }

            return changeManifest;
        }
        
        private List<KeyValuePair<PackageBuildMapping, List<NuGetPackageChange>>> GetBuildToPackageChangeMapping(ChangeManifest changeManifest, BuildTypeDetails buildTypeDetails, Dictionary<NuGetPackageChange, List<PackageBuildMapping>> mappings)
        {
            //var temp = new List<KeyValuePair<PackageBuildMapping, List<NuGetPackageChange>>>();
            var buildPackageMappings = new List<KeyValuePair<PackageBuildMapping, List<NuGetPackageChange>>>();
            var versionLookup = mappings.Keys.ToLookup(x => new {x.OldVersion, x.NewVersion});
            foreach (var lookup in versionLookup)
            {
                var versionPair = lookup.Key;
                foreach (var packageChange in versionLookup[versionPair])
                {
                    PackageBuildMapping build = null;
                    var mapping = mappings[packageChange];

                    if (!mapping.Any())
                    {
                        changeManifest.GenerationLog.Add(new LogEntry(DateTime.Now, Status.Warning, string.Format("Did not find a mapping for package: {0}.", packageChange.PackageId)));
                        continue;
                    }

                    if (mapping.Count == 1)
                    {
                        //We only got one back, this is good...
                        build = mapping.First();

                        changeManifest.GenerationLog.Add(new LogEntry(DateTime.Now, Status.Ok, string.Format("Found singular packages to build mapping {0}.", build.BuildConfigurationName)));
                    }

                    if (mappings.Any())
                    {
                        //Ok, so multiple builds are outputting this package, so we need to try and constrain on project...
                        build =
                            mapping.FirstOrDefault(
                                m => m.Project.Equals(buildTypeDetails.Project.Name, StringComparison.OrdinalIgnoreCase));
                        if (build != null)
                        {
                            changeManifest.GenerationLog.Add(new LogEntry(DateTime.Now, Status.Warning, string.Format("Found duplicate mappings, using package to build mapping {0}.", build.BuildConfigurationName)));
                        }
                    }

                    if (build != null && buildPackageMappings.Exists(x => x.Key.BuildConfigurationId == build.BuildConfigurationId && x.Key.PackageId == String.Format("{0} - {1}", packageChange.OldVersion, packageChange.NewVersion) && x.Key.ServerUrl == build.ServerUrl))
                    {
                        var buildMapping = buildPackageMappings.Find(x => x.Key.BuildConfigurationId == build.BuildConfigurationId && x.Key.PackageId == String.Format("{0} - {1}", packageChange.OldVersion, packageChange.NewVersion) && x.Key.ServerUrl == build.ServerUrl);
                        if (!buildMapping.Value.Contains(packageChange))
                            buildMapping.Value.Add(packageChange);
                    }
                    else if (build != null)
                    {
                        var buildConfiguration = new PackageBuildMapping()
                            {
                                BuildConfigurationId = build.BuildConfigurationId,
                                BuildConfigurationName = build.BuildConfigurationName,
                                PackageId = String.Format("{0} - {1}", packageChange.OldVersion, packageChange.NewVersion),
                                Project = build.Project,
                                ServerUrl = build.ServerUrl
                            };
                        buildPackageMappings.Add(new KeyValuePair<PackageBuildMapping, List<NuGetPackageChange>>(buildConfiguration, new List<NuGetPackageChange> { packageChange }));
                    }
                }
            }
            //foreach (var mapping in mappings)
            //{
            //    PackageBuildMapping build = null;

            //    if (!mapping.Value.Any())
            //    {
            //        changeManifest.GenerationLog.Add(new LogEntry(DateTime.Now,Status.Warning,string.Format("Did not find a mapping for package: {0}.", mapping.Key.PackageId)));
            //        continue;
            //    }

            //    if (mapping.Value.Count == 1)
            //    {
            //        //We only got one back, this is good...
            //        build = mapping.Value.First();

            //        changeManifest.GenerationLog.Add(new LogEntry(DateTime.Now,Status.Ok,string.Format("Found singular packages to build mapping {0}.", build.BuildConfigurationName)));
            //    }
            //    if (mappings.Any())
            //    {
            //        //Ok, so multiple builds are outputting this package, so we need to try and constrain on project...
            //        build = mapping.Value.FirstOrDefault(m => m.Project.Equals(buildTypeDetails.Project.Name, StringComparison.OrdinalIgnoreCase));
            //        if (build != null)
            //        {
            //            changeManifest.GenerationLog.Add(new LogEntry(DateTime.Now,Status.Warning,string.Format("Found duplicate mappings, using package to build mapping {0}.",build.BuildConfigurationName)));
            //        }
            //    }

            //    if (build != null && buildPackageMappings.ContainsKey(build))
            //    {
            //        buildPackageMappings[build].Add(mapping.Key);
            //    }
            //    else
            //    {
            //        buildPackageMappings.Add(build, new List<NuGetPackageChange> { mapping.Key });
            //    }
            //}
            return buildPackageMappings;
        }

        private Dictionary<NuGetPackageChange, List<PackageBuildMapping>> GetPackageChangeToBuildMapping(ChangeManifest changeManifest)
        {
            var changedpackageMapping = new Dictionary<NuGetPackageChange, List<PackageBuildMapping>>();
            foreach (var dependency in changeManifest.NuGetPackageChanges.Where(c => c.Type == NuGetPackageChangeType.Modified))
            {
                var mappings = _packageBuildMappingCache.PackageBuildMappings.Where(m => m.PackageId.Equals(dependency.PackageId, StringComparison.CurrentCultureIgnoreCase)).ToList();
                changedpackageMapping.Add(dependency, mappings);
            }
            return changedpackageMapping;
        }

        private string ResolveToVersion(string buildType)
        {
            string to;
            var runningBuild = _api.GetRunningBuildByBuildType(buildType).FirstOrDefault();
            if (runningBuild != null)
                to = runningBuild.Number;
            else
                throw new ApplicationException(String.Format("Could not resolve a build number for the running build."));
            return to;
        }

        private string ResolveFromVersion(string buildType)
        {
            string from;
            var latestSuccesfull = _api.GetLatestSuccesfulBuildByBuildType(buildType);
            if (latestSuccesfull != null)
                from = latestSuccesfull.Number;
            else
                throw new ApplicationException(String.Format("Could not find latest build for build type {0}", buildType));
            return from;
        }

        private string ResolveBuildTypeId(string projectName, string buildName)
        {
            if (String.IsNullOrEmpty(projectName) || String.IsNullOrEmpty(buildName))
            {
                throw new ApplicationException(String.Format("Could not resolve Project: {0} and BuildName:{1} to a build type", projectName, buildName));
            }
            var resolvedBuildType = _api.GetBuildTypeByProjectAndName(projectName, buildName).FirstOrDefault();
            if (resolvedBuildType != null) 
                return resolvedBuildType.Id;

            return String.Empty;
        }
    }
}