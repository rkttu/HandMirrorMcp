using System.ComponentModel;
using ModelContextProtocol.Server;

namespace HandMirrorMcp.Prompts;

[McpServerPromptType]
public sealed class NuGetInspectorPrompts
{
    [McpServerPrompt(Name = "explore_nuget_package")]
    [Description("Guides you through exploring a NuGet package and its types")]
    public static string ExploreNuGetPackage(
        [Description("The package ID to explore (e.g., 'Newtonsoft.Json')")]
        string packageId,
        [Description("Optional specific version")]
        string? version = null)
    {
        var versionInfo = string.IsNullOrEmpty(version) ? "latest version" : $"version {version}";

        return $"""
            Explore the NuGet package '{packageId}' ({versionInfo})
            
            Follow these steps:
            1. Use 'get_nuget_package_info' to get package metadata and dependencies
            2. Use 'get_nuget_package_versions' to see available versions
            3. Use 'inspect_nuget_package' to download and analyze the assemblies
            4. For interesting types, use 'inspect_nuget_package_type' to get detailed member info
            
            Summarize:
            - Package purpose and description
            - Main namespaces and their responsibilities
            - Key public APIs and how to use them
            - Dependencies and compatibility
            """;
    }

    [McpServerPrompt(Name = "find_nuget_package")]
    [Description("Helps find a NuGet package for a specific purpose")]
    public static string FindNuGetPackage(
        [Description("Description of what you need (e.g., 'JSON serialization', 'HTTP client')")]
        string purpose)
    {
        return $"""
            Find a suitable NuGet package for: {purpose}
            
            Steps:
            1. Use 'search_nuget_packages' with relevant keywords
            2. Compare top results by download count and recent updates
            3. Use 'get_nuget_package_info' on promising packages to check:
               - License compatibility
               - Dependencies
               - Target framework support
            4. Use 'inspect_nuget_package' to verify the API meets your needs
            
            Recommend the best option with reasoning.
            """;
    }

    [McpServerPrompt(Name = "compare_nuget_versions")]
    [Description("Compare different versions of a NuGet package")]
    public static string CompareNuGetVersions(
        [Description("The package ID to compare")]
        string packageId,
        [Description("First version to compare")]
        string version1,
        [Description("Second version to compare")]
        string version2)
    {
        return $"""
            Compare versions {version1} and {version2} of the NuGet package '{packageId}'
            
            Steps:
            1. Use 'get_nuget_package_info' for both versions to compare metadata
            2. Use 'inspect_nuget_package' for both versions
            3. Compare:
               - Target framework changes
               - Namespace additions/removals
               - Type additions/removals
               - Public API changes
               - Dependency changes
            
            Highlight:
            - Breaking changes
            - New features
            - Deprecations
            - Migration considerations
            """;
    }

    [McpServerPrompt(Name = "analyze_package_dependencies")]
    [Description("Analyze the dependency tree of a NuGet package")]
    public static string AnalyzePackageDependencies(
        [Description("The package ID to analyze")]
        string packageId,
        [Description("Optional target framework (e.g., 'net8.0')")]
        string? targetFramework = null)
    {
        var tfmInfo = string.IsNullOrEmpty(targetFramework)
            ? ""
            : $" for {targetFramework}";

        return $"""
            Analyze the dependencies of NuGet package '{packageId}'{tfmInfo}
            
            Steps:
            1. Use 'get_nuget_package_info' to get direct dependencies
            2. For each dependency, recursively get its dependencies
            3. Build a dependency tree
            
            Report:
            - Direct dependencies with version ranges
            - Transitive dependencies
            - Potential version conflicts
            - Total package count in the dependency tree
            - Any deprecated or vulnerable packages
            """;
    }
}
