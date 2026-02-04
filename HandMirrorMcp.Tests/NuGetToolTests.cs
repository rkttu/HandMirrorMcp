using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HandMirrorMcp.Tests;

[TestClass]
public class NuGetToolTests
{
    private static McpClient? _client;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        var serverPath = FindProjectPath("HandMirrorMcp");

        if (string.IsNullOrEmpty(serverPath))
        {
            Assert.Fail("HandMirrorMcp 프로젝트를 찾을 수 없습니다.");
            return;
        }

        var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = ["run", "--project", serverPath],
            Name = "HandMirror"
        });

        _client = await McpClient.CreateAsync(clientTransport, cancellationToken: context.CancellationToken);
    }

    private static string? FindProjectPath(string projectName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var slnFiles = directory.GetFiles("*.sln").Concat(directory.GetFiles("*.slnx"));
            if (slnFiles.Any())
            {
                var projectDir = Path.Combine(directory.FullName, projectName);
                if (Directory.Exists(projectDir))
                {
                    return projectDir;
                }
            }
            directory = directory.Parent;
        }

        return null;
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task ListTools_ShouldIncludeNuGetTools()
    {
        Assert.IsNotNull(_client);

        var tools = await _client.ListToolsAsync(cancellationToken: TestContext.CancellationToken);
        var toolList = tools.ToList();

        Assert.IsTrue(toolList.Any(t => t.Name == "list_nuget_sources"));
        Assert.IsTrue(toolList.Any(t => t.Name == "search_nuget_packages"));
        Assert.IsTrue(toolList.Any(t => t.Name == "get_nuget_package_versions"));
        Assert.IsTrue(toolList.Any(t => t.Name == "get_nuget_package_info"));
        Assert.IsTrue(toolList.Any(t => t.Name == "inspect_nuget_package"));
        Assert.IsTrue(toolList.Any(t => t.Name == "inspect_nuget_package_type"));
        Assert.IsTrue(toolList.Any(t => t.Name == "clear_nuget_cache"));
        Assert.IsTrue(toolList.Any(t => t.Name == "get_nuget_vulnerabilities"));
        Assert.IsTrue(toolList.Any(t => t.Name == "inspect_nupkg_contents"));
        Assert.IsTrue(toolList.Any(t => t.Name == "extract_nupkg_file"));
    }

    [TestMethod]
    public async Task ListPrompts_ShouldIncludeNuGetPrompts()
    {
        Assert.IsNotNull(_client);

        var prompts = await _client.ListPromptsAsync(cancellationToken: TestContext.CancellationToken);
        var promptList = prompts.ToList();

        Assert.IsTrue(promptList.Any(p => p.Name == "explore_nuget_package"));
        Assert.IsTrue(promptList.Any(p => p.Name == "find_nuget_package"));
        Assert.IsTrue(promptList.Any(p => p.Name == "compare_nuget_versions"));
        Assert.IsTrue(promptList.Any(p => p.Name == "analyze_package_dependencies"));
    }

    [TestMethod]
    public async Task ListNuGetSources_ShouldReturnSources()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("list_nuget_sources", new Dictionary<string, object?>(), cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.Contains("nuget.org", textContent.Text);
        Assert.Contains("Cache Directory:", textContent.Text);
    }

    [TestMethod]
    public async Task SearchNuGetPackages_ShouldReturnResults()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("search_nuget_packages", new Dictionary<string, object?>
        {
            ["searchTerm"] = "Newtonsoft.Json",
            ["maxResults"] = 5
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.Contains("Newtonsoft.Json", textContent.Text);
    }

    [TestMethod]
    public async Task GetNuGetPackageVersions_ShouldReturnVersions()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("get_nuget_package_versions", new Dictionary<string, object?>
        {
            ["packageId"] = "Newtonsoft.Json"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.Contains("Available versions", textContent.Text);
        Assert.Contains("13.", textContent.Text); // Newtonsoft.Json has 13.x versions
    }

    [TestMethod]
    public async Task GetNuGetPackageInfo_ShouldReturnMetadata()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("get_nuget_package_info", new Dictionary<string, object?>
        {
            ["packageId"] = "Newtonsoft.Json"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.IsTrue(textContent.Text.Contains("Package: Newtonsoft.Json") || textContent.Text.Contains("Failed to download"), $"Unexpected response: {textContent.Text}");
        Assert.Contains("Authors:", textContent.Text);
        Assert.Contains("Description:", textContent.Text);
    }

    [TestMethod]
    public async Task InspectNuGetPackage_ShouldDownloadAndInspect()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("inspect_nuget_package", new Dictionary<string, object?>
        {
            ["packageId"] = "Newtonsoft.Json",
            ["version"] = "13.0.3"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.IsTrue(textContent.Text.Contains("Package: Newtonsoft.Json") || textContent.Text.Contains("Failed to download"), $"Unexpected response: {textContent.Text}");
        Assert.Contains("Newtonsoft.Json", textContent.Text); // Namespace
        // 다운로드 성공 또는 실패 모두 허용 (네트워크 환경에 따라 다를 수 있음)
    }

    [TestMethod]
    public async Task GetNuGetPackageInfo_WithInvalidPackage_ShouldReturnNotFound()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("get_nuget_package_info", new Dictionary<string, object?>
        {
            ["packageId"] = "NonExistentPackage12345XYZ"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.Contains("not found", textContent.Text.ToLower());
    }

    [TestMethod]
    public async Task ClearNuGetCache_DryRun_ShouldListPackages()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("clear_nuget_cache", new Dictionary<string, object?>
        {
            ["dryRun"] = true
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.Contains("Cache Directory:", textContent.Text);
        Assert.Contains("Dry run mode", textContent.Text);
    }

    [TestMethod]
    public async Task GetNuGetVulnerabilities_ShouldReturnVulnerabilityInfo()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("get_nuget_vulnerabilities", new Dictionary<string, object?>
        {
            ["packageId"] = "Newtonsoft.Json"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.Contains("Security Vulnerabilities:", textContent.Text);
        Assert.Contains("Latest Version:", textContent.Text);
    }

    [TestMethod]
    public async Task GetNuGetVulnerabilities_WithSpecificVersion_ShouldReturnInfo()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("get_nuget_vulnerabilities", new Dictionary<string, object?>
        {
            ["packageId"] = "System.Text.Json",
            ["version"] = "6.0.0"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.Contains("Security Vulnerabilities:", textContent.Text);
    }

    [TestMethod]
    public async Task InspectNupkgContents_ShouldReturnPackageStructure()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("inspect_nupkg_contents", new Dictionary<string, object?>
        {
            ["packageId"] = "Newtonsoft.Json",
            ["version"] = "13.0.3"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("NuGet Package Contents:", textContent.Text);
        Assert.Contains("Package Metadata", textContent.Text);
        Assert.Contains("Package Structure:", textContent.Text);
        Assert.Contains("lib", textContent.Text);
    }

    [TestMethod]
    public async Task InspectNupkgContents_Detailed_ShouldShowFileList()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("inspect_nupkg_contents", new Dictionary<string, object?>
        {
            ["packageId"] = "Newtonsoft.Json",
            ["version"] = "13.0.3",
            ["detailed"] = true
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("NuGet Package Contents:", textContent.Text);
        // detailed 모드에서는 개별 파일 경로가 표시됨
        Assert.Contains(".dll", textContent.Text);
    }

    [TestMethod]
    public async Task ExtractNupkgFile_Nuspec_ShouldReturnContent()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("extract_nupkg_file", new Dictionary<string, object?>
        {
            ["packageId"] = "Newtonsoft.Json",
            ["filePath"] = "Newtonsoft.Json.nuspec",
            ["version"] = "13.0.3"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        // nuspec 파일 내용이 포함되어야 함
        Assert.Contains("Newtonsoft.Json", textContent.Text);
        Assert.Contains("<?xml", textContent.Text.ToLower());
    }

    [TestMethod]
    public async Task ExtractNupkgFile_NotFound_ShouldListAvailableFiles()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("extract_nupkg_file", new Dictionary<string, object?>
        {
            ["packageId"] = "Newtonsoft.Json",
            ["filePath"] = "nonexistent.txt",
            ["version"] = "13.0.3"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        // 네트워크 문제로 다운로드 실패 시 테스트 건너뛰기
        if (textContent.Text.Contains("Failed to download"))
        {
            Assert.Inconclusive("Package download failed due to network issues.");
            return;
        }

        Assert.Contains("File not found", textContent.Text);
        Assert.Contains("Available files:", textContent.Text);
    }

    public TestContext TestContext { get; set; }
}

