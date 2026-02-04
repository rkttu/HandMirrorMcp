using System.ComponentModel;
using ModelContextProtocol.Server;

namespace HandMirrorMcp.Prompts;

[McpServerPromptType]
public sealed class AssemblyInspectorPrompts
{
    [McpServerPrompt(Name = "analyze_assembly")]
    [Description("Guides you through analyzing a .NET assembly step by step")]
    public static string AnalyzeAssembly(
        [Description("The full path to the .NET assembly file (.dll or .exe)")]
        string assemblyPath)
    {
        return $"""
            Please analyze the .NET assembly at: {assemblyPath}
            
            Follow these steps:
            1. First, use 'list_namespaces' to get an overview of the assembly structure
            2. For each namespace of interest, use 'get_type_info' to examine specific types
            3. If you need a complete dump, use 'inspect_assembly'
            
            Summarize the findings including:
            - Target framework and architecture
            - Main namespaces and their purposes
            - Key public types and their relationships
            - Notable attributes or patterns used
            """;
    }

    [McpServerPrompt(Name = "find_type")]
    [Description("Helps locate and understand a specific type in an assembly")]
    public static string FindType(
        [Description("The full path to the .NET assembly file")]
        string assemblyPath,
        [Description("The name of the type to find (can be partial)")]
        string typeName)
    {
        return $"""
            Find and analyze the type '{typeName}' in the assembly at: {assemblyPath}
            
            Steps:
            1. Use 'list_namespaces' to see available namespaces
            2. Use 'get_type_info' with the full type name to get detailed information
            
            Provide:
            - The full qualified name of the type
            - Its kind (class, struct, interface, enum, delegate)
            - All public members and their signatures
            - Any applied attributes
            - Nested types if present
            """;
    }

    [McpServerPrompt(Name = "compare_assemblies")]
    [Description("Compare two .NET assemblies to find differences")]
    public static string CompareAssemblies(
        [Description("Path to the first assembly")]
        string assemblyPath1,
        [Description("Path to the second assembly")]
        string assemblyPath2)
    {
        return $"""
            Compare the following two .NET assemblies:
            - Assembly 1: {assemblyPath1}
            - Assembly 2: {assemblyPath2}
            
            Use 'inspect_assembly' on both and compare:
            - Target framework differences
            - Namespace differences (added/removed)
            - Type differences (added/removed/modified)
            - Member signature changes
            - Attribute changes
            
            Highlight breaking changes that might affect consumers.
            """;
    }
}
