using System.IO.Compression;
using System.Xml.Linq;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace HandMirrorMcp.Services;

/// <summary>
/// Contains all assembly information within a package
/// </summary>
public sealed class PackageAssemblyInfo
{
    /// <summary>
    /// Assemblies in lib/ folder (by TFM)
    /// Key: TFM (e.g., "net8.0", "netstandard2.0")
    /// </summary>
    public Dictionary<string, List<string>> LibAssemblies { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Assemblies in runtimes/ folder (by RID)
    /// Key: "RID/TFM" or "RID/native" (e.g., "win-x64/net8.0", "linux-x64/native")
    /// </summary>
    public Dictionary<string, List<string>> RuntimeAssemblies { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reference assemblies in ref/ folder (by TFM)
    /// Key: TFM
    /// </summary>
    public Dictionary<string, List<string>> RefAssemblies { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if any assemblies exist
    /// </summary>
    public bool HasAnyAssemblies =>
        LibAssemblies.Count > 0 || RuntimeAssemblies.Count > 0 || RefAssemblies.Count > 0;
}

/// <summary>
/// Service for managing NuGet package sources and downloads
/// </summary>
public sealed class NuGetService : IDisposable
{
    private readonly string _cacheDirectory;
    private readonly List<PackageSource> _packageSources;
    private readonly SourceCacheContext _cacheContext;
    private readonly ILogger _logger;
    private bool _disposed;
    private static readonly bool _verboseErrors =
        Environment.GetEnvironmentVariable("HANDMIRROR_DEBUG") == "1";

    public NuGetService()
    {
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HandMirrorMcp",
            "packages");

        Directory.CreateDirectory(_cacheDirectory);

        _packageSources = LoadPackageSources();
        _cacheContext = new SourceCacheContext();
        _logger = NullLogger.Instance;
    }

    /// <summary>
    /// List of registered package sources
    /// </summary>
    public IReadOnlyList<PackageSource> PackageSources => _packageSources;


    /// <summary>
    /// Loads package sources from NuGet configuration files.
    /// </summary>
    private List<PackageSource> LoadPackageSources()
    {
        var sources = new List<PackageSource>();
        var addedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add official NuGet.org source
        sources.Add(new PackageSource("https://api.nuget.org/v3/index.json", "nuget.org"));
        addedUrls.Add("https://api.nuget.org/v3/index.json");

        // Configuration file paths
        var configPaths = GetNuGetConfigPaths();

        // Parse package sources from each config file
        foreach (var configPath in configPaths)
        {
            try
            {
                var parsedSources = ParseNuGetConfig(configPath);
                foreach (var source in parsedSources)
                {
                    if (!addedUrls.Contains(source.Source))
                    {
                        sources.Add(source);
                        addedUrls.Add(source.Source);
                    }
                }
            }
            catch
            {
                // Ignore parsing failures
            }
        }

        return sources;
    }

    /// <summary>
    /// Returns NuGet configuration file paths for each OS.
    /// </summary>
    /// <remarks>
    /// Windows: %appdata%\NuGet\NuGet.Config, %programfiles(x86)%\NuGet\Config\*.config
    /// Linux/macOS: ~/.config/NuGet/NuGet.Config
    /// </remarks>
    private static List<string> GetNuGetConfigPaths()
    {
        var configPaths = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            // Windows: %appdata%\NuGet\NuGet.Config
            var appDataConfig = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NuGet", "NuGet.Config");
            if (File.Exists(appDataConfig))
            {
                configPaths.Add(appDataConfig);
            }

            // Windows: %programfiles(x86)%\NuGet\Config\*.config
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(programFilesX86))
            {
                var nugetConfigDir = Path.Combine(programFilesX86, "NuGet", "Config");
                if (Directory.Exists(nugetConfigDir))
                {
                    configPaths.AddRange(Directory.GetFiles(nugetConfigDir, "*.config"));
                }
            }
        }
        else
        {
            // Linux/macOS: ~/.config/NuGet/NuGet.Config
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

            // Use XDG_CONFIG_HOME if set, otherwise ~/.config
            var configHome = !string.IsNullOrEmpty(xdgConfigHome)
                ? xdgConfigHome
                : Path.Combine(homeDir, ".config");

            var nugetConfig = Path.Combine(configHome, "NuGet", "NuGet.Config");
            if (File.Exists(nugetConfig))
            {
                configPaths.Add(nugetConfig);
            }

            // Check case variations (Linux is case-sensitive)
            var nugetConfigLower = Path.Combine(configHome, "nuget", "nuget.config");
            if (File.Exists(nugetConfigLower) && !configPaths.Contains(nugetConfigLower))
            {
                configPaths.Add(nugetConfigLower);
            }
        }

        return configPaths;
    }

