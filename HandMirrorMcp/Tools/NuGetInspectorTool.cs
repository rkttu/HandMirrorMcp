using System.ComponentModel;
using System.Text;
using HandMirrorMcp.Services;
using ModelContextProtocol.Server;
using Mono.Cecil;
using NuGet.Versioning;
using HandMirrorMcp.Constants;

namespace HandMirrorMcp.Tools;

[McpServerToolType]
public sealed partial class NuGetInspectorTool : IDisposable
{
    private readonly NuGetService _nugetService;
    private readonly RepositoryService _repoService;
    private bool _disposed;

    public NuGetInspectorTool()
    {
        _nugetService = new NuGetService();
        _repoService = new RepositoryService();
    }


    [McpServerTool(Name = "list_nuget_sources")]
    [Description("Lists all configured NuGet package sources including private repositories")]
    public string ListNuGetSources()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Configured NuGet Package Sources:");
        sb.AppendLine(new string('=', 60));

        foreach (var source in _nugetService.PackageSources)
        {
            sb.AppendLine();
            sb.AppendLine($"  Name: {source.Name}");
            sb.AppendLine($"  URL: {source.Source}");
            sb.AppendLine($"  Authenticated: {source.Credentials != null}");
        }

        sb.AppendLine();
        sb.AppendLine($"Cache Directory: {_nugetService.CacheDirectory}");

