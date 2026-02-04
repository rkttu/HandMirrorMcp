using HandMirrorMcp.Services;
using NuGet.Versioning;

namespace HandMirrorMcp.UnitTests;

[TestClass]
public sealed class NuGetServiceTests
{
    private NuGetService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = new NuGetService();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _service.Dispose();
    }

    [TestMethod]
    public void PackageSources_ShouldContainNuGetOrg()
    {
        // Arrange & Act
        var sources = _service.PackageSources;

        // Assert
        Assert.IsNotEmpty(sources, "Should have at least one package source");
        Assert.IsTrue(sources.Any(s => s.Name.Contains("nuget", StringComparison.OrdinalIgnoreCase)),
            "Should contain nuget.org source");
    }

    [TestMethod]
    public async Task GetPackageVersionsAsync_NewtonsoftJson_ShouldReturnVersions()
    {
        // Arrange
        var packageId = "Newtonsoft.Json";

        // Act
        var versions = await _service.GetPackageVersionsAsync(packageId);
        var versionList = versions.ToList();

        // Assert
        Assert.IsNotEmpty(versionList, "Should return versions for Newtonsoft.Json");
        Assert.IsTrue(versionList.Any(v => v.Major >= 13), "Should have version 13.x or higher");
    }

    [TestMethod]
    public async Task GetPackageMetadataAsync_NewtonsoftJson_ShouldReturnMetadata()
    {
        // Arrange
        var packageId = "Newtonsoft.Json";

        // Act
        var metadata = await _service.GetPackageMetadataAsync(packageId);

        // Assert
        Assert.IsNotNull(metadata, "Should return metadata");
        Assert.AreEqual("Newtonsoft.Json", metadata.Identity.Id);
    }

    [TestMethod]
    public async Task DownloadPackageAsync_NewtonsoftJson_ShouldDownloadSuccessfully()
    {
        // Arrange
        var packageId = "Newtonsoft.Json";
        var version = new NuGetVersion("13.0.3");

        // Act
        var result = await _service.DownloadPackageAsync(packageId, version);

        // Assert
        Assert.IsTrue(result.IsSuccess, $"Download should succeed. Error: {result.Error}");
        Assert.IsNotNull(result.Path, "Path should not be null on success");
        Assert.IsTrue(Directory.Exists(result.Path), $"Package directory should exist: {result.Path}");
        
        // Verify nupkg file exists
        var nupkgFiles = Directory.GetFiles(result.Path, "*.nupkg");
        Assert.IsNotEmpty(nupkgFiles, "Should have nupkg file in the directory");
    }

    [TestMethod]
    public async Task DownloadPackageAsync_NonExistentPackage_ShouldFail()
    {
        // Arrange
        var packageId = "NonExistentPackage12345XYZ";
        var version = new NuGetVersion("1.0.0");

        // Act
        var result = await _service.DownloadPackageAsync(packageId, version);

        // Assert
        Assert.IsFalse(result.IsSuccess, "Download should fail for non-existent package");
        Assert.IsNotNull(result.Error, "Should have error message");
    }

    [TestMethod]
    public async Task GetPackageAssembliesAsync_AfterDownload_ShouldReturnAssemblies()
    {
        // Arrange
        var packageId = "Newtonsoft.Json";
        var version = new NuGetVersion("13.0.3");

        // Act
        var downloadResult = await _service.DownloadPackageAsync(packageId, version);
        
        // Skip if download failed
        if (!downloadResult.IsSuccess)
        {
            Assert.Inconclusive($"Download failed: {downloadResult.Error}");
            return;
        }

        var assemblies = await _service.GetPackageAssembliesAsync(downloadResult.Path!);
        var assemblyList = assemblies.ToList();

        // Assert
        Assert.IsNotEmpty(assemblyList, "Should return assemblies");
        Assert.IsTrue(assemblyList.Any(a => a.EndsWith("Newtonsoft.Json.dll", StringComparison.OrdinalIgnoreCase)),
            "Should contain Newtonsoft.Json.dll");
    }
}
