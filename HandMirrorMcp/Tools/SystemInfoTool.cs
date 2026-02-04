using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ModelContextProtocol.Server;
using HandMirrorMcp.Constants;

namespace HandMirrorMcp.Tools;

[McpServerToolType]
public sealed class SystemInfoTool
{
    [McpServerTool(Name = "get_system_info")]
    [Description("Gets information about the system where the MCP server is running. Includes OS, .NET runtime, hardware, and environment details. No admin privileges required.")]
    public string GetSystemInfo(
        [Description("Include environment variables in the output (default: false)")]
        bool includeEnvironmentVariables = false,
        [Description("Include installed .NET SDKs and runtimes if detectable (default: true)")]
        bool includeDotNetInfo = true)
    {
        var sb = new StringBuilder();
        sb.AppendLine("System Information");
        sb.AppendLine(new string('=', 80));

        // OS Information
        sb.AppendLine();
        sb.AppendLine(Emoji.Desktop + $" Operating System:");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"  OS: {RuntimeInformation.OSDescription}");
        sb.AppendLine($"  Platform: {GetPlatformName()}");
        sb.AppendLine($"  Architecture: {RuntimeInformation.OSArchitecture}");
        sb.AppendLine($"  64-bit OS: {Environment.Is64BitOperatingSystem}");

        if (OperatingSystem.IsWindows())
        {
            sb.AppendLine($"  Windows Version: {Environment.OSVersion.Version}");
        }
        else if (OperatingSystem.IsLinux())
        {
            var distroInfo = GetLinuxDistroInfo();
            if (!string.IsNullOrEmpty(distroInfo))
            {
                sb.AppendLine($"  Distribution: {distroInfo}");
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            sb.AppendLine($"  macOS Version: {Environment.OSVersion.Version}");
        }

        // .NET Runtime Information
        sb.AppendLine();
        sb.AppendLine(Emoji.Gear + $" .NET Runtime:");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"  Runtime: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"  Runtime Identifier: {RuntimeInformation.RuntimeIdentifier}");
        sb.AppendLine($"  Process Architecture: {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"  64-bit Process: {Environment.Is64BitProcess}");
        sb.AppendLine($"  CLR Version: {Environment.Version}");

        if (includeDotNetInfo)
        {
            var sdkInfo = GetDotNetSdkInfo();
            if (!string.IsNullOrEmpty(sdkInfo))
            {
                sb.AppendLine();
                sb.AppendLine("  Installed SDKs:");
                sb.AppendLine(sdkInfo);
            }
        }

        // Hardware Information
        sb.AppendLine();
        sb.AppendLine(Emoji.Computer + $" Hardware:");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"  Machine Name: {Environment.MachineName}");
        sb.AppendLine($"  Processor Count: {Environment.ProcessorCount}");

        // Memory (GC managed heap info)
        var gcInfo = GC.GetGCMemoryInfo();
        sb.AppendLine($"  Total Available Memory: {FormatBytes(gcInfo.TotalAvailableMemoryBytes)}");
        sb.AppendLine($"  GC Heap Size: {FormatBytes(GC.GetTotalMemory(false))}");
        sb.AppendLine($"  High Memory Load Threshold: {gcInfo.HighMemoryLoadThresholdBytes / (1024.0 * 1024.0 * 1024.0):F2} GB");

        // User & Process Information
        sb.AppendLine();
        sb.AppendLine("👤 User & Process:");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"  User Name: {Environment.UserName}");
        sb.AppendLine($"  User Domain: {Environment.UserDomainName}");
        sb.AppendLine($"  Interactive: {Environment.UserInteractive}");
        sb.AppendLine($"  Process ID: {Environment.ProcessId}");
        sb.AppendLine($"  Current Directory: {Environment.CurrentDirectory}");