        return sb.ToString();
    }

    [McpServerTool(Name = "search_nuget_packages")]
    [Description("Searches for NuGet packages across all configured sources")]
    public async Task<string> SearchNuGetPackages(
        [Description("The search term to find packages")]
        string searchTerm,
        [Description("Include prerelease versions (default: false)")]
        bool includePrerelease = false,
        [Description("Maximum number of results to return (default: 20)")]
        int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var packages = await _nugetService.SearchPackagesAsync(
                searchTerm, includePrerelease, 0, maxResults, cancellationToken);

            var packageList = packages.ToList();

            if (packageList.Count == 0)
            {
                return $"No packages found for search term: '{searchTerm}'";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {packageList.Count} package(s) for '{searchTerm}':");
            sb.AppendLine(new string('=', 60));

            foreach (var package in packageList)
            {
                sb.AppendLine();
                sb.AppendLine($"  [{package.Identity.Id}] v{package.Identity.Version}");
                sb.AppendLine($"    Authors: {package.Authors}");
                sb.AppendLine($"    Downloads: {package.DownloadCount:N0}");

                if (!string.IsNullOrEmpty(package.Description))
                {
                    var description = package.Description.Length > 200
                        ? package.Description[..200] + "..."
                        : package.Description;
                    sb.AppendLine($"    Description: {description}");
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error searching packages: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_nuget_package_versions")]
    [Description("Gets all available versions of a NuGet package")]
    public async Task<string> GetNuGetPackageVersions(
        [Description("The package ID (e.g., 'Newtonsoft.Json')")]
        string packageId,
        [Description("Include prerelease versions (default: false)")]
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var versions = await _nugetService.GetPackageVersionsAsync(
                packageId, includePrerelease, cancellationToken);

            var versionList = versions.ToList();

            if (versionList.Count == 0)
            {
                return $"No versions found for package: '{packageId}'";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Available versions for '{packageId}':");
            sb.AppendLine(new string('=', 60));

            foreach (var version in versionList.Take(50))
            {
                var prerelease = version.IsPrerelease ? " (prerelease)" : "";
                sb.AppendLine($"  - {version}{prerelease}");
            }

            if (versionList.Count > 50)
            {
                sb.AppendLine($"  ... and {versionList.Count - 50} more versions");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting package versions: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_nuget_package_info")]
    [Description("Gets detailed information about a NuGet package")]
    public async Task<string> GetNuGetPackageInfo(
        [Description("The package ID (e.g., 'Newtonsoft.Json')")]
        string packageId,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            NuGetVersion? nugetVersion = null;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out nugetVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
            }

            var metadata = await _nugetService.GetPackageMetadataAsync(
                packageId, nugetVersion, cancellationToken);

            if (metadata == null)
            {
                return $"Package not found: '{packageId}' {version ?? "(latest)"}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Package: {metadata.Identity.Id}");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine($"  Version: {metadata.Identity.Version}");
            sb.AppendLine($"  Authors: {metadata.Authors}");
            sb.AppendLine($"  Published: {metadata.Published?.ToString("yyyy-MM-dd") ?? "N/A"}");
            sb.AppendLine($"  Downloads: {metadata.DownloadCount:N0}");
            sb.AppendLine($"  License: {metadata.LicenseMetadata?.License ?? metadata.LicenseUrl?.ToString() ?? "N/A"}");
            sb.AppendLine($"  Project URL: {metadata.ProjectUrl?.ToString() ?? "N/A"}");

            if (!string.IsNullOrEmpty(metadata.Description))
            {
                sb.AppendLine();
                sb.AppendLine("Description:");
                sb.AppendLine($"  {metadata.Description}");
            }

            if (!string.IsNullOrEmpty(metadata.Tags))
            {
                sb.AppendLine();
                sb.AppendLine($"Tags: {metadata.Tags}");
            }

            var dependencies = metadata.DependencySets?.ToList();
            if (dependencies?.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Dependencies:");
                foreach (var depGroup in dependencies)
                {
                    sb.AppendLine($"  [{depGroup.TargetFramework.GetShortFolderName()}]");
                    foreach (var dep in depGroup.Packages)
                    {
                        sb.AppendLine($"    - {dep.Id} {dep.VersionRange}");
                    }
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting package info: {ex.Message}";
        }
    }

    [McpServerTool(Name = "inspect_nuget_package")]
    [Description("Downloads and inspects the assemblies in a NuGet package")]
    public async Task<string> InspectNuGetPackage(
        [Description("The package ID (e.g., 'Newtonsoft.Json')")]
        string packageId,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        [Description("Target framework to inspect (e.g., 'net8.0', 'netstandard2.0'). If not specified, the highest version is used.")]
        string? targetFramework = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine version
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }

            // Download package
            var downloadResult = await _nugetService.DownloadPackageAsync(
                packageId, packageVersion, cancellationToken);

            if (!downloadResult.IsSuccess)
            {
                return $"Failed to download package: '{packageId}' v{packageVersion}\n{downloadResult.Error}";
            }

            var packagePath = downloadResult.Path!;

            // Get assembly list
            var assemblies = await _nugetService.GetPackageAssembliesAsync(
                packagePath, targetFramework, cancellationToken);

            var assemblyList = assemblies.ToList();

            if (assemblyList.Count == 0)
            {
                return $"No assemblies found in package '{packageId}' v{packageVersion}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Package: {packageId} v{packageVersion}");
            sb.AppendLine($"Cache Path: {packagePath}");
            sb.AppendLine(new string('=', 80));

            foreach (var assemblyPath in assemblyList)
            {
                if (!File.Exists(assemblyPath))
                    continue;

                try
                {
                    sb.AppendLine();
                    sb.AppendLine($"Assembly: {Path.GetFileName(assemblyPath)}");
                    sb.AppendLine(new string('-', 60));

                    using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

                    // Assembly info
                    sb.AppendLine($"  Full Name: {assembly.FullName}");
                    sb.AppendLine($"  Runtime: {assembly.MainModule.RuntimeVersion}");

                    var targetFrameworkAttr = assembly.CustomAttributes
                        .FirstOrDefault(a => a.AttributeType.Name == "TargetFrameworkAttribute");
                    if (targetFrameworkAttr?.ConstructorArguments.Count > 0)
                    {
                        sb.AppendLine($"  Target Framework: {targetFrameworkAttr.ConstructorArguments[0].Value}");
                    }

                    // Namespace and public type count
                    var publicTypes = assembly.MainModule.Types
                        .Where(t => t.IsPublic)
                        .ToList();

                    var namespaces = publicTypes
                        .Select(t => t.Namespace ?? "<global>")
                        .Distinct()
                        .OrderBy(n => n)
                        .ToList();

                    sb.AppendLine($"  Public Types: {publicTypes.Count}");
                    sb.AppendLine($"  Namespaces: {namespaces.Count}");

                    sb.AppendLine();
                    sb.AppendLine("  Namespaces:");
                    foreach (var ns in namespaces)
                    {
                        var typeCount = publicTypes.Count(t => (t.Namespace ?? "<global>") == ns);
                        sb.AppendLine($"    - {ns} ({typeCount} types)");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  Error reading assembly: {ex.Message}");
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error inspecting package: {ex.Message}";
        }
    }

    [McpServerTool(Name = "inspect_nuget_package_type")]
    [Description("Inspects a specific type in a NuGet package assembly")]
    public async Task<string> InspectNuGetPackageType(
        [Description("The package ID (e.g., 'Newtonsoft.Json')")]
        string packageId,
        [Description("The full type name to inspect (e.g., 'Newtonsoft.Json.JsonConvert')")]
        string typeName,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        [Description("Target framework (e.g., 'net8.0'). If not specified, the highest version is used.")]
        string? targetFramework = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine version
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }

            // Download package
            var downloadResult = await _nugetService.DownloadPackageAsync(
                packageId, packageVersion, cancellationToken);

            if (!downloadResult.IsSuccess)
            {
                return $"Failed to download package: '{packageId}' v{packageVersion}\n{downloadResult.Error}";
            }

            var packagePath = downloadResult.Path!;

            // Find type in assemblies
            var assemblies = await _nugetService.GetPackageAssembliesAsync(
                packagePath, targetFramework, cancellationToken);

            foreach (var assemblyPath in assemblies)
            {
                if (!File.Exists(assemblyPath))
                    continue;

                try
                {
                    using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
                    var type = assembly.MainModule.Types.FirstOrDefault(t => t.FullName == typeName);

                    if (type != null)
                    {
                        // Reuse AssemblyInspectorTool logic
                        var tool = new AssemblyInspectorTool();
                        return tool.GetTypeInfo(assemblyPath, typeName);
                    }
                }
                catch
                {
                    // If assembly read fails, try next
                }
            }

            return $"Type '{typeName}' not found in package '{packageId}' v{packageVersion}";
        }
        catch (Exception ex)
        {
            return $"Error inspecting type: {ex.Message}";
        }
    }

    [McpServerTool(Name = "list_nuget_package_assemblies")]
    [Description("Lists all assemblies in a NuGet package organized by target framework and runtime")]
    public async Task<string> ListNuGetPackageAssemblies(
        [Description("The package ID (e.g., 'System.Text.Json')")]
        string packageId,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine version
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }

            // Download package
            var downloadResult = await _nugetService.DownloadPackageAsync(
                packageId, packageVersion, cancellationToken);

            if (!downloadResult.IsSuccess)
            {
                return $"Failed to download package: '{packageId}' v{packageVersion}\n{downloadResult.Error}";
            }

            var packagePath = downloadResult.Path!;

            // Get all assembly info
            var assemblyInfo = await _nugetService.GetAllPackageAssembliesAsync(
                packagePath, cancellationToken);

            if (!assemblyInfo.HasAnyAssemblies)
            {
                return $"No assemblies found in package '{packageId}' v{packageVersion}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Package: {packageId} v{packageVersion}");
            sb.AppendLine($"Cache Path: {packagePath}");
            sb.AppendLine(new string('=', 80));

            // lib/ assemblies (by TFM)
            if (assemblyInfo.LibAssemblies.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Package + " lib/ (Target Framework Assemblies)");
                sb.AppendLine(new string('-', 60));

                foreach (var (tfm, dlls) in assemblyInfo.LibAssemblies.OrderBy(x => x.Key))
                {
                    sb.AppendLine($"  [{tfm}]");
                    foreach (var dll in dlls)
                    {
                        var fileName = Path.GetFileName(dll);
                        var fileSize = File.Exists(dll) ? new FileInfo(dll).Length : 0;
                        sb.AppendLine($"    - {fileName} ({FormatFileSize(fileSize)})");
                    }
                }
            }

            // ref/ reference assemblies (by TFM)
            if (assemblyInfo.RefAssemblies.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Clipboard + " ref/ (Reference Assemblies)");
                sb.AppendLine(new string('-', 60));

                foreach (var (tfm, dlls) in assemblyInfo.RefAssemblies.OrderBy(x => x.Key))
                {
                    sb.AppendLine($"  [{tfm}]");
                    foreach (var dll in dlls)
                    {
                        var fileName = Path.GetFileName(dll);
                        sb.AppendLine($"    - {fileName}");
                    }
                }
            }

            // runtimes/ assemblies (by RID)
            if (assemblyInfo.RuntimeAssemblies.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Desktop + " runtimes/ (Platform-Specific Assemblies)");
                sb.AppendLine(new string('-', 60));

                foreach (var (key, dlls) in assemblyInfo.RuntimeAssemblies.OrderBy(x => x.Key))
                {
                    sb.AppendLine($"  [{key}]");
                    foreach (var dll in dlls)
                    {
                        var fileName = Path.GetFileName(dll);
                        var fileSize = File.Exists(dll) ? new FileInfo(dll).Length : 0;
                        sb.AppendLine($"    - {fileName} ({FormatFileSize(fileSize)})");
                    }
                }
            }

            // Summary
            sb.AppendLine();
            sb.AppendLine("Summary:");
            sb.AppendLine($"  Target Frameworks: {assemblyInfo.LibAssemblies.Count}");
            sb.AppendLine($"  Reference Assemblies: {assemblyInfo.RefAssemblies.Count} TFMs");
            sb.AppendLine($"  Runtime-Specific: {assemblyInfo.RuntimeAssemblies.Count} RID/TFM combinations");

            var totalAssemblies = assemblyInfo.LibAssemblies.Values.Sum(x => x.Count)
                + assemblyInfo.RefAssemblies.Values.Sum(x => x.Count)
                + assemblyInfo.RuntimeAssemblies.Values.Sum(x => x.Count);
            sb.AppendLine($"  Total Assemblies: {totalAssemblies}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error listing package assemblies: {ex.Message}";
        }
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
        };
    }

    [McpServerTool(Name = "inspect_nuget_native_libs")]
    [Description("Analyzes native libraries included in a NuGet package (runtimes folder, native dependencies)")]
    public async Task<string> InspectNuGetNativeLibraries(
        [Description("The package ID (e.g., 'SkiaSharp', 'SQLitePCLRaw.lib.e_sqlite3')")]
        string packageId,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine version
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }

            // Download package
            var downloadResult = await _nugetService.DownloadPackageAsync(
                packageId, packageVersion, cancellationToken);

            if (!downloadResult.IsSuccess)
            {
                return $"Failed to download package: '{packageId}' v{packageVersion}\n{downloadResult.Error}";
            }

            var packagePath = downloadResult.Path!;

            var sb = new StringBuilder();
            sb.AppendLine($"Package: {packageId} v{packageVersion}");
            sb.AppendLine($"Cache Path: {packagePath}");
            sb.AppendLine(new string('=', 80));

            var nativeInfo = AnalyzeNativeLibraries(packagePath);

            // Native libraries (runtimes/{rid}/native/)
            if (nativeInfo.NativeLibraries.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Wrench + " Native Libraries (runtimes/*/native/)");
                sb.AppendLine(new string('-', 60));

                foreach (var (rid, libs) in nativeInfo.NativeLibraries.OrderBy(x => x.Key))
                {
                    sb.AppendLine($"  [{rid}]");
                    foreach (var lib in libs.OrderBy(l => l.FileName))
                    {
                        sb.AppendLine($"    - {lib.FileName} ({FormatFileSize(lib.FileSize)})");
                        if (!string.IsNullOrEmpty(lib.Architecture))
                        {
                            sb.AppendLine($"        Architecture: {lib.Architecture}");
                        }
                        sb.AppendLine($"        Path: {lib.RelativePath}");
                    }
                }
            }

            // Native libraries deployed with runtime-specific managed assemblies
            if (nativeInfo.RuntimeNativeLibraries.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Package + " Runtime-Specific Libraries (runtimes/*/lib/)");
                sb.AppendLine(new string('-', 60));

                foreach (var (key, libs) in nativeInfo.RuntimeNativeLibraries.OrderBy(x => x.Key))
                {
                    sb.AppendLine($"  [{key}]");
                    foreach (var lib in libs.OrderBy(l => l.FileName))
                    {
                        var managedOrNative = lib.IsManagedAssembly ? "managed" : "native";
                        sb.AppendLine($"    - {lib.FileName} ({FormatFileSize(lib.FileSize)}) [{managedOrNative}]");
                    }
                }
            }

            // Native in build/ folder (copied by MSBuild)
            if (nativeInfo.BuildNativeLibraries.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Hammer + " Build-Time Native Libraries (build/)");
                sb.AppendLine(new string('-', 60));

                foreach (var lib in nativeInfo.BuildNativeLibraries.OrderBy(l => l.RelativePath))
                {
                    sb.AppendLine($"  - {lib.FileName} ({FormatFileSize(lib.FileSize)})");
                    sb.AppendLine($"      Path: {lib.RelativePath}");
                }
            }

            // Native in contentFiles/
            if (nativeInfo.ContentNativeLibraries.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Folder + " Content Native Libraries (contentFiles/)");
                sb.AppendLine(new string('-', 60));

                foreach (var lib in nativeInfo.ContentNativeLibraries.OrderBy(l => l.RelativePath))
                {
                    sb.AppendLine($"  - {lib.FileName} ({FormatFileSize(lib.FileSize)})");
                    sb.AppendLine($"      Path: {lib.RelativePath}");
                }
            }

            // MSBuild props/targets files (may contain native copy logic)
            if (nativeInfo.BuildScripts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Scroll + " Build Scripts (may configure native library copying)");
                sb.AppendLine(new string('-', 60));

                foreach (var script in nativeInfo.BuildScripts.OrderBy(s => s))
                {
                    sb.AppendLine($"  - {script}");
                }
            }

            // Summary
            sb.AppendLine();
            sb.AppendLine("Summary:");

            var totalNativeLibs = nativeInfo.NativeLibraries.Values.Sum(l => l.Count)
                + nativeInfo.RuntimeNativeLibraries.Values.SelectMany(l => l).Count(l => !l.IsManagedAssembly)
                + nativeInfo.BuildNativeLibraries.Count
                + nativeInfo.ContentNativeLibraries.Count;

            var rids = nativeInfo.NativeLibraries.Keys
                .Concat(nativeInfo.RuntimeNativeLibraries.Keys.Select(k => k.Split('/')[0]))
                .Distinct()
                .ToList();

            sb.AppendLine($"  Total Native Libraries: {totalNativeLibs}");
            sb.AppendLine($"  Supported RIDs: {rids.Count}");

            if (rids.Count > 0)
            {
                sb.AppendLine($"  Platforms: {string.Join(", ", rids.Take(10))}");
                if (rids.Count > 10)
                {
                    sb.AppendLine($"             ... and {rids.Count - 10} more");
                }
            }

            // Platform classification
            var windowsRids = rids.Where(r => r.StartsWith("win", StringComparison.OrdinalIgnoreCase)).ToList();
            var linuxRids = rids.Where(r => r.StartsWith("linux", StringComparison.OrdinalIgnoreCase) ||
                                             r.StartsWith("ubuntu", StringComparison.OrdinalIgnoreCase) ||
                                             r.StartsWith("debian", StringComparison.OrdinalIgnoreCase) ||
                                             r.StartsWith("rhel", StringComparison.OrdinalIgnoreCase) ||
                                             r.StartsWith("alpine", StringComparison.OrdinalIgnoreCase)).ToList();
            var macRids = rids.Where(r => r.StartsWith("osx", StringComparison.OrdinalIgnoreCase) ||
                                           r.StartsWith("macos", StringComparison.OrdinalIgnoreCase)).ToList();
            var otherRids = rids.Except(windowsRids).Except(linuxRids).Except(macRids).ToList();

            if (windowsRids.Count > 0 || linuxRids.Count > 0 || macRids.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  Platform Support:");
                if (windowsRids.Count > 0) sb.AppendLine($"    Windows: {string.Join(", ", windowsRids)}");
                if (linuxRids.Count > 0) sb.AppendLine($"    Linux: {string.Join(", ", linuxRids)}");
                if (macRids.Count > 0) sb.AppendLine($"    macOS: {string.Join(", ", macRids)}");
                if (otherRids.Count > 0) sb.AppendLine($"    Other: {string.Join(", ", otherRids)}");
            }

            if (totalNativeLibs == 0 && nativeInfo.BuildScripts.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("  ⚠️ No native libraries found in this package.");
                sb.AppendLine("     This package may be a pure managed library or may depend on");
                sb.AppendLine("     other packages for native components.");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error analyzing native libraries: {ex.Message}";
        }
    }

    private static NativeLibraryAnalysisResult AnalyzeNativeLibraries(string packagePath)
    {
        var result = new NativeLibraryAnalysisResult();

        // Native library extensions
        var nativeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".so", ".dylib", ".a", ".lib", ".pdb", ".dbg", ".dSYM"
        };

        // Helper to check if managed assembly
        bool IsManagedAssembly(string filePath)
        {
            if (!filePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                using var stream = File.OpenRead(filePath);
                using var reader = new BinaryReader(stream);

                // Check DOS header
                if (reader.ReadUInt16() != 0x5A4D) // MZ
                    return false;

                stream.Seek(0x3C, SeekOrigin.Begin);
                var peHeaderOffset = reader.ReadInt32();

                stream.Seek(peHeaderOffset, SeekOrigin.Begin);
                if (reader.ReadUInt32() != 0x00004550) // PE\0\0
                    return false;

                // Skip COFF header
                stream.Seek(peHeaderOffset + 4 + 16, SeekOrigin.Begin);
                var optionalHeaderSize = reader.ReadUInt16();

                if (optionalHeaderSize == 0)
                    return false;

                // Move to Optional header
                stream.Seek(peHeaderOffset + 4 + 20, SeekOrigin.Begin);
                var magic = reader.ReadUInt16();

                // Check CLR header data directory
                int clrHeaderOffset = magic == 0x20B ? 208 : 192; // PE32+ vs PE32
                stream.Seek(peHeaderOffset + 4 + 20 + clrHeaderOffset, SeekOrigin.Begin);

                var clrHeaderRva = reader.ReadUInt32();
                return clrHeaderRva != 0;
            }
            catch
            {
                return false;
            }
        }

        // Extract architecture
        string GetArchitecture(string rid)
        {
            if (rid.Contains("x64") || rid.Contains("amd64")) return "x64";
            if (rid.Contains("x86") || rid.Contains("win32")) return "x86";
            if (rid.Contains("arm64") || rid.Contains("aarch64")) return "ARM64";
            if (rid.Contains("arm")) return "ARM";
            if (rid.Contains("musl")) return "musl";
            return "";
        }

        var allFiles = Directory.GetFiles(packagePath, "*", SearchOption.AllDirectories);

        foreach (var filePath in allFiles)
        {
            var relativePath = Path.GetRelativePath(packagePath, filePath);
            var fileName = Path.GetFileName(filePath);
            var extension = Path.GetExtension(filePath);
            var fileSize = new FileInfo(filePath).Length;

            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // runtimes/{rid}/native/
            if (parts.Length >= 4 &&
                parts[0].Equals("runtimes", StringComparison.OrdinalIgnoreCase) &&
                parts[2].Equals("native", StringComparison.OrdinalIgnoreCase))
            {
                var rid = parts[1];

                if (!result.NativeLibraries.TryGetValue(rid, out List<NativeLibInfo>? value))
                {
                    value = [];
                    result.NativeLibraries[rid] = value;
                }

                value.Add(new NativeLibInfo
                {
                    FileName = fileName,
                    RelativePath = relativePath,
                    FileSize = fileSize,
                    Architecture = GetArchitecture(rid),
                    IsManagedAssembly = false
                });
            }
            // runtimes/{rid}/lib/{tfm}/
            else if (parts.Length >= 5 &&
                     parts[0].Equals("runtimes", StringComparison.OrdinalIgnoreCase) &&
                     parts[2].Equals("lib", StringComparison.OrdinalIgnoreCase) &&
                     nativeExtensions.Contains(extension))
            {
                var rid = parts[1];
                var tfm = parts[3];
                var key = $"{rid}/{tfm}";

                if (!result.RuntimeNativeLibraries.TryGetValue(key, out List<NativeLibInfo>? value))
                {
                    value = [];
                    result.RuntimeNativeLibraries[key] = value;
                }

                value.Add(new NativeLibInfo
                {
                    FileName = fileName,
                    RelativePath = relativePath,
                    FileSize = fileSize,
                    Architecture = GetArchitecture(rid),
                    IsManagedAssembly = IsManagedAssembly(filePath)
                });
            }
            // build/ folder
            else if (parts[0].Equals("build", StringComparison.OrdinalIgnoreCase) ||
                     parts[0].Equals("buildTransitive", StringComparison.OrdinalIgnoreCase))
            {
                if (extension.Equals(".props", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".targets", StringComparison.OrdinalIgnoreCase))
                {
                    result.BuildScripts.Add(relativePath);
                }
                else if (nativeExtensions.Contains(extension) && !IsManagedAssembly(filePath))
                {
                    result.BuildNativeLibraries.Add(new NativeLibInfo
                    {
                        FileName = fileName,
                        RelativePath = relativePath,
                        FileSize = fileSize,
                        IsManagedAssembly = false
                    });
                }
            }
            // contentFiles/
            else if (parts[0].Equals("contentFiles", StringComparison.OrdinalIgnoreCase) &&
                     nativeExtensions.Contains(extension) &&
                     !IsManagedAssembly(filePath))
            {
                result.ContentNativeLibraries.Add(new NativeLibInfo
                {
                    FileName = fileName,
                    RelativePath = relativePath,
                    FileSize = fileSize,
                    IsManagedAssembly = false
                });
            }
        }

        return result;
    }

    private sealed class NativeLibraryAnalysisResult
    {
        // Native libraries in runtimes/{rid}/native/ folder
        public Dictionary<string, List<NativeLibInfo>> NativeLibraries { get; } = new(StringComparer.OrdinalIgnoreCase);

        // Libraries in runtimes/{rid}/lib/{tfm}/ folder
        public Dictionary<string, List<NativeLibInfo>> RuntimeNativeLibraries { get; } = new(StringComparer.OrdinalIgnoreCase);

        // Native libraries in build/ folder
        public List<NativeLibInfo> BuildNativeLibraries { get; } = [];

        // Native libraries in contentFiles/ folder
        public List<NativeLibInfo> ContentNativeLibraries { get; } = [];

        // MSBuild scripts (props/targets)
        public List<string> BuildScripts { get; } = [];
    }

    private sealed class NativeLibInfo
    {
        public required string FileName { get; init; }
        public required string RelativePath { get; init; }
        public long FileSize { get; init; }
        public string? Architecture { get; init; }
        public bool IsManagedAssembly { get; init; }
    }

    [McpServerTool(Name = "inspect_nuget_native_exports")]
    [Description("Analyzes exported functions from native DLLs in a NuGet package. Windows only.")]
    public async Task<string> InspectNuGetNativeExports(
        [Description("The package ID (e.g., 'SkiaSharp', 'SQLitePCLRaw.lib.e_sqlite3')")]
        string packageId,
        [Description("Runtime identifier to analyze (e.g., 'win-x64'). If not specified, analyzes all Windows RIDs.")]
        string? rid = null,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        [Description("Maximum number of exports per DLL to show (default: 50, 0 for all)")]
        int maxExportsPerDll = 50,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "Error: Native DLL export analysis is only supported on Windows.";
        }

        try
        {
            // Determine version
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }

            // Download package
            var downloadResult = await _nugetService.DownloadPackageAsync(
                packageId, packageVersion, cancellationToken);

            if (!downloadResult.IsSuccess)
            {
                return $"Failed to download package: '{packageId}' v{packageVersion}\n{downloadResult.Error}";
            }

            var packagePath = downloadResult.Path!;

            var sb = new StringBuilder();
            sb.AppendLine($"Package: {packageId} v{packageVersion}");
            sb.AppendLine($"Native DLL Export Analysis (Windows)");
            sb.AppendLine(new string('=', 80));

            // Find native DLLs
            var nativeDlls = new List<(string Path, string Rid)>();
            var runtimesPath = Path.Combine(packagePath, "runtimes");

            if (Directory.Exists(runtimesPath))
            {
                foreach (var ridDir in Directory.GetDirectories(runtimesPath))
                {
                    var ridName = Path.GetFileName(ridDir);

                    // RID filtering
                    if (!string.IsNullOrEmpty(rid) &&
                        !ridName.Equals(rid, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Analyze only Windows RID (PE format)
                    if (!ridName.StartsWith("win", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var nativePath = Path.Combine(ridDir, "native");
                    if (Directory.Exists(nativePath))
                    {
                        foreach (var dllPath in Directory.GetFiles(nativePath, "*.dll"))
                        {
                            nativeDlls.Add((dllPath, ridName));
                        }
                    }
                }
            }

            // Check build folder too
            var buildPath = Path.Combine(packagePath, "build");
            if (Directory.Exists(buildPath))
            {
                foreach (var dllPath in Directory.GetFiles(buildPath, "*.dll", SearchOption.AllDirectories))
                {
                    nativeDlls.Add((dllPath, "build"));
                }
            }

            if (nativeDlls.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("No Windows native DLLs found in this package.");
                if (!string.IsNullOrEmpty(rid))
                {
                    sb.AppendLine($"(Filtered by RID: {rid})");
                }
                return sb.ToString();
            }

            var totalExports = 0;
            var totalImportModules = 0;

            foreach (var (dllPath, dllRid) in nativeDlls.OrderBy(x => x.Rid).ThenBy(x => x.Path))
            {
                var peResult = PeAnalyzerService.AnalyzePeFile(dllPath);

                if (peResult == null)
                {
                    continue; // Managed assembly or invalid PE
                }

                sb.AppendLine();
                sb.AppendLine(Emoji.Package + $" [{dllRid}] {peResult.FileName}");
                sb.AppendLine(new string('-', 60));
                sb.AppendLine($"  Architecture: {peResult.Machine} ({(peResult.Is64Bit ? "64-bit" : "32-bit")})");
                sb.AppendLine($"  Exports: {peResult.Exports.Count}");
                sb.AppendLine($"  Import Modules: {peResult.Imports.Count}");

                totalExports += peResult.Exports.Count;
                totalImportModules += peResult.Imports.Count;

                // Display export functions
                if (peResult.Exports.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("  Exported Functions:");

                    var namedExports = peResult.Exports
                        .Where(e => e.Name != null)
                        .OrderBy(e => e.Name)
                        .ToList();

                    var displayCount = maxExportsPerDll > 0
                        ? Math.Min(maxExportsPerDll, namedExports.Count)
                        : namedExports.Count;

                    foreach (var export in namedExports.Take(displayCount))
                    {
                        sb.AppendLine($"    - {export.Name}");
                    }

                    var remaining = namedExports.Count - displayCount;
                    if (remaining > 0)
                    {
                        sb.AppendLine($"    ... and {remaining} more named exports");
                    }

                    var ordinalOnlyCount = peResult.Exports.Count(e => e.Name == null);
                    if (ordinalOnlyCount > 0)
                    {
                        sb.AppendLine($"    ({ordinalOnlyCount} ordinal-only exports)");
                    }
                }

                // Display only main import modules
                if (peResult.Imports.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("  Dependencies:");

                    foreach (var import in peResult.Imports.OrderBy(m => m.Name).Take(10))
                    {
                        sb.AppendLine($"    - {import.Name} ({import.Functions.Count} functions)");
                    }

                    if (peResult.Imports.Count > 10)
                    {
                        sb.AppendLine($"    ... and {peResult.Imports.Count - 10} more modules");
                    }
                }
            }

            // Summary
            sb.AppendLine();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("Summary:");
            sb.AppendLine($"  Native DLLs Analyzed: {nativeDlls.Count}");
            sb.AppendLine($"  Total Exported Functions: {totalExports}");
            sb.AppendLine($"  Total Import Dependencies: {totalImportModules}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error analyzing native exports: {ex.Message}";
        }
    }

    [McpServerTool(Name = "search_nuget_native_exports")]
    [Description("Searches for exported functions by pattern in native DLLs of a NuGet package. Windows only.")]
    public async Task<string> SearchNuGetNativeExports(
        [Description("The package ID (e.g., 'SkiaSharp')")]
        string packageId,
        [Description("Search pattern (case-insensitive, supports * wildcard)")]
        string pattern,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "Error: Native DLL export search is only supported on Windows.";
        }

        try
        {
            // Determine version
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }

            // Download package
            var downloadResult = await _nugetService.DownloadPackageAsync(
                packageId, packageVersion, cancellationToken);

            if (!downloadResult.IsSuccess)
            {
                return $"Failed to download package: '{packageId}' v{packageVersion}\n{downloadResult.Error}";
            }

            var packagePath = downloadResult.Path!;

            // Convert wildcard pattern to regex
            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            var regex = new System.Text.RegularExpressions.Regex(
                regexPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var sb = new StringBuilder();
            sb.AppendLine($"Searching for '{pattern}' in {packageId} v{packageVersion}");
            sb.AppendLine(new string('=', 60));

            var totalMatches = 0;

            // Find and search native DLLs
            var runtimesPath = Path.Combine(packagePath, "runtimes");

            if (Directory.Exists(runtimesPath))
            {
                foreach (var ridDir in Directory.GetDirectories(runtimesPath))
                {
                    var ridName = Path.GetFileName(ridDir);

                    // Windows RID only
                    if (!ridName.StartsWith("win", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var nativePath = Path.Combine(ridDir, "native");
                    if (!Directory.Exists(nativePath))
                    {
                        continue;
                    }

                    foreach (var dllPath in Directory.GetFiles(nativePath, "*.dll"))
                    {
                        var peResult = PeAnalyzerService.AnalyzePeFile(dllPath);
                        if (peResult == null) continue;

                        var matches = peResult.Exports
                            .Where(e => e.Name != null && regex.IsMatch(e.Name))
                            .OrderBy(e => e.Name)
                            .ToList();

                        if (matches.Count > 0)
                        {
                            sb.AppendLine();
                            sb.AppendLine(Emoji.Package + $" [{ridName}] {Path.GetFileName(dllPath)}");

                            foreach (var export in matches)
                            {
                                sb.AppendLine($"  [{export.Ordinal,4}] {export.Name}");
                            }

                            totalMatches += matches.Count;
                        }
                    }
                }
            }

            if (totalMatches == 0)
            {
                sb.AppendLine();
                sb.AppendLine("No matching exports found.");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine($"Total matches: {totalMatches}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error searching native exports: {ex.Message}";
        }
    }

    [McpServerTool(Name = "list_nuget_dev_files")]
    [Description("Lists development resource files in a NuGet package (C/C++ headers, IDL, TLB, DEF files). Useful for P/Invoke signature creation.")]
    public async Task<string> ListNuGetDevFiles(
        [Description("The package ID (e.g., 'SkiaSharp.NativeAssets.Win32')")]
        string packageId,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine version
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }

            // Download package
            var downloadResult = await _nugetService.DownloadPackageAsync(
                packageId, packageVersion, cancellationToken);

            if (!downloadResult.IsSuccess)
            {
                return $"Failed to download package: '{packageId}' v{packageVersion}\n{downloadResult.Error}";
            }

            var packagePath = downloadResult.Path!;

            var sb = new StringBuilder();
            sb.AppendLine($"Package: {packageId} v{packageVersion}");
            sb.AppendLine("Development Resource Files");
            sb.AppendLine(new string('=', 80));

            var devFiles = AnalyzeDevFiles(packagePath);

            // C/C++ header files
            if (devFiles.HeaderFiles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.FileText + " C/C++ Header Files (.h, .hpp, .hxx, .inc)");
                sb.AppendLine(new string('-', 60));
                sb.AppendLine("   Use 'read_nuget_file' to view contents for P/Invoke signatures");
                sb.AppendLine();

                foreach (var file in devFiles.HeaderFiles.OrderBy(f => f.RelativePath))
                {
                    sb.AppendLine($"  - {file.RelativePath} ({FormatFileSize(file.FileSize)})");
                }
            }

            // IDL files
            if (devFiles.IdlFiles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Scroll + " IDL Files (.idl)");
                sb.AppendLine(new string('-', 60));

                foreach (var file in devFiles.IdlFiles.OrderBy(f => f.RelativePath))
                {
                    sb.AppendLine($"  - {file.RelativePath} ({FormatFileSize(file.FileSize)})");
                }
            }

            // Type libraries
            if (devFiles.TypeLibraries.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Wrench + " Type Libraries (.tlb)");
                sb.AppendLine(new string('-', 60));
                sb.AppendLine("   Use 'tlbimp' or OleView to inspect COM interfaces");
                sb.AppendLine();

                foreach (var file in devFiles.TypeLibraries.OrderBy(f => f.RelativePath))
                {
                    sb.AppendLine($"  - {file.RelativePath} ({FormatFileSize(file.FileSize)})");
                }
            }

            // DEF files
            if (devFiles.DefFiles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Clipboard + " Module Definition Files (.def)");
                sb.AppendLine(new string('-', 60));

                foreach (var file in devFiles.DefFiles.OrderBy(f => f.RelativePath))
                {
                    sb.AppendLine($"  - {file.RelativePath} ({FormatFileSize(file.FileSize)})");
                }
            }

            // WinMD files
            if (devFiles.WinMdFiles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Window + " Windows Metadata Files (.winmd)");
                sb.AppendLine(new string('-', 60));

                foreach (var file in devFiles.WinMdFiles.OrderBy(f => f.RelativePath))
                {
                    sb.AppendLine($"  - {file.RelativePath} ({FormatFileSize(file.FileSize)})");
                }
            }

            // Other development files
            if (devFiles.OtherDevFiles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Folder + " Other Development Files");
                sb.AppendLine(new string('-', 60));

                foreach (var file in devFiles.OtherDevFiles.OrderBy(f => f.RelativePath))
                {
                    sb.AppendLine($"  - {file.RelativePath} ({FormatFileSize(file.FileSize)})");
                }
            }

            // Summary
            var totalFiles = devFiles.HeaderFiles.Count + devFiles.IdlFiles.Count +
                             devFiles.TypeLibraries.Count + devFiles.DefFiles.Count +
                             devFiles.WinMdFiles.Count + devFiles.OtherDevFiles.Count;

            sb.AppendLine();
            sb.AppendLine("Summary:");
            sb.AppendLine($"  Header Files: {devFiles.HeaderFiles.Count}");
            sb.AppendLine($"  IDL Files: {devFiles.IdlFiles.Count}");
            sb.AppendLine($"  Type Libraries: {devFiles.TypeLibraries.Count}");
            sb.AppendLine($"  DEF Files: {devFiles.DefFiles.Count}");
            sb.AppendLine($"  WinMD Files: {devFiles.WinMdFiles.Count}");
            sb.AppendLine($"  Other: {devFiles.OtherDevFiles.Count}");
            sb.AppendLine($"  Total: {totalFiles}");

            if (totalFiles == 0)
            {
                sb.AppendLine();
                sb.AppendLine("  ⚠️ No development resource files found in this package.");
                sb.AppendLine("     This package may not include native development headers.");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error listing dev files: {ex.Message}";
        }
    }

    [McpServerTool(Name = "read_nuget_file")]
    [Description("Reads the content of a text file from a NuGet package. Useful for viewing C/C++ headers for P/Invoke signature creation.")]
    public async Task<string> ReadNuGetFile(
        [Description("The package ID (e.g., 'SkiaSharp.NativeAssets.Win32')")]
        string packageId,
        [Description("The relative path to the file within the package (e.g., 'build/native/include/myheader.h')")]
        string filePath,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        [Description("Maximum number of lines to read (default: 500, 0 for all)")]
        int maxLines = 500,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine version
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }

            // Download package
            var downloadResult = await _nugetService.DownloadPackageAsync(
                packageId, packageVersion, cancellationToken);

            if (!downloadResult.IsSuccess)
            {
                return $"Failed to download package: '{packageId}' v{packageVersion}\n{downloadResult.Error}";
            }

            var packagePath = downloadResult.Path!;

            // Normalize file path
            var normalizedPath = filePath.Replace('/', Path.DirectorySeparatorChar)
                                         .Replace('\\', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(packagePath, normalizedPath);

            // Security: Prevent access outside package path
            var fullPackagePath = Path.GetFullPath(packagePath);
            var fullFilePath = Path.GetFullPath(fullPath);

            if (!fullFilePath.StartsWith(fullPackagePath, StringComparison.OrdinalIgnoreCase))
            {
                return "Error: Access denied. Path traversal detected.";
            }

            if (!File.Exists(fullPath))
            {
                // If file not found, suggest similar files
                var suggestions = FindSimilarFiles(packagePath, Path.GetFileName(filePath));

                var errorSb = new StringBuilder();
                errorSb.AppendLine($"File not found: '{filePath}'");

                if (suggestions.Count > 0)
                {
                    errorSb.AppendLine();
                    errorSb.AppendLine("Did you mean one of these?");
                    foreach (var suggestion in suggestions.Take(5))
                    {
                        errorSb.AppendLine($"  - {suggestion}");
                    }
                }

                return errorSb.ToString();
            }

            // Check binary file
            if (IsBinaryFile(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                return $"Error: '{filePath}' appears to be a binary file ({FormatFileSize(fileInfo.Length)}). " +
                       "Use appropriate tools to inspect binary formats like .tlb files.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Package: {packageId} v{packageVersion}");
            sb.AppendLine($"File: {filePath}");
            sb.AppendLine(new string('=', 80));
            sb.AppendLine();

            var lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
            var totalLines = lines.Length;
            var displayLines = maxLines > 0 ? Math.Min(maxLines, totalLines) : totalLines;

            for (int i = 0; i < displayLines; i++)
            {
                sb.AppendLine($"{i + 1,5}: {lines[i]}");
            }

            if (totalLines > displayLines)
            {
                sb.AppendLine();
                sb.AppendLine($"... ({totalLines - displayLines} more lines, use maxLines=0 to show all)");
            }

            sb.AppendLine();
            sb.AppendLine(new string('-', 60));
            sb.AppendLine($"Total lines: {totalLines}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }

    private static DevFilesAnalysisResult AnalyzeDevFiles(string packagePath)
    {
        var result = new DevFilesAnalysisResult();

        // Classify by extension
        var headerExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".h", ".hpp", ".hxx", ".h++", ".hh", ".inc", ".inl"
        };

        var allFiles = Directory.GetFiles(packagePath, "*", SearchOption.AllDirectories);

        foreach (var filePath in allFiles)
        {
            var relativePath = Path.GetRelativePath(packagePath, filePath);
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var fileSize = new FileInfo(filePath).Length;

            // Exclude metadata files
            if (relativePath.StartsWith("[Content_Types]", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("_rels", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileInfo = new DevFileInfo
            {
                FileName = Path.GetFileName(filePath),
                RelativePath = relativePath,
                FileSize = fileSize
            };

            if (headerExtensions.Contains(extension))
            {
                result.HeaderFiles.Add(fileInfo);
            }
            else if (extension == ".idl")
            {
                result.IdlFiles.Add(fileInfo);
            }
            else if (extension == ".tlb")
            {
                result.TypeLibraries.Add(fileInfo);
            }
            else if (extension == ".def")
            {
                result.DefFiles.Add(fileInfo);
            }
            else if (extension == ".winmd")
            {
                result.WinMdFiles.Add(fileInfo);
            }
            else if (extension is ".c" or ".cpp" or ".cxx" or ".cc" or ".manifest" or ".rc" or ".resx")
            {
                result.OtherDevFiles.Add(fileInfo);
            }
        }

        return result;
    }

    private static List<string> FindSimilarFiles(string packagePath, string fileName)
    {
        var results = new List<string>();

        try
        {
            var allFiles = Directory.GetFiles(packagePath, "*", SearchOption.AllDirectories);
            var targetName = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
            var targetExt = Path.GetExtension(fileName).ToLowerInvariant();

            foreach (var file in allFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                var ext = Path.GetExtension(file).ToLowerInvariant();

                // Same or similar name
                if (name == targetName ||
                    name.Contains(targetName) ||
                    targetName.Contains(name) ||
                    (ext == targetExt && name.StartsWith(targetName[..Math.Min(3, targetName.Length)])))
                {
                    results.Add(Path.GetRelativePath(packagePath, file));
                }
            }
        }
        catch
        {
            // Return empty list if search fails
        }

        return results;
    }

    private static bool IsBinaryFile(string filePath)
    {
        try
        {
            // Read only first 8KB to determine binary
            var buffer = new byte[8192];
            using var stream = File.OpenRead(filePath);
            var bytesRead = stream.Read(buffer, 0, buffer.Length);

            // If NULL byte exists, determine as binary
            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                    return true;
            }

            return false;
        }
        catch
        {
            return true; // Consider as binary if read fails
        }
    }

    private sealed class DevFilesAnalysisResult
    {
        public List<DevFileInfo> HeaderFiles { get; } = [];
        public List<DevFileInfo> IdlFiles { get; } = [];
        public List<DevFileInfo> TypeLibraries { get; } = [];
        public List<DevFileInfo> DefFiles { get; } = [];
        public List<DevFileInfo> WinMdFiles { get; } = [];
        public List<DevFileInfo> OtherDevFiles { get; } = [];
    }

    private sealed class DevFileInfo
    {
        public required string FileName { get; init; }
        public required string RelativePath { get; init; }
        public long FileSize { get; init; }
    }

    [McpServerTool(Name = "list_nuget_content_files")]
    [Description("Lists example code, templates, and documentation files in a NuGet package (contentFiles/, content/, samples/, .template.config/).")]
    public async Task<string> ListNuGetContentFiles(
        [Description("The package ID (e.g., 'Microsoft.AspNetCore.Components.WebAssembly.Templates')")]
        string packageId,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine version
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }

            // Download package
            var downloadResult = await _nugetService.DownloadPackageAsync(
                packageId, packageVersion, cancellationToken);

            if (!downloadResult.IsSuccess)
            {
                return $"Failed to download package: '{packageId}' v{packageVersion}\n{downloadResult.Error}";
            }

            var packagePath = downloadResult.Path!;

            var sb = new StringBuilder();
            sb.AppendLine($"Package: {packageId} v{packageVersion}");
            sb.AppendLine("Content Files, Examples, and Templates");
            sb.AppendLine(new string('=', 80));

            var contentInfo = AnalyzeContentFiles(packagePath);

            // dotnet new template
            if (contentInfo.IsTemplate)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Palette + " dotnet new Template Package");
                sb.AppendLine(new string('-', 60));

                if (contentInfo.TemplateConfigs.Count > 0)
                {
                    sb.AppendLine("   Template Configurations:");
                    foreach (var config in contentInfo.TemplateConfigs)
                    {
                        sb.AppendLine($"     - {config}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("   💡 Install with: dotnet new install " + packageId);
            }

            // contentFiles/ (PackageReference style)
            if (contentInfo.ContentFiles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Folder + " contentFiles/ (PackageReference style)");
                sb.AppendLine(new string('-', 60));
                sb.AppendLine("   Files copied to project on package install");
                sb.AppendLine();

                var grouped = contentInfo.ContentFiles
                    .GroupBy(f => GetContentFileCategory(f.RelativePath))
                    .OrderBy(g => g.Key);

                foreach (var group in grouped)
                {
                    sb.AppendLine($"  [{group.Key}]");
                    foreach (var file in group.OrderBy(f => f.RelativePath).Take(20))
                    {
                        sb.AppendLine($"    - {file.RelativePath} ({FormatFileSize(file.FileSize)})");
                    }
                    if (group.Count() > 20)
                    {
                        sb.AppendLine($"    ... and {group.Count() - 20} more files");
                    }
                }
            }

            // content/ (packages.config style)
            if (contentInfo.LegacyContent.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.FolderOpen + " content/ (packages.config style)");
                sb.AppendLine(new string('-', 60));

                foreach (var file in contentInfo.LegacyContent.OrderBy(f => f.RelativePath).Take(30))
                {
                    sb.AppendLine($"  - {file.RelativePath} ({FormatFileSize(file.FileSize)})");
                }
                if (contentInfo.LegacyContent.Count > 30)
                {
                    sb.AppendLine($"  ... and {contentInfo.LegacyContent.Count - 30} more files");
                }
            }

            // Source code files
            if (contentInfo.SourceCodeFiles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Computer + " Source Code Examples");
                sb.AppendLine(new string('-', 60));
                sb.AppendLine("   Use 'read_nuget_file' to view contents");
                sb.AppendLine();

                var byLanguage = contentInfo.SourceCodeFiles
                    .GroupBy(f => GetLanguageFromExtension(Path.GetExtension(f.FileName)))
                    .OrderBy(g => g.Key);

                foreach (var lang in byLanguage)
                {
                    sb.AppendLine($"  [{lang.Key}]");
                    foreach (var file in lang.OrderBy(f => f.RelativePath).Take(15))
                    {
                        sb.AppendLine($"    - {file.RelativePath}");
                    }
                    if (lang.Count() > 15)
                    {
                        sb.AppendLine($"    ... and {lang.Count() - 15} more files");
                    }
                }
            }

            // Documentation files
            if (contentInfo.DocumentationFiles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.FileText + " Documentation Files");
                sb.AppendLine(new string('-', 60));

                foreach (var file in contentInfo.DocumentationFiles.OrderBy(f => f.RelativePath).Take(20))
                {
                    sb.AppendLine($"  - {file.RelativePath} ({FormatFileSize(file.FileSize)})");
                }
                if (contentInfo.DocumentationFiles.Count > 20)
                {
                    sb.AppendLine($"  ... and {contentInfo.DocumentationFiles.Count - 20} more files");
                }
            }

            // Configuration files
            if (contentInfo.ConfigFiles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Gear + " Configuration Files");
                sb.AppendLine(new string('-', 60));

                foreach (var file in contentInfo.ConfigFiles.OrderBy(f => f.RelativePath).Take(15))
                {
                    sb.AppendLine($"  - {file.RelativePath} ({FormatFileSize(file.FileSize)})");
                }
                if (contentInfo.ConfigFiles.Count > 15)
                {
                    sb.AppendLine($"  ... and {contentInfo.ConfigFiles.Count - 15} more files");
                }
            }

            // Summary
            var totalFiles = contentInfo.ContentFiles.Count + contentInfo.LegacyContent.Count +
                             contentInfo.SourceCodeFiles.Count + contentInfo.DocumentationFiles.Count +
                             contentInfo.ConfigFiles.Count;

            sb.AppendLine();
            sb.AppendLine("Summary:");
            sb.AppendLine($"  contentFiles/: {contentInfo.ContentFiles.Count}");
            sb.AppendLine($"  content/ (legacy): {contentInfo.LegacyContent.Count}");
            sb.AppendLine($"  Source Code: {contentInfo.SourceCodeFiles.Count}");
            sb.AppendLine($"  Documentation: {contentInfo.DocumentationFiles.Count}");
            sb.AppendLine($"  Configuration: {contentInfo.ConfigFiles.Count}");
            sb.AppendLine($"  Is Template: {(contentInfo.IsTemplate ? "Yes" : "No")}");
            sb.AppendLine($"  Total: {totalFiles}");

            if (totalFiles == 0 && !contentInfo.IsTemplate)
            {
                sb.AppendLine();
                sb.AppendLine("  ⚠️ No content files found in this package.");
                sb.AppendLine("     This package may be a pure library without examples.");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error listing content files: {ex.Message}";
        }
    }

    private static ContentFilesAnalysisResult AnalyzeContentFiles(string packagePath)
    {
        var result = new ContentFilesAnalysisResult();

        // Classify by extension
        var sourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".vb", ".fs", ".fsx", ".csx", // .NET
            ".cpp", ".c", ".cxx", ".cc", ".hpp", // C/C++
            ".razor", ".cshtml", ".vbhtml", // Razor/Web
            ".xaml", ".axaml", // XAML
            ".js", ".ts", ".jsx", ".tsx", // JavaScript/TypeScript
            ".py", ".ps1", ".psm1", ".psd1" // Scripts
        };

        var docExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".md", ".txt", ".rst", ".html", ".htm",
            ".xml", ".json", ".yaml", ".yml",
            ".pdf" // Binary but document
        };

        var configExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".config", ".settings", ".props", ".targets",
            ".csproj", ".vbproj", ".fsproj", ".sln",
            ".editorconfig", ".gitignore", ".gitattributes",
            ".ruleset", ".globalconfig"
        };

        var allFiles = Directory.GetFiles(packagePath, "*", SearchOption.AllDirectories);

        foreach (var filePath in allFiles)
        {
            var relativePath = Path.GetRelativePath(packagePath, filePath);
            var fileName = Path.GetFileName(filePath);
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var fileSize = new FileInfo(filePath).Length;

            // Exclude metadata files
            if (relativePath.StartsWith("[Content_Types]", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("_rels", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("package", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".sha512", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Exclude lib/, ref/, runtimes/ folders (library assemblies)
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Length > 0 && parts[0].ToLowerInvariant() is "lib" or "ref" or "runtimes" or "analyzers")
            {
                continue;
            }

            var fileInfo = new ContentFileInfo
            {
                FileName = fileName,
                RelativePath = relativePath,
                FileSize = fileSize
            };

            // Check .template.config
            if (relativePath.Contains(".template.config", StringComparison.OrdinalIgnoreCase))
            {
                result.IsTemplate = true;
                if (fileName.Equals("template.json", StringComparison.OrdinalIgnoreCase))
                {
                    result.TemplateConfigs.Add(relativePath);
                }
                continue;
            }

            // contentFiles/ folder
            if (parts.Length > 0 && parts[0].Equals("contentFiles", StringComparison.OrdinalIgnoreCase))
            {
                result.ContentFiles.Add(fileInfo);

                // Add separately if source code
                if (sourceExtensions.Contains(extension) || 
                    fileName.EndsWith(".cs.pp", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".vb.pp", StringComparison.OrdinalIgnoreCase))
                {
                    result.SourceCodeFiles.Add(fileInfo);
                }
                continue;
            }

            // content/ folder (legacy)
            if (parts.Length > 0 && parts[0].Equals("content", StringComparison.OrdinalIgnoreCase))
            {
                result.LegacyContent.Add(fileInfo);

                if (sourceExtensions.Contains(extension) ||
                    fileName.EndsWith(".cs.pp", StringComparison.OrdinalIgnoreCase))
                {
                    result.SourceCodeFiles.Add(fileInfo);
                }
                continue;
            }

            // Source code files (other locations)
            if (sourceExtensions.Contains(extension))
            {
                result.SourceCodeFiles.Add(fileInfo);
            }
            // Documentation files
            else if (docExtensions.Contains(extension))
            {
                result.DocumentationFiles.Add(fileInfo);
            }
            // Configuration files
            else if (configExtensions.Contains(extension))
            {
                result.ConfigFiles.Add(fileInfo);
            }
        }

        return result;
    }

    private static string GetContentFileCategory(string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // contentFiles/{lang}/{tfm}/...
        if (parts.Length >= 3 && parts[0].Equals("contentFiles", StringComparison.OrdinalIgnoreCase))
        {
            var lang = parts[1].ToLowerInvariant() switch
            {
                "cs" => "C#",
                "vb" => "VB.NET",
                "fs" => "F#",
                "any" => "Any Language",
                _ => parts[1]
            };
            return $"{lang} / {parts[2]}";
        }

        return "Other";
    }

    private static string GetLanguageFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" or ".csx" => "C#",
            ".vb" => "VB.NET",
            ".fs" or ".fsx" => "F#",
            ".cpp" or ".cxx" or ".cc" or ".hpp" => "C++",
            ".c" => "C",
            ".razor" => "Razor",
            ".cshtml" or ".vbhtml" => "Razor Views",
            ".xaml" or ".axaml" => "XAML",
            ".js" => "JavaScript",
            ".ts" or ".tsx" => "TypeScript",
            ".jsx" => "React JSX",
            ".py" => "Python",
            ".ps1" or ".psm1" or ".psd1" => "PowerShell",
            _ => extension.TrimStart('.').ToUpperInvariant()
        };
    }

    private sealed class ContentFilesAnalysisResult
    {
        public bool IsTemplate { get; set; }
        public List<string> TemplateConfigs { get; } = [];
        public List<ContentFileInfo> ContentFiles { get; } = [];
        public List<ContentFileInfo> LegacyContent { get; } = [];
        public List<ContentFileInfo> SourceCodeFiles { get; } = [];
        public List<ContentFileInfo> DocumentationFiles { get; } = [];
        public List<ContentFileInfo> ConfigFiles { get; } = [];
    }

    private sealed class ContentFileInfo
    {
        public required string FileName { get; init; }
        public required string RelativePath { get; init; }
        public long FileSize { get; init; }
    }

    [McpServerTool(Name = "compare_nuget_versions")]
    [Description("Compares API differences between two versions of a NuGet package. Detects breaking changes, added/removed types and members.")]
    public async Task<string> CompareNuGetVersions(
        [Description("The package ID (e.g., 'Newtonsoft.Json')")]
        string packageId,
        [Description("The older version to compare from (e.g., '12.0.0')")]
        string fromVersion,
        [Description("The newer version to compare to (e.g., '13.0.0')")]
        string toVersion,
        [Description("Target framework (e.g., 'net8.0'). If not specified, the highest version is used.")]
        string? targetFramework = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!NuGetVersion.TryParse(fromVersion, out var fromNuGetVersion))
            {
                return $"Invalid from version format: '{fromVersion}'";
            }

            if (!NuGetVersion.TryParse(toVersion, out var toNuGetVersion))
            {
                return $"Invalid to version format: '{toVersion}'";
            }

            // Download both versions
            var fromResult = await _nugetService.DownloadPackageAsync(packageId, fromNuGetVersion, cancellationToken);
            var toResult = await _nugetService.DownloadPackageAsync(packageId, toNuGetVersion, cancellationToken);

            if (!fromResult.IsSuccess)
            {
                return $"Failed to download package: '{packageId}' v{fromVersion}\n{fromResult.Error}";
            }

            if (!toResult.IsSuccess)
            {
                return $"Failed to download package: '{packageId}' v{toVersion}\n{toResult.Error}";
            }

            var fromPath = fromResult.Path!;
            var toPath = toResult.Path!;

            // Get assemblies
            var fromAssemblies = (await _nugetService.GetPackageAssembliesAsync(fromPath, targetFramework, cancellationToken)).ToList();
            var toAssemblies = (await _nugetService.GetPackageAssembliesAsync(toPath, targetFramework, cancellationToken)).ToList();

            if (fromAssemblies.Count == 0)
            {
                return $"No assemblies found in '{packageId}' v{fromVersion}";
            }

            if (toAssemblies.Count == 0)
            {
                return $"No assemblies found in '{packageId}' v{toVersion}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"API Comparison: {packageId}");
            sb.AppendLine($"From: v{fromVersion} → To: v{toVersion}");
            sb.AppendLine(new string('=', 80));

            var totalAddedTypes = 0;
            var totalRemovedTypes = 0;
            var totalAddedMembers = 0;
            var totalRemovedMembers = 0;
            var breakingChanges = new List<string>();

            // Compare each assembly
            foreach (var toAssemblyPath in toAssemblies)
            {
                var assemblyName = Path.GetFileName(toAssemblyPath);
                var fromAssemblyPath = fromAssemblies.FirstOrDefault(a => Path.GetFileName(a) == assemblyName);

                if (!File.Exists(toAssemblyPath))
                    continue;

                try
                {
                    using var toAssembly = AssemblyDefinition.ReadAssembly(toAssemblyPath);
                    var toTypes = toAssembly.MainModule.Types
                        .Where(t => t.IsPublic)
                        .ToDictionary(t => t.FullName);

                    Dictionary<string, TypeDefinition> fromTypes;

                    if (fromAssemblyPath != null && File.Exists(fromAssemblyPath))
                    {
                        using var fromAssembly = AssemblyDefinition.ReadAssembly(fromAssemblyPath);
                        fromTypes = fromAssembly.MainModule.Types
                            .Where(t => t.IsPublic)
                            .ToDictionary(t => t.FullName);
                    }
                    else
                    {
                        fromTypes = [];
                        sb.AppendLine();
                        sb.AppendLine(Emoji.Package + $" {assemblyName} (NEW ASSEMBLY)");
                        totalAddedTypes += toTypes.Count;
                        continue;
                    }

                    // Added types
                    var addedTypes = toTypes.Keys.Except(fromTypes.Keys).ToList();
                    // Removed types
                    var removedTypes = fromTypes.Keys.Except(toTypes.Keys).ToList();
                    // Common types
                    var commonTypes = toTypes.Keys.Intersect(fromTypes.Keys).ToList();

                    if (addedTypes.Count == 0 && removedTypes.Count == 0)
                    {
                        // Check only member changes
                        var (AddedMembers, RemovedMembers) = CompareTypeMembers(fromTypes, toTypes, commonTypes);
                        if (AddedMembers.Count == 0 && RemovedMembers.Count == 0)
                            continue;

                        sb.AppendLine();
                        sb.AppendLine(Emoji.Package + $" {assemblyName}");
                        sb.AppendLine(new string('-', 60));

                        if (AddedMembers.Count > 0)
                        {
                            sb.AppendLine();
                            sb.AppendLine($"  ✅ Added Members ({AddedMembers.Count}):");
                            foreach (var member in AddedMembers.Take(20))
                            {
                                sb.AppendLine($"    + {member}");
                            }
                            if (AddedMembers.Count > 20)
                            {
                                sb.AppendLine($"    ... and {AddedMembers.Count - 20} more");
                            }
                            totalAddedMembers += AddedMembers.Count;
                        }

                        if (RemovedMembers.Count > 0)
                        {
                            sb.AppendLine();
                            sb.AppendLine($"  ❌ Removed Members ({RemovedMembers.Count}) ⚠️ BREAKING:");
                            foreach (var member in RemovedMembers.Take(20))
                            {
                                sb.AppendLine($"    - {member}");
                                breakingChanges.Add($"Removed: {member}");
                            }
                            if (RemovedMembers.Count > 20)
                            {
                                sb.AppendLine($"    ... and {RemovedMembers.Count - 20} more");
                            }
                            totalRemovedMembers += RemovedMembers.Count;
                        }

                        continue;
                    }

                    sb.AppendLine();
                    sb.AppendLine(Emoji.Package + $" {assemblyName}");
                    sb.AppendLine(new string('-', 60));

                    if (addedTypes.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"  ✅ Added Types ({addedTypes.Count}):");
                        foreach (var type in addedTypes.OrderBy(t => t).Take(15))
                        {
                            sb.AppendLine($"    + {type}");
                        }
                        if (addedTypes.Count > 15)
                        {
                            sb.AppendLine($"    ... and {addedTypes.Count - 15} more");
                        }
                        totalAddedTypes += addedTypes.Count;
                    }

                    if (removedTypes.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"  ❌ Removed Types ({removedTypes.Count}) ⚠️ BREAKING:");
                        foreach (var type in removedTypes.OrderBy(t => t).Take(15))
                        {
                            sb.AppendLine($"    - {type}");
                            breakingChanges.Add($"Removed type: {type}");
                        }
                        if (removedTypes.Count > 15)
                        {
                            sb.AppendLine($"    ... and {removedTypes.Count - 15} more");
                        }
                        totalRemovedTypes += removedTypes.Count;
                    }

                    // Member changes in common types
                    var memberChangesResult = CompareTypeMembers(fromTypes, toTypes, commonTypes);
                    totalAddedMembers += memberChangesResult.AddedMembers.Count;
                    totalRemovedMembers += memberChangesResult.RemovedMembers.Count;

                    if (memberChangesResult.RemovedMembers.Count > 0)
                    {
                        foreach (var member in memberChangesResult.RemovedMembers)
                        {
                            breakingChanges.Add($"Removed: {member}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine();
                    sb.AppendLine(Emoji.Package + $" {assemblyName}");
                    sb.AppendLine($"  Error: {ex.Message}");
                }
            }

            // Summary
            sb.AppendLine();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("Summary:");
            sb.AppendLine($"  Added Types: {totalAddedTypes}");
            sb.AppendLine($"  Removed Types: {totalRemovedTypes}");
            sb.AppendLine($"  Added Members: {totalAddedMembers}");
            sb.AppendLine($"  Removed Members: {totalRemovedMembers}");

            if (breakingChanges.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Warning + $" BREAKING CHANGES DETECTED: {breakingChanges.Count}");
                sb.AppendLine("   Review removed types and members before upgrading.");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.CheckMark + " No breaking changes detected (additions only).");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error comparing versions: {ex.Message}";
        }
    }

    private static (List<string> AddedMembers, List<string> RemovedMembers) CompareTypeMembers(
        Dictionary<string, TypeDefinition> fromTypes,
        Dictionary<string, TypeDefinition> toTypes,
        List<string> commonTypes)
    {
        var addedMembers = new List<string>();
        var removedMembers = new List<string>();

        foreach (var typeName in commonTypes)
        {
            if (!fromTypes.TryGetValue(typeName, out var fromType) ||
                !toTypes.TryGetValue(typeName, out var toType))
                continue;

            // Compare methods
            var fromMethods = fromType.Methods
                .Where(m => m.IsPublic)
                .Select(m => $"{typeName}.{m.Name}({string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name))})")
                .ToHashSet();

            var toMethods = toType.Methods
                .Where(m => m.IsPublic)
                .Select(m => $"{typeName}.{m.Name}({string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name))})")
                .ToHashSet();

            addedMembers.AddRange(toMethods.Except(fromMethods));
            removedMembers.AddRange(fromMethods.Except(toMethods));

            // Compare properties
            var fromProps = fromType.Properties
                .Where(p => (p.GetMethod?.IsPublic == true) || (p.SetMethod?.IsPublic == true))
                .Select(p => $"{typeName}.{p.Name}")
                .ToHashSet();

            var toProps = toType.Properties
                .Where(p => (p.GetMethod?.IsPublic == true) || (p.SetMethod?.IsPublic == true))
                .Select(p => $"{typeName}.{p.Name}")
                .ToHashSet();

            addedMembers.AddRange(toProps.Except(fromProps).Select(p => p + " (property)"));
            removedMembers.AddRange(fromProps.Except(toProps).Select(p => p + " (property)"));

            // Compare fields
            var fromFields = fromType.Fields
                .Where(f => f.IsPublic)
                .Select(f => $"{typeName}.{f.Name}")
                .ToHashSet();

            var toFields = toType.Fields
                .Where(f => f.IsPublic)
                .Select(f => $"{typeName}.{f.Name}")
                .ToHashSet();

            addedMembers.AddRange(toFields.Except(fromFields).Select(f => f + " (field)"));
            removedMembers.AddRange(fromFields.Except(toFields).Select(f => f + " (field)"));
        }

        return (addedMembers, removedMembers);
    }

    [McpServerTool(Name = "search_nuget_types")]
    [Description("Searches for types by name or namespace pattern in a NuGet package.")]
    public async Task<string> SearchNuGetTypes(
        [Description("The package ID (e.g., 'Newtonsoft.Json')")]
        string packageId,
        [Description("Search pattern (case-insensitive, supports * wildcard). E.g., '*Serializer*', 'System.Text.*'")]
        string pattern,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        [Description("Target framework (e.g., 'net8.0'). If not specified, the highest version is used.")]
        string? targetFramework = null,
        [Description("Include non-public types (default: false)")]
        bool includeInternal = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }

            var downloadResult = await _nugetService.DownloadPackageAsync(
                packageId, packageVersion, cancellationToken);

            if (!downloadResult.IsSuccess)
            {
                return $"Failed to download package: '{packageId}' v{packageVersion}\n{downloadResult.Error}";
            }

            var packagePath = downloadResult.Path!;

            var assemblies = await _nugetService.GetPackageAssembliesAsync(
                packagePath, targetFramework, cancellationToken);

            // Convert wildcard pattern to regex
            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            var regex = new System.Text.RegularExpressions.Regex(
                regexPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var sb = new StringBuilder();
            sb.AppendLine($"Type Search: '{pattern}' in {packageId} v{packageVersion}");
            sb.AppendLine(new string('=', 80));

            var totalMatches = 0;
            var results = new List<(string Assembly, string TypeName, string Kind, string Namespace)>();

            foreach (var assemblyPath in assemblies)
            {
                if (!File.Exists(assemblyPath))
                    continue;

                try
                {
                    using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
                    var assemblyName = Path.GetFileName(assemblyPath);

                    foreach (var type in assembly.MainModule.Types)
                    {
                        if (!includeInternal && !type.IsPublic)
                            continue;

                        // Search by type name or full name
                        if (regex.IsMatch(type.Name) || regex.IsMatch(type.FullName))
                        {
                            var kind = GetTypeKind(type);
                            results.Add((assemblyName, type.FullName, kind, type.Namespace ?? "<global>"));
                            totalMatches++;
                        }

                        // Search nested types too
                        foreach (var nestedType in type.NestedTypes)
                        {
                            if (!includeInternal && !nestedType.IsNestedPublic)
                                continue;

                            if (regex.IsMatch(nestedType.Name) || regex.IsMatch(nestedType.FullName))
                            {
                                var kind = GetTypeKind(nestedType);
                                results.Add((assemblyName, nestedType.FullName, kind, type.Namespace ?? "<global>"));
                                totalMatches++;
                            }
                        }
                    }
                }
                catch
                {
                    // Skip if assembly read fails
                }
            }

            if (totalMatches == 0)
            {
                sb.AppendLine();
                sb.AppendLine("No matching types found.");
                return sb.ToString();
            }

            // Group by namespace
            var byNamespace = results
                .GroupBy(r => r.Namespace)
                .OrderBy(g => g.Key);

            foreach (var nsGroup in byNamespace)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Folder + $" {nsGroup.Key}");
                sb.AppendLine(new string('-', 60));

                foreach (var (assembly, typeName, kind, _) in nsGroup.OrderBy(r => r.TypeName))
                {
                    var shortName = typeName.StartsWith(nsGroup.Key + ".")
                        ? typeName[(nsGroup.Key.Length + 1)..]
                        : typeName;
                    sb.AppendLine($"  [{kind}] {shortName}");
                    sb.AppendLine($"         Assembly: {assembly}");
                }
            }

            sb.AppendLine();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine($"Total matches: {totalMatches}");
            sb.AppendLine();
            sb.AppendLine(Emoji.Bulb + " Use 'inspect_nuget_package_type' to view type details.");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error searching types: {ex.Message}";
        }
    }

    private static string GetTypeKind(TypeDefinition type)
    {
        if (type.IsEnum) return "enum";
        if (type.IsInterface) return "interface";
        if (type.IsValueType) return "struct";
        if (type.BaseType?.FullName == "System.Delegate" ||
            type.BaseType?.FullName == "System.MulticastDelegate") return "delegate";
        if (type.IsAbstract && type.IsSealed) return "static class";
        if (type.IsAbstract) return "abstract class";
        if (type.IsSealed) return "sealed class";
        return "class";
    }

    [McpServerTool(Name = "get_nuget_dependencies_tree")]
    [Description("Analyzes the transitive dependency tree of a NuGet package. Shows all nested dependencies and potential version conflicts.")]
    public async Task<string> GetNuGetDependenciesTree(
        [Description("The package ID (e.g., 'Microsoft.EntityFrameworkCore')")]
        string packageId,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        [Description("Target framework (e.g., 'net8.0', 'netstandard2.0')")]
        string? targetFramework = null,
        [Description("Maximum depth to traverse (default: 5, 0 for unlimited)")]
        int maxDepth = 5,
        CancellationToken cancellationToken = default)
    {
        try
        {
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Dependency Tree: {packageId} v{packageVersion}");
            if (!string.IsNullOrEmpty(targetFramework))
            {
                sb.AppendLine($"Target Framework: {targetFramework}");
            }
            sb.AppendLine(new string('=', 80));

            var visited = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var allPackages = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var conflicts = new List<string>();

            await BuildDependencyTreeAsync(
                packageId, packageVersion, targetFramework,
                0, maxDepth, visited, allPackages, conflicts, sb, cancellationToken);

            // Summary
            sb.AppendLine();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("Summary:");
            sb.AppendLine($"  Total unique packages: {allPackages.Count}");

            // Packages with multiple versions (potential conflicts)
            var multiVersion = allPackages.Where(p => p.Value.Count > 1).ToList();
            if (multiVersion.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Warning + " Packages with multiple versions (potential conflicts):");
                foreach (var (pkgId, versions) in multiVersion.OrderBy(p => p.Key))
                {
                    sb.AppendLine($"  {pkgId}: {string.Join(", ", versions.OrderByDescending(v => v))}");
                }
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.CheckMark + " No version conflicts detected.");
            }

            // Package list (alphabetical)
            sb.AppendLine();
            sb.AppendLine("All Dependencies (alphabetical):");
            foreach (var (pkgId, versions) in allPackages.OrderBy(p => p.Key).Take(50))
            {
                var versionStr = versions.Count == 1
                    ? versions.First()
                    : string.Join(", ", versions.OrderByDescending(v => v));
                sb.AppendLine($"  - {pkgId} ({versionStr})");
            }

            if (allPackages.Count > 50)
            {
                sb.AppendLine($"  ... and {allPackages.Count - 50} more packages");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error analyzing dependencies: {ex.Message}";
        }
    }

    private async Task BuildDependencyTreeAsync(
        string packageId,
        NuGetVersion version,
        string? targetFramework,
        int depth,
        int maxDepth,
        Dictionary<string, List<string>> visited,
        Dictionary<string, HashSet<string>> allPackages,
        List<string> conflicts,
        StringBuilder sb,
        CancellationToken cancellationToken)
    {
        var indent = new string(' ', depth * 2);
        var key = packageId.ToLowerInvariant();
        var versionStr = version.ToString();

        // Check if already visited
        if (visited.TryGetValue(key, out var visitedVersions))
        {
            if (visitedVersions.Contains(versionStr))
            {
                sb.AppendLine($"{indent}├─ {packageId} v{version} (already listed)");
                return;
            }
            visitedVersions.Add(versionStr);
        }
        else
        {
            visited[key] = [versionStr];
        }

        // Add to full package list
        if (!allPackages.TryGetValue(packageId, out var packageVersions))
        {
            packageVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            allPackages[packageId] = packageVersions;
        }
        packageVersions.Add(versionStr);

        sb.AppendLine($"{indent}├─ {packageId} v{version}");

        // Check depth limit
        if (maxDepth > 0 && depth >= maxDepth)
        {
            sb.AppendLine($"{indent}│  └─ (max depth reached)");
            return;
        }

        // Get metadata
        try
        {
            var metadata = await _nugetService.GetPackageMetadataAsync(packageId, version, cancellationToken);

            if (metadata?.DependencySets == null)
                return;

            // Find dependency set matching TFM
            var dependencySet = FindBestDependencySet(metadata.DependencySets, targetFramework);

            if (dependencySet == null || !dependencySet.Packages.Any())
                return;

            var deps = dependencySet.Packages.ToList();
            for (int i = 0; i < deps.Count; i++)
            {
                var dep = deps[i];

                // Use minimum version from version range
                var depVersion = dep.VersionRange.MinVersion ?? new NuGetVersion("0.0.0");

                await BuildDependencyTreeAsync(
                    dep.Id, depVersion, targetFramework,
                    depth + 1, maxDepth, visited, allPackages, conflicts, sb, cancellationToken);
            }
        }
        catch
        {
            // Skip if metadata fetch fails
        }
    }

    private static NuGet.Packaging.PackageDependencyGroup? FindBestDependencySet(
        IEnumerable<NuGet.Packaging.PackageDependencyGroup> dependencySets,
        string? targetFramework)
    {
        var sets = dependencySets.ToList();

        if (sets.Count == 0)
            return null;

        if (!string.IsNullOrEmpty(targetFramework))
        {
            // Find exact matching TFM
            var exact = sets.FirstOrDefault(s =>
                s.TargetFramework.GetShortFolderName().Equals(targetFramework, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;
        }

        // Priority: net8.0 > net6.0 > netstandard2.1 > netstandard2.0 > any
        var priorities = new[] { "net8.0", "net7.0", "net6.0", "net5.0", "netcoreapp3.1", "netstandard2.1", "netstandard2.0", "netstandard1.6" };

        foreach (var tfm in priorities)
        {
            var match = sets.FirstOrDefault(s =>
                s.TargetFramework.GetShortFolderName().StartsWith(tfm, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }

        // Return first if none found
        return sets.FirstOrDefault();
    }

    [McpServerTool(Name = "get_nuget_license_info")]
    [Description("Gets detailed license information for a NuGet package including LICENSE and NOTICE files. Useful for legal review and commercial use assessment.")]
    public async Task<string> GetNuGetLicenseInfo(
        [Description("The package ID (e.g., 'Newtonsoft.Json')")]
        string packageId,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }

            var metadata = await _nugetService.GetPackageMetadataAsync(
                packageId, packageVersion, cancellationToken);

            if (metadata == null)
            {
                return $"Package metadata not found: '{packageId}' v{packageVersion}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"License Information: {packageId} v{packageVersion}");
            sb.AppendLine(new string('=', 80));

            // License info from metadata
            sb.AppendLine();
            sb.AppendLine(Emoji.Clipboard + " License Metadata:");
            sb.AppendLine(new string('-', 60));

            var licenseExpression = metadata.LicenseMetadata?.License;
            var licenseUrl = metadata.LicenseUrl?.ToString();
            var licenseType = metadata.LicenseMetadata?.Type.ToString() ?? "Unknown";

            if (!string.IsNullOrEmpty(licenseExpression))
            {
                sb.AppendLine($"  License: {licenseExpression}");
                sb.AppendLine($"  Type: {licenseType}");

                // Analyze SPDX license
                var licenseInfo = AnalyzeSpdxLicense(licenseExpression);
                if (licenseInfo != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("  📝 License Details:");
                    sb.AppendLine($"    Commercial Use: {(licenseInfo.AllowsCommercialUse ? Emoji.CheckMark + " Allowed" : Emoji.CrossMark + " Restricted")}");
                    sb.AppendLine($"    Modification: {(licenseInfo.AllowsModification ? Emoji.CheckMark + " Allowed" : Emoji.Warning + " Check terms")}");
                    sb.AppendLine($"    Distribution: {(licenseInfo.AllowsDistribution ? Emoji.CheckMark + " Allowed" : Emoji.Warning + " Check terms")}");
                    sb.AppendLine($"    Patent Grant: {(licenseInfo.HasPatentGrant ? Emoji.CheckMark + " Yes" : Emoji.Question + " Not specified")}");
                    sb.AppendLine($"    Copyleft: {(licenseInfo.IsCopyleft ? Emoji.Warning + " Yes (derivative works must use same license)" : Emoji.CheckMark + " No")}");

                    if (!string.IsNullOrEmpty(licenseInfo.Notes))
                    {
                        sb.AppendLine($"    Notes: {licenseInfo.Notes}");
                    }
                }
            }
            else if (!string.IsNullOrEmpty(licenseUrl))
            {
                sb.AppendLine($"  License URL: {licenseUrl}");
                sb.AppendLine("  ⚠️ No SPDX expression - review license URL manually");
            }
            else
            {
                sb.AppendLine("  ⚠️ No license information in metadata");
            }

            // Download package and check license files
            var downloadResult = await _nugetService.DownloadPackageAsync(
                packageId, packageVersion, cancellationToken);

            string? packagePath = downloadResult.IsSuccess ? downloadResult.Path : null;

            if (packagePath != null)
            {
                var licenseFiles = FindLicenseFiles(packagePath);

                if (licenseFiles.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(Emoji.File + " License Files in Package:");
                    sb.AppendLine(new string('-', 60));

                    foreach (var (fileName, filePath, fileSize) in licenseFiles)
                    {
                        sb.AppendLine($"  • {fileName} ({FormatFileSize(fileSize)})");

                        // Display file content (first 50 lines)
                        if (!IsBinaryFile(filePath))
                        {
                            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
                            sb.AppendLine();

                            var displayLines = Math.Min(50, lines.Length);
                            for (int i = 0; i < displayLines; i++)
                            {
                                sb.AppendLine($"    {lines[i]}");
                            }

                            if (lines.Length > displayLines)
                            {
                                sb.AppendLine();
                                sb.AppendLine($"    ... ({lines.Length - displayLines} more lines)");
                            }
                            sb.AppendLine();
                        }
                    }
                }

                // Check NOTICE files
                var noticeFiles = FindNoticeFiles(packagePath);
                if (noticeFiles.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(Emoji.Megaphone + " NOTICE Files (Third-party attributions):");
                    sb.AppendLine(new string('-', 60));

                    foreach (var (fileName, filePath, _) in noticeFiles)
                    {
                        sb.AppendLine($"  • {fileName}");

                        if (!IsBinaryFile(filePath))
                        {
                            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                            var lines = content.Split('\n');
                            var displayLines = Math.Min(30, lines.Length);

                            sb.AppendLine();
                            for (int i = 0; i < displayLines; i++)
                            {
                                sb.AppendLine($"    {lines[i].TrimEnd('\r')}");
                            }

                            if (lines.Length > displayLines)
                            {
                                sb.AppendLine($"    ... ({lines.Length - displayLines} more lines)");
                            }
                        }
                    }
                }

                // Find copyright info
                var copyrightInfo = ExtractCopyrightInfo(packagePath);
                if (copyrightInfo.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(Emoji.Copyright + " Copyright Information:");
                    sb.AppendLine(new string('-', 60));

                    foreach (var copyright in copyrightInfo.Take(10))
                    {
                        sb.AppendLine($"  {copyright}");
                    }
                }
            }

            // Summary
            sb.AppendLine();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("Summary:");

            if (!string.IsNullOrEmpty(licenseExpression))
            {
                var info = AnalyzeSpdxLicense(licenseExpression);
                if (info?.AllowsCommercialUse == true)
                {
                    sb.AppendLine("  ✅ This package appears to be suitable for commercial use.");
                }
                else if (info?.IsCopyleft == true)
                {
                    sb.AppendLine("  ⚠️ Copyleft license - derivative works must use the same license.");
                }
                else
                {
                    sb.AppendLine("  ⚠️ Review license terms before commercial use.");
                }
            }
            else
            {
                sb.AppendLine("  ⚠️ No clear license information - consult legal counsel before use.");
            }

            sb.AppendLine();
            sb.AppendLine(Emoji.Bulb + " This is not legal advice. Consult a lawyer for license compliance.");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting license info: {ex.Message}";
        }
    }

    private static LicenseInfo? AnalyzeSpdxLicense(string expression)
    {
        var exp = expression.ToUpperInvariant();

        // Common permissive licenses
        if (exp.Contains("MIT") || exp.Contains("APACHE-2.0") || exp.Contains("BSD") ||
            exp.Contains("ISC") || exp.Contains("ZLIB") || exp.Contains("UNLICENSE") ||
            exp.Contains("CC0") || exp.Contains("WTFPL"))
        {
            return new LicenseInfo
            {
                AllowsCommercialUse = true,
                AllowsModification = true,
                AllowsDistribution = true,
                HasPatentGrant = exp.Contains("APACHE-2.0"),
                IsCopyleft = false,
                Notes = exp.Contains("APACHE-2.0") ? "Includes patent grant" : null
            };
        }

        // Copyleft licenses
        if (exp.Contains("GPL") || exp.Contains("LGPL") || exp.Contains("AGPL"))
        {
            var isLgpl = exp.Contains("LGPL");
            return new LicenseInfo
            {
                AllowsCommercialUse = true,
                AllowsModification = true,
                AllowsDistribution = true,
                HasPatentGrant = exp.Contains("GPL-3") || exp.Contains("LGPL-3"),
                IsCopyleft = true,
                Notes = isLgpl
                    ? "LGPL allows linking without copyleft for dynamic linking"
                    : "Derivative works must be released under same license"
            };
        }

        // MS licenses
        if (exp.Contains("MS-PL"))
        {
            return new LicenseInfo
            {
                AllowsCommercialUse = true,
                AllowsModification = true,
                AllowsDistribution = true,
                HasPatentGrant = false,
                IsCopyleft = false,
                Notes = "Microsoft Public License"
            };
        }

        if (exp.Contains("MS-RL"))
        {
            return new LicenseInfo
            {
                AllowsCommercialUse = true,
                AllowsModification = true,
                AllowsDistribution = true,
                HasPatentGrant = false,
                IsCopyleft = true,
                Notes = "Microsoft Reciprocal License - copyleft for file-level changes"
            };
        }

        // MPL
        if (exp.Contains("MPL"))
        {
            return new LicenseInfo
            {
                AllowsCommercialUse = true,
                AllowsModification = true,
                AllowsDistribution = true,
                HasPatentGrant = true,
                IsCopyleft = true,
                Notes = "File-level copyleft only"
            };
        }

        return null;
    }

    private static List<(string FileName, string FilePath, long FileSize)> FindLicenseFiles(string packagePath)
    {
        var results = new List<(string, string, long)>();
        var patterns = new[] { "LICENSE*", "LICENCE*", "license*", "licence*", "COPYING*", "copying*" };

        foreach (var pattern in patterns)
        {
            foreach (var file in Directory.GetFiles(packagePath, pattern, SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(packagePath, file);
                if (relativePath.StartsWith("_rels") || relativePath.StartsWith("[Content_Types]"))
                    continue;

                results.Add((relativePath, file, new FileInfo(file).Length));
            }
        }

        return [.. results.DistinctBy(r => r.Item1)];
    }

    private static List<(string FileName, string FilePath, long FileSize)> FindNoticeFiles(string packagePath)
    {
        var results = new List<(string, string, long)>();
        var patterns = new[] { "NOTICE*", "notice*", "THIRD-PARTY*", "third-party*", "ATTRIBUTION*", "attribution*" };

        foreach (var pattern in patterns)
        {
            foreach (var file in Directory.GetFiles(packagePath, pattern, SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(packagePath, file);
                if (relativePath.StartsWith("_rels") || relativePath.StartsWith("[Content_Types]"))
                    continue;

                results.Add((relativePath, file, new FileInfo(file).Length));
            }
        }

        return [.. results.DistinctBy(r => r.Item1)];
    }

    private static List<string> ExtractCopyrightInfo(string packagePath)
    {
        var results = new HashSet<string>();

        // Extract copyright from AssemblyInfo
        foreach (var dllPath in Directory.GetFiles(packagePath, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                using var assembly = AssemblyDefinition.ReadAssembly(dllPath);
                var copyrightAttr = assembly.CustomAttributes
                    .FirstOrDefault(a => a.AttributeType.Name == "AssemblyCopyrightAttribute");

                if (copyrightAttr?.ConstructorArguments.Count > 0)
                {
                    var copyright = copyrightAttr.ConstructorArguments[0].Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(copyright))
                    {
                        results.Add(copyright);
                    }
                }
            }
            catch
            {
                // Skip if assembly read fails
            }
        }

        return [.. results];
    }

    private sealed class LicenseInfo
    {
        public bool AllowsCommercialUse { get; init; }
        public bool AllowsModification { get; init; }
        public bool AllowsDistribution { get; init; }
        public bool HasPatentGrant { get; init; }
        public bool IsCopyleft { get; init; }
        public string? Notes { get; init; }
    }

    [McpServerTool(Name = "list_nuget_analyzers")]
    [Description("Lists Roslyn analyzers and code analysis rules included in a NuGet package.")]
    public async Task<string> ListNuGetAnalyzers(
        [Description("The package ID (e.g., 'Microsoft.CodeAnalysis.NetAnalyzers')")]
        string packageId,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }


            var downloadResult = await _nugetService.DownloadPackageAsync(
                packageId, packageVersion, cancellationToken);

            if (!downloadResult.IsSuccess)
            {
                return $"Failed to download package: '{packageId}' v{packageVersion}\n{downloadResult.Error}";
            }

            var packagePath = downloadResult.Path!;

            var sb = new StringBuilder();
            sb.AppendLine($"Roslyn Analyzers: {packageId} v{packageVersion}");
            sb.AppendLine(new string('=', 80));

            // Check analyzers/ folder
            var analyzersPath = Path.Combine(packagePath, "analyzers");
            var analyzerDlls = new List<(string Path, string Language, string Runtime)>();

            if (Directory.Exists(analyzersPath))
            {
                foreach (var file in Directory.GetFiles(analyzersPath, "*.dll", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(analyzersPath, file);
                    var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    var language = "any";
                    var runtime = "any";

                    // analyzers/{language}/{runtime}/
                    if (parts.Length >= 2)
                    {
                        var folder = parts[0].ToLowerInvariant();
                        if (folder is "dotnet" or "roslyn" or "portable")
                        {
                            runtime = folder;
                            if (parts.Length >= 3)
                            {
                                language = parts[1].ToLowerInvariant() switch
                                {
                                    "cs" => "C#",
                                    "vb" => "VB.NET",
                                    _ => parts[1]
                                };
                            }
                        }
                        else if (folder is "cs" or "vb")
                        {
                            language = folder == "cs" ? "C#" : "VB.NET";
                        }
                    }

                    analyzerDlls.Add((file, language, runtime));
                }
            }

            if (analyzerDlls.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Warning + " No analyzer assemblies found in this package.");
                sb.AppendLine("   This package may not contain Roslyn analyzers.");
                return sb.ToString();
            }

            // Analyzer assembly info
            sb.AppendLine();
            sb.AppendLine(Emoji.Package + " Analyzer Assemblies:");
            sb.AppendLine(new string('-', 60));

            var allDiagnostics = new List<(string Id, string Title, string Category, string Severity)>();

            foreach (var (dllPath, language, runtime) in analyzerDlls.OrderBy(a => a.Path))
            {
                var fileName = Path.GetFileName(dllPath);
                sb.AppendLine($"  • {fileName}");
                sb.AppendLine($"    Language: {language}, Runtime: {runtime}");

                // Extract diagnostic IDs from analyzer
                try
                {
                    var diagnostics = ExtractDiagnosticIds(dllPath);
                    if (diagnostics.Count > 0)
                    {
                        sb.AppendLine($"    Diagnostics: {diagnostics.Count}");
                        allDiagnostics.AddRange(diagnostics);
                    }
                }
                catch
                {
                    // Skip if diagnostic extraction fails
                }
            }

            // Diagnostic rules list
            if (allDiagnostics.Count > 0)
            {
                var uniqueDiagnostics = allDiagnostics
                    .DistinctBy(d => d.Id)
                    .OrderBy(d => d.Id)
                    .ToList();

                sb.AppendLine();
                sb.AppendLine(Emoji.Clipboard + " Diagnostic Rules:");
                sb.AppendLine(new string('-', 60));

                // Group by category
                var byCategory = uniqueDiagnostics
                    .GroupBy(d => d.Category)
                    .OrderBy(g => g.Key);

                foreach (var category in byCategory)
                {
                    sb.AppendLine();
                    sb.AppendLine($"  [{category.Key}]");

                    foreach (var (Id, Title, Category, Severity) in category.Take(20))
                    {
                        var severityIcon = Severity switch
                        {
                            "Error" => Emoji.CrossMark + "",
                            "Warning" => Emoji.Warning + "",
                            "Info" => Emoji.Info + "",
                            _ => Emoji.Bulb + ""
                        };
                        sb.AppendLine($"    {severityIcon} {Id}: {Title}");
                    }

                    if (category.Count() > 20)
                    {
                        sb.AppendLine($"    ... and {category.Count() - 20} more rules");
                    }
                }

                sb.AppendLine();
                sb.AppendLine($"Total diagnostic rules: {uniqueDiagnostics.Count}");
            }

            // Check editorconfig rule files
            var ruleSets = Directory.GetFiles(packagePath, "*.ruleset", SearchOption.AllDirectories).ToList();
            var editorConfigs = Directory.GetFiles(packagePath, ".editorconfig*", SearchOption.AllDirectories).ToList();
            var globalConfigs = Directory.GetFiles(packagePath, "*.globalconfig", SearchOption.AllDirectories).ToList();

            if (ruleSets.Count > 0 || editorConfigs.Count > 0 || globalConfigs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Gear + " Configuration Files:");
                sb.AppendLine(new string('-', 60));

                foreach (var file in ruleSets.Concat(editorConfigs).Concat(globalConfigs))
                {
                    var relativePath = Path.GetRelativePath(packagePath, file);
                    sb.AppendLine($"  • {relativePath}");
                }
            }

            // Summary
            sb.AppendLine();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("Summary:");
            sb.AppendLine($"  Analyzer assemblies: {analyzerDlls.Count}");
            sb.AppendLine($"  Total diagnostic rules: {allDiagnostics.DistinctBy(d => d.Id).Count()}");
            sb.AppendLine($"  Configuration files: {ruleSets.Count + editorConfigs.Count + globalConfigs.Count}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error listing analyzers: {ex.Message}";
        }
    }

    private static List<(string Id, string Title, string Category, string Severity)> ExtractDiagnosticIds(string assemblyPath)
    {
        var results = new List<(string, string, string, string)>();

        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

            foreach (var type in assembly.MainModule.Types)
            {
                // Find types inheriting DiagnosticAnalyzer
                if (!InheritsFromDiagnosticAnalyzer(type))
                    continue;

                // Extract info from DiagnosticDescriptor fields
                foreach (var field in type.Fields)
                {
                    if (field.FieldType.Name != "DiagnosticDescriptor")
                        continue;

                    // Find initialization value in static constructor (simple heuristic)
                    var fieldName = field.Name;

                    // Extract common ID pattern
                    if (fieldName.Contains("Descriptor") || fieldName.Contains("Rule"))
                    {
                        // ID is usually in "CA1234" or "IDE0001" format
                        var possibleId = ExtractDiagnosticIdFromType(type, field.Name);
                        if (!string.IsNullOrEmpty(possibleId))
                        {
                            results.Add((possibleId, fieldName.Replace("Descriptor", "").Replace("Rule", ""), "General", "Warning"));
                        }
                    }
                }
            }
        }
        catch
        {
            // Return empty list if analysis fails
        }

        return results;
    }

    private static bool InheritsFromDiagnosticAnalyzer(TypeDefinition type)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (current.Name == "DiagnosticAnalyzer")
                return true;

            try
            {
                current = current.Resolve()?.BaseType;
            }
            catch
            {
                break;
            }
        }
        return false;
    }

    private static string? ExtractDiagnosticIdFromType(TypeDefinition type, string fieldName)
    {
        // Find ID from constant fields
        foreach (var field in type.Fields)
        {
            if (!field.HasConstant || field.FieldType.FullName != "System.String")
                continue;

            var value = field.Constant?.ToString();
            if (value != null && TfmRegex().IsMatch(value))
            {
                return value;
            }
        }

        return null;
    }

    [McpServerTool(Name = "analyze_nuget_compatibility")]
    [Description("Analyzes compatibility between a NuGet package and a target framework. Shows which TFMs are supported and potential compatibility issues.")]
    public async Task<string> AnalyzeNuGetCompatibility(
        [Description("The package ID (e.g., 'Newtonsoft.Json')")]
        string packageId,
        [Description("The target framework to check compatibility for (e.g., 'net8.0', 'net48', 'netstandard2.0')")]
        string targetFramework,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Compatibility Analysis: {packageId} v{packageVersion}");
            sb.AppendLine($"Target Framework: {targetFramework}");
            sb.AppendLine(new string('=', 80));

            // Download package
            var downloadResult = await _nugetService.DownloadPackageAsync(
                packageId, packageVersion, cancellationToken);

            if (!downloadResult.IsSuccess)
            {
                return $"Failed to download package: '{packageId}' v{packageVersion}\n{downloadResult.Error}";
            }

            var packagePath = downloadResult.Path!;

            // Get package metadata
            var metadata = await _nugetService.GetPackageMetadataAsync(
                packageId, packageVersion, cancellationToken);

            // Analyze supported TFMs
            var assemblyInfo = await _nugetService.GetAllPackageAssembliesAsync(packagePath, cancellationToken);
            var supportedTfms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tfm in assemblyInfo.LibAssemblies.Keys)
                supportedTfms.Add(tfm);
            foreach (var tfm in assemblyInfo.RefAssemblies.Keys)
                supportedTfms.Add(tfm);

            sb.AppendLine();
            sb.AppendLine(Emoji.Package + " Supported Target Frameworks:");
            sb.AppendLine(new string('-', 60));

            var orderedTfms = supportedTfms
                .OrderBy(t => GetTfmSortOrder(t))
                .ThenBy(t => t)
                .ToList();

            foreach (var tfm in orderedTfms)
            {
                var isMatch = IsCompatibleTfm(tfm, targetFramework);
                var icon = isMatch ? Emoji.CheckMark + "" : "  ";
                sb.AppendLine($"  {icon} {tfm}");
            }

            // Compatibility assessment
            sb.AppendLine();
            sb.AppendLine(Emoji.MagnifyingGlass + " Compatibility Analysis:");
            sb.AppendLine(new string('-', 60));

            var compatibility = AnalyzeTfmCompatibility(targetFramework, orderedTfms);

            if (compatibility.IsCompatible)
            {
                sb.AppendLine($"  ✅ COMPATIBLE with {targetFramework}");
                sb.AppendLine();
                sb.AppendLine($"  Best matching TFM: {compatibility.BestMatch}");

                if (compatibility.BestMatch != targetFramework)
                {
                    sb.AppendLine();
                    sb.AppendLine($"  ℹ️ Package doesn't have exact match for {targetFramework}.");
                    sb.AppendLine($"     Using compatible fallback: {compatibility.BestMatch}");
                }
            }
            else
            {
                sb.AppendLine($"  ❌ NOT COMPATIBLE with {targetFramework}");
                sb.AppendLine();
                sb.AppendLine("  Reasons:");
                foreach (var reason in compatibility.IncompatibilityReasons)
                {
                    sb.AppendLine($"    • {reason}");
                }

                if (compatibility.SuggestedAlternatives.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("  💡 Alternatives:");
                    foreach (var alt in compatibility.SuggestedAlternatives)
                    {
                        sb.AppendLine($"    • {alt}");
                    }
                }
            }

            // Check dependencies
            if (metadata?.DependencySets != null)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Clipboard + " Dependencies for Target Framework:");
                sb.AppendLine(new string('-', 60));

                var bestDepSet = FindBestDependencySet(metadata.DependencySets, targetFramework);

                if (bestDepSet != null)
                {
                    sb.AppendLine($"  Using dependency set: {bestDepSet.TargetFramework.GetShortFolderName()}");
                    sb.AppendLine();

                    var deps = bestDepSet.Packages.ToList();
                    if (deps.Count > 0)
                    {
                        foreach (var dep in deps.OrderBy(d => d.Id))
                        {
                            sb.AppendLine($"    - {dep.Id} {dep.VersionRange}");
                        }
                    }
                    else
                    {
                        sb.AppendLine("    (No dependencies)");
                    }
                }
                else
                {
                    sb.AppendLine("  ⚠️ No matching dependency set found");
                }
            }

            // Check runtime support
            var runtimeInfo = AnalyzeNativeLibraries(packagePath);
            if (runtimeInfo.NativeLibraries.Count > 0 || runtimeInfo.RuntimeNativeLibraries.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Desktop + " Native Runtime Support:");
                sb.AppendLine(new string('-', 60));

                var allRids = runtimeInfo.NativeLibraries.Keys
                    .Concat(runtimeInfo.RuntimeNativeLibraries.Keys.Select(k => k.Split('/')[0]))
                    .Distinct()
                    .OrderBy(r => r)
                    .ToList();

                var grouped = new Dictionary<string, List<string>>();
                foreach (var rid in allRids)
                {
                    var platform = GetPlatformFromRid(rid);
                    if (!grouped.ContainsKey(platform))
                        grouped[platform] = [];
                    grouped[platform].Add(rid);
                }

                foreach (var (platform, rids) in grouped.OrderBy(g => g.Key))
                {
                    sb.AppendLine($"  {platform}: {string.Join(", ", rids)}");
                }

                sb.AppendLine();
                sb.AppendLine("  ⚠️ This package contains native libraries.");
                sb.AppendLine("     Ensure your deployment platform is supported.");
            }

            // Summary
            sb.AppendLine();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("Summary:");
            sb.AppendLine($"  Package: {packageId} v{packageVersion}");
            sb.AppendLine($"  Target: {targetFramework}");
            sb.AppendLine($"  Compatible: {(compatibility.IsCompatible ? Emoji.CheckMark + " Yes" : Emoji.CrossMark + " No")}");
            if (compatibility.IsCompatible)
            {
                sb.AppendLine($"  Best Match: {compatibility.BestMatch}");
            }
            sb.AppendLine($"  Supported TFMs: {supportedTfms.Count}");
            sb.AppendLine($"  Has Native Libs: {(runtimeInfo.NativeLibraries.Count > 0 ? "Yes" : "No")}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error analyzing compatibility: {ex.Message}";
        }
    }

    private static int GetTfmSortOrder(string tfm)
    {
        var lower = tfm.ToLowerInvariant();
        if (lower.StartsWith("net8")) return 10;
        if (lower.StartsWith("net7")) return 11;
        if (lower.StartsWith("net6")) return 12;
        if (lower.StartsWith("net5")) return 13;
        if (lower.StartsWith("netcoreapp3")) return 20;
        if (lower.StartsWith("netcoreapp2")) return 21;
        if (lower.StartsWith("netcoreapp1")) return 22;
        if (lower.StartsWith("netstandard2.1")) return 30;
        if (lower.StartsWith("netstandard2.0")) return 31;
        if (lower.StartsWith("netstandard1")) return 32;
        if (lower.StartsWith("net4")) return 40;
        return 100;
    }

    private static bool IsCompatibleTfm(string packageTfm, string targetTfm)
    {
        var pkg = packageTfm.ToLowerInvariant();
        var target = targetTfm.ToLowerInvariant();
        return pkg == target;
    }

    private static string GetPlatformFromRid(string rid)
    {
        var lower = rid.ToLowerInvariant();
        if (lower.StartsWith("win")) return "Windows";
        if (lower.StartsWith("linux") || lower.StartsWith("ubuntu") || 
            lower.StartsWith("debian") || lower.StartsWith("rhel") || 
            lower.StartsWith("alpine") || lower.StartsWith("centos")) return "Linux";
        if (lower.StartsWith("osx") || lower.StartsWith("macos")) return "macOS";
        if (lower.StartsWith("ios")) return "iOS";
        if (lower.StartsWith("android")) return "Android";
        return "Other";
    }

    private static TfmCompatibilityResult AnalyzeTfmCompatibility(string targetTfm, List<string> supportedTfms)
    {
        var target = targetTfm.ToLowerInvariant();
        var result = new TfmCompatibilityResult();

        // If exact match
        var exact = supportedTfms.FirstOrDefault(t => t.Equals(target, StringComparison.InvariantCultureIgnoreCase));
        if (exact != null)
        {
            result.IsCompatible = true;
            result.BestMatch = exact;
            return result;
        }

        // .NET (Core/5+) target
        if (target.StartsWith("net") && !target.StartsWith("netstandard") && 
            !target.StartsWith("netcoreapp") && !target.StartsWith("netframework"))
        {
            // net8.0 is compatible with netstandard2.1, netstandard2.0, net7.0, net6.0, etc.
            var compatibleOrder = new[] { "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.5", "netstandard1.4", "netstandard1.3" };

            foreach (var compat in compatibleOrder)
            {
                var match = supportedTfms.FirstOrDefault(t => t.StartsWith(compat, StringComparison.InvariantCultureIgnoreCase));
                if (match != null)
                {
                    result.IsCompatible = true;
                    result.BestMatch = match;
                    return result;
                }
            }

            // net5.0+ is also compatible with previous versions
            if (TryParseDotNetVersion(target, out var targetVersion))
            {
                foreach (var tfm in supportedTfms.OrderByDescending(t => t))
                {
                    if (TryParseDotNetVersion(tfm.ToLowerInvariant(), out var tfmVersion))
                    {
                        if (tfmVersion <= targetVersion)
                        {
                            result.IsCompatible = true;
                            result.BestMatch = tfm;
                            return result;
                        }
                    }
                }
            }
        }

        // .NET Framework target
        if (target.StartsWith("net4") || target.StartsWith("netframework"))
        {
            var netStandard20 = supportedTfms.FirstOrDefault(t => t.StartsWith("netstandard2.0", StringComparison.InvariantCultureIgnoreCase));
            if (netStandard20 != null && (target.StartsWith("net48") || target.StartsWith("net472") || target.StartsWith("net471") || target.StartsWith("net47")))
            {
                result.IsCompatible = true;
                result.BestMatch = netStandard20;
                return result;
            }

            var netStandard16 = supportedTfms.FirstOrDefault(t => t.StartsWith("netstandard1.", StringComparison.InvariantCultureIgnoreCase));
            if (netStandard16 != null && (target.StartsWith("net46") || target.StartsWith("net45")))
            {
                result.IsCompatible = true;
                result.BestMatch = netStandard16;
                return result;
            }

            result.IncompatibilityReasons.Add($"Package does not support .NET Framework {target}");
            result.SuggestedAlternatives.Add("Consider upgrading to .NET 6+ or .NET Framework 4.7.2+");
        }

        // .NET Standard target
        if (target.StartsWith("netstandard"))
        {
            var targetStdVersion = target.Replace("netstandard", "");

            foreach (var tfm in supportedTfms)
            {
                if (tfm.StartsWith("netstandard", StringComparison.InvariantCultureIgnoreCase))
                {
                    var stdVersion = tfm.ToLowerInvariant().Replace("netstandard", "");
                    if (CompareVersionStrings(stdVersion, targetStdVersion) <= 0)
                    {
                        result.IsCompatible = true;
                        result.BestMatch = tfm;
                        return result;
                    }
                }
            }

            result.IncompatibilityReasons.Add($"Package requires higher .NET Standard version than {target}");
        }

        if (!result.IsCompatible && result.IncompatibilityReasons.Count == 0)
        {
            result.IncompatibilityReasons.Add($"No compatible TFM found for {targetTfm}");
            result.SuggestedAlternatives.Add($"Supported TFMs: {string.Join(", ", supportedTfms.Take(5))}");
        }

        return result;
    }

    private static bool TryParseDotNetVersion(string tfm, out decimal version)
    {
        version = 0;
        var match = NetVersionRegex().Match(tfm);
        if (match.Success)
        {
            version = decimal.Parse($"{match.Groups[1].Value}.{match.Groups[2].Value}");
            return true;
        }
        return false;
    }

    private static int CompareVersionStrings(string v1, string v2)
    {
        var parts1 = v1.Split('.').Select(int.Parse).ToArray();
        var parts2 = v2.Split('.').Select(int.Parse).ToArray();

        for (int i = 0; i < Math.Max(parts1.Length, parts2.Length); i++)
        {
            var p1 = i < parts1.Length ? parts1[i] : 0;
            var p2 = i < parts2.Length ? parts2[i] : 0;

            if (p1 < p2) return -1;
            if (p1 > p2) return 1;
        }
        return 0;
    }

    private sealed class TfmCompatibilityResult
    {
        public bool IsCompatible { get; set; }
        public string? BestMatch { get; set; }
        public List<string> IncompatibilityReasons { get; } = [];
        public List<string> SuggestedAlternatives { get; } = [];
    }

    [McpServerTool(Name = "find_package_by_type")]
    [Description("Searches for NuGet packages that provide a specific type. Useful for resolving CS0246 errors ('type or namespace not found').")]
    public async Task<string> FindPackageByType(
        [Description("The type name to search for (e.g., 'JsonSerializer', 'ILogger', 'HttpClient')")]
        string typeName,
        [Description("Optional namespace hint (e.g., 'System.Text.Json', 'Microsoft.Extensions.Logging')")]
        string? namespaceHint = null,
        [Description("Maximum number of packages to search (default: 20)")]
        int maxPackagesToSearch = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Finding packages that provide type: {typeName}");
            if (!string.IsNullOrEmpty(namespaceHint))
            {
                sb.AppendLine($"Namespace hint: {namespaceHint}");
            }
            sb.AppendLine(new string('=', 80));

            var foundPackages = new List<(string PackageId, string Version, string TypeFullName, string Namespace)>();

            // Strategy 1: Well-known type-package mapping
            var knownMappings = GetKnownTypePackageMappings();
            var searchKey = string.IsNullOrEmpty(namespaceHint) 
                ? typeName 
                : $"{namespaceHint}.{typeName}";

            if (knownMappings.TryGetValue(typeName, out var knownPackage))
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Books + " Known Package Mapping:");
                sb.AppendLine(new string('-', 60));
                sb.AppendLine($"  Type: {typeName}");
                sb.AppendLine($"  Package: {knownPackage.PackageId}");
                sb.AppendLine($"  Namespace: {knownPackage.Namespace}");
                sb.AppendLine();
                sb.AppendLine($"  💡 Install with: dotnet add package {knownPackage.PackageId}");
                sb.AppendLine($"  💡 Add using: using {knownPackage.Namespace};");

                foundPackages.Add((knownPackage.PackageId, "latest", $"{knownPackage.Namespace}.{typeName}", knownPackage.Namespace));
            }

            // Strategy 2: Search related packages if namespace hint provided
            if (!string.IsNullOrEmpty(namespaceHint))
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.MagnifyingGlass + " Searching by namespace...");
                sb.AppendLine(new string('-', 60));

                // Infer package name from namespace
                var possiblePackageNames = InferPackageNamesFromNamespace(namespaceHint);

                foreach (var possibleName in possiblePackageNames.Take(5))
                {
                    try
                    {
                        var versions = await _nugetService.GetPackageVersionsAsync(
                            possibleName, includePrerelease: false, cancellationToken);

                        var latestVersion = versions.FirstOrDefault();
                        if (latestVersion != null)
                        {
                            // Check type in package
                            var downloadResult = await _nugetService.DownloadPackageAsync(
                                possibleName, latestVersion, cancellationToken);

                            if (downloadResult.IsSuccess)
                            {
                                var packagePath = downloadResult.Path!;
                                var assemblies = await _nugetService.GetPackageAssembliesAsync(
                                    packagePath, null, cancellationToken);

                                foreach (var assemblyPath in assemblies)
                                {
                                    if (!File.Exists(assemblyPath)) continue;

                                    try
                                    {
                                        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

                                        var matchingType = assembly.MainModule.Types
                                            .FirstOrDefault(t => t.IsPublic && 
                                                (t.Name == typeName || t.FullName.EndsWith($".{typeName}")));

                                        if (matchingType != null)
                                        {
                                            var existing = foundPackages.FirstOrDefault(f => 
                                                f.PackageId.Equals(possibleName, StringComparison.OrdinalIgnoreCase));

                                            if (existing == default)
                                            {
                                                foundPackages.Add((possibleName, latestVersion.ToString(), 
                                                    matchingType.FullName, matchingType.Namespace ?? ""));

                                                sb.AppendLine($"  ✅ Found in: {possibleName} v{latestVersion}");
                                                sb.AppendLine($"     Type: {matchingType.FullName}");
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            // Strategy 3: Search type name using NuGet search API
            sb.AppendLine();
            sb.AppendLine(Emoji.MagnifyingGlassLeft + " Searching NuGet packages...");
            sb.AppendLine(new string('-', 60));

            var searchTerms = new List<string> { typeName };
            if (!string.IsNullOrEmpty(namespaceHint))
            {
                searchTerms.Add(namespaceHint.Split('.').Last());
                searchTerms.Add(namespaceHint);
            }

            var searchedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var searchTerm in searchTerms)
            {
                try
                {
                    var packages = await _nugetService.SearchPackagesAsync(
                        searchTerm, includePrerelease: false, 0, maxPackagesToSearch, cancellationToken);

                    foreach (var package in packages)
                    {
                        if (searchedPackages.Contains(package.Identity.Id))
                            continue;

                        searchedPackages.Add(package.Identity.Id);

                        try
                        {
                            var downloadResult = await _nugetService.DownloadPackageAsync(
                                package.Identity.Id, package.Identity.Version, cancellationToken);

                            if (!downloadResult.IsSuccess) continue;

                            var packagePath = downloadResult.Path!;
                            var assemblies = await _nugetService.GetPackageAssembliesAsync(
                                packagePath, null, cancellationToken);

                            foreach (var assemblyPath in assemblies)
                            {
                                if (!File.Exists(assemblyPath)) continue;

                                try
                                {
                                    using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

                                    var matchingType = assembly.MainModule.Types
                                        .FirstOrDefault(t => t.IsPublic && 
                                            (t.Name == typeName || t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)));

                                    if (matchingType != null)
                                    {
                                        var existing = foundPackages.FirstOrDefault(f => 
                                            f.PackageId.Equals(package.Identity.Id, StringComparison.OrdinalIgnoreCase));

                                        if (existing == default)
                                        {
                                            foundPackages.Add((package.Identity.Id, package.Identity.Version.ToString(), 
                                                matchingType.FullName, matchingType.Namespace ?? ""));

                                            sb.AppendLine($"  ✅ Found in: {package.Identity.Id} v{package.Identity.Version}");
                                            sb.AppendLine($"     Type: {matchingType.FullName}");
                                            sb.AppendLine($"     Downloads: {package.DownloadCount:N0}");
                                        }
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }

                        if (foundPackages.Count >= 10)
                            break;
                    }
                }
                catch { }

                if (foundPackages.Count >= 10)
                    break;
            }

            // Result summary
            sb.AppendLine();
            sb.AppendLine(new string('=', 80));

            if (foundPackages.Count > 0)
            {
                sb.AppendLine($"Found {foundPackages.Count} package(s) containing '{typeName}':");
                sb.AppendLine();

                var orderedResults = foundPackages
                    .DistinctBy(f => f.PackageId.ToLowerInvariant())
                    .OrderBy(f => f.PackageId)
                    .ToList();

                foreach (var (packageId, version, typeFullName, ns) in orderedResults)
                {
                    sb.AppendLine(Emoji.Package + $" {packageId}");
                    sb.AppendLine($"   Type: {typeFullName}");
                    sb.AppendLine($"   Install: dotnet add package {packageId}");
                    if (!string.IsNullOrEmpty(ns))
                    {
                        sb.AppendLine($"   Using: using {ns};");
                    }
                    sb.AppendLine();
                }

                // Most recommended package
                var recommended = orderedResults.FirstOrDefault();
                if (recommended != default)
                {
                    sb.AppendLine(Emoji.Bulb + " Recommended:");
                    sb.AppendLine($"   dotnet add package {recommended.PackageId}");
                    if (!string.IsNullOrEmpty(recommended.Namespace))
                    {
                        sb.AppendLine($"   using {recommended.Namespace};");
                    }
                }
            }
            else
            {
                sb.AppendLine(Emoji.CrossMark + $" No packages found containing type '{typeName}'");
                sb.AppendLine();
                sb.AppendLine(Emoji.Bulb + " Suggestions:");
                sb.AppendLine("  • Check the spelling of the type name");
                sb.AppendLine("  • Try providing a namespace hint");
                sb.AppendLine("  • The type might be in a .NET built-in assembly (no package needed)");
                sb.AppendLine("  • Search on nuget.org directly");

                // Check built-in type
                var builtInHint = GetBuiltInTypeHint(typeName);
                if (!string.IsNullOrEmpty(builtInHint))
                {
                    sb.AppendLine();
                    sb.AppendLine($"  ℹ️ {builtInHint}");
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error finding package by type: {ex.Message}";
        }
    }

    private static Dictionary<string, (string PackageId, string Namespace)> GetKnownTypePackageMappings()
    {
        return new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            // System.Text.Json
            ["JsonSerializer"] = ("System.Text.Json", "System.Text.Json"),
            ["JsonDocument"] = ("System.Text.Json", "System.Text.Json"),
            ["JsonElement"] = ("System.Text.Json", "System.Text.Json"),
            ["JsonSerializerOptions"] = ("System.Text.Json", "System.Text.Json"),

            // Newtonsoft.Json
            ["JObject"] = ("Newtonsoft.Json", "Newtonsoft.Json.Linq"),
            ["JArray"] = ("Newtonsoft.Json", "Newtonsoft.Json.Linq"),
            ["JToken"] = ("Newtonsoft.Json", "Newtonsoft.Json.Linq"),
            ["JsonConvert"] = ("Newtonsoft.Json", "Newtonsoft.Json"),

            // Microsoft.Extensions.Logging
            ["ILogger"] = ("Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.Logging"),
            ["ILoggerFactory"] = ("Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.Logging"),
            ["LogLevel"] = ("Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.Logging"),

            // Microsoft.Extensions.DependencyInjection
            ["IServiceCollection"] = ("Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.DependencyInjection"),
            ["IServiceProvider"] = ("Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.DependencyInjection"),
            ["ServiceProvider"] = ("Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection"),

            // Microsoft.Extensions.Configuration
            ["IConfiguration"] = ("Microsoft.Extensions.Configuration.Abstractions", "Microsoft.Extensions.Configuration"),
            ["ConfigurationBuilder"] = ("Microsoft.Extensions.Configuration", "Microsoft.Extensions.Configuration"),

            // Microsoft.Extensions.Options
            ["IOptions"] = ("Microsoft.Extensions.Options", "Microsoft.Extensions.Options"),

            // Entity Framework Core
            ["DbContext"] = ("Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore"),
            ["DbSet"] = ("Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore"),

            // AutoMapper
            ["IMapper"] = ("AutoMapper", "AutoMapper"),
            ["MapperConfiguration"] = ("AutoMapper", "AutoMapper"),

            // MediatR
            ["IMediator"] = ("MediatR.Contracts", "MediatR"),
            ["IRequest"] = ("MediatR.Contracts", "MediatR"),

            // FluentValidation
            ["AbstractValidator"] = ("FluentValidation", "FluentValidation"),
            ["IValidator"] = ("FluentValidation", "FluentValidation"),

            // Polly
            ["IAsyncPolicy"] = ("Polly", "Polly"),
            ["Policy"] = ("Polly", "Polly"),

            // Serilog
            ["Log"] = ("Serilog", "Serilog"),
            ["LoggerConfiguration"] = ("Serilog", "Serilog"),

            // xUnit
            ["Fact"] = ("xunit", "Xunit"),
            ["Theory"] = ("xunit", "Xunit"),

            // NUnit
            ["TestFixture"] = ("NUnit", "NUnit.Framework"),
            ["Test"] = ("NUnit", "NUnit.Framework"),

            // Moq
            ["Mock"] = ("Moq", "Moq"),

            // HttpClient (built-in but common question)
            ["HttpClient"] = ("System.Net.Http", "System.Net.Http"),
            ["HttpClientFactory"] = ("Microsoft.Extensions.Http", "System.Net.Http"),
            ["IHttpClientFactory"] = ("Microsoft.Extensions.Http", "System.Net.Http"),

            // ASP.NET Core
            ["ControllerBase"] = ("Microsoft.AspNetCore.Mvc.Core", "Microsoft.AspNetCore.Mvc"),
            ["ApiController"] = ("Microsoft.AspNetCore.Mvc.Core", "Microsoft.AspNetCore.Mvc"),
        };
    }

    private static List<string> InferPackageNamesFromNamespace(string ns)
    {
        var results = new List<string>();
        var parts = ns.Split('.');

        // Full namespace
        results.Add(ns);

        // Front part combinations
        for (int i = parts.Length; i >= 2; i--)
        {
            results.Add(string.Join(".", parts.Take(i)));
        }

        // .Abstractions version
        results.Add($"{ns}.Abstractions");

        return [.. results.Distinct()];
    }

    private static string? GetBuiltInTypeHint(string typeName)
    {
        var builtInTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HttpClient"] = "HttpClient is built-in. Add 'using System.Net.Http;'",
            ["Task"] = "Task is built-in. Add 'using System.Threading.Tasks;'",
            ["List"] = "List<T> is built-in. Add 'using System.Collections.Generic;'",
            ["Dictionary"] = "Dictionary<TKey, TValue> is built-in. Add 'using System.Collections.Generic;'",
            ["Regex"] = "Regex is built-in. Add 'using System.Text.RegularExpressions;'",
            ["StringBuilder"] = "StringBuilder is built-in. Add 'using System.Text;'",
            ["File"] = "File is built-in. Add 'using System.IO;'",
            ["Path"] = "Path is built-in. Add 'using System.IO;'",
            ["Directory"] = "Directory is built-in. Add 'using System.IO;'",
            ["Stream"] = "Stream is built-in. Add 'using System.IO;'",
            ["DateTime"] = "DateTime is built-in. No using statement needed.",
            ["TimeSpan"] = "TimeSpan is built-in. No using statement needed.",
            ["Guid"] = "Guid is built-in. No using statement needed.",
        };

        return builtInTypes.TryGetValue(typeName, out var hint) ? hint : null;
    }

    [McpServerTool(Name = "get_nuget_changelog")]
    [Description("Gets changelog or release notes for a NuGet package from the package itself or its GitHub repository.")]
    public async Task<string> GetNuGetChangelog(
        [Description("The package ID (e.g., 'Newtonsoft.Json')")]
        string packageId,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        [Description("Maximum number of lines to display (default: 300, 0 for all)")]
        int maxLines = 300,
        CancellationToken cancellationToken = default)
    {
        try
        {
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Changelog: {packageId} v{packageVersion}");
            sb.AppendLine(new string('=', 80));

            // 1. Check CHANGELOG file in package
            var downloadResult = await _nugetService.DownloadPackageAsync(
                packageId, packageVersion, cancellationToken);

            string? packagePath = downloadResult.IsSuccess ? downloadResult.Path : null;

            if (packagePath != null)
            {
                var changelogFile = FindChangelogFile(packagePath);

                if (changelogFile != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Source: Package (embedded)");
                    sb.AppendLine($"File: {Path.GetFileName(changelogFile)}");
                    sb.AppendLine(new string('-', 60));
                    sb.AppendLine();

                    var lines = await File.ReadAllLinesAsync(changelogFile, cancellationToken);
                    var displayLines = maxLines > 0 ? Math.Min(maxLines, lines.Length) : lines.Length;

                    for (int i = 0; i < displayLines; i++)
                    {
                        sb.AppendLine(lines[i]);
                    }

                    if (lines.Length > displayLines)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"... ({lines.Length - displayLines} more lines)");
                    }

                    return sb.ToString();
                }
            }

            // 2. Check Release Notes in package metadata
            var metadata = await _nugetService.GetPackageMetadataAsync(
                packageId, packageVersion, cancellationToken);

            if (metadata != null)
            {
                // nuspec releaseNotes field (not directly accessible from metadata, check from package)
                var nuspecPath = Path.Combine(packagePath ?? "", $"{packageId}.nuspec");
                if (File.Exists(nuspecPath))
                {
                    var releaseNotes = ExtractReleaseNotesFromNuspec(nuspecPath);
                    if (!string.IsNullOrWhiteSpace(releaseNotes))
                    {
                        sb.AppendLine();
                        sb.AppendLine("Source: Package (.nuspec)");
                        sb.AppendLine(new string('-', 60));
                        sb.AppendLine();
                        sb.AppendLine(releaseNotes);
                        sb.AppendLine();
                    }
                }
            }

            // 3. Get release info from GitHub
            if (metadata != null)
            {
                var repoUrl = metadata.ProjectUrl?.ToString();
                var repoInfo = RepositoryService.ParseRepositoryUrl(repoUrl);

                if (repoInfo?.Type == RepositoryType.GitHub)
                {
                    var releases = await _repoService.GetGitHubReleasesAsync(
                        repoInfo.Owner, repoInfo.Name, 10, cancellationToken);

                    if (releases != null && releases.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"Source: GitHub Releases ({repoInfo.Owner}/{repoInfo.Name})");
                        sb.AppendLine(new string('-', 60));

                        foreach (var release in releases)
                        {
                            sb.AppendLine();
                            sb.AppendLine(Emoji.Package + $" {release.TagName} - {release.Name}");
                            sb.AppendLine($"   Published: {release.PublishedAt:yyyy-MM-dd}");
                            if (release.Prerelease)
                            {
                                sb.AppendLine("   ⚠️ Pre-release");
                            }

                            if (!string.IsNullOrWhiteSpace(release.Body))
                            {
                                sb.AppendLine();
                                var bodyLines = release.Body.Split('\n');
                                var displayBodyLines = Math.Min(30, bodyLines.Length);

                                for (int i = 0; i < displayBodyLines; i++)
                                {
                                    sb.AppendLine($"   {bodyLines[i].TrimEnd('\r')}");
                                }

                                if (bodyLines.Length > displayBodyLines)
                                {
                                    sb.AppendLine($"   ... ({bodyLines.Length - displayBodyLines} more lines)");
                                }
                            }

                            sb.AppendLine($"   🔗 {release.HtmlUrl}");
                        }

                        return sb.ToString();
                    }
                }
            }

            // If changelog not found
            sb.AppendLine();
            sb.AppendLine(Emoji.CrossMark + " No changelog found.");
            sb.AppendLine();
            sb.AppendLine("Checked locations:");
            sb.AppendLine("  • CHANGELOG.md, HISTORY.md in package");
            sb.AppendLine("  • Release notes in .nuspec");
            sb.AppendLine("  • GitHub Releases");

            if (metadata?.ProjectUrl != null)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Bulb + $" Check project URL: {metadata.ProjectUrl}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting changelog: {ex.Message}";
        }
    }

    private static string? FindChangelogFile(string packagePath)
    {
        var patterns = new[]
        {
            "CHANGELOG*", "changelog*", "Changelog*",
            "HISTORY*", "history*", "History*",
            "CHANGES*", "changes*", "Changes*",
            "RELEASE*", "release*", "Release*",
            "NEWS*", "news*"
        };

        foreach (var pattern in patterns)
        {
            var files = Directory.GetFiles(packagePath, pattern, SearchOption.TopDirectoryOnly);
            var mdFile = files.FirstOrDefault(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
            if (mdFile != null)
                return mdFile;

            var txtFile = files.FirstOrDefault(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
            if (txtFile != null)
                return txtFile;

            var noExt = files.FirstOrDefault(f => !Path.HasExtension(f));
            if (noExt != null)
                return noExt;
        }

        return null;
    }

    private static string? ExtractReleaseNotesFromNuspec(string nuspecPath)
    {
        try
        {
            var doc = System.Xml.Linq.XDocument.Load(nuspecPath);
            var ns = doc.Root?.GetDefaultNamespace() ?? System.Xml.Linq.XNamespace.None;

            var releaseNotes = doc.Descendants(ns + "releaseNotes").FirstOrDefault()?.Value;
            return releaseNotes?.Trim();
        }
        catch
        {
            return null;
        }
    }

    [McpServerTool(Name = "get_nuget_readme")]
    [Description("Gets the README content for a NuGet package, either from the package itself or from its source repository (GitHub/GitLab).")]
    public async Task<string> GetNuGetReadme(
        [Description("The package ID (e.g., 'Newtonsoft.Json')")]
        string packageId,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        [Description("Maximum number of lines to display (default: 200, 0 for all)")]
        int maxLines = 200,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine version
            NuGetVersion packageVersion;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(
                    packageId, includePrerelease: false, cancellationToken);
                var latestVersion = versions.FirstOrDefault();
                if (latestVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
                packageVersion = latestVersion;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Package: {packageId} v{packageVersion}");
            sb.AppendLine(new string('=', 80));

            // 1. First check README file in package
            var downloadResult = await _nugetService.DownloadPackageAsync(
                packageId, packageVersion, cancellationToken);

            string? packagePath = downloadResult.IsSuccess ? downloadResult.Path : null;

            if (packagePath != null)
            {
                var readmeFiles = new[] { "README.md", "README.txt", "README", "readme.md", "Readme.md" };
                foreach (var readmeName in readmeFiles)
                {
                    var readmePath = Path.Combine(packagePath, readmeName);
                    if (File.Exists(readmePath))
                    {
                        sb.AppendLine($"Source: Package (embedded)");
                        sb.AppendLine($"File: {readmeName}");
                        sb.AppendLine(new string('-', 60));
                        sb.AppendLine();

                        var lines = await File.ReadAllLinesAsync(readmePath, cancellationToken);
                        var displayLines = maxLines > 0 ? Math.Min(maxLines, lines.Length) : lines.Length;

                        for (int i = 0; i < displayLines; i++)
                        {
                            sb.AppendLine(lines[i]);
                        }

                        if (lines.Length > displayLines)
                        {
                            sb.AppendLine();
                            sb.AppendLine($"... ({lines.Length - displayLines} more lines)");
                        }

                        return sb.ToString();
                    }
                }
            }

            // 2. Check source repository from package metadata
            var metadata = await _nugetService.GetPackageMetadataAsync(
                packageId, packageVersion, cancellationToken);

            if (metadata == null)
            {
                return $"Package metadata not found: '{packageId}' v{packageVersion}";
            }

            // Extract repository info from Repository URL or Project URL
            var repoUrl = metadata.ProjectUrl?.ToString();

            var repoInfo = RepositoryService.ParseRepositoryUrl(repoUrl);

            if (repoInfo == null)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.CrossMark + " No README found in package and no supported repository URL.");
                sb.AppendLine($"   Project URL: {repoUrl ?? "N/A"}");
                return sb.ToString();
            }

            // 3. Get README from GitHub/GitLab
            ReadmeResult? readme = null;

            if (repoInfo.Type == RepositoryType.GitHub)
            {
                readme = await _repoService.GetGitHubReadmeAsync(
                    repoInfo.Owner, repoInfo.Name, cancellationToken);
            }
            else if (repoInfo.Type == RepositoryType.GitLab)
            {
                readme = await _repoService.GetGitLabReadmeAsync(
                    repoInfo.Host, repoInfo.Owner, repoInfo.Name, cancellationToken);
            }

            if (readme == null || string.IsNullOrEmpty(readme.Content))
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.CrossMark + $" Could not retrieve README from {repoInfo.Type}");
                sb.AppendLine($"   Repository: {repoInfo.Url}");
                return sb.ToString();
            }

            sb.AppendLine($"Source: {repoInfo.Type} Repository");
            sb.AppendLine($"Repository: {repoInfo.Owner}/{repoInfo.Name}");
            if (!string.IsNullOrEmpty(readme.HtmlUrl))
            {
                sb.AppendLine($"URL: {readme.HtmlUrl}");
            }
            sb.AppendLine(new string('-', 60));
            sb.AppendLine();

            var contentLines = readme.Content.Split('\n');
            var displayContentLines = maxLines > 0 ? Math.Min(maxLines, contentLines.Length) : contentLines.Length;

            for (int i = 0; i < displayContentLines; i++)
            {
                sb.AppendLine(contentLines[i].TrimEnd('\r'));
            }

            if (contentLines.Length > displayContentLines)
            {
                sb.AppendLine();
                sb.AppendLine($"... ({contentLines.Length - displayContentLines} more lines)");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting README: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_nuget_wiki")]
    [Description("Gets GitHub wiki information for a NuGet package. Returns wiki page URLs if the repository has a wiki.")]
    public async Task<string> GetNuGetWiki(
        [Description("The package ID (e.g., 'Newtonsoft.Json')")]
        string packageId,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine version (for metadata lookup)
            NuGetVersion? packageVersion = null;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }

            // Get package metadata
            var metadata = await _nugetService.GetPackageMetadataAsync(
                packageId, packageVersion, cancellationToken);

            if (metadata == null)
            {
                return $"Package not found: '{packageId}'";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Package: {metadata.Identity.Id} v{metadata.Identity.Version}");
            sb.AppendLine(new string('=', 80));

            // Extract GitHub info from Repository URL
            var repoUrl = metadata.ProjectUrl?.ToString();
            var repoInfo = RepositoryService.ParseRepositoryUrl(repoUrl);

            if (repoInfo == null)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.CrossMark + " No supported repository URL found.");
                sb.AppendLine($"   Project URL: {repoUrl ?? "N/A"}");
                return sb.ToString();
            }

            if (repoInfo.Type != RepositoryType.GitHub)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Warning + $" Wiki lookup is only supported for GitHub repositories.");
                sb.AppendLine($"   Detected: {repoInfo.Type}");
                sb.AppendLine($"   Repository: {repoInfo.Url}");
                return sb.ToString();
            }

            sb.AppendLine($"Repository: {repoInfo.Owner}/{repoInfo.Name}");
            sb.AppendLine();

            // Get GitHub wiki info
            var wiki = await _repoService.GetGitHubWikiAsync(
                repoInfo.Owner, repoInfo.Name, cancellationToken);

            if (wiki == null)
            {
                sb.AppendLine(Emoji.CrossMark + " Could not retrieve wiki information from GitHub.");
                return sb.ToString();
            }

            if (!wiki.HasWiki)
            {
                sb.AppendLine(Emoji.Books + " Wiki Status: Not enabled or empty");
                sb.AppendLine();
                sb.AppendLine("This repository does not have a wiki enabled.");
                return sb.ToString();
            }

            sb.AppendLine(Emoji.Books + " Wiki Status: Available");
            sb.AppendLine($"📎 Wiki URL: {wiki.WikiUrl}");
            sb.AppendLine();

            if (wiki.Pages.Count > 0)
            {
                sb.AppendLine(Emoji.File + " Wiki Pages:");
                sb.AppendLine(new string('-', 60));

                foreach (var page in wiki.Pages)
                {
                    sb.AppendLine($"  • {page.Title}");
                    sb.AppendLine($"    {page.Url}");
                }

                sb.AppendLine();
                sb.AppendLine($"Total pages found: {wiki.Pages.Count}");
            }
            else
            {
                sb.AppendLine("No wiki pages found (wiki may be empty or protected).");
            }

            sb.AppendLine();
            sb.AppendLine(Emoji.Bulb + " Tips:");
            sb.AppendLine($"  - Clone wiki: git clone https://github.com/{repoInfo.Owner}/{repoInfo.Name}.wiki.git");
            sb.AppendLine($"  - Browse online: {wiki.WikiUrl}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting wiki info: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_nuget_repo_info")]
    [Description("Gets source repository information for a NuGet package including README and wiki availability.")]
    public async Task<string> GetNuGetRepoInfo(
        [Description("The package ID (e.g., 'Newtonsoft.Json')")]
        string packageId,
        [Description("The specific version (optional, defaults to latest)")]
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine version
            NuGetVersion? packageVersion = null;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out var parsedVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
                packageVersion = parsedVersion;
            }

            // Get package metadata
            var metadata = await _nugetService.GetPackageMetadataAsync(
                packageId, packageVersion, cancellationToken);

            if (metadata == null)
            {
                return $"Package not found: '{packageId}'";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Package: {metadata.Identity.Id} v{metadata.Identity.Version}");
            sb.AppendLine(new string('=', 80));

            sb.AppendLine();
            sb.AppendLine(Emoji.Package + " Package URLs:");
            sb.AppendLine($"  Project URL: {metadata.ProjectUrl?.ToString() ?? "N/A"}");
            sb.AppendLine($"  License URL: {metadata.LicenseUrl?.ToString() ?? "N/A"}");

            var repoUrl = metadata.ProjectUrl?.ToString();
            var repoInfo = RepositoryService.ParseRepositoryUrl(repoUrl);

            if (repoInfo == null)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.CrossMark + " No supported repository detected.");
                sb.AppendLine("   Supported: GitHub, GitLab, Bitbucket, Azure DevOps");
                return sb.ToString();
            }

            sb.AppendLine();
            sb.AppendLine(Emoji.Link + " Repository Information:");
            sb.AppendLine($"  Type: {repoInfo.Type}");
            sb.AppendLine($"  Host: {repoInfo.Host}");
            sb.AppendLine($"  Owner: {repoInfo.Owner}");
            sb.AppendLine($"  Name: {repoInfo.Name}");
            sb.AppendLine($"  URL: {repoInfo.Url}");

            // GitHub/GitLab specific info
            if (repoInfo.Type == RepositoryType.GitHub)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Books + " Available Resources:");

                // Check README
                var readme = await _repoService.GetGitHubReadmeAsync(
                    repoInfo.Owner, repoInfo.Name, cancellationToken);

                if (readme != null)
                {
                    sb.AppendLine($"  ✅ README: {readme.FileName} ({FormatFileSize(readme.Size)})");
                    sb.AppendLine($"     URL: {readme.HtmlUrl}");
                    sb.AppendLine($"     → Use 'get_nuget_readme' to view content");
                }
                else
                {
                    sb.AppendLine("  ❌ README: Not found");
                }

                // Check Wiki
                var wiki = await _repoService.GetGitHubWikiAsync(
                    repoInfo.Owner, repoInfo.Name, cancellationToken);

                if (wiki?.HasWiki == true)
                {
                    sb.AppendLine($"  ✅ Wiki: {wiki.Pages.Count} page(s) found");
                    sb.AppendLine($"     URL: {wiki.WikiUrl}");
                    sb.AppendLine($"     → Use 'get_nuget_wiki' to view pages");
                }
                else
                {
                    sb.AppendLine("  ❌ Wiki: Not enabled or empty");
                }

                sb.AppendLine();
                sb.AppendLine(Emoji.Link + " Quick Links:");
                sb.AppendLine($"  Issues: https://github.com/{repoInfo.Owner}/{repoInfo.Name}/issues");
                sb.AppendLine($"  Releases: https://github.com/{repoInfo.Owner}/{repoInfo.Name}/releases");
                sb.AppendLine($"  Actions: https://github.com/{repoInfo.Owner}/{repoInfo.Name}/actions");
            }
            else if (repoInfo.Type == RepositoryType.GitLab)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Books + " Available Resources:");

                var readme = await _repoService.GetGitLabReadmeAsync(
                    repoInfo.Host, repoInfo.Owner, repoInfo.Name, cancellationToken);

                if (readme != null)
                {
                    sb.AppendLine($"  ✅ README: {readme.FileName}");
                    sb.AppendLine($"     → Use 'get_nuget_readme' to view content");
                }
                else
                {
                    sb.AppendLine("  ❌ README: Not found");
                }

                sb.AppendLine();
                sb.AppendLine(Emoji.Link + " Quick Links:");
                sb.AppendLine($"  Issues: https://{repoInfo.Host}/{repoInfo.Owner}/{repoInfo.Name}/-/issues");
                sb.AppendLine($"  Merge Requests: https://{repoInfo.Host}/{repoInfo.Owner}/{repoInfo.Name}/-/merge_requests");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting repository info: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_nuget_vulnerabilities")]
    [Description("Gets known security vulnerabilities for a NuGet package from nuget.org. Shows CVE information, severity levels, and affected versions.")]
    public async Task<string> GetNuGetVulnerabilities(
        [Description("The package ID (e.g., 'Newtonsoft.Json', 'System.Text.Json')")]
        string packageId,
        [Description("The specific version to check (optional, checks all versions if not specified)")]
        string? version = null,
        [Description("Include versions without vulnerabilities in the output (default: false)")]
        bool includeSecureVersions = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            NuGetVersion? nugetVersion = null;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out nugetVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
            }

            var vulnInfo = await _nugetService.GetPackageVulnerabilitiesAsync(
                packageId, nugetVersion, cancellationToken);

            if (vulnInfo == null)
            {
                return $"Package not found or vulnerability information unavailable: '{packageId}'\n\n" +
                       "Note: Vulnerability information is only available from nuget.org.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Security Vulnerabilities: {packageId}");
            sb.AppendLine(new string('=', 80));

            if (vulnInfo.LatestVersion != null)
            {
                sb.AppendLine($"Latest Version: {vulnInfo.LatestVersion}");
                if (vulnInfo.LatestVersionHasVulnerabilities)
                {
                    sb.AppendLine(Emoji.Warning + " WARNING: Latest version has known vulnerabilities!");
                }
                else
                {
                    sb.AppendLine(Emoji.CheckMark + " Latest version has no known vulnerabilities.");
                }
                sb.AppendLine();
            }

            if (!vulnInfo.HasAnyVulnerabilities)
            {
                sb.AppendLine(Emoji.Celebration + " No known vulnerabilities found for this package.");
                sb.AppendLine();
                sb.AppendLine("Note: This only includes vulnerabilities reported to nuget.org.");
                sb.AppendLine("Always check security advisories from the package maintainers.");
                return sb.ToString();
            }

            sb.AppendLine(Emoji.Siren + $" Found {vulnInfo.VulnerableVersions.Count} version(s) with vulnerabilities:");
            sb.AppendLine(new string('-', 60));

            foreach (var versionInfo in vulnInfo.VulnerableVersions.OrderByDescending(v => v.Version))
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Package + $" Version {versionInfo.Version}");

                foreach (var vuln in versionInfo.Vulnerabilities)
                {
                    var severityIcon = vuln.Severity.ToUpperInvariant() switch
                    {
                        "CRITICAL" => Emoji.RedCircle + "",
                        "HIGH" => Emoji.OrangeCircle + "",
                        "MODERATE" or "MEDIUM" => Emoji.YellowCircle + "",
                        "LOW" => Emoji.GreenCircle + "",
                        _ => Emoji.WhiteCircle + ""
                    };

                    sb.AppendLine($"   {severityIcon} Severity: {vuln.Severity}");
                    sb.AppendLine($"      Advisory: {vuln.AdvisoryUrl}");
                }
            }

            // Recommendations
            sb.AppendLine();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine(Emoji.Bulb + " Recommendations:");

            if (!vulnInfo.LatestVersionHasVulnerabilities && vulnInfo.LatestVersion != null)
            {
                sb.AppendLine($"   • Upgrade to version {vulnInfo.LatestVersion} (no known vulnerabilities)");
            }
            else
            {
                sb.AppendLine("   • Check the advisory URLs for mitigation guidance");
                sb.AppendLine("   • Consider using an alternative package if no fix is available");
            }

            sb.AppendLine("   • Review the GitHub Security Advisories for more details");
            sb.AppendLine("   • Run 'dotnet list package --vulnerable' to check your projects");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting vulnerability info: {ex.Message}";
        }
    }

    [McpServerTool(Name = "inspect_nupkg_contents")]
    [Description("Inspects the internal structure and files of a NuGet package (.nupkg). Shows .nuspec metadata, build assets, analyzers, and other package contents.")]
    public async Task<string> InspectNupkgContents(
        [Description("The package ID (e.g., 'Newtonsoft.Json')")]
        string packageId,
        [Description("The specific version to inspect (optional, uses latest if not specified)")]
        string? version = null,
        [Description("Show detailed file listing (default: false, shows summary only)")]
        bool detailed = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            NuGetVersion? nugetVersion = null;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out nugetVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(packageId, true, cancellationToken);
                nugetVersion = versions.FirstOrDefault();
                if (nugetVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
            }

            var downloadResult = await _nugetService.DownloadPackageAsync(packageId, nugetVersion, cancellationToken);
            if (!downloadResult.IsSuccess)
            {
                return $"Failed to download package: {packageId} {nugetVersion}\n{downloadResult.Error}";
            }

            var packagePath = downloadResult.Path!;

            var nupkgPath = Directory.GetFiles(packagePath, "*.nupkg").FirstOrDefault();
            if (nupkgPath == null)
            {
                return $"Package file not found in cache: {packageId} {nugetVersion}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"NuGet Package Contents: {packageId} {nugetVersion}");
            sb.AppendLine(new string('=', 80));
            sb.AppendLine();

            using var archive = System.IO.Compression.ZipFile.OpenRead(nupkgPath);

            // File classification
            var files = archive.Entries.Select(e => e.FullName).ToList();
            var nuspecFile = files.FirstOrDefault(f => f.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            var libFiles = files.Where(f => f.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)).ToList();
            var buildFiles = files.Where(f => f.StartsWith("build/", StringComparison.OrdinalIgnoreCase) ||
                                               f.StartsWith("buildTransitive/", StringComparison.OrdinalIgnoreCase)).ToList();
            var analyzerFiles = files.Where(f => f.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase)).ToList();
            var contentFiles = files.Where(f => f.StartsWith("content/", StringComparison.OrdinalIgnoreCase) ||
                                                 f.StartsWith("contentFiles/", StringComparison.OrdinalIgnoreCase)).ToList();
            var runtimeFiles = files.Where(f => f.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase)).ToList();
            var refFiles = files.Where(f => f.StartsWith("ref/", StringComparison.OrdinalIgnoreCase)).ToList();
            var toolsFiles = files.Where(f => f.StartsWith("tools/", StringComparison.OrdinalIgnoreCase)).ToList();
            var docFiles = files.Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                                            f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                                            f.Equals("readme.md", StringComparison.OrdinalIgnoreCase) ||
                                            f.Contains("license", StringComparison.OrdinalIgnoreCase)).ToList();

            // Analyze .nuspec
            if (nuspecFile != null)
            {
                sb.AppendLine(Emoji.Clipboard + " Package Metadata (.nuspec):");
                sb.AppendLine(new string('-', 60));
                var nuspecEntry = archive.GetEntry(nuspecFile);
                if (nuspecEntry != null)
                {
                    using var stream = nuspecEntry.Open();
                    var nuspecDoc = System.Xml.Linq.XDocument.Load(stream);
                    var ns = nuspecDoc.Root?.Name.Namespace;
                    
                    if (ns == null)
                    {
                        sb.AppendLine("  (Unable to parse nuspec namespace)");
                    }
                    else
                    {
                        var metadata = nuspecDoc.Root?.Element(ns + "metadata");

                        if (metadata != null)
                        {
                            var props = new[] { "id", "version", "authors", "owners", "license", "licenseUrl",
                                               "projectUrl", "repository", "requireLicenseAcceptance", "description",
                                               "releaseNotes", "copyright", "tags", "language", "readme" };

                            foreach (var prop in props)
                            {
                                var element = metadata.Element(ns + prop);
                                if (element != null && !string.IsNullOrWhiteSpace(element.Value))
                                {
                                    var value = element.Value.Length > 100 ? element.Value[..100] + "..." : element.Value;
                                    sb.AppendLine($"  {prop}: {value.Replace("\n", " ").Trim()}");
                                }
                            }

                            // Dependencies summary
                            var dependencies = metadata.Element(ns + "dependencies");
                            if (dependencies != null)
                            {
                                var groups = dependencies.Elements(ns + "group").ToList();
                                var directDeps = dependencies.Elements(ns + "dependency").ToList();

                                sb.AppendLine();
                                sb.AppendLine("  Dependencies:");
                                if (groups.Count > 0)
                                {
                                    foreach (var group in groups)
                                {
                                    var tfm = group.Attribute("targetFramework")?.Value ?? "any";
                                    var deps = group.Elements(ns + "dependency").ToList();
                                    sb.AppendLine($"    [{tfm}] {deps.Count} package(s)");
                                }
                            }
                            else if (directDeps.Count > 0)
                            {
                                sb.AppendLine($"    {directDeps.Count} package(s)");
                            }
                            else
                            {
                                sb.AppendLine("    (none)");
                            }
                        }

                        // FrameworkAssemblies
                        var frameworkAssemblies = metadata.Element(ns + "frameworkAssemblies");
                        if (frameworkAssemblies != null)
                        {
                            var assemblies = frameworkAssemblies.Elements(ns + "frameworkAssembly").ToList();
                            if (assemblies.Count > 0)
                            {
                                sb.AppendLine();
                                sb.AppendLine($"  Framework Assemblies: {assemblies.Count}");
                            }
                        }
                        }
                    }
                }
                sb.AppendLine();
            }

            // Folder summary
            sb.AppendLine(Emoji.Folder + " Package Structure:");
            sb.AppendLine(new string('-', 60));

            void PrintSection(string icon, string name, List<string> fileList, bool showTfms = false)
            {
                if (fileList.Count == 0) return;

                if (showTfms)
                {
                    var tfms = fileList
                        .Select(f => f.Split('/'))
                        .Where(parts => parts.Length >= 2)
                        .Select(parts => parts[1])
                        .Distinct()
                        .OrderBy(t => t)
                        .ToList();

                    sb.AppendLine($"  {icon} {name}: {fileList.Count} file(s)");
                    sb.AppendLine($"       TFMs: {string.Join(", ", tfms)}");
                }
                else
                {
                    sb.AppendLine($"  {icon} {name}: {fileList.Count} file(s)");
                }

                if (detailed)
                {
                    foreach (var file in fileList.Take(20))
                    {
                        var entry = archive.GetEntry(file);
                        var size = entry?.Length ?? 0;
                        sb.AppendLine($"       - {file} ({FormatFileSize(size)})");
                    }
                    if (fileList.Count > 20)
                    {
                        sb.AppendLine($"       ... and {fileList.Count - 20} more");
                    }
                }
            }

            PrintSection(Emoji.Books + "", "lib (assemblies)", libFiles, showTfms: true);
            PrintSection(Emoji.Ruler + "", "ref (reference assemblies)", refFiles, showTfms: true);
            PrintSection(Emoji.Wrench + "", "build (MSBuild)", buildFiles, showTfms: false);
            PrintSection(Emoji.MagnifyingGlass + "", "analyzers (Roslyn)", analyzerFiles, showTfms: false);
            PrintSection(Emoji.Desktop + "", "runtimes (native)", runtimeFiles, showTfms: false);
            PrintSection(Emoji.HammerAndWrench + "", "tools", toolsFiles, showTfms: false);
            PrintSection(Emoji.File + "", "content", contentFiles, showTfms: false);
            PrintSection(Emoji.FileText + "", "docs/license", docFiles, showTfms: false);

            // Notable files highlight
            sb.AppendLine();
            sb.AppendLine(Emoji.Sparkles + " Notable Files:");
            sb.AppendLine(new string('-', 60));

            var notableFiles = new List<string>();

            // Build props/targets
            var propsTargets = buildFiles.Where(f =>
                f.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)).ToList();
            if (propsTargets.Count > 0)
            {
                sb.AppendLine("  🔧 MSBuild Extensions:");
                foreach (var file in propsTargets)
                {
                    sb.AppendLine($"       {file}");
                }
            }

            // Analyzers
            if (analyzerFiles.Count > 0)
            {
                var analyzerDlls = analyzerFiles.Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();
                sb.AppendLine($"  🔍 Roslyn Analyzers: {analyzerDlls.Count} DLL(s)");
            }

            // Readme
            var readme = files.FirstOrDefault(f =>
                f.Equals("readme.md", StringComparison.OrdinalIgnoreCase) ||
                f.Equals("readme.txt", StringComparison.OrdinalIgnoreCase));
            if (readme != null)
            {
                sb.AppendLine($"  📖 Readme: {readme}");
            }

            // License
            var license = files.FirstOrDefault(f =>
                f.Contains("license", StringComparison.OrdinalIgnoreCase));
            if (license != null)
            {
                sb.AppendLine($"  📜 License: {license}");
            }

            // Total
            sb.AppendLine();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine($"Total files: {files.Count}");
            sb.AppendLine($"Package size: {FormatFileSize(new FileInfo(nupkgPath).Length)}");
            sb.AppendLine();
            sb.AppendLine(Emoji.Bulb + " Use 'extract_nupkg_file' to view specific file contents.");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error inspecting package: {ex.Message}";
        }
    }

    [McpServerTool(Name = "extract_nupkg_file")]
    [Description("Extracts and displays the content of a specific file from a NuGet package. Useful for viewing .nuspec, .props, .targets, readme, license, or other text files.")]
    public async Task<string> ExtractNupkgFile(
        [Description("The package ID (e.g., 'Newtonsoft.Json')")]
        string packageId,
        [Description("The path to the file within the package (e.g., 'Newtonsoft.Json.nuspec', 'build/Newtonsoft.Json.props')")]
        string filePath,
        [Description("The specific version to extract from (optional, uses latest if not specified)")]
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            NuGetVersion? nugetVersion = null;
            if (!string.IsNullOrEmpty(version))
            {
                if (!NuGetVersion.TryParse(version, out nugetVersion))
                {
                    return $"Invalid version format: '{version}'";
                }
            }
            else
            {
                var versions = await _nugetService.GetPackageVersionsAsync(packageId, true, cancellationToken);
                nugetVersion = versions.FirstOrDefault();
                if (nugetVersion == null)
                {
                    return $"Package not found: '{packageId}'";
                }
            }

            var downloadResult = await _nugetService.DownloadPackageAsync(packageId, nugetVersion, cancellationToken);
            if (!downloadResult.IsSuccess)
            {
                return $"Failed to download package: {packageId} {nugetVersion} {downloadResult.Error}";
            }

            var packagePath = downloadResult.Path!;

            var nupkgPath = Directory.GetFiles(packagePath, "*.nupkg").FirstOrDefault();
            if (nupkgPath == null)
            {
                return $"Package file not found in cache: {packageId} {nugetVersion}";
            }


            using var archive = System.IO.Compression.ZipFile.OpenRead(nupkgPath);

            // Find file (case-insensitive, partial matching supported)
            var normalizedPath = filePath.Replace('\\', '/');
            var entry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));

            // Try partial matching if no exact match
            if (entry == null)
            {
                var candidates = archive.Entries
                    .Where(e => e.FullName.EndsWith(normalizedPath, StringComparison.OrdinalIgnoreCase) ||
                               e.FullName.Contains(normalizedPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (candidates.Count == 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"File not found: '{filePath}'");
                    sb.AppendLine();
                    sb.AppendLine("Available files:");

                    var relevantFiles = archive.Entries
                        .Where(e => !e.FullName.EndsWith('/'))
                        .Select(e => e.FullName)
                        .Where(f => f.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        .Take(30)
                        .ToList();

                    foreach (var file in relevantFiles)
                    {
                        sb.AppendLine($"  - {file}");
                    }

                    return sb.ToString();
                }

                if (candidates.Count == 1)
                {
                    entry = candidates[0];
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"Multiple matches found for '{filePath}':");
                    sb.AppendLine();
                    foreach (var candidate in candidates.Take(10))
                    {
                        sb.AppendLine($"  - {candidate.FullName}");
                    }
                    sb.AppendLine();
                    sb.AppendLine("Please specify the full path.");
                    return sb.ToString();
                }
            }

            // Check binary file
            var binaryExtensions = new[] { ".dll", ".exe", ".pdb", ".nupkg", ".snupkg", ".zip", ".png", ".jpg", ".gif", ".ico" };
            if (binaryExtensions.Any(ext => entry.FullName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                return $"Cannot display binary file: {entry.FullName} ({FormatFileSize(entry.Length)})\n\n" +
                       "Use 'inspect_nuget_package' for assembly analysis.";
            }

            // Size limit
            const long MaxFileSize = 500 * 1024; // 500KB
            if (entry.Length > MaxFileSize)
            {
                return $"File too large to display: {entry.FullName} ({FormatFileSize(entry.Length)})\n" +
                       $"Maximum displayable size: {FormatFileSize(MaxFileSize)}";
            }

            // Read file content
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync(cancellationToken);

            var result = new StringBuilder();
            result.AppendLine(Emoji.File + $" {entry.FullName}");
            result.AppendLine($"   Package: {packageId} {nugetVersion}");
            result.AppendLine($"   Size: {FormatFileSize(entry.Length)}");
            result.AppendLine(new string('=', 80));
            result.AppendLine();
            result.AppendLine(content);

            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"Error extracting file: {ex.Message}";
        }
    }

    [McpServerTool(Name = "clear_nuget_cache")]
    [Description("Clears the HandMirrorMcp NuGet package cache directory. Removes all downloaded packages to free up disk space.")]
    public string ClearNuGetCache(
        [Description("If true, only shows what would be deleted without actually deleting (default: false)")]
        bool dryRun = false)
    {
        try
        {
            var cacheDir = _nugetService.CacheDirectory;

            if (!Directory.Exists(cacheDir))
            {
                return $"Cache directory does not exist: {cacheDir}";
            }

            var sb = new StringBuilder();
            sb.AppendLine("HandMirrorMcp NuGet Cache Cleanup");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine($"Cache Directory: {cacheDir}");
            sb.AppendLine();

            var directories = Directory.GetDirectories(cacheDir);
            var totalSize = 0L;
            var packageCount = 0;

            foreach (var dir in directories)
            {
                var dirInfo = new DirectoryInfo(dir);
                var dirSize = GetDirectorySize(dirInfo);
                totalSize += dirSize;
                packageCount++;

                sb.AppendLine($"  📦 {dirInfo.Name} ({FormatFileSize(dirSize)})");
            }

            sb.AppendLine();
            sb.AppendLine(new string('-', 60));
            sb.AppendLine($"Total packages: {packageCount}");
            sb.AppendLine($"Total size: {FormatFileSize(totalSize)}");
            sb.AppendLine();

            if (dryRun)
            {
                sb.AppendLine(Emoji.MagnifyingGlass + " Dry run mode - no files were deleted.");
                sb.AppendLine("   Run with dryRun=false to actually clear the cache.");
            }
            else
            {
                var deletedCount = 0;
                var deletedSize = 0L;
                var errors = new List<string>();

                foreach (var dir in directories)
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        var dirSize = GetDirectorySize(dirInfo);

                        Directory.Delete(dir, recursive: true);

                        deletedCount++;
                        deletedSize += dirSize;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{Path.GetFileName(dir)}: {ex.Message}");
                    }
                }

                sb.AppendLine(Emoji.CheckMark + $" Deleted {deletedCount} package(s), freed {FormatFileSize(deletedSize)}");

                if (errors.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(Emoji.Warning + " Errors:");
                    foreach (var error in errors)
                    {
                        sb.AppendLine($"  - {error}");
                    }
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error clearing cache: {ex.Message}";
        }
    }

    private static long GetDirectorySize(DirectoryInfo dir)
    {
        var size = 0L;

        try
        {
            foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
            {
                size += file.Length;
            }
        }
        catch
        {
            // Ignore inaccessible files
        }

        return size;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _nugetService.Dispose();
            _repoService.Dispose();
            _disposed = true;
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^net(\d+)\.(\d+)")]
    private static partial System.Text.RegularExpressions.Regex NetVersionRegex();
    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Z]{2,}\d{4}$")]
    private static partial System.Text.RegularExpressions.Regex TfmRegex();
}






