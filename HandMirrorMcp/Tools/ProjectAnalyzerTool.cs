using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ModelContextProtocol.Server;
using HandMirrorMcp.Constants;

namespace HandMirrorMcp.Tools;

[McpServerToolType]
public sealed partial class ProjectAnalyzerTool
{
    [McpServerTool(Name = "analyze_csproj")]
    [Description("Analyzes a .NET project file (.csproj) and identifies common issues, configuration problems, and provides recommendations.")]
    public string AnalyzeCsproj(
        [Description("The full path to the .csproj file to analyze")]
        string csprojPath,
        [Description("Check for package version conflicts with other projects in the solution (default: true)")]
        bool checkSolutionConflicts = true)
    {
        if (!File.Exists(csprojPath))
        {
            return $"Error: File not found: {csprojPath}";
        }

        if (!csprojPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return $"Error: Not a .csproj file: {csprojPath}";
        }

        try
        {
            var doc = XDocument.Load(csprojPath);
            var sb = new StringBuilder();
            var issues = new List<ProjectIssue>();
            var warnings = new List<ProjectIssue>();
            var info = new List<string>();

            sb.AppendLine($"Project Analysis: {Path.GetFileName(csprojPath)}");
            sb.AppendLine(new string('=', 80));
            sb.AppendLine($"Path: {csprojPath}");
            sb.AppendLine();

            // Check SDK-style
            var projectElement = doc.Root;
            if (projectElement == null)
            {
                return "Error: Invalid project file - no root element";
            }

            var sdk = projectElement.Attribute("Sdk")?.Value;
            var isSdkStyle = sdk != null;

            sb.AppendLine(Emoji.Clipboard + " Project Type:");
            sb.AppendLine(new string('-', 60));
            if (isSdkStyle)
            {
                sb.AppendLine($"  SDK Style: Yes");
                sb.AppendLine($"  SDK: {sdk}");
            }
            else
            {
                sb.AppendLine($"  SDK Style: No (Legacy format)");
                warnings.Add(new ProjectIssue(
                    "Legacy project format",
                    "Consider migrating to SDK-style project format for better tooling support.",
                    "https://learn.microsoft.com/en-us/dotnet/core/project-sdk/overview"));
            }

            // Analyze Target Framework
            sb.AppendLine();
            sb.AppendLine(Emoji.Target + $" Target Framework:");
            sb.AppendLine(new string('-', 60));

            var tfm = GetElementValue(doc, "TargetFramework");
            var tfms = GetElementValue(doc, "TargetFrameworks");

            if (!string.IsNullOrEmpty(tfms))
            {
                sb.AppendLine($"  Multi-targeting: {tfms}");
                var frameworks = tfms.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var f in frameworks)
                {
                    var (Status, Issue) = AnalyzeTargetFramework(f.Trim());
                    sb.AppendLine($"    - {f}: {Status}");
                    if (Issue != null) warnings.Add(Issue);
                }
            }
            else if (!string.IsNullOrEmpty(tfm))
            {
                var (Status, Issue) = AnalyzeTargetFramework(tfm);
                sb.AppendLine($"  Target: {tfm}");
                sb.AppendLine($"  Status: {Status}");
                if (Issue != null) warnings.Add(Issue);
            }
            else
            {
                issues.Add(new ProjectIssue(
                    "No TargetFramework specified",
                    "Project must specify TargetFramework or TargetFrameworks.",
                    null));
            }

            // Output Type
            var outputType = GetElementValue(doc, "OutputType");
            if (!string.IsNullOrEmpty(outputType))
            {
                sb.AppendLine($"  Output Type: {outputType}");
            }

            // Analyze package references
            sb.AppendLine();
            sb.AppendLine(Emoji.Package + " Package References:");
            sb.AppendLine(new string('-', 60));

            var packageRefs = doc.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference")
                .Select(e => new PackageRef
                {
                    Name = e.Attribute("Include")?.Value ?? "",
                    Version = e.Attribute("Version")?.Value ?? e.Element(XName.Get("Version", e.Name.NamespaceName))?.Value ?? "",
                    PrivateAssets = e.Attribute("PrivateAssets")?.Value ?? e.Element(XName.Get("PrivateAssets", e.Name.NamespaceName))?.Value
                })
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .ToList();

            if (packageRefs.Count == 0)
            {
                sb.AppendLine("  No package references found.");
            }
            else
            {
                sb.AppendLine($"  Total: {packageRefs.Count} packages");
                sb.AppendLine();

                var packagesWithoutVersion = packageRefs.Where(p => string.IsNullOrEmpty(p.Version)).ToList();
                var packagesWithFloatingVersion = packageRefs.Where(p => p.Version?.Contains('*') == true).ToList();

                foreach (var pkg in packageRefs.OrderBy(p => p.Name))
                {
                    var versionDisplay = string.IsNullOrEmpty(pkg.Version) ? "(no version - CPM?)" : pkg.Version;
                    sb.AppendLine($"  - {pkg.Name} {versionDisplay}");

                    // Check known problematic packages
                    CheckKnownPackageIssues(pkg, warnings);
                }

                if (packagesWithoutVersion.Count > 0 && !HasCentralPackageManagement(doc))
                {
                    warnings.Add(new ProjectIssue(
                        $"{packagesWithoutVersion.Count} packages without explicit version",
                        "Packages should have explicit versions unless using Central Package Management.",
                        "https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management"));
                }

                if (packagesWithFloatingVersion.Count > 0)
                {
                    warnings.Add(new ProjectIssue(
                        $"{packagesWithFloatingVersion.Count} packages with floating versions",
                        "Floating versions (e.g., 1.0.*) can cause reproducibility issues.",
                        null));
                }
            }

            // Analyze project references
            sb.AppendLine();
            sb.AppendLine(Emoji.Link + " Project References:");
            sb.AppendLine(new string('-', 60));

            var projectRefs = doc.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            if (projectRefs.Count == 0)
            {
                sb.AppendLine("  No project references found.");
            }
            else
            {
                foreach (var proj in projectRefs.OrderBy(p => p))
                {
                    var projPath = Path.Combine(Path.GetDirectoryName(csprojPath) ?? "", proj!);
                    var exists = File.Exists(projPath);
                    var icon = exists ? Emoji.CheckMark + "" : Emoji.CrossMark + "";
                    sb.AppendLine($"  {icon} {proj}");

                    if (!exists)
                    {
                        issues.Add(new ProjectIssue(
                            $"Project reference not found: {proj}",
                            "The referenced project file does not exist.",
                            null));
                    }
                }
            }

            // Analyze main properties
            sb.AppendLine();
            sb.AppendLine(Emoji.Gear + " Key Properties:");
            sb.AppendLine(new string('-', 60));

            var keyProperties = new[]
            {
                ("Nullable", "Nullable reference types"),
                ("ImplicitUsings", "Implicit global usings"),
                ("LangVersion", "C# language version"),
                ("EnableAOT", "Native AOT compilation"),
                ("PublishAot", "Publish as native AOT"),
                ("InvariantGlobalization", "Invariant globalization"),
                ("TreatWarningsAsErrors", "Treat warnings as errors"),
                ("WarningsAsErrors", "Specific warnings as errors"),
                ("NoWarn", "Suppressed warnings"),
                ("GenerateDocumentationFile", "XML documentation"),
                ("IsPackable", "Can be packed as NuGet"),
                ("RootNamespace", "Root namespace"),
                ("AssemblyName", "Assembly name")
            };

            foreach (var (propName, description) in keyProperties)
            {
                var value = GetElementValue(doc, propName);
                if (!string.IsNullOrEmpty(value))
                {
                    sb.AppendLine($"  {propName}: {value}");
                }
            }

            // Nullable check
            var nullable = GetElementValue(doc, "Nullable");
            if (string.IsNullOrEmpty(nullable) && isSdkStyle)
            {
                info.Add(Emoji.Bulb + " Consider enabling nullable reference types: <Nullable>enable</Nullable>");
            }

            // Check version conflicts with other projects in solution
            if (checkSolutionConflicts)
            {
                var solutionPath = FindSolutionFile(csprojPath);
                if (solutionPath != null)
                {
                    sb.AppendLine();
                    sb.AppendLine(Emoji.MagnifyingGlass + " Solution Analysis:");
                    sb.AppendLine(new string('-', 60));
                    sb.AppendLine($"  Solution: {Path.GetFileName(solutionPath)}");

                    var conflicts = CheckSolutionPackageConflicts(solutionPath, csprojPath, packageRefs);
                    if (conflicts.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("  ⚠️ Package version conflicts detected:");
                        foreach (var conflict in conflicts)
                        {
                            sb.AppendLine($"    - {conflict}");
                            warnings.Add(new ProjectIssue(
                                $"Version conflict: {conflict}",
                                "Different versions of the same package across projects can cause runtime issues.",
                                null));
                        }
                    }
                    else
                    {
                        sb.AppendLine("  ✅ No package version conflicts detected.");
                    }
                }
            }

            // Issues and warnings summary
            if (issues.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.RedCircle + " ERRORS:");
                sb.AppendLine(new string('-', 60));
                foreach (var issue in issues)
                {
                    sb.AppendLine($"  ❌ {issue.Title}");
                    sb.AppendLine($"     {issue.Description}");
                    if (issue.HelpUrl != null)
                    {
                        sb.AppendLine($"     📖 {issue.HelpUrl}");
                    }
                }
            }

