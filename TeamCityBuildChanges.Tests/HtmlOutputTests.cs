using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TeamCityBuildChanges.ExternalApi.TeamCity;
using TeamCityBuildChanges.IssueDetailResolvers;
using TeamCityBuildChanges.Output;
using TeamCityBuildChanges.Testing;

namespace TeamCityBuildChanges.Tests
{
    [TestFixture]
    public class HtmlOutputTests
    {
        [Test]
        public void Test()
        {
            var manifest = TestHelpers.CreateSimpleChangeManifest();
            manifest.NuGetPackageChanges = TestHelpers.CreateSimpleNuGetPackageDependencies();
            var result = new RazorOutputRenderer(@".\templates\Default.cshtml").Render(manifest);
            File.WriteAllText(String.Format(@"{0}\test.html", Directory.GetCurrentDirectory()), result);
            Assert.True(true);//Giddyup.
        }
    }
}