    /// <summary>
    /// Parses a NuGet.Config file to extract package sources.
    /// </summary>
    private static List<PackageSource> ParseNuGetConfig(string configPath)
    {
        var sources = new List<PackageSource>();

        var doc = XDocument.Load(configPath);
        var packageSources = doc.Root?.Element("packageSources");

        if (packageSources == null)
            return sources;

        // List of disabled sources
        var disabledSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var disabledElement = doc.Root?.Element("disabledPackageSources");
        if (disabledElement != null)
        {
            foreach (var add in disabledElement.Elements("add"))
            {
                var key = add.Attribute("key")?.Value;
                var value = add.Attribute("value")?.Value;
                if (key != null && value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                {
                    disabledSources.Add(key);
                }
            }
        }

        // Credential information
        var credentials = new Dictionary<string, (string? username, string? password)>(StringComparer.OrdinalIgnoreCase);
        var credentialsElement = doc.Root?.Element("packageSourceCredentials");
        if (credentialsElement != null)
        {
            foreach (var sourceElement in credentialsElement.Elements())
            {
                var sourceName = sourceElement.Name.LocalName;
                var username = sourceElement.Elements("add")
                    .FirstOrDefault(e => e.Attribute("key")?.Value == "Username")?.Attribute("value")?.Value;
                var password = sourceElement.Elements("add")
                    .FirstOrDefault(e => e.Attribute("key")?.Value == "ClearTextPassword")?.Attribute("value")?.Value;

                credentials[sourceName] = (username, password);
            }
        }

        foreach (var add in packageSources.Elements("add"))
        {
            var name = add.Attribute("key")?.Value;
            var url = add.Attribute("value")?.Value;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
                continue;

            if (disabledSources.Contains(name))
                continue;

            var source = new PackageSource(url, name);

            // Set credentials
            if (credentials.TryGetValue(name, out var cred) && cred.username != null)
            {
                source.Credentials = new PackageSourceCredential(
                    name, cred.username, cred.password ?? "", isPasswordClearText: true, validAuthenticationTypesText: null);
            }

            sources.Add(source);
        }

        return sources;
    }

    /// <summary>
    /// Searches for packages.
    /// </summary>
    public async Task<IEnumerable<IPackageSearchMetadata>> SearchPackagesAsync(
        string searchTerm,
        bool includePrerelease = false,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var results = new List<IPackageSearchMetadata>();

        foreach (var source in _packageSources)
        {
            try
            {
                var repository = Repository.Factory.GetCoreV3(source);
                var searchResource = await repository.GetResourceAsync<PackageSearchResource>(cancellationToken);

                if (searchResource != null)
                {
                    var searchFilter = new SearchFilter(includePrerelease);
                    var packages = await searchResource.SearchAsync(
                        searchTerm,
                        searchFilter,
                        skip,
                        take,
                        _logger,
                        cancellationToken);

                    results.AddRange(packages);
                }
            }
            catch
            {
                // Ignore source access failures
            }
        }

        return results.DistinctBy(p => p.Identity.Id);
    }

    /// <summary>
    /// Gets all versions of a package.
    /// </summary>
    public async Task<IEnumerable<NuGetVersion>> GetPackageVersionsAsync(
        string packageId,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        var versions = new List<NuGetVersion>();

        foreach (var source in _packageSources)
        {
            try
            {
                var repository = Repository.Factory.GetCoreV3(source);
                var findResource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);

                if (findResource != null)
                {
                    var allVersions = await findResource.GetAllVersionsAsync(
                        packageId,
                        _cacheContext,
                        _logger,
                        cancellationToken);

                    versions.AddRange(allVersions);
                }
            }
            catch
            {
                // Ignore source access failures
            }
        }

        var result = versions.Distinct().OrderByDescending(v => v);
        return includePrerelease ? result : result.Where(v => !v.IsPrerelease);
    }