        // Paths
        sb.AppendLine();
        sb.AppendLine(Emoji.Folder + $" System Paths:");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"  Temp Path: {Path.GetTempPath()}");
        sb.AppendLine($"  User Profile: {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}");
        sb.AppendLine($"  App Data: {Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}");
        sb.AppendLine($"  Local App Data: {Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}");

        if (OperatingSystem.IsWindows())
        {
            sb.AppendLine($"  Program Files: {Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}");
            sb.AppendLine($"  System Directory: {Environment.SystemDirectory}");
        }

        // Culture Information
        sb.AppendLine();
        sb.AppendLine("🌐 Locale:");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"  Current Culture: {CultureInfo.CurrentCulture.Name} ({CultureInfo.CurrentCulture.DisplayName})");
        sb.AppendLine($"  UI Culture: {CultureInfo.CurrentUICulture.Name} ({CultureInfo.CurrentUICulture.DisplayName})");
        sb.AppendLine($"  Time Zone: {TimeZoneInfo.Local.DisplayName}");
        sb.AppendLine($"  UTC Offset: {TimeZoneInfo.Local.BaseUtcOffset}");

        // Environment Variables (optional)
        if (includeEnvironmentVariables)
        {
            sb.AppendLine();
            sb.AppendLine(Emoji.Wrench + $" Key Environment Variables:");
            sb.AppendLine(new string('-', 60));

            var importantVars = new[]
            {
                "PATH", "DOTNET_ROOT", "DOTNET_HOST_PATH",
                "NUGET_PACKAGES", "USERPROFILE", "HOME",
                "LANG", "LC_ALL", "TERM", "SHELL",
                "MSBuildSDKsPath", "DOTNET_CLI_TELEMETRY_OPTOUT"
            };

            foreach (var varName in importantVars)
            {
                var value = Environment.GetEnvironmentVariable(varName);
                if (!string.IsNullOrEmpty(value))
                {
                    if (varName == "PATH")
                    {
                        var paths = value.Split(Path.PathSeparator);
                        sb.AppendLine($"  {varName}: ({paths.Length} entries)");
                        foreach (var path in paths.Take(10))
                        {
                            sb.AppendLine($"    - {path}");
                        }
                        if (paths.Length > 10)
                        {
                            sb.AppendLine($"    ... and {paths.Length - 10} more");
                        }
                    }
                    else
                    {
                        var displayValue = value.Length > 100 ? value[..100] + "..." : value;
                        sb.AppendLine($"  {varName}: {displayValue}");
                    }
                }
            }
        }

        // Feature Detection
        sb.AppendLine();
        sb.AppendLine(Emoji.MagnifyingGlass + $" Feature Detection:");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"  Container: {IsRunningInContainer()}");
        sb.AppendLine($"  WSL: {IsRunningInWsl()}");
        sb.AppendLine($"  CI Environment: {IsRunningInCi()}");

        // Capabilities relevant for .NET development
        sb.AppendLine();
        sb.AppendLine(Emoji.HammerAndWrench + $" .NET Development Capabilities:");
        sb.AppendLine(new string('-', 60));

        var dotnetPath = GetDotNetExecutablePath();
        sb.AppendLine($"  dotnet CLI: {(dotnetPath != null ? Emoji.CheckMark + " Available" : Emoji.CrossMark + " Not found in PATH")}");
        if (dotnetPath != null)
        {
            sb.AppendLine($"    Path: {dotnetPath}");
        }

        var nugetCachePath = GetNuGetCachePath();
        sb.AppendLine($"  NuGet Cache: {nugetCachePath}");

        return sb.ToString();
    }

    [McpServerTool(Name = "get_dotnet_info")]
    [Description("Gets detailed information about installed .NET SDKs and runtimes on the system.")]
    public async Task<string> GetDotNetInfo(CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine(".NET Installation Information");
        sb.AppendLine(new string('=', 80));

        var dotnetPath = GetDotNetExecutablePath();
        if (dotnetPath == null)
        {
            sb.AppendLine();
            sb.AppendLine(Emoji.CrossMark + $" dotnet CLI not found in PATH.");
            sb.AppendLine();
            sb.AppendLine("To install .NET, visit: https://dot.net/download");
            return sb.ToString();
        }

        sb.AppendLine($"dotnet CLI: {dotnetPath}");
        sb.AppendLine();

        // Run dotnet --list-sdks
        try
        {
            var sdksOutput = await RunDotNetCommandAsync("--list-sdks", cancellationToken);
            sb.AppendLine(Emoji.Package + $" Installed SDKs:");
            sb.AppendLine(new string('-', 60));
            if (string.IsNullOrWhiteSpace(sdksOutput))
            {
                sb.AppendLine("  No SDKs installed.");
            }
            else
            {
                foreach (var line in sdksOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    sb.AppendLine($"  {line.Trim()}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  Error listing SDKs: {ex.Message}");
        }

        sb.AppendLine();

        // Run dotnet --list-runtimes
        try
        {
            var runtimesOutput = await RunDotNetCommandAsync("--list-runtimes", cancellationToken);
            sb.AppendLine(Emoji.Gear + $" Installed Runtimes:");
            sb.AppendLine(new string('-', 60));
            if (string.IsNullOrWhiteSpace(runtimesOutput))
            {
                sb.AppendLine("  No runtimes installed.");
            }
            else
            {
                var runtimes = runtimesOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .GroupBy(l => l.Split(' ')[0])
                    .ToList();

                foreach (var group in runtimes)
                {
                    sb.AppendLine($"  [{group.Key}]");
                    foreach (var runtime in group.OrderByDescending(r => r))
                    {
                        var parts = runtime.Split(' ', 2);
                        if (parts.Length >= 2)
                        {
                            sb.AppendLine($"    - {parts[1]}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  Error listing runtimes: {ex.Message}");
        }

        sb.AppendLine();

        // Run dotnet --info for additional details
        try
        {
            var infoOutput = await RunDotNetCommandAsync("--info", cancellationToken);
            
            // Extract host and commit info
            var lines = infoOutput.Split('\n');
            var hostSection = false;

            sb.AppendLine(Emoji.Info + $" Host Information:");
            sb.AppendLine(new string('-', 60));

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Host"))
                {
                    hostSection = true;
                    continue;
                }
                if (hostSection && string.IsNullOrWhiteSpace(trimmed))
                {
                    break;
                }
                if (hostSection && trimmed.Contains(':'))
                {
                    sb.AppendLine($"  {trimmed}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  Error getting host info: {ex.Message}");
        }

        return sb.ToString();
    }

    private static string GetPlatformName()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsLinux()) return "Linux";
        if (OperatingSystem.IsMacOS()) return "macOS";
        if (OperatingSystem.IsFreeBSD()) return "FreeBSD";
        if (OperatingSystem.IsAndroid()) return "Android";
        if (OperatingSystem.IsIOS()) return "iOS";
        if (OperatingSystem.IsBrowser()) return "Browser (WASM)";
        return "Unknown";
    }

    private static string? GetLinuxDistroInfo()
    {
        try
        {
            if (File.Exists("/etc/os-release"))
            {
                var lines = File.ReadAllLines("/etc/os-release");
                var prettyName = lines.FirstOrDefault(l => l.StartsWith("PRETTY_NAME="));
                if (prettyName != null)
                {
                    return prettyName.Split('=', 2)[1].Trim('"');
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        return null;
    }

    private static string? GetDotNetSdkInfo()
    {
        try
        {
            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (string.IsNullOrEmpty(dotnetRoot))
            {
                if (OperatingSystem.IsWindows())
                {
                    dotnetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
                }
                else
                {
                    dotnetRoot = "/usr/share/dotnet";
                    if (!Directory.Exists(dotnetRoot))
                    {
                        dotnetRoot = "/usr/local/share/dotnet";
                    }
                }
            }

            var sdkPath = Path.Combine(dotnetRoot, "sdk");
            if (Directory.Exists(sdkPath))
            {
                var sdks = Directory.GetDirectories(sdkPath)
                    .Select(Path.GetFileName)
                    .Where(n => n != null && char.IsDigit(n[0]))
                    .OrderByDescending(v => v)
                    .Take(5)
                    .ToList();

                if (sdks.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var sdk in sdks)
                    {
                        sb.AppendLine($"    - {sdk}");
                    }
                    return sb.ToString().TrimEnd();
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        return null;
    }

    private static string? GetDotNetExecutablePath()
    {
        var fileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var pathEnv = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(pathEnv))
            return null;

        foreach (var path in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var fullPath = Path.Combine(path, fileName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
                // Skip invalid paths
            }
        }

        return null;
    }

    private static string GetNuGetCachePath()
    {
        var nugetPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(nugetPackages))
        {
            return nugetPackages;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".nuget", "packages");
    }

    private static string IsRunningInContainer()
    {
        // Check for Docker/container indicators
        if (File.Exists("/.dockerenv"))
            return "Yes (Docker)";

        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            return "Yes";

        if (File.Exists("/proc/1/cgroup"))
        {
            try
            {
                var content = File.ReadAllText("/proc/1/cgroup");
                if (content.Contains("docker") || content.Contains("kubepods") || content.Contains("containerd"))
                    return "Yes (detected via cgroup)";
            }
            catch { }
        }

        return "No";
    }

    private static string IsRunningInWsl()
    {
        if (!OperatingSystem.IsLinux())
            return "N/A";

        try
        {
            if (File.Exists("/proc/version"))
            {
                var version = File.ReadAllText("/proc/version");
                if (version.Contains("microsoft", StringComparison.OrdinalIgnoreCase) ||
                    version.Contains("WSL", StringComparison.OrdinalIgnoreCase))
                {
                    return version.Contains("WSL2", StringComparison.OrdinalIgnoreCase) ? "Yes (WSL2)" : "Yes (WSL1)";
                }
            }
        }
        catch { }

        return "No";
    }

    private static string IsRunningInCi()
    {
        // Common CI environment variables
        if (Environment.GetEnvironmentVariable("CI") == "true")
            return "Yes";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
            return "Yes (GitHub Actions)";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_PIPELINES")))
            return "Yes (Azure Pipelines)";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")))
            return "Yes (Azure DevOps)";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JENKINS_URL")))
            return "Yes (Jenkins)";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITLAB_CI")))
            return "Yes (GitLab CI)";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRAVIS")))
            return "Yes (Travis CI)";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CIRCLECI")))
            return "Yes (CircleCI)";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TEAMCITY_VERSION")))
            return "Yes (TeamCity)";

        return "No";
    }

    private static async Task<string> RunDotNetCommandAsync(string arguments, CancellationToken cancellationToken)
    {
        var fileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return output;
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
            _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
        };
    }
}


