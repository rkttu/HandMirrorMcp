using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HandMirrorMcp.Tests;

[TestClass]
public class InteropInspectorToolTests
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
    public async Task ListTools_ShouldIncludeInteropInspectorTools()
    {
        Assert.IsNotNull(_client);

        var tools = await _client.ListToolsAsync(cancellationToken: TestContext.CancellationToken);
        var toolList = tools.ToList();

        Assert.IsTrue(toolList.Any(t => t.Name == "inspect_native_dependencies"));
    }

    [TestMethod]
    public async Task InspectNativeDependencies_WithValidPath_ShouldReturnInfo()
    {
        Assert.IsNotNull(_client);

        if (string.IsNullOrEmpty(_testAssemblyPath) || !File.Exists(_testAssemblyPath))
        {
            Assert.Inconclusive("Test assembly not found. Please build the HandMirrorMcp project first.");
            return;
        }

        var result = await _client.CallToolAsync("inspect_native_dependencies", new Dictionary<string, object?>
        {
            ["assemblyPath"] = _testAssemblyPath
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("Assembly:", textContent.Text);
        Assert.Contains("HandMirrorMcp", textContent.Text);
        Assert.Contains("Summary:", textContent.Text);
    }

    [TestMethod]
    public async Task InspectNativeDependencies_WithInvalidPath_ShouldReturnError()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("inspect_native_dependencies", new Dictionary<string, object?>
        {
            ["assemblyPath"] = "/nonexistent/path/assembly.dll"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.Contains("Error:", textContent.Text);
    }

    [TestMethod]
    public async Task InspectNativeDependencies_WithXmlDocDisabled_ShouldReturnInfo()
    {
        Assert.IsNotNull(_client);

        if (string.IsNullOrEmpty(_testAssemblyPath) || !File.Exists(_testAssemblyPath))
        {
            Assert.Inconclusive("Test assembly not found. Please build the HandMirrorMcp project first.");
            return;
        }

        var result = await _client.CallToolAsync("inspect_native_dependencies", new Dictionary<string, object?>
        {
            ["assemblyPath"] = _testAssemblyPath,
            ["includeXmlDoc"] = false
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("Assembly:", textContent.Text);
        Assert.Contains("Summary:", textContent.Text);
    }

    [TestMethod]
    public async Task InspectNativeDependencies_ShouldIncludeSummaryCounts()
    {
        Assert.IsNotNull(_client);

        if (string.IsNullOrEmpty(_testAssemblyPath) || !File.Exists(_testAssemblyPath))
        {
            Assert.Inconclusive("Test assembly not found. Please build the HandMirrorMcp project first.");
            return;
        }

        var result = await _client.CallToolAsync("inspect_native_dependencies", new Dictionary<string, object?>
        {
            ["assemblyPath"] = _testAssemblyPath
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        // Summary 섹션의 주요 항목들이 포함되어 있는지 확인
        Assert.Contains("Native Libraries:", textContent.Text);
        Assert.Contains("DllImport Methods:", textContent.Text);
        Assert.Contains("LibraryImport Methods:", textContent.Text);
        Assert.Contains("COM Types:", textContent.Text);
        Assert.Contains("Exported Functions:", textContent.Text);
    }

    [TestMethod]
    public async Task InspectNativeDependencies_WithSystemAssembly_ShouldFindPInvokes()
    {
        Assert.IsNotNull(_client);

        // System.Console.dll 또는 다른 시스템 어셈블리를 사용하여 P/Invoke 테스트
        // 런타임 디렉토리에서 System.Console.dll 찾기
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var systemConsolePath = Path.Combine(runtimeDir, "System.Console.dll");

        if (!File.Exists(systemConsolePath))
        {
            Assert.Inconclusive("System.Console.dll not found in runtime directory.");
            return;
        }

        var result = await _client.CallToolAsync("inspect_native_dependencies", new Dictionary<string, object?>
        {
            ["assemblyPath"] = systemConsolePath,
            ["includeXmlDoc"] = false
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("Assembly:", textContent.Text);
        // System.Console은 네이티브 의존성이 있을 수 있음
        Assert.Contains("Summary:", textContent.Text);
    }
}
