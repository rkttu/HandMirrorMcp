using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HandMirrorMcp.Tests;

[TestClass]
public class AssemblyInspectorToolTests
{
    private static McpClient? _client;
    private static string? _testAssemblyPath;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        var serverPath = FindProjectPath("HandMirrorMcp");

        if (string.IsNullOrEmpty(serverPath))
        {
            Assert.Fail("HandMirrorMcp 프로젝트를 찾을 수 없습니다.");
            return;
        }

        // 테스트용 어셈블리 경로 설정 (HandMirrorMcp.dll 자체를 사용)
        _testAssemblyPath = Path.Combine(serverPath, "bin", "Debug", "net8.0", "HandMirrorMcp.dll");

        // 빌드된 DLL이 없으면 다른 경로 시도
        if (!File.Exists(_testAssemblyPath))
        {
            _testAssemblyPath = Path.Combine(serverPath, "bin", "Release", "net8.0", "HandMirrorMcp.dll");
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
    public async Task ListTools_ShouldIncludeAssemblyInspectorTools()
    {
        Assert.IsNotNull(_client);

        var tools = await _client.ListToolsAsync(cancellationToken: TestContext.CancellationToken);
        var toolList = tools.ToList();

        Assert.IsTrue(toolList.Any(t => t.Name == "inspect_assembly"));
        Assert.IsTrue(toolList.Any(t => t.Name == "list_namespaces"));
        Assert.IsTrue(toolList.Any(t => t.Name == "get_type_info"));
    }

    [TestMethod]
    public async Task ListPrompts_ShouldIncludeAssemblyInspectorPrompts()
    {
        Assert.IsNotNull(_client);

        var prompts = await _client.ListPromptsAsync(cancellationToken: TestContext.CancellationToken);
        var promptList = prompts.ToList();

        Assert.IsTrue(promptList.Any(p => p.Name == "analyze_assembly"));
        Assert.IsTrue(promptList.Any(p => p.Name == "compare_assemblies"));
    }

    [TestMethod]
    public async Task InspectAssembly_WithValidPath_ShouldReturnAssemblyInfo()
    {
        Assert.IsNotNull(_client);

        if (string.IsNullOrEmpty(_testAssemblyPath) || !File.Exists(_testAssemblyPath))
        {
            Assert.Inconclusive("Test assembly not found. Please build the HandMirrorMcp project first.");
            return;
        }

        var result = await _client.CallToolAsync("inspect_assembly", new Dictionary<string, object?>
        {
            ["assemblyPath"] = _testAssemblyPath
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("Assembly:", textContent.Text);
        Assert.Contains("HandMirrorMcp", textContent.Text);
        Assert.Contains("Namespace:", textContent.Text);
    }

    [TestMethod]
    public async Task InspectAssembly_WithInvalidPath_ShouldReturnError()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("inspect_assembly", new Dictionary<string, object?>
        {
            ["assemblyPath"] = "/nonexistent/path/assembly.dll"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.Contains("Error:", textContent.Text);
    }

    [TestMethod]
    public async Task InspectAssembly_WithXmlDocDisabled_ShouldReturnInfo()
    {
        Assert.IsNotNull(_client);

        if (string.IsNullOrEmpty(_testAssemblyPath) || !File.Exists(_testAssemblyPath))
        {
            Assert.Inconclusive("Test assembly not found. Please build the HandMirrorMcp project first.");
            return;
        }

        var result = await _client.CallToolAsync("inspect_assembly", new Dictionary<string, object?>
        {
            ["assemblyPath"] = _testAssemblyPath,
            ["includeXmlDoc"] = false
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("Assembly:", textContent.Text);
    }

    [TestMethod]
    public async Task ListNamespaces_WithValidPath_ShouldReturnNamespaces()
    {
        Assert.IsNotNull(_client);

        if (string.IsNullOrEmpty(_testAssemblyPath) || !File.Exists(_testAssemblyPath))
        {
            Assert.Inconclusive("Test assembly not found. Please build the HandMirrorMcp project first.");
            return;
        }

        var result = await _client.CallToolAsync("list_namespaces", new Dictionary<string, object?>
        {
            ["assemblyPath"] = _testAssemblyPath
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("Namespaces in", textContent.Text);
        Assert.Contains("HandMirrorMcp", textContent.Text);
    }

    [TestMethod]
    public async Task ListNamespaces_WithInvalidPath_ShouldReturnError()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("list_namespaces", new Dictionary<string, object?>
        {
            ["assemblyPath"] = "/nonexistent/path/assembly.dll"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.Contains("Error:", textContent.Text);
    }

    [TestMethod]
    public async Task GetTypeInfo_WithValidType_ShouldReturnTypeDetails()
    {
        Assert.IsNotNull(_client);

        if (string.IsNullOrEmpty(_testAssemblyPath) || !File.Exists(_testAssemblyPath))
        {
            Assert.Inconclusive("Test assembly not found. Please build the HandMirrorMcp project first.");
            return;
        }

        var result = await _client.CallToolAsync("get_type_info", new Dictionary<string, object?>
        {
            ["assemblyPath"] = _testAssemblyPath,
            ["typeName"] = "HandMirrorMcp.Tools.AssemblyInspectorTool"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("AssemblyInspectorTool", textContent.Text);
        Assert.Contains("[class]", textContent.Text);
    }

    [TestMethod]
    public async Task GetTypeInfo_WithInvalidType_ShouldReturnError()
    {
        Assert.IsNotNull(_client);

        if (string.IsNullOrEmpty(_testAssemblyPath) || !File.Exists(_testAssemblyPath))
        {
            Assert.Inconclusive("Test assembly not found. Please build the HandMirrorMcp project first.");
            return;
        }

        var result = await _client.CallToolAsync("get_type_info", new Dictionary<string, object?>
        {
            ["assemblyPath"] = _testAssemblyPath,
            ["typeName"] = "NonExistent.Type.Name"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.Contains("Error:", textContent.Text);
        Assert.Contains("not found", textContent.Text);
    }

    [TestMethod]
    public async Task GetTypeInfo_WithXmlDocEnabled_ShouldIncludeDocumentation()
    {
        Assert.IsNotNull(_client);

        if (string.IsNullOrEmpty(_testAssemblyPath) || !File.Exists(_testAssemblyPath))
        {
            Assert.Inconclusive("Test assembly not found. Please build the HandMirrorMcp project first.");
            return;
        }

        var result = await _client.CallToolAsync("get_type_info", new Dictionary<string, object?>
        {
            ["assemblyPath"] = _testAssemblyPath,
            ["typeName"] = "HandMirrorMcp.Constants.Emoji",
            ["includeXmlDoc"] = true
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("Emoji", textContent.Text);
    }
}
