<p align="center">
  <img src="HandMirrorMcp.png" alt="HandMirrorMcp Logo" width="128" height="128" />
</p>

<p align="center">
  <a href="https://github.com/rkttu/HandMirrorMcp"><img src="https://img.shields.io/github/stars/rkttu/HandMirrorMcp?style=flat-square" alt="GitHub Stars" /></a>
  <a href="https://github.com/rkttu/HandMirrorMcp/fork"><img src="https://img.shields.io/github/forks/rkttu/HandMirrorMcp?style=flat-square" alt="GitHub Forks" /></a>
  <a href="https://github.com/rkttu/HandMirrorMcp/issues"><img src="https://img.shields.io/github/issues/rkttu/HandMirrorMcp?style=flat-square" alt="GitHub Issues" /></a>
  <a href="https://www.nuget.org/packages/HandMirrorMcp"><img src="https://img.shields.io/nuget/v/HandMirrorMcp?style=flat-square" alt="NuGet Version" /></a>
  <a href="https://www.nuget.org/packages/HandMirrorMcp"><img src="https://img.shields.io/nuget/dt/HandMirrorMcp?style=flat-square" alt="NuGet Downloads" /></a>
  <a href="https://github.com/rkttu/HandMirrorMcp/actions/workflows/publish-nuget.yml"><img src="https://img.shields.io/github/actions/workflow/status/rkttu/HandMirrorMcp/publish-nuget.yml?style=flat-square&label=CI%2FCD" alt="CI/CD Status" /></a>
  <a href="https://github.com/rkttu/HandMirrorMcp/blob/master/LICENSE"><img src="https://img.shields.io/github/license/rkttu/HandMirrorMcp?style=flat-square" alt="License" /></a>
</p>

# HandMirror MCP Server

A Model Context Protocol (MCP) server for .NET assembly and NuGet package inspection. HandMirror helps AI coding agents understand .NET APIs accurately and resolve build errors by providing direct access to assembly metadata and NuGet package information.

## 🎯 Purpose

AI coding assistants often hallucinate or guess API details, leading to build errors and wasted development iterations. HandMirror solves this by:

- Providing **accurate, version-specific API information** directly from assemblies
- Enabling **verification of method signatures, types, and namespaces** before writing code
- Helping **diagnose and fix .NET build errors** quickly
- Supporting analysis of **native interop** (P/Invoke, COM) dependencies

## ✨ Features

### Assembly Inspection

- **`inspect_assembly`** - Full analysis of all public types, members, and attributes with XML documentation
- **`list_namespaces`** - List all namespaces in an assembly
- **`get_type_info`** - Get detailed information about a specific type

### NuGet Package Exploration

- **`search_nuget_packages`** - Search for packages by keyword
- **`get_nuget_package_info`** - Get package metadata and dependencies
- **`get_nuget_package_versions`** - List all available versions
- **`inspect_nuget_package`** - Analyze assemblies in a package
- **`inspect_nuget_package_type`** - Get detailed type info from a package
- **`list_nuget_sources`** - List configured NuGet package sources
- **`clear_nuget_cache`** - Clear the local NuGet package cache
- **`get_nuget_vulnerabilities`** - Check for known security vulnerabilities
- **`inspect_nupkg_contents`** - Inspect contents of a .nupkg file
- **`extract_nupkg_file`** - Extract specific files from a .nupkg

### Native Interop Analysis

- **`inspect_native_dependencies`** - Find P/Invoke (DllImport/LibraryImport) and COM types in an assembly

### Project Analysis

- **`analyze_csproj`** - Analyze .NET project files and identify issues
- **`analyze_solution`** - Analyze solution files
- **`explain_build_error`** - Get explanations for common build errors
- **`analyze_file_based_app`** - Analyze file-based apps
- **`analyze_config_file`** - Analyze configuration files
- **`analyze_packages_config`** - Analyze packages.config files

### System Information

- **`get_system_info`** - Get system information (OS, .NET runtime, hardware)
- **`get_dotnet_info`** - Get detailed .NET installation information

## 📋 Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- MCP-compatible client (e.g., Claude Desktop, VS Code with Copilot)

## 🚀 Installation

### Build from Source

```bash
git clone https://github.com/rkttu/HandMirrorMcp.git
cd HandMirrorMcp
dotnet build
```

### Run the Server

```bash
dotnet run --project HandMirrorMcp
```

## ⚙️ Configuration

### Claude Desktop

Add the following to your Claude Desktop configuration file:

**Windows:** `%APPDATA%\Claude\claude_desktop_config.json`
**macOS:** `~/Library/Application Support/Claude/claude_desktop_config.json`

```json
{
  "mcpServers": {
    "handmirror": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\HandMirrorMcp"]
    }
  }
}
```

Or if you've built the project:

```json
{
  "mcpServers": {
    "handmirror": {
      "command": "C:\\path\\to\\HandMirrorMcp\\bin\\Debug\\net8.0\\HandMirrorMcp.exe"
    }
  }
}
```

### VS Code with GitHub Copilot

Add to your VS Code settings or workspace settings:

```json
{
  "servers": {
    "handmirror": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/HandMirrorMcp"]
    }
  }
}
```

## 📖 Usage Examples

### Verify API Before Writing Code

When you need to use an unfamiliar .NET API:

```text
Use inspect_nuget_package_type to show me the HttpClient class from System.Net.Http
```

### Diagnose Build Errors

When you encounter errors like `CS0246`, `CS1061`, or `CS7036`:

```text
I'm getting CS1061 error. Use inspect_nuget_package to check the Newtonsoft.Json package 
and show me the available methods on JObject
```

### Explore NuGet Packages

```text
Search for packages related to "json serialization" and show me the top results
```

### Check Package Vulnerabilities

```text
Check if there are any known vulnerabilities in System.Text.Json version 6.0.0
```

### Analyze Project Issues

```text
Analyze my .csproj file at C:\MyProject\MyProject.csproj and identify any issues
```

## 🏗️ Architecture

```text
HandMirrorMcp/
├── Constants/
│   └── Emoji.cs              # Unicode emoji constants for output formatting
├── Prompts/
│   ├── AssemblyInspectorPrompts.cs
│   └── NuGetInspectorPrompts.cs
├── Services/
│   ├── NuGetService.cs       # NuGet package operations
│   ├── PeAnalyzerService.cs  # PE file analysis
│   ├── RepositoryService.cs  # Repository operations
│   └── XmlDocService.cs      # XML documentation parsing
├── Tools/
│   ├── AssemblyInspectorTool.cs
│   ├── InteropInspectorTool.cs
│   ├── NuGetInspectorTool.cs
│   ├── ProjectAnalyzerTool.cs
│   └── SystemInfoTool.cs
└── Program.cs
```

## 🧪 Testing

Run the test suite:

```bash
dotnet test
```

The tests use MSTest and connect to the actual MCP server for integration testing.

## 📦 Dependencies

- **[ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol)** - MCP server implementation
- **[Microsoft.Extensions.Hosting](https://www.nuget.org/packages/Microsoft.Extensions.Hosting)** - .NET hosting abstractions
- **[Mono.Cecil](https://www.nuget.org/packages/Mono.Cecil)** - .NET assembly inspection
- **[NuGet.Protocol](https://www.nuget.org/packages/NuGet.Protocol)** - NuGet package operations

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the Apache License 2.0 - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- [Model Context Protocol](https://modelcontextprotocol.io/) for the MCP specification
- [Mono.Cecil](https://github.com/jbevain/cecil) for .NET assembly inspection capabilities
- The .NET community for continuous inspiration

---

**HandMirror** - *Look before you code* 🪞
