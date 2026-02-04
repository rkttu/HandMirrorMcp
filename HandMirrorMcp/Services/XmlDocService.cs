using System.Xml.Linq;

namespace HandMirrorMcp.Services;

/// <summary>
/// Service for parsing and querying XML documentation (xmldoc) for assemblies
/// </summary>
public sealed class XmlDocService
{
    private readonly Dictionary<string, XmlDocMember> _members = new(StringComparer.Ordinal);
    private readonly string? _xmlPath;

    /// <summary>
    /// Loads XML documentation from an assembly path.
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly file (.dll)</param>
    public XmlDocService(string assemblyPath)
    {
        // Find .xml file with the same name as the assembly
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");

        if (!File.Exists(xmlPath))
        {
            // Try different case variations (Linux)
            var directory = Path.GetDirectoryName(assemblyPath);
            var fileName = Path.GetFileNameWithoutExtension(assemblyPath);

            if (directory != null)
            {
                var xmlFiles = Directory.GetFiles(directory, "*.xml")
                    .Where(f => Path.GetFileNameWithoutExtension(f)
                        .Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (xmlFiles.Count > 0)
                {
                    xmlPath = xmlFiles[0];
                }
            }
        }

        if (File.Exists(xmlPath))
        {
            _xmlPath = xmlPath;
            LoadXmlDoc(xmlPath);
        }
    }

    /// <summary>
    /// Whether XML documentation is loaded
    /// </summary>
    public bool HasDocumentation => _members.Count > 0;

    /// <summary>
    /// Path to the XML documentation file
    /// </summary>
    public string? XmlPath => _xmlPath;

    private void LoadXmlDoc(string xmlPath)
    {
        try
        {
            var doc = XDocument.Load(xmlPath);
            var members = doc.Root?.Element("members")?.Elements("member");

            if (members == null)
                return;

            foreach (var member in members)
            {
                var name = member.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(name))
                    continue;

                var xmlDocMember = new XmlDocMember
                {
                    Name = name,
                    Summary = GetElementText(member, "summary"),
                    Remarks = GetElementText(member, "remarks"),
                    Returns = GetElementText(member, "returns"),
                    Example = GetElementText(member, "example"),
                    Value = GetElementText(member, "value"),
                };

                // Parameters
                foreach (var param in member.Elements("param"))
                {
                    var paramName = param.Attribute("name")?.Value;
                    if (!string.IsNullOrEmpty(paramName))
                    {
                        xmlDocMember.Parameters[paramName] = NormalizeWhitespace(param.Value);
                    }
                }

                // Type parameters
                foreach (var typeParam in member.Elements("typeparam"))
                {
                    var paramName = typeParam.Attribute("name")?.Value;
                    if (!string.IsNullOrEmpty(paramName))
                    {
                        xmlDocMember.TypeParameters[paramName] = NormalizeWhitespace(typeParam.Value);
                    }
                }

                // Exceptions
                foreach (var exception in member.Elements("exception"))
                {
                    var cref = exception.Attribute("cref")?.Value;
                    if (!string.IsNullOrEmpty(cref))
                    {
                        xmlDocMember.Exceptions[cref] = NormalizeWhitespace(exception.Value);
                    }
                }

                _members[name] = xmlDocMember;
            }
        }
        catch
        {
            // Ignore XML parsing failures
        }
    }

    private static string? GetElementText(XElement parent, string elementName)
    {
        var element = parent.Element(elementName);
        if (element == null)
            return null;

        return NormalizeWhitespace(element.Value);
    }

    private static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Normalize whitespace across multiple lines
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l));

        return string.Join(" ", lines);
    }

    /// <summary>
    /// Gets documentation for a type.
    /// </summary>
    public XmlDocMember? GetTypeDoc(string fullTypeName)
    {
        var key = $"T:{fullTypeName}";
        return _members.GetValueOrDefault(key);
    }

    /// <summary>
    /// Gets documentation for a method.
    /// </summary>
    public XmlDocMember? GetMethodDoc(string fullTypeName, string methodName, IEnumerable<string>? parameterTypes = null)
    {
        // If no parameters
        var key = $"M:{fullTypeName}.{methodName}";

        if (parameterTypes != null && parameterTypes.Any())
        {
            key += $"({string.Join(",", parameterTypes)})";
        }

        if (_members.TryGetValue(key, out var member))
            return member;

        // If exact signature not found, search by method name only
        var prefix = $"M:{fullTypeName}.{methodName}";
        var match = _members.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => _members[k])
            .FirstOrDefault();

        return match;
    }

    /// <summary>
    /// Gets documentation for a property.
    /// </summary>
    public XmlDocMember? GetPropertyDoc(string fullTypeName, string propertyName)
    {
        var key = $"P:{fullTypeName}.{propertyName}";
        return _members.GetValueOrDefault(key);
    }

    /// <summary>
    /// Gets documentation for a field.
    /// </summary>
    public XmlDocMember? GetFieldDoc(string fullTypeName, string fieldName)
    {
        var key = $"F:{fullTypeName}.{fieldName}";
        return _members.GetValueOrDefault(key);
    }

    /// <summary>
    /// Gets documentation for an event.
    /// </summary>
    public XmlDocMember? GetEventDoc(string fullTypeName, string eventName)
    {
        var key = $"E:{fullTypeName}.{eventName}";
        return _members.GetValueOrDefault(key);
    }

    /// <summary>
    /// Gets documentation for a constructor.
    /// </summary>
    public XmlDocMember? GetConstructorDoc(string fullTypeName, IEnumerable<string>? parameterTypes = null)
    {
        var key = $"M:{fullTypeName}.#ctor";

        if (parameterTypes != null && parameterTypes.Any())
        {
            key += $"({string.Join(",", parameterTypes)})";
        }

        if (_members.TryGetValue(key, out var member))
            return member;

        // If exact signature not found, search by constructor name only
        var prefix = $"M:{fullTypeName}.#ctor";
        var match = _members.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => _members[k])
            .FirstOrDefault();

        return match;
    }
}

/// <summary>
/// Member information from XML documentation
/// </summary>
public sealed class XmlDocMember
{
    /// <summary>
    /// Member identifier (e.g., "T:Namespace.ClassName", "M:Namespace.ClassName.Method")
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Summary description
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Additional remarks
    /// </summary>
    public string? Remarks { get; init; }

    /// <summary>
    /// Return value description
    /// </summary>
    public string? Returns { get; init; }

    /// <summary>
    /// Example
    /// </summary>
    public string? Example { get; init; }

    /// <summary>
    /// Value description (for properties)
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// Description per parameter
    /// </summary>
    public Dictionary<string, string> Parameters { get; } = [];

    /// <summary>
    /// Description per type parameter
    /// </summary>
    public Dictionary<string, string> TypeParameters { get; } = [];

    /// <summary>
    /// Description per exception
    /// </summary>
    public Dictionary<string, string> Exceptions { get; } = [];

    /// <summary>
    /// Check if documentation exists
    /// </summary>
    public bool HasContent =>
        !string.IsNullOrEmpty(Summary) ||
        !string.IsNullOrEmpty(Remarks) ||
        !string.IsNullOrEmpty(Returns) ||
        Parameters.Count > 0;
}