    /// <summary>
    /// Gets package metadata.
    /// </summary>
    public async Task<IPackageSearchMetadata?> GetPackageMetadataAsync(
        string packageId,
        NuGetVersion? version = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var source in _packageSources)
        {
            try
            {
                var repository = Repository.Factory.GetCoreV3(source);
                var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>(cancellationToken);

                if (metadataResource != null)
                {
                    var metadata = await metadataResource.GetMetadataAsync(
                        packageId,
                        includePrerelease: true,
                        includeUnlisted: false,
                        _cacheContext,
                        _logger,
                        cancellationToken);

                    var metadataList = metadata.ToList();

                    if (version != null)
                    {
                        return metadataList.FirstOrDefault(m => m.Identity.Version == version);
                    }

                    return metadataList.OrderByDescending(m => m.Identity.Version).FirstOrDefault();
                }
            }
            catch
            {
                // Ignore source access failures
            }
        }

        return null;
    }

    /// <summary>
    /// Downloads a package and returns the cache path.
    /// </summary>
    public async Task<DownloadResult> DownloadPackageAsync(
        string packageId,
        NuGetVersion version,
        CancellationToken cancellationToken = default)
    {
        var packagePath = Path.Combine(_cacheDirectory, $"{packageId}.{version}");
        var nupkgPath = Path.Combine(packagePath, $"{packageId}.{version}.nupkg");

        // Return path if already cached and valid
        if (Directory.Exists(packagePath) && File.Exists(nupkgPath))
        {
            if (IsValidNupkg(nupkgPath))
            {
                return DownloadResult.Success(packagePath);
            }
            else
            {
                // Invalid cached file, delete and re-download
                try
                {
                    Directory.Delete(packagePath, true);
                }
                catch
                {
                    return DownloadResult.Failure($"Cached package is corrupted and could not be deleted: {nupkgPath}");
                }
            }
        }

        Directory.CreateDirectory(packagePath);

        var errors = new List<string>();

        foreach (var source in _packageSources)
        {
            try
            {
                var repository = Repository.Factory.GetCoreV3(source);
                
                // Try DownloadResource first (preferred method)
                var downloadResource = await repository.GetResourceAsync<DownloadResource>(cancellationToken);
                
                if (downloadResource != null)
                {
                    var packageIdentity = new NuGet.Packaging.Core.PackageIdentity(packageId, version);
                    var downloadContext = new PackageDownloadContext(_cacheContext);
                    
                    using var downloadResult = await downloadResource.GetDownloadResourceResultAsync(
                        packageIdentity,
                        downloadContext,
                        globalPackagesFolder: _cacheDirectory,
                        _logger,
                        cancellationToken);
                    
                    if (downloadResult.Status == DownloadResourceResultStatus.Available ||
                        downloadResult.Status == DownloadResourceResultStatus.AvailableWithoutStream)
                    {
                        // Copy to our cache location
                        if (downloadResult.PackageStream != null)
                        {
                            await using var fileStream = new FileStream(nupkgPath, FileMode.Create, FileAccess.Write);
                            await downloadResult.PackageStream.CopyToAsync(fileStream, cancellationToken);
                        }
                        else if (downloadResult.PackageReader != null)
                        {
                            // Package already extracted, copy from source
                            var sourcePath = downloadResult.PackageReader.GetNuspecFile();
                            var sourceDir = Path.GetDirectoryName(sourcePath);
                            if (sourceDir != null && Directory.Exists(sourceDir))
                            {
                                // Copy the nupkg if it exists
                                var sourceNupkg = Directory.GetFiles(sourceDir, "*.nupkg").FirstOrDefault();
                                if (sourceNupkg != null)
                                {
                                    File.Copy(sourceNupkg, nupkgPath, overwrite: true);
                                }
                            }
                        }
                        
                        // Verify and extract
                        if (File.Exists(nupkgPath) && IsValidNupkg(nupkgPath))
                        {
                            await ExtractPackageAsync(nupkgPath, packagePath, cancellationToken);
                            return DownloadResult.Success(packagePath);
                        }
                        else if (downloadResult.PackageReader != null)
                        {
                            // Use PackageReader to extract directly
                            var files = await downloadResult.PackageReader.GetFilesAsync(cancellationToken);
                            foreach (var file in files)
                            {
                                var targetFile = Path.Combine(packagePath, file);
                                var targetDir = Path.GetDirectoryName(targetFile);
                                if (targetDir != null) Directory.CreateDirectory(targetDir);
                                
                                using var entryStream = await downloadResult.PackageReader.GetStreamAsync(file, cancellationToken);
                                await using var targetStream = File.Create(targetFile);
                                await entryStream.CopyToAsync(targetStream, cancellationToken);
                            }
                            
                            // Create a marker to indicate package is extracted
                            if (!File.Exists(nupkgPath))
                            {
                                // Try to get nupkg from global packages folder
                                var globalNupkg = Path.Combine(_cacheDirectory, packageId.ToLowerInvariant(), version.ToString(), $"{packageId.ToLowerInvariant()}.{version}.nupkg");
                                if (File.Exists(globalNupkg))
                                {
                                    File.Copy(globalNupkg, nupkgPath, overwrite: true);
                                }
                            }
                            
                            return DownloadResult.Success(packagePath);
                        }
                    }
                    else if (downloadResult.Status == DownloadResourceResultStatus.NotFound)
                    {
                        errors.Add($"[{source.Name}] Package not found");
                    }
                    else
                    {
                        errors.Add($"[{source.Name}] Download status: {downloadResult.Status}");
                    }
                }
                else
                {
                    // Fallback to FindPackageByIdResource
                    var findResource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                    
                    if (findResource != null)
                    {
                        await using var packageStream = new FileStream(nupkgPath, FileMode.Create, FileAccess.Write);
                        var success = await findResource.CopyNupkgToStreamAsync(
                            packageId,
                            version,
                            packageStream,
                            _cacheContext,
                            _logger,
                            cancellationToken);

                        await packageStream.FlushAsync(cancellationToken);
                        packageStream.Close();

                        if (success && File.Exists(nupkgPath) && new FileInfo(nupkgPath).Length > 0)
                        {
                            if (IsValidNupkg(nupkgPath))
                            {
                                await ExtractPackageAsync(nupkgPath, packagePath, cancellationToken);
                                return DownloadResult.Success(packagePath);
                            }
                            else
                            {
                                var firstBytes = await ReadFirstBytesAsync(nupkgPath, 100);
                                errors.Add($"[{source.Name}] Downloaded file is not a valid nupkg. {firstBytes}");
                                try { File.Delete(nupkgPath); } catch { }
                            }
                        }
                        else
                        {
                            errors.Add($"[{source.Name}] FindPackageByIdResource download failed");
                            try { File.Delete(nupkgPath); } catch { }
                        }
                    }
                    else
                    {
                        errors.Add($"[{source.Name}] No download resource available");
                    }
                }
            }
            catch (Exception ex)
            {
                var errorDetail = _verboseErrors ? ex.ToString() : ex.Message;
                errors.Add($"[{source.Name}] {errorDetail}");
            }
        }

        // Clean up created directory if download fails
        if (Directory.Exists(packagePath))
        {
            try
            {
                Directory.Delete(packagePath, true);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }

        var errorMessage = errors.Count > 0
            ? string.Join(Environment.NewLine, errors)
            : "No package sources available";

        return DownloadResult.Failure(errorMessage);
    }

    /// <summary>
    /// Validates if a file is a valid nupkg (zip) file.
    /// </summary>
    private static bool IsValidNupkg(string nupkgPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(nupkgPath);
            // Check if it has a .nuspec file (required for valid nupkg)
            return archive.Entries.Any(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the first N bytes of a file for diagnostic purposes.
    /// </summary>
    private static async Task<string> ReadFirstBytesAsync(string filePath, int count)
    {
        try
        {
            var bytes = new byte[count];
            await using var stream = File.OpenRead(filePath);
            var bytesRead = await stream.ReadAsync(bytes.AsMemory(0, count));
            
            // Try to interpret as text first
            var text = System.Text.Encoding.UTF8.GetString(bytes, 0, bytesRead);
            if (text.StartsWith("<!") || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                return $"HTML response detected: {text[..Math.Min(50, text.Length)]}...";
            }
            if (text.StartsWith("{") || text.StartsWith("["))
            {
                return $"JSON response: {text[..Math.Min(50, text.Length)]}...";
            }
            
            // Check for PK signature (zip file)
            if (bytesRead >= 2 && bytes[0] == 0x50 && bytes[1] == 0x4B)
            {
                return "Valid ZIP signature (PK), but file may be corrupted";
            }
            
            // Return hex dump
            return $"Hex: {BitConverter.ToString(bytes, 0, Math.Min(bytesRead, 20))}";
        }
        catch (Exception ex)
        {
            return $"Could not read file: {ex.Message}";
        }
    }

    /// <summary>
    /// Extracts a nupkg file.
    /// </summary>
    private static async Task ExtractPackageAsync(string nupkgPath, string targetPath, CancellationToken cancellationToken)
    {
        using var packageReader = new PackageArchiveReader(nupkgPath);

        var files = await packageReader.GetFilesAsync(cancellationToken);

        foreach (var file in files)
        {
            var filePath = Path.Combine(targetPath, file);
            var directory = Path.GetDirectoryName(filePath);

            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            await using var fileStream = File.Create(filePath);
            await using var entryStream = await packageReader.GetStreamAsync(file, cancellationToken);
            await entryStream.CopyToAsync(fileStream, cancellationToken);
        }
    }

    /// <summary>
    /// Gets assembly paths for a specific framework from a package.
    /// </summary>
    public async Task<IEnumerable<string>> GetPackageAssembliesAsync(
        string packagePath,
        string? targetFramework = null,
        CancellationToken cancellationToken = default)
    {
        var nupkgPath = Directory.GetFiles(packagePath, "*.nupkg").FirstOrDefault();
        if (nupkgPath == null)
            return [];

        using var packageReader = new PackageArchiveReader(nupkgPath);

        var libItems = await packageReader.GetLibItemsAsync(cancellationToken);
        var libItemsList = libItems.ToList();

        if (!libItemsList.Any())
            return [];

        // Select target framework
        FrameworkSpecificGroup? selectedGroup = null;

        if (!string.IsNullOrEmpty(targetFramework))
        {
            selectedGroup = libItemsList.FirstOrDefault(g =>
                g.TargetFramework.GetShortFolderName().Equals(targetFramework, StringComparison.OrdinalIgnoreCase));
        }

        selectedGroup ??= libItemsList
            .OrderByDescending(g => g.TargetFramework.Version)
            .FirstOrDefault();

        if (selectedGroup == null)
            return [];

        return selectedGroup.Items
            .Where(item => item.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(item => Path.Combine(packagePath, item));
    }

    /// <summary>
    /// Gets all assembly information by TFM and runtime from a package.
    /// </summary>
    public async Task<PackageAssemblyInfo> GetAllPackageAssembliesAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        var result = new PackageAssemblyInfo();

        var nupkgPath = Directory.GetFiles(packagePath, "*.nupkg").FirstOrDefault();
        if (nupkgPath == null)
            return result;

        using var packageReader = new PackageArchiveReader(nupkgPath);

        // Assemblies in lib/ folder (by TFM)
        var libItems = await packageReader.GetLibItemsAsync(cancellationToken);
        foreach (var group in libItems)
        {
            var tfm = group.TargetFramework.GetShortFolderName();
            var dlls = group.Items
                .Where(item => item.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(item => Path.Combine(packagePath, item))
                .ToList();

            if (dlls.Count > 0)
            {
                result.LibAssemblies[tfm] = dlls;
            }
        }

        // Assemblies in runtimes/ folder (by RID)
        var files = await packageReader.GetFilesAsync(cancellationToken);
        var runtimeFiles = files
            .Where(f => f.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase) &&
                        f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var file in runtimeFiles)
        {
            // runtimes/{rid}/lib/{tfm}/{assembly}.dll or
            // runtimes/{rid}/native/{assembly}.dll format
            var parts = file.Split('/');
            if (parts.Length >= 3)
            {
                var rid = parts[1]; // e.g., win-x64, linux-x64
                var key = rid;

                // If there is a TFM under lib
                if (parts.Length >= 4 && parts[2].Equals("lib", StringComparison.OrdinalIgnoreCase))
                {
                    var tfm = parts[3];
                    key = $"{rid}/{tfm}";
                }
                else if (parts[2].Equals("native", StringComparison.OrdinalIgnoreCase))
                {
                    key = $"{rid}/native";
                }

                if (!result.RuntimeAssemblies.ContainsKey(key))
                {
                    result.RuntimeAssemblies[key] = [];
                }

                result.RuntimeAssemblies[key].Add(Path.Combine(packagePath, file));
            }
        }

        // Reference assemblies in ref/ folder
        var refItems = await packageReader.GetItemsAsync("ref", cancellationToken);
        foreach (var group in refItems)
        {
            var tfm = group.TargetFramework.GetShortFolderName();
            if (string.IsNullOrEmpty(tfm))
            {
                // Parse in ref/{tfm}/ format
                var firstItem = group.Items.FirstOrDefault();
                if (firstItem != null)
                {
                    var parts = firstItem.Split('/');
                    if (parts.Length >= 2)
                    {
                        tfm = parts[1];
                    }
                }
            }

            var dlls = group.Items
                .Where(item => item.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(item => Path.Combine(packagePath, item))
                .ToList();

            if (dlls.Count > 0)
            {
                result.RefAssemblies[tfm ?? "unknown"] = dlls;
            }
        }

        return result;
    }

    /// <summary>
    /// Cache directory path
    /// </summary>
    public string CacheDirectory => _cacheDirectory;

    /// <summary>
    /// Gets security vulnerability information for a package. (nuget.org only)
    /// </summary>
    public async Task<PackageVulnerabilityInfo?> GetPackageVulnerabilitiesAsync(
        string packageId,
        NuGetVersion? version = null,
        CancellationToken cancellationToken = default)
    {
        // Use nuget.org source only (vulnerability info is only available from nuget.org)
        var nugetOrgSource = _packageSources.FirstOrDefault(s =>
            s.Source.Contains("api.nuget.org", StringComparison.OrdinalIgnoreCase));

        if (nugetOrgSource == null)
        {
            return null;
        }

        try
        {
            var repository = Repository.Factory.GetCoreV3(nugetOrgSource);
            var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>(cancellationToken);

            if (metadataResource == null)
            {
                return null;
            }

            var metadata = await metadataResource.GetMetadataAsync(
                packageId,
                includePrerelease: true,
                includeUnlisted: false,
                _cacheContext,
                _logger,
                cancellationToken);

            var metadataList = metadata.ToList();

            if (metadataList.Count == 0)
            {
                return null;
            }

            var result = new PackageVulnerabilityInfo
            {
                PackageId = packageId
            };

            // If a specific version is specified, check only that version; otherwise check all versions
            var versionsToCheck = version != null
                ? metadataList.Where(m => m.Identity.Version == version)
                : metadataList.OrderByDescending(m => m.Identity.Version);

            foreach (var meta in versionsToCheck)
            {
                var vulnerabilities = meta.Vulnerabilities?.ToList();
                if (vulnerabilities != null && vulnerabilities.Count > 0)
                {
                    var versionInfo = new PackageVersionVulnerabilityInfo
                    {
                        Version = meta.Identity.Version
                    };

                    foreach (var vuln in vulnerabilities)
                    {
                        versionInfo.Vulnerabilities.Add(new VulnerabilityDetail
                        {
                            AdvisoryUrl = vuln.AdvisoryUrl?.ToString() ?? "",
                            Severity = vuln.Severity.ToString()
                        });
                    }

                    result.VulnerableVersions.Add(versionInfo);
                }
            }

            // Add latest version info
            var latestVersion = metadataList.OrderByDescending(m => m.Identity.Version).FirstOrDefault();
            if (latestVersion != null)
            {
                result.LatestVersion = latestVersion.Identity.Version;
                result.LatestVersionHasVulnerabilities = latestVersion.Vulnerabilities?.Any() == true;
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cacheContext.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Package vulnerability information
/// </summary>
public sealed class PackageVulnerabilityInfo
{
    public required string PackageId { get; init; }
    public NuGetVersion? LatestVersion { get; set; }
    public bool LatestVersionHasVulnerabilities { get; set; }
    public List<PackageVersionVulnerabilityInfo> VulnerableVersions { get; } = [];

    public bool HasAnyVulnerabilities => VulnerableVersions.Count > 0;
}

/// <summary>
/// Vulnerability information for a specific version
/// </summary>
public sealed class PackageVersionVulnerabilityInfo
{
    public required NuGetVersion Version { get; init; }
    public List<VulnerabilityDetail> Vulnerabilities { get; } = [];
}

/// <summary>
/// Individual vulnerability details
/// </summary>
public sealed class VulnerabilityDetail
{
    public required string AdvisoryUrl { get; init; }
    public required string Severity { get; init; }
}

/// <summary>
/// Result of a package download operation
/// </summary>
public sealed class DownloadResult
{
    public bool IsSuccess { get; }
    public string? Path { get; }
    public string? Error { get; }

    private DownloadResult(bool isSuccess, string? path, string? error)
    {
        IsSuccess = isSuccess;
        Path = path;
        Error = error;
    }

    public static DownloadResult Success(string path) => new(true, path, null);
    public static DownloadResult Failure(string error) => new(false, null, error);
}







