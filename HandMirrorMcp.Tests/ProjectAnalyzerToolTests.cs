using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HandMirrorMcp.Tests;

[TestClass]
public class ProjectAnalyzerToolTests
{
    private static McpClient? _client;
    private static string? _testProjectPath;
    private static string? _testSolutionPath;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        var serverPath = FindProjectPath("HandMirrorMcp");

        if (string.IsNullOrEmpty(serverPath))
        {
            Assert.Fail("HandMirrorMcp 프로젝트를 찾을 수 없습니다.");
            return;
        }

        _testProjectPath = Path.Combine(serverPath, "HandMirrorMcp.csproj");
        _testSolutionPath = FindSolutionPath(serverPath);

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

    private static string? FindSolutionPath(string projectPath)
    {
        var directory = new DirectoryInfo(projectPath);

        while (directory != null)
        {
            var slnFiles = directory.GetFiles("*.sln").Concat(directory.GetFiles("*.slnx")).ToList();
            if (slnFiles.Count > 0)
            {
                return slnFiles[0].FullName;
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
    public async Task ListTools_ShouldIncludeProjectAnalyzerTools()
    {
        Assert.IsNotNull(_client);

        var tools = await _client.ListToolsAsync(cancellationToken: TestContext.CancellationToken);
        var toolList = tools.ToList();

        Assert.IsTrue(toolList.Any(t => t.Name == "analyze_csproj"));
        Assert.IsTrue(toolList.Any(t => t.Name == "analyze_solution"));
        Assert.IsTrue(toolList.Any(t => t.Name == "explain_build_error"));
        Assert.IsTrue(toolList.Any(t => t.Name == "analyze_file_based_app"));
        Assert.IsTrue(toolList.Any(t => t.Name == "analyze_config_file"));
        Assert.IsTrue(toolList.Any(t => t.Name == "analyze_packages_config"));
    }

    [TestMethod]
    public async Task AnalyzeCsproj_ShouldReturnProjectDetails()
    {
        Assert.IsNotNull(_client);
        Assert.IsNotNull(_testProjectPath);

        var result = await _client.CallToolAsync("analyze_csproj", new Dictionary<string, object?>
        {
            ["csprojPath"] = _testProjectPath
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        // 주요 섹션이 포함되어 있는지 확인
        Assert.Contains("Project Analysis:", textContent.Text);
        Assert.Contains("Target Framework:", textContent.Text);
        Assert.Contains("Package References:", textContent.Text);
        Assert.Contains("Key Properties:", textContent.Text);
    }

    [TestMethod]
    public async Task AnalyzeCsproj_WithInvalidPath_ShouldReturnError()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("analyze_csproj", new Dictionary<string, object?>
        {
            ["csprojPath"] = "/nonexistent/path/project.csproj"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);
        Assert.Contains("Error:", textContent.Text);
    }

    [TestMethod]
    public async Task AnalyzeSolution_ShouldReturnSolutionDetails()
    {
        Assert.IsNotNull(_client);

        // 솔루션 파일이 없으면 테스트 건너뛰기
        if (string.IsNullOrEmpty(_testSolutionPath))
        {
            Assert.Inconclusive("Solution file not found for testing.");
            return;
        }

        var result = await _client.CallToolAsync("analyze_solution", new Dictionary<string, object?>
        {
            ["solutionPath"] = _testSolutionPath
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("Solution Analysis:", textContent.Text);
        Assert.Contains("Projects:", textContent.Text);
    }

    [TestMethod]
    public async Task ExplainBuildError_CS0246_ShouldReturnExplanation()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("explain_build_error", new Dictionary<string, object?>
        {
            ["errorCode"] = "CS0246"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("CS0246", textContent.Text);
        Assert.Contains("type or namespace", textContent.Text.ToLower());
        Assert.Contains("Solutions:", textContent.Text);
    }

    [TestMethod]
    public async Task ExplainBuildError_NU1605_ShouldReturnExplanation()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("explain_build_error", new Dictionary<string, object?>
        {
            ["errorCode"] = "NU1605"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("NU1605", textContent.Text);
        Assert.Contains("NuGet", textContent.Text);
        Assert.Contains("downgrade", textContent.Text.ToLower());
    }

    [TestMethod]
    public async Task ExplainBuildError_WithContext_ShouldAnalyzeErrorMessage()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("explain_build_error", new Dictionary<string, object?>
        {
            ["errorCode"] = "CS0246",
            ["errorMessage"] = "The type or namespace name 'JsonSerializer' could not be found"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("Context Analysis:", textContent.Text);
        Assert.Contains("JsonSerializer", textContent.Text);
    }

    [TestMethod]
    public async Task ExplainBuildError_NETSDK1045_ShouldReturnExplanation()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("explain_build_error", new Dictionary<string, object?>
        {
            ["errorCode"] = "NETSDK1045"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("NETSDK1045", textContent.Text);
        Assert.Contains(".NET SDK", textContent.Text);
    }

    [TestMethod]
    public async Task ExplainBuildError_UnknownError_ShouldProvideGuidance()
    {
        Assert.IsNotNull(_client);

        var result = await _client.CallToolAsync("explain_build_error", new Dictionary<string, object?>
        {
            ["errorCode"] = "CS9999"
        }, cancellationToken: TestContext.CancellationToken);

        Assert.IsNotNull(result);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.IsNotNull(textContent);

        Assert.Contains("CS9999", textContent.Text);
        Assert.Contains("Unknown error code", textContent.Text);
        Assert.Contains("C# compiler", textContent.Text);
    }

    [TestMethod]
    public async Task AnalyzeFileBasedApp_WithDirectives_ShouldReturnAnalysis()
    {
        Assert.IsNotNull(_client);

        // 테스트용 임시 파일 생성
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_app_{Guid.NewGuid():N}.cs");
        try
        {
            var content = """
                #!/usr/bin/env dotnet run
                #:sdk Microsoft.NET.Sdk.Web
                #:package Newtonsoft.Json@13.0.3
                #:package Serilog
                #:property Nullable=enable
                #:property LangVersion=preview

                Console.WriteLine("Hello, World!");
                """;

            await File.WriteAllTextAsync(tempFile, content, TestContext.CancellationToken);

            var result = await _client.CallToolAsync("analyze_file_based_app", new Dictionary<string, object?>
            {
                ["csFilePath"] = tempFile
            }, cancellationToken: TestContext.CancellationToken);

            Assert.IsNotNull(result);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.IsNotNull(textContent);

            Assert.Contains("File-Based App Analysis:", textContent.Text);
            Assert.Contains("Shebang:", textContent.Text);
            Assert.Contains("dotnet run", textContent.Text);
            Assert.Contains("SDK:", textContent.Text);
            Assert.Contains("Microsoft.NET.Sdk.Web", textContent.Text);
            Assert.Contains("Package References:", textContent.Text);
            Assert.Contains("Newtonsoft.Json", textContent.Text);
            Assert.Contains("@13.0.3", textContent.Text);
            Assert.Contains("Serilog", textContent.Text);
            Assert.Contains("Properties:", textContent.Text);
            Assert.Contains("Nullable: enable", textContent.Text);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [TestMethod]
    public async Task AnalyzeFileBasedApp_WithoutDirectives_ShouldIndicateRegularFile()
    {
        Assert.IsNotNull(_client);

        // 테스트용 임시 파일 생성 (지시문 없음)
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_regular_{Guid.NewGuid():N}.cs");
        try
        {
            var content = """
                using System;

                Console.WriteLine("Hello, World!");
                """;

            await File.WriteAllTextAsync(tempFile, content, TestContext.CancellationToken);

            var result = await _client.CallToolAsync("analyze_file_based_app", new Dictionary<string, object?>
            {
                ["csFilePath"] = tempFile
            }, cancellationToken: TestContext.CancellationToken);

            Assert.IsNotNull(result);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.IsNotNull(textContent);

            Assert.Contains("No file-based app directives found", textContent.Text);
            Assert.Contains("regular C# file", textContent.Text);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [TestMethod]
    public async Task AnalyzeFileBasedApp_WithProjectReference_ShouldAnalyze()
    {
        Assert.IsNotNull(_client);

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_projref_{Guid.NewGuid():N}.cs");
        try
        {
            var content = """
                #:project ../MyLib/MyLib.csproj
                #:package System.Text.Json

                Console.WriteLine("Hello!");
                """;

            await File.WriteAllTextAsync(tempFile, content, TestContext.CancellationToken);

            var result = await _client.CallToolAsync("analyze_file_based_app", new Dictionary<string, object?>
            {
                ["csFilePath"] = tempFile
            }, cancellationToken: TestContext.CancellationToken);

            Assert.IsNotNull(result);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.IsNotNull(textContent);

            Assert.Contains("Project References:", textContent.Text);
            Assert.Contains("MyLib.csproj", textContent.Text);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [TestMethod]
    public async Task AnalyzeConfigFile_WebConfig_ShouldReturnAnalysis()
    {
        Assert.IsNotNull(_client);

        var tempFile = Path.Combine(Path.GetTempPath(), "web.config");
        try
        {
            var content = """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <appSettings>
                    <add key="Setting1" value="Value1" />
                    <add key="ApiKey" value="secret123" />
                  </appSettings>
                  <connectionStrings>
                    <add name="DefaultConnection" connectionString="Server=localhost;Database=MyDb;Trusted_Connection=True;" providerName="System.Data.SqlClient" />
                  </connectionStrings>
                  <system.web>
                    <compilation debug="true" targetFramework="4.8" />
                    <authentication mode="Forms" />
                    <customErrors mode="Off" />
                  </system.web>
                </configuration>
                """;

            await File.WriteAllTextAsync(tempFile, content, TestContext.CancellationToken);

            var result = await _client.CallToolAsync("analyze_config_file", new Dictionary<string, object?>
            {
                ["configPath"] = tempFile
            }, cancellationToken: TestContext.CancellationToken);

            Assert.IsNotNull(result);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.IsNotNull(textContent);

            Assert.Contains("Configuration Analysis:", textContent.Text);
            Assert.Contains("ASP.NET/IIS Web Configuration", textContent.Text);
            Assert.Contains("App Settings:", textContent.Text);
            Assert.Contains("Connection Strings:", textContent.Text);
            Assert.Contains("DefaultConnection", textContent.Text);
            Assert.Contains("WARNINGS:", textContent.Text); // debug=true, customErrors=Off 경고
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [TestMethod]
    public async Task AnalyzeConfigFile_AppConfig_WithBindingRedirects_ShouldAnalyze()
    {
        Assert.IsNotNull(_client);

        var tempFile = Path.Combine(Path.GetTempPath(), "app.config");
        try
        {
            var content = """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <runtime>
                    <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
                      <dependentAssembly>
                        <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
                        <bindingRedirect oldVersion="0.0.0.0-13.0.0.0" newVersion="13.0.0.0" />
                      </dependentAssembly>
                      <dependentAssembly>
                        <assemblyIdentity name="System.Memory" publicKeyToken="cc7b13ffcd2ddd51" culture="neutral" />
                        <bindingRedirect oldVersion="0.0.0.0-4.0.1.2" newVersion="4.0.1.2" />
                      </dependentAssembly>
                    </assemblyBinding>
                  </runtime>
                </configuration>
                """;

            await File.WriteAllTextAsync(tempFile, content, TestContext.CancellationToken);

            var result = await _client.CallToolAsync("analyze_config_file", new Dictionary<string, object?>
            {
                ["configPath"] = tempFile
            }, cancellationToken: TestContext.CancellationToken);

            Assert.IsNotNull(result);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.IsNotNull(textContent);

            Assert.Contains("Assembly Binding Redirects:", textContent.Text);
            Assert.Contains("Newtonsoft.Json", textContent.Text);
            Assert.Contains("13.0.0.0", textContent.Text);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [TestMethod]
    public async Task AnalyzePackagesConfig_ShouldReturnPackageList()
    {
        Assert.IsNotNull(_client);

        var tempDir = Path.Combine(Path.GetTempPath(), $"pkgtest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "packages.config");

        try
        {
            var content = """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net48" />
                  <package id="EntityFramework" version="6.4.4" targetFramework="net48" />
                  <package id="log4net" version="2.0.15" targetFramework="net48" />
                  <package id="Microsoft.AspNet.Mvc" version="5.2.7" targetFramework="net48" />
                </packages>
                """;

            await File.WriteAllTextAsync(tempFile, content, TestContext.CancellationToken);

            var result = await _client.CallToolAsync("analyze_packages_config", new Dictionary<string, object?>
            {
                ["packagesConfigPath"] = tempFile
            }, cancellationToken: TestContext.CancellationToken);

            Assert.IsNotNull(result);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.IsNotNull(textContent);

            Assert.Contains("Packages.config Analysis:", textContent.Text);
            Assert.Contains("Legacy NuGet", textContent.Text);
            Assert.Contains("Newtonsoft.Json", textContent.Text);
            Assert.Contains("EntityFramework", textContent.Text);
            Assert.Contains("net48", textContent.Text);
            Assert.Contains("WARNINGS:", textContent.Text); // Legacy format 경고
            Assert.Contains("Migration Guide:", textContent.Text);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [TestMethod]
    public async Task AnalyzePackagesConfig_WithMultipleVersions_ShouldWarn()
    {
        Assert.IsNotNull(_client);

        var tempDir = Path.Combine(Path.GetTempPath(), $"pkgtest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "packages.config");

        try
        {
            var content = """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="Newtonsoft.Json" version="12.0.3" targetFramework="net45" />
                  <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net48" />
                </packages>
                """;

            await File.WriteAllTextAsync(tempFile, content, TestContext.CancellationToken);

            var result = await _client.CallToolAsync("analyze_packages_config", new Dictionary<string, object?>
            {
                ["packagesConfigPath"] = tempFile
            }, cancellationToken: TestContext.CancellationToken);

            Assert.IsNotNull(result);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.IsNotNull(textContent);

            Assert.Contains("Multiple Versions Detected:", textContent.Text);
            Assert.Contains("12.0.3", textContent.Text);
            Assert.Contains("13.0.3", textContent.Text);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    public TestContext TestContext { get; set; }
}
