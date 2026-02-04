using HandMirrorMcp.Services;

namespace HandMirrorMcp.UnitTests;

[TestClass]
public sealed class RepositoryServiceTests
{
    private RepositoryService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = new RepositoryService();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _service.Dispose();
    }

    #region ParseRepositoryUrl Tests

    [TestMethod]
    public void ParseRepositoryUrl_GitHubUrl_ShouldParseCorrectly()
    {
        // Arrange
        var url = "https://github.com/JamesNK/Newtonsoft.Json";

        // Act
        var result = RepositoryService.ParseRepositoryUrl(url);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(RepositoryType.GitHub, result.Type);
        Assert.AreEqual("JamesNK", result.Owner);
        Assert.AreEqual("Newtonsoft.Json", result.Name);
        Assert.AreEqual("github.com", result.Host);
    }

    [TestMethod]
    public void ParseRepositoryUrl_GitHubUrlWithGitExtension_ShouldRemoveGitExtension()
    {
        // Arrange
        var url = "https://github.com/dotnet/runtime.git";

        // Act
        var result = RepositoryService.ParseRepositoryUrl(url);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(RepositoryType.GitHub, result.Type);
        Assert.AreEqual("dotnet", result.Owner);
        Assert.AreEqual("runtime", result.Name);
    }

    [TestMethod]
    public void ParseRepositoryUrl_GitLabUrl_ShouldParseCorrectly()
    {
        // Arrange
        var url = "https://gitlab.com/gitlab-org/gitlab";

        // Act
        var result = RepositoryService.ParseRepositoryUrl(url);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(RepositoryType.GitLab, result.Type);
        Assert.AreEqual("gitlab-org", result.Owner);
        Assert.AreEqual("gitlab", result.Name);
    }

    [TestMethod]
    public void ParseRepositoryUrl_BitbucketUrl_ShouldParseCorrectly()
    {
        // Arrange
        var url = "https://bitbucket.org/atlassian/stash";

        // Act
        var result = RepositoryService.ParseRepositoryUrl(url);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(RepositoryType.Bitbucket, result.Type);
        Assert.AreEqual("atlassian", result.Owner);
        Assert.AreEqual("stash", result.Name);
    }

    [TestMethod]
    public void ParseRepositoryUrl_AzureDevOpsUrl_ShouldParseCorrectly()
    {
        // Arrange
        var url = "https://dev.azure.com/myorg/myproject";

        // Act
        var result = RepositoryService.ParseRepositoryUrl(url);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(RepositoryType.AzureDevOps, result.Type);
        Assert.AreEqual("myorg", result.Owner);
        Assert.AreEqual("myproject", result.Name);
    }

    [TestMethod]
    public void ParseRepositoryUrl_VisualStudioComUrl_ShouldParseAsAzureDevOps()
    {
        // Arrange
        var url = "https://myorg.visualstudio.com/myproject/repo";

        // Act
        var result = RepositoryService.ParseRepositoryUrl(url);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(RepositoryType.AzureDevOps, result.Type);
    }

    [TestMethod]
    public void ParseRepositoryUrl_NullUrl_ShouldReturnNull()
    {
        // Act
        var result = RepositoryService.ParseRepositoryUrl(null);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ParseRepositoryUrl_EmptyUrl_ShouldReturnNull()
    {
        // Act
        var result = RepositoryService.ParseRepositoryUrl("");

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ParseRepositoryUrl_WhitespaceUrl_ShouldReturnNull()
    {
        // Act
        var result = RepositoryService.ParseRepositoryUrl("   ");

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ParseRepositoryUrl_InvalidUrl_ShouldReturnNull()
    {
        // Act
        var result = RepositoryService.ParseRepositoryUrl("not-a-valid-url");

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ParseRepositoryUrl_UnknownHost_ShouldReturnNull()
    {
        // Arrange
        var url = "https://unknown-host.com/owner/repo";

        // Act
        var result = RepositoryService.ParseRepositoryUrl(url);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ParseRepositoryUrl_UrlWithOnlyOwner_ShouldReturnNull()
    {
        // Arrange
        var url = "https://github.com/JamesNK";

        // Act
        var result = RepositoryService.ParseRepositoryUrl(url);

        // Assert
        Assert.IsNull(result, "URL with only owner should not parse successfully");
    }

    #endregion

    #region GetGitHubReadmeAsync Tests

    [TestMethod]
    public async Task GetGitHubReadmeAsync_ValidRepo_ShouldReturnReadme()
    {
        // Arrange
        var owner = "JamesNK";
        var repo = "Newtonsoft.Json";

        // Act
        var result = await _service.GetGitHubReadmeAsync(owner, repo);

        // Assert
        Assert.IsNotNull(result, "Should return README for valid repository");
        Assert.IsNotNull(result.Content, "README content should not be null");
        Assert.IsNotEmpty(result.Content, "README content should not be empty");
    }

    [TestMethod]
    public async Task GetGitHubReadmeAsync_NonExistentRepo_ShouldReturnNull()
    {
        // Arrange
        var owner = "nonexistent-owner-xyz123";
        var repo = "nonexistent-repo-abc456";

        // Act
        var result = await _service.GetGitHubReadmeAsync(owner, repo);

        // Assert
        Assert.IsNull(result, "Should return null for non-existent repository");
    }

    [TestMethod]
    public async Task GetGitHubReadmeAsync_WithCancellation_ShouldReturnNull()
    {
        // Arrange
        var owner = "JamesNK";
        var repo = "Newtonsoft.Json";
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act - The service catches exceptions internally and returns null
        var result = await _service.GetGitHubReadmeAsync(owner, repo, cts.Token);
        
        // Assert
        Assert.IsNull(result, "Should return null when cancelled");
    }

    #endregion

    #region GetGitHubWikiAsync Tests

    [TestMethod]
    public async Task GetGitHubWikiAsync_NonExistentRepo_ShouldReturnNull()
    {
        // Arrange
        var owner = "nonexistent-owner-xyz123";
        var repo = "nonexistent-repo-abc456";

        // Act
        var result = await _service.GetGitHubWikiAsync(owner, repo);

        // Assert
        Assert.IsNull(result, "Should return null for non-existent repository");
    }

    #endregion
}
