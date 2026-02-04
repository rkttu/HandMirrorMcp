using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HandMirrorMcp.Tests;

[TestClass]
public class SystemInfoToolTests
{
    private static McpClient? _client;

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

        var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = ["run", "--project", serverPath],
            Name = "HandMirror"
        });

        _client = await McpClient.CreateAsync(clientTransport);
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
    public async Task ListTools_ShouldIncludeSystemInfoTools()
    {
        Assert.IsNotNull(_client);

        var tools = await _client.ListToolsAsync();
        var toolList = tools.ToList();

        Assert.IsTrue(toolList.Any(t => t.Name == "get_system_info"));
        Assert.IsTrue(toolList.Any(t => t.Name == "get_dotnet_info"));
    }

    [TestMethod]
    public async Task GetSystemInfo_ShouldReturnSystemDetails()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("get_system_info", new Dictionary<string, object?>());

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        // Verify key sections are present
        Assert.Contains("Operating System:", textContent.Text);
        Assert.Contains(".NET Runtime:", textContent.Text);
        Assert.Contains("Hardware:", textContent.Text);
        Assert.Contains("Processor Count:", textContent.Text);
    }

    [TestMethod]
    public async Task GetSystemInfo_WithEnvironmentVariables_ShouldIncludePath()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("get_system_info", new Dictionary<string, object?>
        {
            ["includeEnvironmentVariables"] = true
        });

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("Key Environment Variables:", textContent.Text);
        Assert.Contains("PATH:", textContent.Text);
    }

    [TestMethod]
    public async Task GetDotNetInfo_ShouldReturnDotNetDetails()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("get_dotnet_info", new Dictionary<string, object?>());

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains(".NET Installation Information", textContent.Text);
        // dotnet CLI should be available since we're running the MCP server with dotnet
        Assert.Contains("dotnet CLI:", textContent.Text);
    }
}
