using System.ComponentModel;
using System.Text;
using HandMirrorMcp.Services;
using ModelContextProtocol.Server;
using Mono.Cecil;
using HandMirrorMcp.Constants;

namespace HandMirrorMcp.Tools;

[McpServerToolType]
public sealed class InteropInspectorTool
{
    [McpServerTool(Name = "inspect_native_dependencies")]
    [Description("Analyzes P/Invoke (DllImport, LibraryImport) and COM interop dependencies in a .NET assembly")]
    public string InspectNativeDependencies(
        [Description("The full path to the .NET assembly file (.dll or .exe) to inspect")]
        string assemblyPath,
        [Description("Include XML documentation if available (default: true)")]
        bool includeXmlDoc = true)
    {
        if (!File.Exists(assemblyPath))
        {
            return $"Error: File not found: {assemblyPath}";
        }

        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            var xmlDoc = includeXmlDoc ? new XmlDocService(assemblyPath) : null;
            var sb = new StringBuilder();

            sb.AppendLine($"Assembly: {assembly.FullName}");
            sb.AppendLine(new string('=', 80));

            var pinvokeInfo = AnalyzePInvoke(assembly, xmlDoc);
            var comInfo = AnalyzeCom(assembly, xmlDoc);

            // P/Invoke section
            if (pinvokeInfo.DllImports.Count > 0 || pinvokeInfo.LibraryImports.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Link + $" P/Invoke Native Dependencies");
                sb.AppendLine(new string('-', 60));

                // Group by native library
                var allNativeLibs = pinvokeInfo.DllImports
                    .Select(m => m.DllName)
                    .Concat(pinvokeInfo.LibraryImports.Select(m => m.LibraryName))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n);

                sb.AppendLine();
                sb.AppendLine("  Native Libraries:");
                foreach (var lib in allNativeLibs)
                {
                    var dllImportCount = pinvokeInfo.DllImports.Count(m => 
                        m.DllName.Equals(lib, StringComparison.OrdinalIgnoreCase));
                    var libImportCount = pinvokeInfo.LibraryImports.Count(m => 
                        m.LibraryName.Equals(lib, StringComparison.OrdinalIgnoreCase));
                    var total = dllImportCount + libImportCount;
                    sb.AppendLine($"    - {lib} ({total} functions)");
                }

                // DllImport details
                if (pinvokeInfo.DllImports.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("  [DllImport] Methods:");

                    foreach (var group in pinvokeInfo.DllImports.GroupBy(m => m.DllName).OrderBy(g => g.Key))
                    {
                        sb.AppendLine($"    [{group.Key}]");
                        foreach (var method in group.OrderBy(m => m.MethodName))
                        {
                            sb.AppendLine($"      - {method.DeclaringType}.{method.MethodName}");
                            if (!string.IsNullOrEmpty(method.EntryPoint) && method.EntryPoint != method.MethodName)
                            {
                                sb.AppendLine($"          EntryPoint: {method.EntryPoint}");
                            }
                            if (!string.IsNullOrEmpty(method.CallingConvention))
                            {
                                sb.AppendLine($"          CallingConvention: {method.CallingConvention}");
                            }
                            if (!string.IsNullOrEmpty(method.CharSet))
                            {
                                sb.AppendLine($"          CharSet: {method.CharSet}");
                            }
                            if (method.SetLastError)
                            {
                                sb.AppendLine($"          SetLastError: true");
                            }
                            if (!string.IsNullOrEmpty(method.Signature))
                            {
                                sb.AppendLine($"          Signature: {method.Signature}");
                            }
                            if (!string.IsNullOrEmpty(method.Summary))
                            {
                                sb.AppendLine($"          /// {method.Summary}");
                            }
                        }
                    }
                }

