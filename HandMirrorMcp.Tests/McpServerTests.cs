using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HandMirrorMcp.Tests;

[TestClass]
public class McpServerTests
{
    private static McpClient? _client;
    private static string? _serverPath;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        // 솔루션 루트를 기준으로 프로젝트 경로 찾기
        _serverPath = FindProjectPath("HandMirrorMcp");

        if (string.IsNullOrEmpty(_serverPath))
        {
            Assert.Fail("HandMirrorMcp 프로젝트를 찾을 수 없습니다.");
            return;
        }

        var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = ["run", "--project", _serverPath],
            Name = "HandMirror"
        });

        _client = await McpClient.CreateAsync(clientTransport, cancellationToken: context.CancellationToken);
    }

    /// <summary>
    /// 솔루션 파일(.sln 또는 .slnx)을 기준으로 프로젝트 경로를 찾습니다.
    /// </summary>
    private static string? FindProjectPath(string projectName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        // 상위 디렉터리를 탐색하면서 솔루션 파일 찾기
        while (directory != null)
        {
            var slnFiles = directory.GetFiles("*.sln").Concat(directory.GetFiles("*.slnx"));
            if (slnFiles.Any())
            {
                // 솔루션 루트에서 프로젝트 폴더 찾기
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
    public void ServerInfo_ShouldHaveCorrectName()
    {
        Assert.IsNotNull(_client);
        Assert.AreEqual("HandMirror", _client.ServerInfo?.Name);
    }

    [TestMethod]
    public void ServerInfo_ShouldHaveVersion()
    {
        Assert.IsNotNull(_client);
        Assert.IsFalse(string.IsNullOrEmpty(_client.ServerInfo?.Version));
    }

    [TestMethod]
    public void ServerInstructions_ShouldNotBeEmpty()
    {
        Assert.IsNotNull(_client);
        Assert.IsFalse(string.IsNullOrEmpty(_client.ServerInstructions));
        Assert.Contains("HandMirror", _client.ServerInstructions);
    }

    [TestMethod]
    public async Task ListTools_ShouldReturnThreeTools()
    {
        Assert.IsNotNull(_client);

        var tools = await _client.ListToolsAsync(cancellationToken: TestContext.CancellationToken);
        var toolList = tools.ToList();

        Assert.IsGreaterThanOrEqualTo(3, toolList.Count, $"Expected at least 3 tools, got {toolList.Count}");
        Assert.IsTrue(toolList.Any(t => t.Name == "inspect_assembly"));
        Assert.IsTrue(toolList.Any(t => t.Name == "list_namespaces"));
        Assert.IsTrue(toolList.Any(t => t.Name == "get_type_info"));
    }

    [TestMethod]
    public async Task ListPrompts_ShouldReturnThreePrompts()
    {
        Assert.IsNotNull(_client);

        var prompts = await _client.ListPromptsAsync(cancellationToken: TestContext.CancellationToken);
        var promptList = prompts.ToList();

        Assert.IsGreaterThanOrEqualTo(3, promptList.Count, $"Expected at least 3 prompts, got {promptList.Count}");
        Assert.IsTrue(promptList.Any(p => p.Name == "analyze_assembly"));
        Assert.IsTrue(promptList.Any(p => p.Name == "find_type"));
        Assert.IsTrue(promptList.Any(p => p.Name == "compare_assemblies"));
    }

    [TestMethod]
    public async Task ListNamespaces_WithValidAssembly_ShouldReturnNamespaces()
    {
        Assert.IsNotNull(_client);

        var testAssemblyPath = typeof(Mono.Cecil.AssemblyDefinition).Assembly.Location;

        var result = await _client.CallToolAsync("list_namespaces", new Dictionary<string, object?>
        {
            ["assemblyPath"] = testAssemblyPath
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);
        if (result.IsError == true) { var errContent = result.Content.OfType<TextContentBlock>().FirstOrDefault(); Assert.Fail($"Tool returned error: {errContent?.Text}"); }
        Assert.IsNotEmpty(result.Content);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.Contains("Mono.Cecil", textContent.Text);
    }

    [TestMethod]
    public async Task ListNamespaces_WithInvalidPath_ShouldReturnError()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("list_namespaces", new Dictionary<string, object?>
        {
            ["assemblyPath"] = @"C:\nonexistent\fake.dll"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);
        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.IsTrue(textContent.Text.Contains("Error") || textContent.Text.Contains("not found"));
    }

    [TestMethod]
    public async Task InspectAssembly_WithValidAssembly_ShouldReturnDetailedInfo()
    {
        Assert.IsNotNull(_client);

        var testAssemblyPath = typeof(Mono.Cecil.AssemblyDefinition).Assembly.Location;

        var result = await _client.CallToolAsync("inspect_assembly", new Dictionary<string, object?>
        {
            ["assemblyPath"] = testAssemblyPath
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);
        if (result.IsError == true) { var errContent = result.Content.OfType<TextContentBlock>().FirstOrDefault(); Assert.Fail($"Tool returned error: {errContent?.Text}"); }

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        // 어셈블리 정보 포함 확인
        Assert.Contains("Assembly:", textContent.Text);
        Assert.Contains("Architecture:", textContent.Text);
        Assert.Contains("Namespace:", textContent.Text);
    }

    [TestMethod]
    public async Task GetTypeInfo_WithValidType_ShouldReturnTypeDetails()
    {
        Assert.IsNotNull(_client);

        var testAssemblyPath = typeof(Mono.Cecil.AssemblyDefinition).Assembly.Location;

        var result = await _client.CallToolAsync("get_type_info", new Dictionary<string, object?>
        {
            ["assemblyPath"] = testAssemblyPath,
            ["typeName"] = "Mono.Cecil.AssemblyDefinition"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.IsTrue(textContent.Text.Contains("[class]") || textContent.Text.Contains("McpClient"));
    }

    [TestMethod]
    public async Task GetTypeInfo_WithInvalidType_ShouldReturnError()
    {
        Assert.IsNotNull(_client);

        var testAssemblyPath = typeof(Mono.Cecil.AssemblyDefinition).Assembly.Location;

        var result = await _client.CallToolAsync("get_type_info", new Dictionary<string, object?>
        {
            ["assemblyPath"] = testAssemblyPath,
            ["typeName"] = "NonExistent.FakeType"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);
        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.IsTrue(textContent.Text.Contains("Error") || textContent.Text.Contains("not found"));
    }

    public TestContext TestContext { get; set; }
}



