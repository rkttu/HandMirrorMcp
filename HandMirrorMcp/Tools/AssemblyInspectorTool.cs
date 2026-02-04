using System.ComponentModel;
using System.Text;
using HandMirrorMcp.Services;
using ModelContextProtocol.Server;
using Mono.Cecil;

namespace HandMirrorMcp.Tools;

[McpServerToolType]
public sealed class AssemblyInspectorTool
{
    [McpServerTool(Name = "inspect_assembly")]
    [Description("Inspects a .NET assembly and lists all public types, members, and their attributes with XML documentation. Returns namespaces, classes, structs, delegates, interfaces, enums, methods, properties, fields, events, and nested types.")]
    public string InspectAssembly(
        [Description("The full path to the .NET assembly file (.dll or .exe) to inspect")]
        string assemblyPath,
        [Description("Include XML documentation comments if available (default: true)")]
        bool includeXmlDoc = true)
    {
        if (!File.Exists(assemblyPath))
        {
            return $"Error: File not found: {assemblyPath}";
        }

        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            var xmlDocService = includeXmlDoc ? new XmlDocService(assemblyPath) : null;
            var sb = new StringBuilder();

            sb.AppendLine($"Assembly: {assembly.FullName}");
            sb.AppendLine(new string('=', 80));

            AppendAssemblyInfo(sb, assembly);

            if (xmlDocService?.HasDocumentation == true)
            {
                sb.AppendLine($"  XML Documentation: {xmlDocService.XmlPath}");
            }

            var typesByNamespace = assembly.MainModule.Types
                .Where(t => IsPublicType(t))
                .GroupBy(t => string.IsNullOrEmpty(t.Namespace) ? "<global>" : t.Namespace)
                .OrderBy(g => g.Key);

            foreach (var namespaceGroup in typesByNamespace)
            {
                sb.AppendLine();
                sb.AppendLine($"Namespace: {namespaceGroup.Key}");
                sb.AppendLine(new string('-', 60));

                foreach (var type in namespaceGroup.OrderBy(t => t.Name))
                {
                    AppendType(sb, type, 0, xmlDocService);
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading assembly: {ex.Message}";
        }
    }

    [McpServerTool(Name = "list_namespaces")]
    [Description("Lists all namespaces in a .NET assembly")]
    public string ListNamespaces(
        [Description("The full path to the .NET assembly file (.dll or .exe)")]
        string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
        {
            return $"Error: File not found: {assemblyPath}";
        }

        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            var namespaces = assembly.MainModule.Types
                .Where(t => IsPublicType(t))
                .Select(t => string.IsNullOrEmpty(t.Namespace) ? "<global>" : t.Namespace)
                .Distinct()
                .OrderBy(n => n);

            var sb = new StringBuilder();
            sb.AppendLine($"Namespaces in {assembly.Name.Name}:");
            foreach (var ns in namespaces)
            {
                sb.AppendLine($"  - {ns}");
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading assembly: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_type_info")]
    [Description("Gets detailed information about a specific type in a .NET assembly with XML documentation")]
    public string GetTypeInfo(
        [Description("The full path to the .NET assembly file (.dll or .exe)")]
        string assemblyPath,
        [Description("The full name of the type (e.g., 'MyNamespace.MyClass')")]
        string typeName,
        [Description("Include XML documentation comments if available (default: true)")]
        bool includeXmlDoc = true)
    {
        if (!File.Exists(assemblyPath))
        {
            return $"Error: File not found: {assemblyPath}";
        }

        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            var xmlDocService = includeXmlDoc ? new XmlDocService(assemblyPath) : null;
            var type = assembly.MainModule.Types
                .FirstOrDefault(t => t.FullName == typeName);

            if (type == null)
            {
                return $"Error: Type '{typeName}' not found in assembly";
            }

            var sb = new StringBuilder();
            AppendType(sb, type, 0, xmlDocService);
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading assembly: {ex.Message}";
        }
    }

    private static bool IsPublicType(TypeDefinition type)
    {
        return type.IsPublic || type.IsNestedPublic;
    }

    private static void AppendAssemblyInfo(StringBuilder sb, AssemblyDefinition assembly)
    {
        var module = assembly.MainModule;

        string architecture = module.Architecture switch
        {
            TargetArchitecture.I386 => "x86",
            TargetArchitecture.AMD64 => "x64",
            TargetArchitecture.IA64 => "IA64",
            TargetArchitecture.ARM => "ARM",
            TargetArchitecture.ARMv7 => "ARMv7",
            TargetArchitecture.ARM64 => "ARM64",
            _ => module.Architecture.ToString()
        };

        sb.AppendLine($"  Architecture: {architecture}");
        sb.AppendLine($"  Runtime: {module.RuntimeVersion}");

        var targetFrameworkAttr = assembly.CustomAttributes
            .FirstOrDefault(a => a.AttributeType.Name == "TargetFrameworkAttribute");

        if (targetFrameworkAttr != null && targetFrameworkAttr.ConstructorArguments.Count > 0)
        {
            sb.AppendLine($"  TargetFramework: {targetFrameworkAttr.ConstructorArguments[0].Value}");
        }

        if (assembly.CustomAttributes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  [Assembly Attributes]");
            foreach (var attr in assembly.CustomAttributes.OrderBy(a => a.AttributeType.Name))
            {
                AppendAttribute(sb, attr, "    ");
            }
        }
    }

    private static void AppendAttribute(StringBuilder sb, CustomAttribute attr, string indent)
    {
        string attrName = attr.AttributeType.Name.Replace("Attribute", "");
        var args = new List<string>();

        foreach (var arg in attr.ConstructorArguments)
        {
            args.Add(FormatAttributeValue(arg.Value));
        }

        foreach (var prop in attr.Properties)
        {
            args.Add($"{prop.Name} = {FormatAttributeValue(prop.Argument.Value)}");
        }

        foreach (var field in attr.Fields)
        {
            args.Add($"{field.Name} = {FormatAttributeValue(field.Argument.Value)}");
        }

        string argsStr = args.Count > 0 ? $"({string.Join(", ", args)})" : "";
        sb.AppendLine($"{indent}[{attrName}{argsStr}]");
    }

    private static string FormatAttributeValue(object? value)
    {
        return value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            bool b => b.ToString().ToLower(),
            TypeReference t => $"typeof({t.Name})",
            _ => value.ToString() ?? ""
        };
    }

    private static void AppendAttributes(StringBuilder sb, IEnumerable<CustomAttribute> attributes, string indent)
    {
        foreach (var attr in attributes
            .Where(a => !IsCompilerGeneratedAttribute(a))
            .OrderBy(a => a.AttributeType.Name))
        {
            AppendAttribute(sb, attr, indent);
        }
    }

    private static bool IsCompilerGeneratedAttribute(CustomAttribute attr)
    {
        var name = attr.AttributeType.Name;
        return name is "CompilerGeneratedAttribute"
            or "AsyncStateMachineAttribute"
            or "IteratorStateMachineAttribute"
            or "DebuggerHiddenAttribute"
            or "DebuggerStepThroughAttribute";
    }

    private static bool IsAccessibleMember(MethodDefinition? method)
    {
        if (method == null) return false;
        return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
    }

    private static bool IsAccessibleField(FieldDefinition field)
    {
        return field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;
    }

    private static string GetTypeKind(TypeDefinition type)
    {
        if (type.IsEnum) return "enum";
        if (type.IsInterface) return "interface";
        if (type.IsValueType) return "struct";
        if (type.BaseType?.FullName == "System.MulticastDelegate") return "delegate";
        return "class";
    }

    private static string GetAccessModifier(MethodDefinition? method)
    {
        if (method == null) return "";
        if (method.IsPublic) return "public";
        if (method.IsFamilyOrAssembly) return "protected internal";
        if (method.IsFamily) return "protected";
        return "";
    }

    private static string GetFieldAccessModifier(FieldDefinition field)
    {
        if (field.IsPublic) return "public";
        if (field.IsFamilyOrAssembly) return "protected internal";
        if (field.IsFamily) return "protected";
        return "";
    }

    private static void AppendType(StringBuilder sb, TypeDefinition type, int indent, XmlDocService? xmlDoc = null)
    {
        string indentStr = new string(' ', indent * 2);
        string typeKind = GetTypeKind(type);

        sb.AppendLine();
        AppendAttributes(sb, type.CustomAttributes, indentStr);
        sb.AppendLine($"{indentStr}[{typeKind}] {type.Name}");

        // Type XML documentation
        var typeDoc = xmlDoc?.GetTypeDoc(type.FullName);
        if (typeDoc?.Summary != null)
        {
            sb.AppendLine($"{indentStr}  /// {typeDoc.Summary}");
        }

        if (type.IsEnum)
        {
            foreach (var field in type.Fields.Where(f => f.IsStatic && f.IsPublic))
            {
                var fieldDoc = xmlDoc?.GetFieldDoc(type.FullName, field.Name);
                if (fieldDoc?.Summary != null)
                {
                    sb.AppendLine($"{indentStr}    /// {fieldDoc.Summary}");
                }
                sb.AppendLine($"{indentStr}  const {field.Name}");
            }
            return;
        }

        if (typeKind == "delegate")
        {
            var invokeMethod = type.Methods.FirstOrDefault(m => m.Name == "Invoke");
            if (invokeMethod != null)
            {
                string parameters = string.Join(", ", invokeMethod.Parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                sb.AppendLine($"{indentStr}  ({parameters}) => {invokeMethod.ReturnType.Name}");
            }
            return;
        }

        // Fields
        var accessibleFields = type.Fields
            .Where(f => IsAccessibleField(f) && !f.IsSpecialName)
            .OrderBy(f => f.Name);

        foreach (var field in accessibleFields)
        {
            string modifier = GetFieldAccessModifier(field);
            string staticMod = field.IsStatic ? "static " : "";
            AppendAttributes(sb, field.CustomAttributes, $"{indentStr}    ");

            var fieldDoc = xmlDoc?.GetFieldDoc(type.FullName, field.Name);
            if (fieldDoc?.Summary != null)
            {
                sb.AppendLine($"{indentStr}    /// {fieldDoc.Summary}");
            }

            sb.AppendLine($"{indentStr}  [field] {modifier} {staticMod}{field.FieldType.Name} {field.Name}");
        }

        // Properties
        var accessibleProperties = type.Properties
            .Where(p => IsAccessibleMember(p.GetMethod) || IsAccessibleMember(p.SetMethod))
            .OrderBy(p => p.Name);

        foreach (var prop in accessibleProperties)
        {
            var getAccess = GetAccessModifier(prop.GetMethod);
            var setAccess = GetAccessModifier(prop.SetMethod);
            string accessors = "";
            if (!string.IsNullOrEmpty(getAccess)) accessors += $"get({getAccess}) ";
            if (!string.IsNullOrEmpty(setAccess)) accessors += $"set({setAccess})";
            AppendAttributes(sb, prop.CustomAttributes, $"{indentStr}    ");

            var propDoc = xmlDoc?.GetPropertyDoc(type.FullName, prop.Name);
            if (propDoc?.Summary != null)
            {
                sb.AppendLine($"{indentStr}    /// {propDoc.Summary}");
            }

            sb.AppendLine($"{indentStr}  [property] {prop.PropertyType.Name} {prop.Name} {{ {accessors}}}");
        }

        // Events
        var accessibleEvents = type.Events
            .Where(e => IsAccessibleMember(e.AddMethod) || IsAccessibleMember(e.RemoveMethod))
            .OrderBy(e => e.Name);

        foreach (var evt in accessibleEvents)
        {
            var access = GetAccessModifier(evt.AddMethod);
            AppendAttributes(sb, evt.CustomAttributes, $"{indentStr}    ");

            var eventDoc = xmlDoc?.GetEventDoc(type.FullName, evt.Name);
            if (eventDoc?.Summary != null)
            {
                sb.AppendLine($"{indentStr}    /// {eventDoc.Summary}");
            }

            sb.AppendLine($"{indentStr}  [event] {access} {evt.EventType.Name} {evt.Name}");
        }

        // Methods
        var accessibleMethods = type.Methods
            .Where(m => IsAccessibleMember(m) && !m.IsSpecialName && !m.IsConstructor)
            .OrderBy(m => m.Name);

        foreach (var method in accessibleMethods)
        {
            string modifier = GetAccessModifier(method);
            string staticMod = method.IsStatic ? "static " : "";
            string virtualMod = method.IsVirtual && !method.IsFinal ? "virtual " : "";
            string abstractMod = method.IsAbstract ? "abstract " : "";
            string parameters = string.Join(", ", method.Parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
            AppendAttributes(sb, method.CustomAttributes, $"{indentStr}    ");

            var methodDoc = xmlDoc?.GetMethodDoc(type.FullName, method.Name);
            AppendMethodDoc(sb, methodDoc, method, $"{indentStr}    ");

            sb.AppendLine($"{indentStr}  [method] {modifier} {staticMod}{virtualMod}{abstractMod}{method.ReturnType.Name} {method.Name}({parameters})");
        }

        // Constructors
        var accessibleCtors = type.Methods
            .Where(m => IsAccessibleMember(m) && m.IsConstructor)
            .OrderBy(m => m.Parameters.Count);

        foreach (var ctor in accessibleCtors)
        {
            string modifier = GetAccessModifier(ctor);
            string staticMod = ctor.IsStatic ? "static " : "";
            string parameters = string.Join(", ", ctor.Parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
            AppendAttributes(sb, ctor.CustomAttributes, $"{indentStr}    ");

            var ctorDoc = xmlDoc?.GetConstructorDoc(type.FullName);
            AppendMethodDoc(sb, ctorDoc, ctor, $"{indentStr}    ");

            sb.AppendLine($"{indentStr}  [ctor] {modifier} {staticMod}{type.Name}({parameters})");
        }

        // Nested Types
        var nestedTypes = type.NestedTypes
            .Where(t => t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamilyOrAssembly)
            .OrderBy(t => t.Name);

        foreach (var nestedType in nestedTypes)
        {
            AppendType(sb, nestedType, indent + 1, xmlDoc);
        }
    }

    private static void AppendMethodDoc(StringBuilder sb, XmlDocMember? doc, MethodDefinition method, string indent)
    {
        if (doc == null)
            return;

        if (doc.Summary != null)
        {
            sb.AppendLine($"{indent}/// {doc.Summary}");
        }

        // Parameter descriptions
        foreach (var param in method.Parameters)
        {
            if (doc.Parameters.TryGetValue(param.Name, out var paramDesc))
            {
                sb.AppendLine($"{indent}/// @param {param.Name}: {paramDesc}");
            }
        }

        // Return value description
        if (doc.Returns != null && method.ReturnType.FullName != "System.Void")
        {
            sb.AppendLine($"{indent}/// @returns: {doc.Returns}");
        }

        // Exceptions
        foreach (var (exType, exDesc) in doc.Exceptions)
        {
            var exTypeName = exType.StartsWith("T:") ? exType[2..] : exType;
            sb.AppendLine($"{indent}/// @throws {exTypeName}: {exDesc}");
        }
    }
}