            if (warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.YellowCircle + " WARNINGS:");
                sb.AppendLine(new string('-', 60));
                foreach (var warning in warnings)
                {
                    sb.AppendLine($"  ⚠️ {warning.Title}");
                    sb.AppendLine($"     {warning.Description}");
                    if (warning.HelpUrl != null)
                    {
                        sb.AppendLine($"     📖 {warning.HelpUrl}");
                    }
                }
            }

            if (info.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Bulb + " SUGGESTIONS:");
                sb.AppendLine(new string('-', 60));
                foreach (var item in info)
                {
                    sb.AppendLine($"  {item}");
                }
            }

            // Summary
            sb.AppendLine();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("Summary:");
            sb.AppendLine($"  Errors: {issues.Count}");
            sb.AppendLine($"  Warnings: {warnings.Count}");
            sb.AppendLine($"  Packages: {packageRefs.Count}");
            sb.AppendLine($"  Project References: {projectRefs.Count}");

            if (issues.Count == 0 && warnings.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.CheckMark + " No issues found. Project configuration looks good!");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error analyzing project file: {ex.Message}";
        }
    }

    [McpServerTool(Name = "analyze_solution")]
    [Description("Analyzes a .NET solution file (.sln or .slnx) and all its projects. Identifies cross-project issues, package conflicts, and provides an overview.")]
    public string AnalyzeSolution(
        [Description("The full path to the solution file (.sln or .slnx) to analyze")]
        string solutionPath)
    {
        if (!File.Exists(solutionPath))
        {
            return $"Error: File not found: {solutionPath}";
        }

        var ext = Path.GetExtension(solutionPath).ToLowerInvariant();
        if (ext != ".sln" && ext != ".slnx")
        {
            return $"Error: Not a solution file (.sln or .slnx): {solutionPath}";
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Solution Analysis: {Path.GetFileName(solutionPath)}");
            sb.AppendLine(new string('=', 80));
            sb.AppendLine($"Path: {solutionPath}");
            sb.AppendLine($"Format: {(ext == ".slnx" ? "XML (slnx)" : "Classic (sln)")}");
            sb.AppendLine();

            // Extract project list
            var projects = ext == ".slnx"
                ? ParseSlnxProjects(solutionPath)
                : ParseSlnProjects(solutionPath);

            var solutionDir = Path.GetDirectoryName(solutionPath) ?? "";

            sb.AppendLine(Emoji.Folder + " Projects:");
            sb.AppendLine(new string('-', 60));
            sb.AppendLine($"  Total: {projects.Count}");
            sb.AppendLine();

            var allPackages = new Dictionary<string, List<(string Project, string Version)>>(StringComparer.OrdinalIgnoreCase);
            var projectInfos = new List<(string Name, string Path, string? Tfm, int PackageCount, bool Exists)>();

            foreach (var project in projects.OrderBy(p => p.Name))
            {
                var fullPath = Path.IsPathRooted(project.Path)
                    ? project.Path
                    : Path.GetFullPath(Path.Combine(solutionDir, project.Path));

                var exists = File.Exists(fullPath);
                var icon = exists ? Emoji.CheckMark + "" : Emoji.CrossMark + "";

                if (exists && fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var doc = XDocument.Load(fullPath);
                        var tfm = GetElementValue(doc, "TargetFramework") ?? GetElementValue(doc, "TargetFrameworks");

                        var packages = doc.Descendants()
                            .Where(e => e.Name.LocalName == "PackageReference")
                            .Select(e => new
                            {
                                Name = e.Attribute("Include")?.Value ?? "",
                                Version = e.Attribute("Version")?.Value ?? e.Element(XName.Get("Version", e.Name.NamespaceName))?.Value ?? ""
                            })
                            .Where(p => !string.IsNullOrEmpty(p.Name))
                            .ToList();

                        projectInfos.Add((project.Name, project.Path, tfm, packages.Count, true));

                        sb.AppendLine($"  {icon} {project.Name}");
                        sb.AppendLine($"       Path: {project.Path}");
                        sb.AppendLine($"       TFM: {tfm ?? "N/A"}");
                        sb.AppendLine($"       Packages: {packages.Count}");

                        foreach (var pkg in packages)
                        {
                            if (!allPackages.TryGetValue(pkg.Name, out List<(string Project, string Version)>? value))
                            {
                                value = [];
                                allPackages[pkg.Name] = value;
                            }

                            value.Add((project.Name, pkg.Version));
                        }
                    }
                    catch
                    {
                        projectInfos.Add((project.Name, project.Path, null, 0, true));
                        sb.AppendLine($"  ⚠️ {project.Name} (failed to parse)");
                        sb.AppendLine($"       Path: {project.Path}");
                    }
                }
                else
                {
                    projectInfos.Add((project.Name, project.Path, null, 0, exists));
                    sb.AppendLine($"  {icon} {project.Name}");
                    sb.AppendLine($"       Path: {project.Path}");
                    if (!exists)
                    {
                        sb.AppendLine($"       ⚠️ Project file not found!");
                    }
                }
            }

            // Analyze package version conflicts
            sb.AppendLine();
            sb.AppendLine(Emoji.Package + " Package Version Analysis:");
            sb.AppendLine(new string('-', 60));

            var conflicts = allPackages
                .Where(p => p.Value.Select(v => v.Version).Distinct().Count() > 1)
                .ToList();

            if (conflicts.Count > 0)
            {
                sb.AppendLine($"  ⚠️ {conflicts.Count} packages with version conflicts:");
                sb.AppendLine();

                foreach (var conflict in conflicts.OrderBy(c => c.Key))
                {
                    sb.AppendLine($"  📦 {conflict.Key}:");
                    foreach (var (projectName, version) in conflict.Value.OrderBy(v => v.Version))
                    {
                        var displayVersion = string.IsNullOrEmpty(version) ? "(no version)" : version;
                        sb.AppendLine($"      - {projectName}: {displayVersion}");
                    }
                }
            }
            else
            {
                sb.AppendLine("  ✅ No package version conflicts detected.");
            }

            // Analyze TFM
            sb.AppendLine();
            sb.AppendLine(Emoji.Target + $" Target Framework Summary:");
            sb.AppendLine(new string('-', 60));

            var tfmGroups = projectInfos
                .Where(p => !string.IsNullOrEmpty(p.Tfm))
                .GroupBy(p => p.Tfm)
                .ToList();

            foreach (var group in tfmGroups.OrderBy(g => g.Key))
            {
                sb.AppendLine($"  {group.Key}: {group.Count()} project(s)");
            }

            // Recommendations
            sb.AppendLine();
            sb.AppendLine(Emoji.Bulb + " Recommendations:");
            sb.AppendLine(new string('-', 60));

            if (conflicts.Count > 0)
            {
                sb.AppendLine("  • Consider using Central Package Management (Directory.Packages.props)");
                sb.AppendLine("    to ensure consistent package versions across all projects.");
                sb.AppendLine("    📖 https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management");
            }

            var missingProjects = projectInfos.Where(p => !p.Exists).ToList();
            if (missingProjects.Count > 0)
            {
                sb.AppendLine($"  • {missingProjects.Count} project file(s) not found. Check solution references.");
            }

            var legacyTfms = tfmGroups.Where(g =>
                g.Key?.StartsWith("net4", StringComparison.OrdinalIgnoreCase) == true ||
                g.Key?.StartsWith("netstandard1", StringComparison.OrdinalIgnoreCase) == true).ToList();

            if (legacyTfms.Count > 0)
            {
                sb.AppendLine("  • Some projects target older frameworks. Consider upgrading to modern .NET.");
            }

            // Summary
            sb.AppendLine();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("Summary:");
            sb.AppendLine($"  Total Projects: {projects.Count}");
            sb.AppendLine($"  Valid Projects: {projectInfos.Count(p => p.Exists)}");
            sb.AppendLine($"  Total Unique Packages: {allPackages.Count}");
            sb.AppendLine($"  Version Conflicts: {conflicts.Count}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error analyzing solution: {ex.Message}";
        }
    }

    [McpServerTool(Name = "analyze_file_based_app")]
    [Description("Analyzes a .NET 10+ file-based app (.cs file with #: directives). File-based apps use special directives like #:package, #:property, #:sdk, and shebang lines to define project configuration within a single C# file.")]
    public string AnalyzeFileBasedApp(
        [Description("The full path to the .cs file to analyze")]
        string csFilePath)
    {
        if (!File.Exists(csFilePath))
        {
            return $"Error: File not found: {csFilePath}";
        }

        if (!csFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return $"Error: Not a C# file: {csFilePath}";
        }

        try
        {
            var lines = File.ReadAllLines(csFilePath);
            var sb = new StringBuilder();
            var issues = new List<ProjectIssue>();
            var warnings = new List<ProjectIssue>();
            var info = new List<string>();

            // Parse file-based app directives
            var fileBasedApp = ParseFileBasedAppDirectives(lines);

            if (!fileBasedApp.HasAnyDirectives)
            {
                return $"No file-based app directives found in: {Path.GetFileName(csFilePath)}\n\n" +
                       "This appears to be a regular C# file, not a .NET 10+ file-based app.\n\n" +
                       "File-based apps use directives like:\n" +
                       "  #!/usr/bin/env dotnet run   (shebang for Unix execution)\n" +
                       "  #:package Newtonsoft.Json   (NuGet package reference)\n" +
                       "  #:sdk Microsoft.NET.Sdk.Web (SDK specification)\n" +
                       "  #:property LangVersion=preview (MSBuild property)\n\n" +
                       Emoji.Book + " https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10#file-based-apps";
            }

            sb.AppendLine($"File-Based App Analysis: {Path.GetFileName(csFilePath)}");
            sb.AppendLine(new string('=', 80));
            sb.AppendLine($"Path: {csFilePath}");
            sb.AppendLine($"Type: .NET 10+ File-Based Application");
            sb.AppendLine();

            // Analyze Shebang
            if (fileBasedApp.Shebang != null)
            {
                sb.AppendLine("🐚 Shebang:");
                sb.AppendLine(new string('-', 60));
                sb.AppendLine($"  {fileBasedApp.Shebang.Line}");
                sb.AppendLine($"  Interpreter: {fileBasedApp.Shebang.Interpreter}");
                if (fileBasedApp.Shebang.Arguments.Count > 0)
                {
                    sb.AppendLine($"  Arguments: {string.Join(" ", fileBasedApp.Shebang.Arguments)}");
                }
                sb.AppendLine($"  Unix Executable: ✅ Can run with './{Path.GetFileName(csFilePath)}'");
                sb.AppendLine();
            }

            // Analyze SDK
            sb.AppendLine(Emoji.Clipboard + " SDK:");
            sb.AppendLine(new string('-', 60));
            if (fileBasedApp.Sdk != null)
            {
                sb.AppendLine($"  {fileBasedApp.Sdk}");
                
                var sdkInfo = fileBasedApp.Sdk.ToLowerInvariant() switch
                {
                    "microsoft.net.sdk" => "Console/Library application",
                    "microsoft.net.sdk.web" => "ASP.NET Core web application",
                    "microsoft.net.sdk.worker" => "Background service/Worker",
                    "microsoft.net.sdk.razor" => "Razor class library",
                    "microsoft.net.sdk.blazorwebassembly" => "Blazor WebAssembly",
                    _ => "Custom SDK"
                };
                sb.AppendLine($"  Type: {sdkInfo}");
            }
            else
            {
                sb.AppendLine("  (default: Microsoft.NET.Sdk)");
                info.Add(Emoji.Bulb + " You can specify SDK with '#:sdk Microsoft.NET.Sdk.Web' for web apps");
            }
            sb.AppendLine();

            // Analyze package references
            sb.AppendLine(Emoji.Package + " Package References:");
            sb.AppendLine(new string('-', 60));
            if (fileBasedApp.Packages.Count == 0)
            {
                sb.AppendLine("  No package references found.");
            }
            else
            {
                sb.AppendLine($"  Total: {fileBasedApp.Packages.Count} packages");
                sb.AppendLine();

                foreach (var pkg in fileBasedApp.Packages.OrderBy(p => p.Name))
                {
                    var versionDisplay = string.IsNullOrEmpty(pkg.Version) ? "(latest)" : $"@{pkg.Version}";
                    sb.AppendLine($"  - {pkg.Name} {versionDisplay}");

                    // Warn about package without version
                    if (string.IsNullOrEmpty(pkg.Version))
                    {
                        warnings.Add(new ProjectIssue(
                            $"Package '{pkg.Name}' has no version specified",
                            "Consider pinning version with '#:package PackageName@1.0.0' for reproducibility.",
                            null));
                    }
                }
            }
            sb.AppendLine();

            // Analyze project references
            sb.AppendLine(Emoji.Link + " Project References:");
            sb.AppendLine(new string('-', 60));
            if (fileBasedApp.ProjectReferences.Count == 0)
            {
                sb.AppendLine("  No project references found.");
            }
            else
            {
                foreach (var proj in fileBasedApp.ProjectReferences)
                {
                    var projPath = Path.Combine(Path.GetDirectoryName(csFilePath) ?? "", proj);
                    var exists = File.Exists(projPath);
                    var icon = exists ? Emoji.CheckMark + "" : Emoji.CrossMark + "";
                    sb.AppendLine($"  {icon} {proj}");

                    if (!exists)
                    {
                        issues.Add(new ProjectIssue(
                            $"Project reference not found: {proj}",
                            "The referenced project file does not exist.",
                            null));
                    }
                }
            }
            sb.AppendLine();

            // Analyze properties
            sb.AppendLine(Emoji.Gear + " Properties:");
            sb.AppendLine(new string('-', 60));
            if (fileBasedApp.Properties.Count == 0)
            {
                sb.AppendLine("  No custom properties defined.");
                info.Add(Emoji.Bulb + " You can set properties with '#:property Nullable=enable'");
            }
            else
            {
                foreach (var prop in fileBasedApp.Properties.OrderBy(p => p.Key))
                {
                    sb.AppendLine($"  {prop.Key}: {prop.Value}");
                }
            }
            sb.AppendLine();

            // Directive statistics
            sb.AppendLine("📊 Directive Statistics:");
            sb.AppendLine(new string('-', 60));
            sb.AppendLine($"  First code line: {fileBasedApp.FirstCodeLineNumber}");
            sb.AppendLine($"  Directive lines: {fileBasedApp.DirectiveCount}");
            if (fileBasedApp.InvalidDirectives.Count > 0)
            {
                sb.AppendLine($"  Invalid directives: {fileBasedApp.InvalidDirectives.Count}");
                foreach (var invalid in fileBasedApp.InvalidDirectives)
                {
                    issues.Add(new ProjectIssue(
                        $"Invalid directive at line {invalid.LineNumber}: {invalid.Line}",
                        "Check the directive syntax. Valid formats: #:package, #:sdk, #:property, #:project",
                        null));
                }
            }
            sb.AppendLine();

            // Issues and warnings
            if (issues.Count > 0)
            {
                sb.AppendLine(Emoji.RedCircle + " ERRORS:");
                sb.AppendLine(new string('-', 60));
                foreach (var issue in issues)
                {
                    sb.AppendLine($"  ❌ {issue.Title}");
                    sb.AppendLine($"     {issue.Description}");
                }
                sb.AppendLine();
            }

            if (warnings.Count > 0)
            {
                sb.AppendLine(Emoji.YellowCircle + " WARNINGS:");
                sb.AppendLine(new string('-', 60));
                foreach (var warning in warnings)
                {
                    sb.AppendLine($"  ⚠️ {warning.Title}");
                    sb.AppendLine($"     {warning.Description}");
                }
                sb.AppendLine();
            }

            if (info.Count > 0)
            {
                sb.AppendLine(Emoji.Bulb + " SUGGESTIONS:");
                sb.AppendLine(new string('-', 60));
                foreach (var item in info)
                {
                    sb.AppendLine($"  {item}");
                }
                sb.AppendLine();
            }

            // Usage guide
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("Usage:");
            sb.AppendLine($"  dotnet run {Path.GetFileName(csFilePath)}");
            if (fileBasedApp.Shebang != null)
            {
                sb.AppendLine($"  ./{Path.GetFileName(csFilePath)}  (Unix, after chmod +x)");
            }
            sb.AppendLine();
            sb.AppendLine(Emoji.Book + " Documentation:");
            sb.AppendLine("  https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10#file-based-apps");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error analyzing file-based app: {ex.Message}";
        }
    }

    private static FileBasedAppInfo ParseFileBasedAppDirectives(string[] lines)
    {
        var result = new FileBasedAppInfo();
        var inDirectiveSection = true;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            // Shebang (first line only)
            if (i == 0 && trimmed.StartsWith("#!"))
            {
                var parts = trimmed[2..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                result.Shebang = new ShebangInfo
                {
                    Line = trimmed,
                    Interpreter = parts.Length > 0 ? parts[0] : "",
                    Arguments = parts.Length > 1 ? [.. parts[1..]] : []
                };
                result.DirectiveCount++;
                continue;
            }

            // Process only in directive section
            if (inDirectiveSection)
            {
                // #: directive
                if (trimmed.StartsWith("#:"))
                {
                    result.DirectiveCount++;
                    var directive = trimmed[2..].Trim();

                    // #:package
                    if (directive.StartsWith("package ", StringComparison.OrdinalIgnoreCase))
                    {
                        var packageSpec = directive[8..].Trim();
                        var atIndex = packageSpec.IndexOf('@');
                        if (atIndex > 0)
                        {
                            result.Packages.Add(new FileBasedPackageRef
                            {
                                Name = packageSpec[..atIndex],
                                Version = packageSpec[(atIndex + 1)..]
                            });
                        }
                        else
                        {
                            result.Packages.Add(new FileBasedPackageRef
                            {
                                Name = packageSpec,
                                Version = null
                            });
                        }
                    }
                    // #:sdk
                    else if (directive.StartsWith("sdk ", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Sdk = directive[4..].Trim();
                    }
                    // #:property
                    else if (directive.StartsWith("property ", StringComparison.OrdinalIgnoreCase))
                    {
                        var propSpec = directive[9..].Trim();
                        var eqIndex = propSpec.IndexOf('=');
                        if (eqIndex > 0)
                        {
                            var key = propSpec[..eqIndex].Trim();
                            var value = propSpec[(eqIndex + 1)..].Trim();
                            result.Properties[key] = value;
                        }
                        else
                        {
                            result.InvalidDirectives.Add(new InvalidDirective
                            {
                                LineNumber = i + 1,
                                Line = trimmed
                            });
                        }
                    }
                    // #:project
                    else if (directive.StartsWith("project ", StringComparison.OrdinalIgnoreCase))
                    {
                        result.ProjectReferences.Add(directive[8..].Trim());
                    }
                    else
                    {
                        result.InvalidDirectives.Add(new InvalidDirective
                        {
                            LineNumber = i + 1,
                            Line = trimmed
                        });
                    }
                    continue;
                }

                // Regular comment (starts with # but not #:)
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*"))
                {
                    continue;
                }

                // First actual code line (not a directive)
                if (!trimmed.StartsWith('#'))
                {
                    result.FirstCodeLineNumber = i + 1;
                    inDirectiveSection = false;
                }
            }
            else
            {
                // Warn if #: directive found after directive section
                if (trimmed.StartsWith("#:"))
                {
                    result.InvalidDirectives.Add(new InvalidDirective
                    {
                        LineNumber = i + 1,
                        Line = trimmed + " (directive after code started)"
                    });
                }
            }
        }

        return result;
    }

    [McpServerTool(Name = "analyze_config_file")]
    [Description("Analyzes .NET Framework configuration files (web.config, app.config, *.exe.config). Identifies common issues with binding redirects, connection strings, app settings, and other configuration elements.")]
    public string AnalyzeConfigFile(
        [Description("The full path to the configuration file (web.config, app.config, or *.exe.config)")]
        string configPath)
    {
        if (!File.Exists(configPath))
        {
            return $"Error: File not found: {configPath}";
        }

        var fileName = Path.GetFileName(configPath).ToLowerInvariant();
        if (!fileName.EndsWith(".config"))
        {
            return $"Error: Not a .config file: {configPath}";
        }

        try
        {
            var doc = XDocument.Load(configPath);
            var sb = new StringBuilder();
            var issues = new List<ProjectIssue>();
            var warnings = new List<ProjectIssue>();
            var info = new List<string>();

            var configType = fileName switch
            {
                "web.config" => "ASP.NET/IIS Web Configuration",
                "app.config" => "Application Configuration (source)",
                _ when fileName.EndsWith(".exe.config") => "Application Configuration (deployed)",
                _ => "Configuration File"
            };

            sb.AppendLine($"Configuration Analysis: {Path.GetFileName(configPath)}");
            sb.AppendLine(new string('=', 80));
            sb.AppendLine($"Path: {configPath}");
            sb.AppendLine($"Type: {configType}");
            sb.AppendLine();

            var root = doc.Root;
            if (root == null || root.Name.LocalName != "configuration")
            {
                return "Error: Invalid configuration file - root element must be <configuration>";
            }

            // Analyze configSections
            var configSections = root.Element("configSections");
            if (configSections != null)
            {
                var sections = configSections.Descendants()
                    .Where(e => e.Name.LocalName == "section" || e.Name.LocalName == "sectionGroup")
                    .ToList();
                if (sections.Count > 0)
                {
                    sb.AppendLine(Emoji.Clipboard + " Custom Config Sections:");
                    sb.AppendLine(new string('-', 60));
                    foreach (var section in sections.Take(10))
                    {
                        var name = section.Attribute("name")?.Value ?? "(unnamed)";
                        var type = section.Attribute("type")?.Value;
                        sb.AppendLine($"  - {name}");
                        if (type != null && type.Length > 60)
                        {
                            sb.AppendLine($"      Type: {type[..60]}...");
                        }
                    }
                    if (sections.Count > 10)
                    {
                        sb.AppendLine($"  ... and {sections.Count - 10} more");
                    }
                    sb.AppendLine();
                }
            }

            // Analyze appSettings
            var appSettings = root.Element("appSettings");
            if (appSettings != null)
            {
                var settings = appSettings.Elements("add").ToList();
                sb.AppendLine(Emoji.Gear + " App Settings:");
                sb.AppendLine(new string('-', 60));
                sb.AppendLine($"  Total: {settings.Count} setting(s)");

                if (settings.Count > 0)
                {
                    sb.AppendLine();
                    foreach (var setting in settings.Take(15))
                    {
                        var key = setting.Attribute("key")?.Value ?? "(no key)";
                        var value = setting.Attribute("value")?.Value ?? "";

                        // Mask sensitive information
                        var displayValue = IsSensitiveKey(key)
                            ? "***MASKED***"
                            : (value.Length > 50 ? value[..50] + "..." : value);

                        sb.AppendLine($"  - {key}: {displayValue}");
                    }
                    if (settings.Count > 15)
                    {
                        sb.AppendLine($"  ... and {settings.Count - 15} more");
                    }
                }
                sb.AppendLine();
            }

            // Analyze connectionStrings
            var connectionStrings = root.Element("connectionStrings");
            if (connectionStrings != null)
            {
                var connStrs = connectionStrings.Elements("add").ToList();
                sb.AppendLine(Emoji.Link + " Connection Strings:");
                sb.AppendLine(new string('-', 60));
                sb.AppendLine($"  Total: {connStrs.Count} connection string(s)");

                if (connStrs.Count > 0)
                {
                    sb.AppendLine();
                    foreach (var connStr in connStrs)
                    {
                        var name = connStr.Attribute("name")?.Value ?? "(unnamed)";
                        var connString = connStr.Attribute("connectionString")?.Value ?? "";
                        var provider = connStr.Attribute("providerName")?.Value;

                        sb.AppendLine($"  📌 {name}");
                        if (provider != null)
                        {
                            sb.AppendLine($"       Provider: {provider}");
                        }

                        // Extract only server/database info from connection string (excluding password)
                        var serverMatch = ServerNameRegex().Match(connString);
                        var dbMatch = DatabaseNameRegex().Match(connString);

                        if (serverMatch.Success)
                            sb.AppendLine($"       Server: {serverMatch.Groups[1].Value}");
                        if (dbMatch.Success)
                            sb.AppendLine($"       Database: {dbMatch.Groups[1].Value}");

                        // Detect LocalDB
                        if (connString.Contains("LocalDB", StringComparison.OrdinalIgnoreCase) ||
                            connString.Contains("localdb", StringComparison.OrdinalIgnoreCase))
                        {
                            info.Add(Emoji.Bulb + " '{name}' uses LocalDB - ensure LocalDB is installed on target machines");
                        }
                    }
                }
                sb.AppendLine();
            }

            // Analyze runtime/assemblyBinding (binding redirects)
            var runtime = root.Element("runtime");
            if (runtime != null)
            {
                var ns = XNamespace.Get("urn:schemas-microsoft-com:asm.v1");
                var assemblyBinding = runtime.Element(ns + "assemblyBinding");

                if (assemblyBinding != null)
                {
                    var dependentAssemblies = assemblyBinding.Elements(ns + "dependentAssembly").ToList();

                    sb.AppendLine("🔀 Assembly Binding Redirects:");
                    sb.AppendLine(new string('-', 60));
                    sb.AppendLine($"  Total: {dependentAssemblies.Count} redirect(s)");

                    if (dependentAssemblies.Count > 0)
                    {
                        sb.AppendLine();
                        var redirectList = new List<(string Name, string OldVersion, string NewVersion)>();

                        foreach (var da in dependentAssemblies)
                        {
                            var identity = da.Element(ns + "assemblyIdentity");
                            var bindingRedirect = da.Element(ns + "bindingRedirect");

                            if (identity != null && bindingRedirect != null)
                            {
                                var name = identity.Attribute("name")?.Value ?? "(unknown)";
                                var oldVersion = bindingRedirect.Attribute("oldVersion")?.Value ?? "";
                                var newVersion = bindingRedirect.Attribute("newVersion")?.Value ?? "";

                                redirectList.Add((name, oldVersion, newVersion));

                                // Check version range
                                if (oldVersion.Contains('-'))
                                {
                                    var parts = oldVersion.Split('-');
                                    if (parts.Length == 2 && parts[0] != "0.0.0.0")
                                    {
                                        warnings.Add(new ProjectIssue(
                                            $"Binding redirect for '{name}' doesn't start from 0.0.0.0",
                                            $"Consider using '0.0.0.0-{parts[1]}' to cover all versions.",
                                            null));
                                    }
                                }
                            }
                        }

                        foreach (var (name, oldVersion, newVersion) in redirectList.OrderBy(r => r.Name).Take(20))
                        {
                            sb.AppendLine($"  - {name}");
                            sb.AppendLine($"      {oldVersion} → {newVersion}");
                        }

                        if (redirectList.Count > 20)
                        {
                            sb.AppendLine($"  ... and {redirectList.Count - 20} more");
                        }
                    }
                    sb.AppendLine();
                }
            }

            // Analyze system.web (web.config only)
            if (fileName == "web.config")
            {
                var systemWeb = root.Element("system.web");
                if (systemWeb != null)
                {
                    sb.AppendLine("🌐 ASP.NET Configuration (system.web):");
                    sb.AppendLine(new string('-', 60));

                    // compilation
                    var compilation = systemWeb.Element("compilation");
                    if (compilation != null)
                    {
                        var debug = compilation.Attribute("debug")?.Value;
                        var targetFramework = compilation.Attribute("targetFramework")?.Value;

                        if (debug == "true")
                        {
                            warnings.Add(new ProjectIssue(
                                "Debug compilation is enabled",
                                "Set debug='false' in production for better performance.",
                                null));
                            sb.AppendLine("  ⚠️ Debug: true (should be false in production)");
                        }
                        else
                        {
                            sb.AppendLine($"  Debug: {debug ?? "not set"}");
                        }

                        if (targetFramework != null)
                        {
                            sb.AppendLine($"  Target Framework: {targetFramework}");
                        }
                    }

                    // authentication
                    var authentication = systemWeb.Element("authentication");
                    if (authentication != null)
                    {
                        var mode = authentication.Attribute("mode")?.Value ?? "None";
                        sb.AppendLine($"  Authentication: {mode}");
                    }

                    // customErrors
                    var customErrors = systemWeb.Element("customErrors");
                    if (customErrors != null)
                    {
                        var mode = customErrors.Attribute("mode")?.Value ?? "RemoteOnly";
                        sb.AppendLine($"  Custom Errors: {mode}");

                        if (mode == "Off")
                        {
                            warnings.Add(new ProjectIssue(
                                "Custom errors are disabled",
                                "Set mode='RemoteOnly' or 'On' in production to hide detailed error information.",
                                null));
                        }
                    }

                    // httpRuntime
                    var httpRuntime = systemWeb.Element("httpRuntime");
                    if (httpRuntime != null)
                    {
                        var targetFx = httpRuntime.Attribute("targetFramework")?.Value;
                        var maxRequestLength = httpRuntime.Attribute("maxRequestLength")?.Value;
                        var executionTimeout = httpRuntime.Attribute("executionTimeout")?.Value;

                        if (targetFx != null) sb.AppendLine($"  HTTP Runtime Target: {targetFx}");
                        if (maxRequestLength != null) sb.AppendLine($"  Max Request Length: {maxRequestLength} KB");
                        if (executionTimeout != null) sb.AppendLine($"  Execution Timeout: {executionTimeout} sec");
                    }

                    sb.AppendLine();
                }

                // Analyze system.webServer (IIS 7+)
                var systemWebServer = root.Element("system.webServer");
                if (systemWebServer != null)
                {
                    sb.AppendLine(Emoji.Desktop + " IIS Configuration (system.webServer):");
                    sb.AppendLine(new string('-', 60));

                    var handlers = systemWebServer.Element("handlers");
                    if (handlers != null)
                    {
                        var handlerList = handlers.Elements().ToList();
                        sb.AppendLine($"  Handlers: {handlerList.Count}");
                    }

                    var modules = systemWebServer.Element("modules");
                    if (modules != null)
                    {
                        var moduleList = modules.Elements().ToList();
                        sb.AppendLine($"  Modules: {moduleList.Count}");
                    }

                    var staticContent = systemWebServer.Element("staticContent");
                    if (staticContent != null)
                    {
                        var mimeTypes = staticContent.Elements("mimeMap").ToList();
                        if (mimeTypes.Count > 0)
                        {
                            sb.AppendLine($"  Custom MIME Types: {mimeTypes.Count}");
                        }
                    }

                    var rewrite = systemWebServer.Element("rewrite");
                    if (rewrite != null)
                    {
                        var rules = rewrite.Descendants("rule").ToList();
                        sb.AppendLine($"  URL Rewrite Rules: {rules.Count}");
                    }

                    sb.AppendLine();
                }
            }

            // Issues and warnings summary
            if (issues.Count > 0)
            {
                sb.AppendLine(Emoji.RedCircle + " ERRORS:");
                sb.AppendLine(new string('-', 60));
                foreach (var issue in issues)
                {
                    sb.AppendLine($"  ❌ {issue.Title}");
                    sb.AppendLine($"     {issue.Description}");
                }
                sb.AppendLine();
            }

            if (warnings.Count > 0)
            {
                sb.AppendLine(Emoji.YellowCircle + " WARNINGS:");
                sb.AppendLine(new string('-', 60));
                foreach (var warning in warnings)
                {
                    sb.AppendLine($"  ⚠️ {warning.Title}");
                    sb.AppendLine($"     {warning.Description}");
                }
                sb.AppendLine();
            }

            if (info.Count > 0)
            {
                sb.AppendLine(Emoji.Bulb + " SUGGESTIONS:");
                sb.AppendLine(new string('-', 60));
                foreach (var item in info)
                {
                    sb.AppendLine($"  {item}");
                }
                sb.AppendLine();
            }

            // Summary
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("Summary:");
            sb.AppendLine($"  Config Type: {configType}");
            sb.AppendLine($"  Errors: {issues.Count}");
            sb.AppendLine($"  Warnings: {warnings.Count}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error analyzing configuration file: {ex.Message}";
        }
    }

    [McpServerTool(Name = "analyze_packages_config")]
    [Description("Analyzes a packages.config file (legacy NuGet package references for .NET Framework projects). Identifies outdated packages, version conflicts, and recommends migration to PackageReference.")]
    public async Task<string> AnalyzePackagesConfig(
        [Description("The full path to the packages.config file")]
        string packagesConfigPath,
        [Description("Check for newer versions of packages (default: true)")]
        bool checkForUpdates = true)
    {
        if (!File.Exists(packagesConfigPath))
        {
            return $"Error: File not found: {packagesConfigPath}";
        }

        if (!Path.GetFileName(packagesConfigPath).Equals("packages.config", StringComparison.OrdinalIgnoreCase))
        {
            return $"Error: Not a packages.config file: {packagesConfigPath}";
        }

        try
        {
            var doc = XDocument.Load(packagesConfigPath);
            var sb = new StringBuilder();
            var issues = new List<ProjectIssue>();
            var warnings = new List<ProjectIssue>();
            var info = new List<string>();

            sb.AppendLine($"Packages.config Analysis: {Path.GetFileName(packagesConfigPath)}");
            sb.AppendLine(new string('=', 80));
            sb.AppendLine($"Path: {packagesConfigPath}");
            sb.AppendLine($"Format: Legacy NuGet (packages.config)");
            sb.AppendLine();

            // Recommend migration
            warnings.Add(new ProjectIssue(
                "Legacy packages.config format detected",
                "Consider migrating to PackageReference format for better dependency resolution and performance.",
                "https://learn.microsoft.com/en-us/nuget/consume-packages/migrate-packages-config-to-package-reference"));

            var packages = doc.Root?.Elements("package")
                .Select(p => new
                {
                    Id = p.Attribute("id")?.Value ?? "",
                    Version = p.Attribute("version")?.Value ?? "",
                    TargetFramework = p.Attribute("targetFramework")?.Value,
                    DevelopmentDependency = p.Attribute("developmentDependency")?.Value == "true"
                })
                .Where(p => !string.IsNullOrEmpty(p.Id))
                .ToList() ?? [];

            sb.AppendLine(Emoji.Package + " Package References:");
            sb.AppendLine(new string('-', 60));
            sb.AppendLine($"  Total: {packages.Count} package(s)");
            sb.AppendLine();

            if (packages.Count == 0)
            {
                sb.AppendLine("  No packages found.");
            }
            else
            {
                // Group by TFM
                var tfmGroups = packages
                    .GroupBy(p => p.TargetFramework ?? "(not specified)")
                    .OrderBy(g => g.Key)
                    .ToList();

                foreach (var group in tfmGroups)
                {
                    sb.AppendLine($"  📁 Target Framework: {group.Key}");
                    foreach (var pkg in group.OrderBy(p => p.Id))
                    {
                        var devDep = pkg.DevelopmentDependency ? " [dev]" : "";
                        sb.AppendLine($"      - {pkg.Id} v{pkg.Version}{devDep}");
                    }
                    sb.AppendLine();
                }

                // Check for different versions of the same package
                var duplicates = packages
                    .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Select(p => p.Version).Distinct().Count() > 1)
                    .ToList();

                if (duplicates.Count > 0)
                {
                    sb.AppendLine(Emoji.Warning + " Multiple Versions Detected:");
                    sb.AppendLine(new string('-', 60));
                    foreach (var dup in duplicates)
                    {
                        sb.AppendLine($"  📦 {dup.Key}:");
                        foreach (var ver in dup.Select(p => new { p.Version, p.TargetFramework }).Distinct())
                        {
                            sb.AppendLine($"      - v{ver.Version} ({ver.TargetFramework ?? "no TFM"})");
                        }
                        warnings.Add(new ProjectIssue(
                            $"Package '{dup.Key}' has multiple versions",
                            "This can cause runtime conflicts. Consider consolidating to a single version.",
                            null));
                    }
                    sb.AppendLine();
                }

                // Check known vulnerable/outdated packages
                var knownIssues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Newtonsoft.Json"] = "12.0.0",
                    ["System.Net.Http"] = "4.3.4",
                    ["Microsoft.AspNet.Mvc"] = "5.2.7",
                    ["log4net"] = "2.0.15",
                    ["jQuery"] = "3.5.0"
                };

                foreach (var pkg in packages)
                {
                    if (knownIssues.TryGetValue(pkg.Id, out var minVersion))
                    {
                        if (Version.TryParse(pkg.Version.Split('-')[0], out var currentVer) &&
                            Version.TryParse(minVersion, out var minVer) &&
                            currentVer < minVer)
                        {
                            warnings.Add(new ProjectIssue(
                                $"Potentially outdated/vulnerable: {pkg.Id} v{pkg.Version}",
                                $"Consider upgrading to at least v{minVersion}.",
                                null));
                        }
                    }
                }

                // Find related project files
                var projectDir = Path.GetDirectoryName(packagesConfigPath);
                if (projectDir != null)
                {
                    var csprojFiles = Directory.GetFiles(projectDir, "*.csproj");
                    if (csprojFiles.Length > 0)
                    {
                        sb.AppendLine(Emoji.Link + " Related Project:");
                        sb.AppendLine(new string('-', 60));
                        foreach (var csproj in csprojFiles)
                        {
                            sb.AppendLine($"  - {Path.GetFileName(csproj)}");
                        }
                        sb.AppendLine();

                        info.Add(Emoji.Bulb + " Use 'analyze_csproj' to analyze the related project file.");
                    }
                }
            }

            // Issues and warnings summary
            if (issues.Count > 0)
            {
                sb.AppendLine(Emoji.RedCircle + " ERRORS:");
                sb.AppendLine(new string('-', 60));
                foreach (var issue in issues)
                {
                    sb.AppendLine($"  ❌ {issue.Title}");
                    sb.AppendLine($"     {issue.Description}");
                    if (issue.HelpUrl != null)
                    {
                        sb.AppendLine($"     📖 {issue.HelpUrl}");
                    }
                }
                sb.AppendLine();
            }

            if (warnings.Count > 0)
            {
                sb.AppendLine(Emoji.YellowCircle + " WARNINGS:");
                sb.AppendLine(new string('-', 60));
                foreach (var warning in warnings)
                {
                    sb.AppendLine($"  ⚠️ {warning.Title}");
                    sb.AppendLine($"     {warning.Description}");
                    if (warning.HelpUrl != null)
                    {
                        sb.AppendLine($"     📖 {warning.HelpUrl}");
                    }
                }
                sb.AppendLine();
            }

            if (info.Count > 0)
            {
                sb.AppendLine(Emoji.Bulb + " SUGGESTIONS:");
                sb.AppendLine(new string('-', 60));
                foreach (var item in info)
                {
                    sb.AppendLine($"  {item}");
                }
                sb.AppendLine();
            }

            // Summary
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("Summary:");
            sb.AppendLine($"  Total Packages: {packages.Count}");
            sb.AppendLine($"  Errors: {issues.Count}");
            sb.AppendLine($"  Warnings: {warnings.Count}");
            sb.AppendLine();
            sb.AppendLine(Emoji.Book + " Migration Guide:");
            sb.AppendLine("  https://learn.microsoft.com/en-us/nuget/consume-packages/migrate-packages-config-to-package-reference");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error analyzing packages.config: {ex.Message}";
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        var sensitivePatterns = new[]
        {
            "password", "pwd", "secret", "key", "token", "apikey", "api_key",
            "connectionstring", "connstr", "credential", "auth"
        };

        var lowerKey = key.ToLowerInvariant();
        return sensitivePatterns.Any(p => lowerKey.Contains(p));
    }

    [McpServerTool(Name = "explain_build_error")]
    [Description("Explains a .NET/MSBuild/C# build error code and provides solutions. Supports CS, MSB, NU, NETSDK error codes.")]
    public string ExplainBuildError(
        [Description("The error code (e.g., 'CS0246', 'NU1605', 'NETSDK1005', 'MSB4019')")]
        string errorCode,
        [Description("Additional context or the full error message (optional)")]
        string? errorMessage = null)
    {
        var code = errorCode.Trim().ToUpperInvariant();
        var sb = new StringBuilder();

        sb.AppendLine($"Build Error Explanation: {code}");
        sb.AppendLine(new string('=', 80));

        var explanation = GetErrorExplanation(code);

        if (explanation != null)
        {
            sb.AppendLine();
            sb.AppendLine(Emoji.Clipboard + $" Category: {explanation.Category}");
            sb.AppendLine(Emoji.RedCircle + $" Severity: {explanation.Severity}");
            sb.AppendLine();
            sb.AppendLine(Emoji.Book + " Description:");
            sb.AppendLine(new string('-', 60));
            sb.AppendLine($"  {explanation.Description}");
            sb.AppendLine();
            sb.AppendLine(Emoji.Wrench + " Common Causes:");
            sb.AppendLine(new string('-', 60));
            foreach (var cause in explanation.Causes)
            {
                sb.AppendLine($"  • {cause}");
            }
            sb.AppendLine();
            sb.AppendLine(Emoji.CheckMark + " Solutions:");
            sb.AppendLine(new string('-', 60));
            for (int i = 0; i < explanation.Solutions.Count; i++)
            {
                sb.AppendLine($"  {i + 1}. {explanation.Solutions[i]}");
            }

            if (explanation.HelpUrl != null)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.Books + " Documentation: {explanation.HelpUrl}");
            }

            // HandMirrorMcp tool recommendations
            if (explanation.RecommendedTools.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Emoji.HammerAndWrench + " Recommended HandMirrorMcp tools:");
                sb.AppendLine(new string('-', 60));
                foreach (var tool in explanation.RecommendedTools)
                {
                    sb.AppendLine($"  • {tool}");
                }
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine(Emoji.Warning + " Unknown error code. Here's general guidance:");
            sb.AppendLine();

            if (code.StartsWith("CS"))
            {
                sb.AppendLine("This is a C# compiler error.");
                sb.AppendLine(Emoji.Books + " Search: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/{code.ToLowerInvariant()}");
            }
            else if (code.StartsWith("MSB"))
            {
                sb.AppendLine("This is an MSBuild error.");
                sb.AppendLine(Emoji.Books + " Search: https://learn.microsoft.com/en-us/visualstudio/msbuild/errors/{code.ToLowerInvariant()}");
            }
            else if (code.StartsWith("NU"))
            {
                sb.AppendLine("This is a NuGet error.");
                sb.AppendLine(Emoji.Books + " Search: https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/{code.ToLowerInvariant()}");
            }
            else if (code.StartsWith("NETSDK"))
            {
                sb.AppendLine("This is a .NET SDK error.");
                sb.AppendLine(Emoji.Books + " Search: https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/{code.ToLowerInvariant()}");
            }
        }

        // Additional context analysis
        if (!string.IsNullOrEmpty(errorMessage))
        {
            sb.AppendLine();
            sb.AppendLine(Emoji.FileText + " Context Analysis:");
            sb.AppendLine(new string('-', 60));

            // Try to extract type name
            var typeMatch = TypeNameRegex().Match(errorMessage);
            if (typeMatch.Success)
            {
                var typeName = typeMatch.Groups[1].Value;
                sb.AppendLine($"  Missing type: '{typeName}'");
                sb.AppendLine($"  💡 Use 'find_package_by_type' tool with typeName='{typeName}'");
            }

            // Try to extract package name
            var packageMatch = PackageNameRegex().Match(errorMessage);
            if (packageMatch.Success)
            {
                var packageName = packageMatch.Groups[1].Value;
                sb.AppendLine($"  Related package: '{packageName}'");
                sb.AppendLine($"  💡 Use 'get_nuget_package_info' tool with packageId='{packageName}'");
            }

            // Try to extract version
            var versionMatch = VersionRegex().Match(errorMessage);
            if (versionMatch.Success)
            {
                var version = versionMatch.Groups[0].Value;
                sb.AppendLine($"  Version mentioned: '{version}'");
            }
        }

        return sb.ToString();
    }

    #region Helper Methods

    private static string? GetElementValue(XDocument doc, string elementName)
    {
        return doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == elementName)?.Value;
    }

    private static (string Status, ProjectIssue? Issue) AnalyzeTargetFramework(string tfm)
    {
        var lower = tfm.ToLowerInvariant();

        // .NET 9+
        if (lower.StartsWith("net9"))
            return (Emoji.CheckMark + " Current (.NET 9)", null);

        // .NET 8 LTS
        if (lower.StartsWith("net8"))
            return (Emoji.CheckMark + " LTS (.NET 8 - supported until Nov 2026)", null);

        // .NET 7 (out of support)
        if (lower.StartsWith("net7"))
            return (Emoji.Warning + " Out of support (.NET 7 - ended May 2024)", new ProjectIssue(
                $"{tfm} is out of support",
                "Upgrade to .NET 8 (LTS) or .NET 9.",
                "https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core"));

        // .NET 6 LTS
        if (lower.StartsWith("net6"))
            return (Emoji.Warning + " End of life (.NET 6 - ended Nov 2024)", new ProjectIssue(
                $"{tfm} is end of life",
                "Upgrade to .NET 8 (LTS) for continued support.",
                "https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core"));

        // .NET 5 (out of support)
        if (lower.StartsWith("net5"))
            return (Emoji.RedCircle + " Out of support (.NET 5)", new ProjectIssue(
                $"{tfm} is out of support",
                "Upgrade to .NET 8 (LTS) immediately.",
                null));

        // .NET Core 3.x
        if (lower.StartsWith("netcoreapp3"))
            return (Emoji.RedCircle + " Out of support (.NET Core 3.x)", new ProjectIssue(
                $"{tfm} is out of support",
                "Upgrade to .NET 8 (LTS).",
                null));

        // .NET Standard
        if (lower.StartsWith("netstandard2.1"))
            return (Emoji.CheckMark + " .NET Standard 2.1 (wide compatibility)", null);
        if (lower.StartsWith("netstandard2.0"))
            return (Emoji.CheckMark + " .NET Standard 2.0 (widest compatibility)", null);
        if (lower.StartsWith("netstandard"))
            return (Emoji.Warning + " Old .NET Standard version", new ProjectIssue(
                $"{tfm} is an older .NET Standard",
                "Consider upgrading to netstandard2.0 for better API surface.",
                null));

        // .NET Framework
        if (lower.StartsWith("net4"))
            return (Emoji.Warning + " .NET Framework (legacy)", new ProjectIssue(
                $"{tfm} is .NET Framework",
                "Consider migrating to .NET 8 for cross-platform support and performance improvements.",
                "https://learn.microsoft.com/en-us/dotnet/core/porting/"));

        return ($"Unknown: {tfm}", null);
    }

    private static bool HasCentralPackageManagement(XDocument doc)
    {
        var managePkgVersionsCentrally = GetElementValue(doc, "ManagePackageVersionsCentrally");
        return managePkgVersionsCentrally?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static void CheckKnownPackageIssues(PackageRef pkg, List<ProjectIssue> warnings)
    {
        var name = pkg.Name.ToLowerInvariant();

        // Known problematic packages
        if (name == "microsoft.aspnetcore.mvc" && !string.IsNullOrEmpty(pkg.Version))
        {
            if (pkg.Version.StartsWith("1.") || pkg.Version.StartsWith("2.0"))
            {
                warnings.Add(new ProjectIssue(
                    $"Old ASP.NET Core MVC version: {pkg.Version}",
                    "This version is out of support. Upgrade to ASP.NET Core 8.0+.",
                    null));
            }
        }

        if (name == "newtonsoft.json" && !string.IsNullOrEmpty(pkg.Version))
        {
            if (Version.TryParse(pkg.Version.Split('-')[0], out var v) && v < new Version(13, 0))
            {
                warnings.Add(new ProjectIssue(
                    $"Older Newtonsoft.Json version: {pkg.Version}",
                    "Consider upgrading to 13.0.3+ or using System.Text.Json for new projects.",
                    null));
            }
        }
    }

    private static string? FindSolutionFile(string csprojPath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(csprojPath) ?? "");

        while (dir != null)
        {
            var slnFiles = dir.GetFiles("*.sln").Concat(dir.GetFiles("*.slnx")).ToList();
            if (slnFiles.Count > 0)
            {
                return slnFiles[0].FullName;
            }
            dir = dir.Parent;
        }

        return null;
    }

    private static List<string> CheckSolutionPackageConflicts(string solutionPath, string currentCsprojPath, List<PackageRef> currentPackages)
    {
        var conflicts = new List<string>();
        var ext = Path.GetExtension(solutionPath).ToLowerInvariant();
        var solutionDir = Path.GetDirectoryName(solutionPath) ?? "";

        var projects = ext == ".slnx"
            ? ParseSlnxProjects(solutionPath)
            : ParseSlnProjects(solutionPath);

        foreach (var project in projects)
        {
            var fullPath = Path.IsPathRooted(project.Path)
                ? project.Path
                : Path.GetFullPath(Path.Combine(solutionDir, project.Path));

            if (fullPath.Equals(currentCsprojPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!File.Exists(fullPath) || !fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var doc = XDocument.Load(fullPath);
                var otherPackages = doc.Descendants()
                    .Where(e => e.Name.LocalName == "PackageReference")
                    .Select(e => new
                    {
                        Name = e.Attribute("Include")?.Value ?? "",
                        Version = e.Attribute("Version")?.Value ?? e.Element(XName.Get("Version", e.Name.NamespaceName))?.Value ?? ""
                    })
                    .Where(p => !string.IsNullOrEmpty(p.Name))
                    .ToDictionary(p => p.Name, p => p.Version, StringComparer.OrdinalIgnoreCase);

                foreach (var pkg in currentPackages)
                {
                    if (otherPackages.TryGetValue(pkg.Name, out var otherVersion))
                    {
                        if (!string.IsNullOrEmpty(pkg.Version) && !string.IsNullOrEmpty(otherVersion) &&
                            !pkg.Version.Equals(otherVersion, StringComparison.OrdinalIgnoreCase))
                        {
                            conflicts.Add($"{pkg.Name}: {pkg.Version} vs {otherVersion} (in {project.Name})");
                        }
                    }
                }
            }
            catch
            {
                // Skip projects that can't be parsed
            }
        }

        return conflicts;
    }

    private static List<(string Name, string Path)> ParseSlnProjects(string slnPath)
    {
        var projects = new List<(string Name, string Path)>();
        var regex = SlnProjectRegex();

        foreach (var line in File.ReadLines(slnPath))
        {
            var match = regex.Match(line);
            if (match.Success)
            {
                var name = match.Groups[1].Value;
                var path = match.Groups[2].Value.Replace('\\', Path.DirectorySeparatorChar);
                projects.Add((name, path));
            }
        }

        return projects;
    }

    private static List<(string Name, string Path)> ParseSlnxProjects(string slnxPath)
    {
        var projects = new List<(string Name, string Path)>();

        try
        {
            var doc = XDocument.Load(slnxPath);

            foreach (var projectElement in doc.Descendants().Where(e => e.Name.LocalName == "Project"))
            {
                var path = projectElement.Attribute("Path")?.Value;
                if (!string.IsNullOrEmpty(path))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    projects.Add((name, path.Replace('\\', Path.DirectorySeparatorChar)));
                }
            }
        }
        catch
        {
            // Failed to parse slnx
        }

        return projects;
    }

    private static ErrorExplanation? GetErrorExplanation(string code)
    {
        return code switch
        {
            // C# Compiler Errors
            "CS0246" => new ErrorExplanation
            {
                Category = "C# Compiler",
                Severity = "Error",
                Description = "The type or namespace name could not be found. This usually means a missing using directive or assembly reference.",
                Causes = [
                    "Missing 'using' statement for the namespace",
                    "Missing NuGet package reference",
                    "Missing project reference",
                    "Typo in type name",
                    "Target framework doesn't include the type"
                ],
                Solutions = [
                    "Add the correct 'using' statement at the top of your file",
                    "Install the required NuGet package: dotnet add package <PackageName>",
                    "Add a project reference: dotnet add reference <ProjectPath>",
                    "Check for typos in the type name",
                    "Verify the type exists in your target framework"
                ],
                HelpUrl = "https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0246",
                RecommendedTools = [
                    "find_package_by_type - Find which NuGet package provides the missing type",
                    "search_nuget_packages - Search for packages by name"
                ]
            },

            "CS1061" => new ErrorExplanation
            {
                Category = "C# Compiler",
                Severity = "Error",
                Description = "The type does not contain a definition for the specified member and no accessible extension method could be found.",
                Causes = [
                    "Method or property doesn't exist on this type",
                    "Using wrong overload or wrong type",
                    "Missing using statement for extension methods",
                    "API changed between package versions"
                ],
                Solutions = [
                    "Check the correct method/property name in documentation",
                    "Add using statement for extension method namespace",
                    "Verify you're using the correct type",
                    "Check if the API exists in your package version"
                ],
                HelpUrl = "https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs1061",
                RecommendedTools = [
                    "inspect_nuget_package_type - View the actual members of a type",
                    "compare_nuget_versions - Check API differences between versions"
                ]
            },

            "CS0103" => new ErrorExplanation
            {
                Category = "C# Compiler",
                Severity = "Error",
                Description = "The name does not exist in the current context.",
                Causes = [
                    "Variable not declared",
                    "Typo in variable name",
                    "Variable out of scope",
                    "Missing using directive for static methods"
                ],
                Solutions = [
                    "Declare the variable before using it",
                    "Check for typos",
                    "Ensure variable is in scope",
                    "Add 'using static' for static method access"
                ],
                HelpUrl = "https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0103",
                RecommendedTools = []
            },

            "CS0029" => new ErrorExplanation
            {
                Category = "C# Compiler",
                Severity = "Error",
                Description = "Cannot implicitly convert type 'X' to type 'Y'.",
                Causes = [
                    "Assigning incompatible types",
                    "Missing explicit cast",
                    "Wrong generic type parameter",
                    "Async method missing 'await'"
                ],
                Solutions = [
                    "Add explicit cast if conversion is valid",
                    "Use correct type",
                    "Add 'await' for Task types",
                    "Use conversion methods like .ToString(), Parse(), etc."
                ],
                HelpUrl = "https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0029",
                RecommendedTools = []
            },

            // NuGet Errors
            "NU1605" => new ErrorExplanation
            {
                Category = "NuGet",
                Severity = "Warning (treated as error)",
                Description = "Detected package downgrade. A package reference has a lower version than a dependency requires.",
                Causes = [
                    "Direct package reference with lower version than transitive dependency",
                    "Conflicting version requirements between packages",
                    "Manually pinned lower version"
                ],
                Solutions = [
                    "Remove the direct package reference to use the higher version",
                    "Update the direct reference to match or exceed the required version",
                    "Add <NoWarn>NU1605</NoWarn> if downgrade is intentional (not recommended)"
                ],
                HelpUrl = "https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1605",
                RecommendedTools = [
                    "get_nuget_dependencies_tree - View the full dependency tree",
                    "analyze_csproj - Check for version conflicts"
                ]
            },

            "NU1101" => new ErrorExplanation
            {
                Category = "NuGet",
                Severity = "Error",
                Description = "Unable to find package. No packages exist with this id in sources.",
                Causes = [
                    "Package name typo",
                    "Package doesn't exist on nuget.org",
                    "Package is on a private feed not configured",
                    "Network connectivity issues"
                ],
                Solutions = [
                    "Verify the package name on nuget.org",
                    "Check NuGet.config for correct package sources",
                    "Add private feed credentials if needed",
                    "Check network connectivity"
                ],
                HelpUrl = "https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1101",
                RecommendedTools = [
                    "search_nuget_packages - Search for the correct package name",
                    "list_nuget_sources - View configured package sources"
                ]
            },

            "NU1202" => new ErrorExplanation
            {
                Category = "NuGet",
                Severity = "Error",
                Description = "Package is not compatible with your project's target framework.",
                Causes = [
                    "Package doesn't support your target framework",
                    "Package only supports newer .NET versions",
                    "Package is platform-specific (Windows only, etc.)"
                ],
                Solutions = [
                    "Find an alternative package that supports your framework",
                    "Upgrade your project's target framework",
                    "Use a different version of the package",
                    "Check package's supported frameworks on nuget.org"
                ],
                HelpUrl = "https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1202",
                RecommendedTools = [
                    "analyze_nuget_compatibility - Check package framework compatibility",
                    "list_nuget_package_assemblies - View supported TFMs in package"
                ]
            },

            // .NET SDK Errors
            "NETSDK1005" => new ErrorExplanation
            {
                Category = ".NET SDK",
                Severity = "Error",
                Description = "Assets file doesn't exist. Run NuGet package restore.",
                Causes = [
                    "NuGet restore not run",
                    "project.assets.json missing or deleted",
                    "Corrupted NuGet cache"
                ],
                Solutions = [
                    "Run 'dotnet restore'",
                    "Delete 'obj' folder and restore again",
                    "Clear NuGet cache: dotnet nuget locals all --clear"
                ],
                HelpUrl = "https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/netsdk1005",
                RecommendedTools = [
                    "clear_nuget_cache - Clear HandMirrorMcp's package cache"
                ]
            },

            "NETSDK1045" => new ErrorExplanation
            {
                Category = ".NET SDK",
                Severity = "Error",
                Description = "The current .NET SDK does not support targeting the specified framework.",
                Causes = [
                    "Project targets newer .NET than installed SDK",
                    "SDK not installed",
                    "global.json specifies unavailable SDK version"
                ],
                Solutions = [
                    "Install the required .NET SDK from https://dot.net",
                    "Update global.json to use installed SDK version",
                    "Downgrade project's target framework"
                ],
                HelpUrl = "https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/netsdk1045",
                RecommendedTools = [
                    "get_dotnet_info - View installed SDKs and runtimes"
                ]
            },

            "NETSDK1004" => new ErrorExplanation
            {
                Category = ".NET SDK",
                Severity = "Error",
                Description = "Assets file not found. Run NuGet package restore.",
                Causes = [
                    "NuGet restore not run",
                    "Build before restore completed"
                ],
                Solutions = [
                    "Run 'dotnet restore' before building",
                    "Ensure restore completes successfully"
                ],
                HelpUrl = "https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/netsdk1004",
                RecommendedTools = []
            },

            // MSBuild Errors
            "MSB4019" => new ErrorExplanation
            {
                Category = "MSBuild",
                Severity = "Error",
                Description = "The imported project was not found.",
                Causes = [
                    "Missing SDK or workload",
                    "Corrupted installation",
                    "Missing props/targets file",
                    "Wrong Visual Studio/SDK version"
                ],
                Solutions = [
                    "Install required SDK or workload",
                    "Repair .NET installation",
                    "Check for required workloads: dotnet workload list",
                    "Install workload: dotnet workload install <workload>"
                ],
                HelpUrl = "https://learn.microsoft.com/en-us/visualstudio/msbuild/errors/msb4019",
                RecommendedTools = [
                    "get_dotnet_info - Check installed SDKs and runtimes"
                ]
            },

            "MSB3073" => new ErrorExplanation
            {
                Category = "MSBuild",
                Severity = "Error",
                Description = "A command exited with a non-zero code.",
                Causes = [
                    "Pre/post build event failed",
                    "Custom MSBuild task failed",
                    "Script or tool not found"
                ],
                Solutions = [
                    "Check the full error output for details",
                    "Verify pre/post build commands work manually",
                    "Check paths and permissions"
                ],
                HelpUrl = null,
                RecommendedTools = []
            },

            _ => null
        };
    }

    [GeneratedRegex(@"type or namespace name '(\w+)'", RegexOptions.IgnoreCase)]
    private static partial Regex TypeNameRegex();

    [GeneratedRegex(@"package '?([\w\.]+)'?", RegexOptions.IgnoreCase)]
    private static partial Regex PackageNameRegex();

    [GeneratedRegex(@"\d+\.\d+(\.\d+)?(-[\w\.]+)?", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"Project\(""\{[^}]+\}""\)\s*=\s*""([^""]+)"",\s*""([^""]+)""")]
    private static partial Regex SlnProjectRegex();

    #endregion

    #region Types

    private sealed class ProjectIssue(string title, string description, string? helpUrl)
    {
        public string Title { get; } = title;
        public string Description { get; } = description;
        public string? HelpUrl { get; } = helpUrl;
    }

    private sealed class PackageRef
    {
        public required string Name { get; init; }
        public string? Version { get; init; }
        public string? PrivateAssets { get; init; }
    }

    private sealed class ErrorExplanation
    {
        public required string Category { get; init; }
        public required string Severity { get; init; }
        public required string Description { get; init; }
        public required List<string> Causes { get; init; }
        public required List<string> Solutions { get; init; }
        public string? HelpUrl { get; init; }
        public required List<string> RecommendedTools { get; init; }
    }

    private sealed class FileBasedAppInfo
    {
        public ShebangInfo? Shebang { get; set; }
        public string? Sdk { get; set; }
        public List<FileBasedPackageRef> Packages { get; } = [];
        public List<string> ProjectReferences { get; } = [];
        public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<InvalidDirective> InvalidDirectives { get; } = [];
        public int DirectiveCount { get; set; }
        public int FirstCodeLineNumber { get; set; } = 1;

        public bool HasAnyDirectives =>
            Shebang != null ||
            Sdk != null ||
            Packages.Count > 0 ||
            ProjectReferences.Count > 0 ||
            Properties.Count > 0;
    }

    private sealed class ShebangInfo
    {
        public required string Line { get; init; }
        public required string Interpreter { get; init; }
        public required List<string> Arguments { get; init; }
    }

    private sealed class FileBasedPackageRef
    {
        public required string Name { get; init; }
        public string? Version { get; init; }
    }

    private sealed class InvalidDirective
    {
        public required int LineNumber { get; init; }
        public required string Line { get; init; }
    }

    [GeneratedRegex(@"(?:Database|Initial Catalog)=([^;]+)", RegexOptions.IgnoreCase, "ko-KR")]
    private static partial Regex DatabaseNameRegex();
    [GeneratedRegex(@"(?:Server|Data Source)=([^;]+)", RegexOptions.IgnoreCase, "ko-KR")]
    private static partial Regex ServerNameRegex();

    #endregion
}