                // LibraryImport details (.NET 7+)
                if (pinvokeInfo.LibraryImports.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("  [LibraryImport] Methods (.NET 7+ Source Generated):");

                    foreach (var group in pinvokeInfo.LibraryImports.GroupBy(m => m.LibraryName).OrderBy(g => g.Key))
                    {
                        sb.AppendLine($"    [{group.Key}]");
                        foreach (var method in group.OrderBy(m => m.MethodName))
                        {
                            sb.AppendLine($"      - {method.DeclaringType}.{method.MethodName}");
                            if (!string.IsNullOrEmpty(method.EntryPoint) && method.EntryPoint != method.MethodName)
                            {
                                sb.AppendLine($"          EntryPoint: {method.EntryPoint}");
                            }
                            if (!string.IsNullOrEmpty(method.StringMarshalling))
                            {
                                sb.AppendLine($"          StringMarshalling: {method.StringMarshalling}");
                            }
                            if (!string.IsNullOrEmpty(method.Signature))
                            {
                                sb.AppendLine($"          Signature: {method.Signature}");
                            }
                            if (!string.IsNullOrEmpty(method.Summary))
                            {
                                sb.AppendLine($"          /// {method.Summary}");
                            }
                        }
                    }
                }
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Link + $" P/Invoke: No native dependencies found");
            }

            // COM section
            if (comInfo.ComTypes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("🔌 COM Interop Types");
                sb.AppendLine(new string('-', 60));

                // Interfaces
                var interfaces = comInfo.ComTypes.Where(t => t.IsInterface).ToList();
                if (interfaces.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("  COM Interfaces:");
                    foreach (var iface in interfaces.OrderBy(t => t.FullName))
                    {
                        sb.AppendLine($"    - {iface.FullName}");
                        if (!string.IsNullOrEmpty(iface.Guid))
                        {
                            sb.AppendLine($"        GUID: {iface.Guid}");
                        }
                        if (!string.IsNullOrEmpty(iface.InterfaceType))
                        {
                            sb.AppendLine($"        InterfaceType: {iface.InterfaceType}");
                        }
                        if (!string.IsNullOrEmpty(iface.Summary))
                        {
                            sb.AppendLine($"        /// {iface.Summary}");
                        }
                    }
                }

                // CoClass
                var coclasses = comInfo.ComTypes.Where(t => t.IsCoClass).ToList();
                if (coclasses.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("  COM CoClasses:");
                    foreach (var coclass in coclasses.OrderBy(t => t.FullName))
                    {
                        sb.AppendLine($"    - {coclass.FullName}");
                        if (!string.IsNullOrEmpty(coclass.Guid))
                        {
                            sb.AppendLine($"        CLSID: {coclass.Guid}");
                        }
                        if (!string.IsNullOrEmpty(coclass.ProgId))
                        {
                            sb.AppendLine($"        ProgId: {coclass.ProgId}");
                        }
                    }
                }

                // TypeLib references
                if (comInfo.TypeLibRefs.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("  Type Library References:");
                    foreach (var typeLib in comInfo.TypeLibRefs)
                    {
                        sb.AppendLine($"    - {typeLib}");
                    }
                }
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("🔌 COM Interop: No COM types found");
            }

            // UnmanagedCallersOnly (.NET 5+)
            var unmanagedCallersOnly = AnalyzeUnmanagedCallersOnly(assembly, xmlDoc);
            if (unmanagedCallersOnly.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("📤 Exported Native Functions (UnmanagedCallersOnly)");
                sb.AppendLine(new string('-', 60));

                foreach (var method in unmanagedCallersOnly.OrderBy(m => m.MethodName))
                {
                    sb.AppendLine($"  - {method.DeclaringType}.{method.MethodName}");
                    if (!string.IsNullOrEmpty(method.EntryPoint))
                    {
                        sb.AppendLine($"      EntryPoint: {method.EntryPoint}");
                    }
                    if (!string.IsNullOrEmpty(method.CallingConvention))
                    {
                        sb.AppendLine($"      CallConvs: {method.CallingConvention}");
                    }
                    if (!string.IsNullOrEmpty(method.Signature))
                    {
                        sb.AppendLine($"      Signature: {method.Signature}");
                    }
                    if (!string.IsNullOrEmpty(method.Summary))
                    {
                        sb.AppendLine($"      /// {method.Summary}");
                    }
                }
            }

            // Summary
            sb.AppendLine();
            sb.AppendLine("Summary:");
            sb.AppendLine($"  Native Libraries: {pinvokeInfo.DllImports.Select(m => m.DllName).Concat(pinvokeInfo.LibraryImports.Select(m => m.LibraryName)).Distinct(StringComparer.OrdinalIgnoreCase).Count()}");
            sb.AppendLine($"  DllImport Methods: {pinvokeInfo.DllImports.Count}");
            sb.AppendLine($"  LibraryImport Methods: {pinvokeInfo.LibraryImports.Count}");
            sb.AppendLine($"  COM Types: {comInfo.ComTypes.Count}");
            sb.AppendLine($"  Exported Functions: {unmanagedCallersOnly.Count}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading assembly: {ex.Message}";
        }
    }

    private static PInvokeAnalysisResult AnalyzePInvoke(AssemblyDefinition assembly, XmlDocService? xmlDoc)
    {
        var result = new PInvokeAnalysisResult();

        foreach (var type in assembly.MainModule.Types)
        {
            AnalyzePInvokeInType(type, result, xmlDoc);
        }

        return result;
    }

    private static void AnalyzePInvokeInType(TypeDefinition type, PInvokeAnalysisResult result, XmlDocService? xmlDoc)
    {
        foreach (var method in type.Methods)
        {
            // DllImport (extern methods)
            if (method.IsPInvokeImpl && method.PInvokeInfo != null)
            {
                var pinfo = method.PInvokeInfo;
                var methodDoc = xmlDoc?.GetMethodDoc(type.FullName, method.Name);

                result.DllImports.Add(new DllImportInfo
                {
                    DllName = pinfo.Module?.Name ?? "unknown",
                    MethodName = method.Name,
                    DeclaringType = type.FullName,
                    EntryPoint = pinfo.EntryPoint,
                    CallingConvention = GetCallingConvention(pinfo),
                    CharSet = GetCharSet(pinfo),
                    SetLastError = pinfo.SupportsLastError,
                    Signature = GetMethodSignature(method),
                    Summary = methodDoc?.Summary
                });
            }

            // Check LibraryImport attribute
            var libraryImportAttr = method.CustomAttributes
                .FirstOrDefault(a => a.AttributeType.Name == "LibraryImportAttribute");

            if (libraryImportAttr != null)
            {
                var libraryName = libraryImportAttr.ConstructorArguments.Count > 0
                    ? libraryImportAttr.ConstructorArguments[0].Value?.ToString() ?? "unknown"
                    : "unknown";

                var entryPoint = libraryImportAttr.Properties
                    .FirstOrDefault(p => p.Name == "EntryPoint").Argument.Value?.ToString();

                var stringMarshalling = libraryImportAttr.Properties
                    .FirstOrDefault(p => p.Name == "StringMarshalling").Argument.Value?.ToString();

                var methodDoc = xmlDoc?.GetMethodDoc(type.FullName, method.Name);

                result.LibraryImports.Add(new LibraryImportInfo
                {
                    LibraryName = libraryName,
                    MethodName = method.Name,
                    DeclaringType = type.FullName,
                    EntryPoint = entryPoint,
                    StringMarshalling = stringMarshalling,
                    Signature = GetMethodSignature(method),
                    Summary = methodDoc?.Summary
                });
            }
        }

        // Also analyze nested types
        foreach (var nestedType in type.NestedTypes)
        {
            AnalyzePInvokeInType(nestedType, result, xmlDoc);
        }
    }

    private static ComAnalysisResult AnalyzeCom(AssemblyDefinition assembly, XmlDocService? xmlDoc)
    {
        var result = new ComAnalysisResult();

        // Assembly-level TypeLib references
        foreach (var attr in assembly.CustomAttributes)
        {
            if (attr.AttributeType.Name == "ImportedFromTypeLibAttribute" ||
                attr.AttributeType.Name == "PrimaryInteropAssemblyAttribute")
            {
                var value = attr.ConstructorArguments.FirstOrDefault().Value?.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    result.TypeLibRefs.Add(value);
                }
            }
        }

        foreach (var type in assembly.MainModule.Types)
        {
            AnalyzeComInType(type, result, xmlDoc);
        }

        return result;
    }

    private static void AnalyzeComInType(TypeDefinition type, ComAnalysisResult result, XmlDocService? xmlDoc)
    {
        var hasComImport = type.CustomAttributes.Any(a => a.AttributeType.Name == "ComImportAttribute");
        var hasCoClass = type.CustomAttributes.Any(a => a.AttributeType.Name == "CoClassAttribute");
        var hasGuid = type.CustomAttributes.Any(a => a.AttributeType.Name == "GuidAttribute");
        var hasComVisible = type.CustomAttributes.Any(a => 
            a.AttributeType.Name == "ComVisibleAttribute" && 
            a.ConstructorArguments.FirstOrDefault().Value is true);

        if (hasComImport || hasCoClass || (hasGuid && (type.IsInterface || hasComVisible)))
        {
            var guidAttr = type.CustomAttributes
                .FirstOrDefault(a => a.AttributeType.Name == "GuidAttribute");
            var guid = guidAttr?.ConstructorArguments.FirstOrDefault().Value?.ToString();

            var interfaceTypeAttr = type.CustomAttributes
                .FirstOrDefault(a => a.AttributeType.Name == "InterfaceTypeAttribute");
            var interfaceType = interfaceTypeAttr?.ConstructorArguments.FirstOrDefault().Value?.ToString();

            var progIdAttr = type.CustomAttributes
                .FirstOrDefault(a => a.AttributeType.Name == "ProgIdAttribute");
            var progId = progIdAttr?.ConstructorArguments.FirstOrDefault().Value?.ToString();

            var typeDoc = xmlDoc?.GetTypeDoc(type.FullName);

            result.ComTypes.Add(new ComTypeInfo
            {
                FullName = type.FullName,
                IsInterface = type.IsInterface,
                IsCoClass = hasCoClass || (!type.IsInterface && hasComImport),
                Guid = guid,
                InterfaceType = interfaceType,
                ProgId = progId,
                Summary = typeDoc?.Summary
            });
        }

        // Also analyze nested types
        foreach (var nestedType in type.NestedTypes)
        {
            AnalyzeComInType(nestedType, result, xmlDoc);
        }
    }

    private static List<UnmanagedCallersOnlyInfo> AnalyzeUnmanagedCallersOnly(
        AssemblyDefinition assembly, XmlDocService? xmlDoc)
    {
        var result = new List<UnmanagedCallersOnlyInfo>();

        foreach (var type in assembly.MainModule.Types)
        {
            AnalyzeUnmanagedCallersOnlyInType(type, result, xmlDoc);
        }

        return result;
    }

    private static void AnalyzeUnmanagedCallersOnlyInType(
        TypeDefinition type, List<UnmanagedCallersOnlyInfo> result, XmlDocService? xmlDoc)
    {
        foreach (var method in type.Methods)
        {
            var attr = method.CustomAttributes
                .FirstOrDefault(a => a.AttributeType.Name == "UnmanagedCallersOnlyAttribute");

            if (attr != null)
            {
                var entryPoint = attr.Properties
                    .FirstOrDefault(p => p.Name == "EntryPoint").Argument.Value?.ToString();

                var callConvs = attr.Properties
                    .FirstOrDefault(p => p.Name == "CallConvs").Argument.Value;

                var callConvsStr = "";
                if (callConvs is CustomAttributeArgument[] callConvArray)
                {
                    callConvsStr = string.Join(", ", callConvArray
                        .Select(c => (c.Value as TypeReference)?.Name ?? c.Value?.ToString() ?? ""));
                }

                var methodDoc = xmlDoc?.GetMethodDoc(type.FullName, method.Name);

                result.Add(new UnmanagedCallersOnlyInfo
                {
                    MethodName = method.Name,
                    DeclaringType = type.FullName,
                    EntryPoint = entryPoint,
                    CallingConvention = callConvsStr,
                    Signature = GetMethodSignature(method),
                    Summary = methodDoc?.Summary
                });
            }
        }

        foreach (var nestedType in type.NestedTypes)
        {
            AnalyzeUnmanagedCallersOnlyInType(nestedType, result, xmlDoc);
        }
    }


    private static string GetCallingConvention(PInvokeInfo pinfo)
    {
        // Use Mono.Cecil's PInvokeAttributes to check calling convention
        var attrs = pinfo.Attributes;

        if ((attrs & PInvokeAttributes.CallConvCdecl) != 0) return "Cdecl";
        if ((attrs & PInvokeAttributes.CallConvStdCall) != 0) return "StdCall";
        if ((attrs & PInvokeAttributes.CallConvThiscall) != 0) return "ThisCall";
        if ((attrs & PInvokeAttributes.CallConvFastcall) != 0) return "FastCall";
        if ((attrs & PInvokeAttributes.CallConvWinapi) != 0) return "WinApi";

        return "";
    }

    private static string GetCharSet(PInvokeInfo pinfo)
    {
        if (pinfo.IsCharSetAnsi) return "Ansi";
        if (pinfo.IsCharSetUnicode) return "Unicode";
        if (pinfo.IsCharSetAuto) return "Auto";
        return "";
    }

    private static string GetMethodSignature(MethodDefinition method)
    {
        var parameters = string.Join(", ", method.Parameters
            .Select(p => $"{p.ParameterType.Name} {p.Name}"));
        return $"{method.ReturnType.Name} ({parameters})";
    }

    // Internal classes
    private sealed class PInvokeAnalysisResult
    {
        public List<DllImportInfo> DllImports { get; } = [];
        public List<LibraryImportInfo> LibraryImports { get; } = [];
    }

    private sealed class DllImportInfo
    {
        public required string DllName { get; init; }
        public required string MethodName { get; init; }
        public required string DeclaringType { get; init; }
        public string? EntryPoint { get; init; }
        public string? CallingConvention { get; init; }
        public string? CharSet { get; init; }
        public bool SetLastError { get; init; }
        public string? Signature { get; init; }
        public string? Summary { get; init; }
    }

    private sealed class LibraryImportInfo
    {
        public required string LibraryName { get; init; }
        public required string MethodName { get; init; }
        public required string DeclaringType { get; init; }
        public string? EntryPoint { get; init; }
        public string? StringMarshalling { get; init; }
        public string? Signature { get; init; }
        public string? Summary { get; init; }
    }

    private sealed class ComAnalysisResult
    {
        public List<ComTypeInfo> ComTypes { get; } = [];
        public List<string> TypeLibRefs { get; } = [];
    }

    private sealed class ComTypeInfo
    {
        public required string FullName { get; init; }
        public bool IsInterface { get; init; }
        public bool IsCoClass { get; init; }
        public string? Guid { get; init; }
        public string? InterfaceType { get; init; }
        public string? ProgId { get; init; }
        public string? Summary { get; init; }
    }

    private sealed class UnmanagedCallersOnlyInfo
    {
        public required string MethodName { get; init; }
        public required string DeclaringType { get; init; }
        public string? EntryPoint { get; init; }
        public string? CallingConvention { get; init; }
        public string? Signature { get; init; }
        public string? Summary { get; init; }
    }

    [McpServerTool(Name = "inspect_native_dll")]
    [Description("Analyzes a native (unmanaged) DLL's exported and imported functions. Similar to Dependency Walker. Windows only.")]
    public string InspectNativeDll(
        [Description("The full path to the native DLL file")]
        string dllPath,
        [Description("Include imported functions (dependencies) (default: true)")]
        bool includeImports = true,
        [Description("Maximum number of exports to show (default: 100, 0 for all)")]
        int maxExports = 100)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "Error: Native DLL analysis is only supported on Windows.";
        }

        if (!File.Exists(dllPath))
        {
            return $"Error: File not found: {dllPath}";
        }

        try
        {
            var result = PeAnalyzerService.AnalyzePeFile(dllPath);

            if (result == null)
            {
                return $"Error: '{Path.GetFileName(dllPath)}' is not a valid PE file.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Native DLL Analysis: {result.FileName}");
            sb.AppendLine(new string('=', 80));

            // Basic information
            sb.AppendLine();
            sb.AppendLine(Emoji.Clipboard + $" PE File Information");
            sb.AppendLine(new string('-', 60));
            sb.AppendLine($"  Path: {result.FilePath}");
            sb.AppendLine($"  Machine: {result.Machine}");
            sb.AppendLine($"  Architecture: {(result.Is64Bit ? "64-bit (PE32+)" : "32-bit (PE32)")}");
            sb.AppendLine($"  Type: {(result.IsDll ? "DLL" : "Executable")}");

            // Export functions
            sb.AppendLine();
            sb.AppendLine($"📤 Exported Functions ({result.Exports.Count} total)");
            sb.AppendLine(new string('-', 60));

            if (result.Exports.Count == 0)
            {
                sb.AppendLine("  No exported functions found.");
            }
            else
            {
                var namedExports = result.Exports.Where(e => e.Name != null).OrderBy(e => e.Name).ToList();
                var ordinalOnlyExports = result.Exports.Where(e => e.Name == null).OrderBy(e => e.Ordinal).ToList();

                sb.AppendLine($"  Named exports: {namedExports.Count}");
                sb.AppendLine($"  Ordinal-only exports: {ordinalOnlyExports.Count}");
                sb.AppendLine();

                var displayCount = maxExports > 0 ? Math.Min(maxExports, result.Exports.Count) : result.Exports.Count;
                var displayed = 0;

                // Named exports first
                foreach (var export in namedExports)
                {
                    if (displayed >= displayCount) break;
                    sb.AppendLine($"  [{export.Ordinal,4}] {export.Name}");
                    displayed++;
                }

                // Ordinal-only exports
                foreach (var export in ordinalOnlyExports)
                {
                    if (displayed >= displayCount) break;
                    sb.AppendLine($"  [{export.Ordinal,4}] (ordinal only) @ RVA 0x{export.Rva:X8}");
                    displayed++;
                }

                if (result.Exports.Count > displayCount)
                {
                    sb.AppendLine($"  ... and {result.Exports.Count - displayCount} more exports");
                }
            }

            // Import functions (dependencies)
            if (includeImports)
            {
                sb.AppendLine();
                sb.AppendLine($"📥 Imported Modules (Dependencies) ({result.Imports.Count} modules)");
                sb.AppendLine(new string('-', 60));

                if (result.Imports.Count == 0)
                {
                    sb.AppendLine("  No imports found.");
                }
                else
                {
                    foreach (var module in result.Imports.OrderBy(m => m.Name))
                    {
                        sb.AppendLine($"  [{module.Name}] ({module.Functions.Count} functions)");

                        // Show only first 10 functions
                        var funcsToShow = module.Functions.Take(10).ToList();
                        foreach (var func in funcsToShow)
                        {
                            if (func.IsOrdinal)
                            {
                                sb.AppendLine($"      @{func.Ordinal}");
                            }
                            else
                            {
                                sb.AppendLine($"      {func.Name}");
                            }
                        }

                        if (module.Functions.Count > 10)
                        {
                            sb.AppendLine($"      ... and {module.Functions.Count - 10} more functions");
                        }
                    }
                }
            }

            // Summary
            sb.AppendLine();
            sb.AppendLine("Summary:");
            sb.AppendLine($"  Total Exports: {result.Exports.Count}");
            sb.AppendLine($"  Named Exports: {result.Exports.Count(e => e.Name != null)}");
            sb.AppendLine($"  Total Import Modules: {result.Imports.Count}");
            sb.AppendLine($"  Total Import Functions: {result.Imports.Sum(m => m.Functions.Count)}");

            // Classify known system DLL dependencies
            var systemDlls = result.Imports
                .Where(m => IsSystemDll(m.Name))
                .Select(m => m.Name)
                .ToList();

            var nonSystemDlls = result.Imports
                .Where(m => !IsSystemDll(m.Name))
                .Select(m => m.Name)
                .ToList();

            if (systemDlls.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"  System DLLs: {string.Join(", ", systemDlls.Take(5))}");
                if (systemDlls.Count > 5)
                {
                    sb.AppendLine($"               ... and {systemDlls.Count - 5} more");
                }
            }

            if (nonSystemDlls.Count > 0)
            {
                sb.AppendLine($"  Other DLLs: {string.Join(", ", nonSystemDlls)}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error analyzing native DLL: {ex.Message}";
        }
    }

    [McpServerTool(Name = "search_native_exports")]
    [Description("Searches for exported functions by name pattern in a native DLL. Windows only.")]
    public string SearchNativeExports(
        [Description("The full path to the native DLL file")]
        string dllPath,
        [Description("Search pattern (case-insensitive, supports * wildcard)")]
        string pattern)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "Error: Native DLL analysis is only supported on Windows.";
        }

        if (!File.Exists(dllPath))
        {
            return $"Error: File not found: {dllPath}";
        }

        try
        {
            var result = PeAnalyzerService.AnalyzePeFile(dllPath);

            if (result == null)
            {
                return $"Error: '{Path.GetFileName(dllPath)}' is not a valid PE file.";
            }

            // Convert wildcard pattern to regex
            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            var regex = new System.Text.RegularExpressions.Regex(
                regexPattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var matches = result.Exports
                .Where(e => e.Name != null && regex.IsMatch(e.Name))
                .OrderBy(e => e.Name)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Search results for '{pattern}' in {Path.GetFileName(dllPath)}:");
            sb.AppendLine(new string('=', 60));

            if (matches.Count == 0)
            {
                sb.AppendLine("  No matching exports found.");
            }
            else
            {
                sb.AppendLine($"Found {matches.Count} matching export(s):");
                sb.AppendLine();

                foreach (var export in matches)
                {
                    sb.AppendLine($"  [{export.Ordinal,4}] {export.Name}");
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error searching exports: {ex.Message}";
        }
    }

    private static bool IsSystemDll(string dllName)
    {
        var name = dllName.ToLowerInvariant();
        return name.StartsWith("kernel32") ||
               name.StartsWith("ntdll") ||
               name.StartsWith("user32") ||
               name.StartsWith("gdi32") ||
               name.StartsWith("advapi32") ||
               name.StartsWith("shell32") ||
               name.StartsWith("ole32") ||
               name.StartsWith("oleaut32") ||
               name.StartsWith("msvc") ||
               name.StartsWith("vcruntime") ||
               name.StartsWith("ucrtbase") ||
               name.StartsWith("api-ms-") ||
               name.StartsWith("ext-ms-") ||
               name.StartsWith("combase") ||
               name.StartsWith("rpcrt4") ||
               name.StartsWith("ws2_32") ||
               name.StartsWith("crypt32") ||
               name.StartsWith("secur32") ||
               name.StartsWith("bcrypt") ||
               name.StartsWith("shlwapi");
    }
}



