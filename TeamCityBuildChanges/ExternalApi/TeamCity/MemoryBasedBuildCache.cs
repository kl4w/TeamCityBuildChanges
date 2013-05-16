using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RestSharp;

namespace TeamCityBuildChanges.ExternalApi.TeamCity
{
    public class MemoryBasedBuildCache
    {
        private readonly ConcurrentDictionary<string, BuildTypeDetails> _buildTypeDetailsCache;
        private readonly ConcurrentDictionary<string, BuildDetails> _buildDetailsCache;
        private readonly ConcurrentDictionary<string, ChangeList> _buildChangeListCache;
        private readonly ConcurrentDictionary<string, ChangeDetail> _changeDetailsCache;
        private readonly ConcurrentDictionary<int, List<TeamCityApi.PackageDetails>> _buildNuGetDependenciesCache;
        private readonly ConcurrentDictionary<string, IRestResponse> _restRequestCache;

        public MemoryBasedBuildCache()
        {
            _buildTypeDetailsCache = new ConcurrentDictionary<string, BuildTypeDetails>();
            _buildDetailsCache = new ConcurrentDictionary<string, BuildDetails>();
            _buildChangeListCache = new ConcurrentDictionary<string, ChangeList>();
            _changeDetailsCache = new ConcurrentDictionary<string, ChangeDetail>();
            _buildNuGetDependenciesCache = new ConcurrentDictionary<int, List<TeamCityApi.PackageDetails>>();
            _restRequestCache = new ConcurrentDictionary<string, IRestResponse>();
        }

        public bool TryCacheForRestRequest(string url, out IRestResponse request)
        {
            if (_restRequestCache.ContainsKey(url))
            {
                request = _restRequestCache[url];
                return true;
            }
            request = null;
            return false;
        }

        public void AddCacheRequest(string url, IRestResponse request)
        {
            if (!_restRequestCache.ContainsKey(url))
                _restRequestCache.TryAdd(url, request);
        }

        public bool TryCacheForDetailsByBuildTypeId(string buildTypeId, out BuildTypeDetails buildTypeDetails)
        {
            if (_buildTypeDetailsCache.ContainsKey(buildTypeId))
            {
                buildTypeDetails = _buildTypeDetailsCache[buildTypeId];
                return true;
            }
            buildTypeDetails = null;
            return false;
        }

        public bool TryCacheForBuildsByBuildTypeId(string buildTypeId, out IEnumerable<Build> builds)
        {
            builds = _buildDetailsCache.Values.Where(b => b.BuildTypeId == buildTypeId);
            return builds.Any();
        }

        public bool TryCacheForBuildDetailsByBuildId(string buildId, out BuildDetails buildDetails)
        {
            if (_buildDetailsCache.ContainsKey(buildId))
            {
                buildDetails = _buildDetailsCache[buildId];
                return true;
            }
            buildDetails = null;
            return false;
        }

        public bool TryCacheForChangeListByBuildId(string buildId, out ChangeList changeList)
        {
            if (_buildChangeListCache.ContainsKey(buildId))
            {
                changeList = _buildChangeListCache[buildId];
                return true;
            }
            changeList = null;
            return false;
        }

        public bool TryCacheForChangeDetailsByChangeId(string changeId, out ChangeDetail changeDetail)
        {
            if (_changeDetailsCache.ContainsKey(changeId))
            {
                changeDetail = _changeDetailsCache[changeId];
                return true;
            }
            changeDetail = null;
            return false;
        }

        public bool TryCacheForNuGetDependenciesByBuildTypeAndBuildId(string buildTypeId, string buildId, out List<TeamCityApi.PackageDetails> packageDetails)
        {
            if (_buildNuGetDependenciesCache.ContainsKey(buildTypeId.GetHashCode() ^ buildId.GetHashCode()))
            {
                packageDetails = _buildNuGetDependenciesCache[buildTypeId.GetHashCode() ^ buildId.GetHashCode()];
                return true;
            }
            packageDetails = new List<TeamCityApi.PackageDetails>();
            return false;
        }

        public void AddCacheBuildTypeDetailsById(string id, BuildTypeDetails buildTypeDetails)
        {
            if (buildTypeDetails != null && !_buildTypeDetailsCache.ContainsKey(id))
                _buildTypeDetailsCache.TryAdd(id, buildTypeDetails);
        }

        public void AddCacheBuildDetailsEntry(BuildDetails buildDetails)
        {
            if (buildDetails != null && !_buildDetailsCache.ContainsKey(buildDetails.Id))
                _buildDetailsCache.TryAdd(buildDetails.Id, buildDetails);
        }

        public void AddCacheChangeListByBuildIdEntry(string buildId, ChangeList changeList)
        {
            if (changeList != null && !_buildChangeListCache.ContainsKey(buildId))
                _buildChangeListCache.TryAdd(buildId, changeList);
        }

        public void AddCacheChangeDetailsByChangeIdEntry(string changeId, ChangeDetail changeDetail)
        {
            if (changeDetail != null && !_changeDetailsCache.ContainsKey(changeId))
                _changeDetailsCache.TryAdd(changeId, changeDetail);
        }

        public void AddCacheNuGetDependencies(string buildTypeId, string buildId, List<TeamCityApi.PackageDetails> packageDetails)
        {
            if (packageDetails != null && !_buildNuGetDependenciesCache.ContainsKey(buildTypeId.GetHashCode() ^ buildId.GetHashCode()))
                _buildNuGetDependenciesCache.TryAdd(buildTypeId.GetHashCode() ^ buildId.GetHashCode(), packageDetails);
        }
    }
}
