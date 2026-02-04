# 🧪 HandMirror TestFlight Playbook

This document provides test scenarios to validate the value proposition of the HandMirror MCP server.

---

## 📋 Table of Contents

1. [Test Objectives](#-test-objectives)
2. [Prerequisites](#-prerequisites)
3. [Scenario 1: Exploring Unfamiliar NuGet Packages](#-scenario-1-exploring-unfamiliar-nuget-packages)
4. [Scenario 2: Resolving Build Errors](#-scenario-2-resolving-build-errors)
5. [Scenario 3: Comparing Package Versions](#-scenario-3-comparing-package-versions)
6. [Scenario 4: Native Interop Analysis](#-scenario-4-native-interop-analysis)
7. [Expected Results and Evaluation Criteria](#-expected-results-and-evaluation-criteria)

---

## 🎯 Test Objectives

HandMirror solves the following problems:

| Problem | Traditional Approach | HandMirror Approach |
| --------- | --------------------- | --------------------- |
| Guessing API names | Web search, GitHub source search | Direct assembly inspection |
| Case sensitivity errors | Trial and error | Exact namespace verification |
| Version-specific API differences | Documentation search (often outdated) | Version-specific assembly comparison |
| Extension method locations | Guessing | Namespace search |

---

## 🔧 Prerequisites

### 1. Build and Run the MCP Server

```powershell
cd D:\Projects\HandMirrorMcp
dotnet build -c Release
```

### 2. Verify VS Code Configuration

`.vscode/mcp.json`:

```json
{
  "servers": {
    "handmirror": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "HandMirrorMcp/HandMirrorMcp.csproj",
        "-v",
        "q"
      ]
    }
  }
}
```

### 3. Create TestFlight Project

```powershell
mkdir TestFlight
cd TestFlight
dotnet new console
```

---

## 🚀 Scenario 1: Exploring Unfamiliar NuGet Packages

### Objective

Find and use the correct API for "less-known" NuGet packages without documentation hunting.

### Test Packages for Scenario 1

- **DbUp-SQLite** - Database migration tool
- **LiteDB** - Embedded NoSQL database

### Steps

#### Step 1: Add Packages

```powershell
dotnet add package DbUp-SQLite
dotnet add package LiteDB
```

#### Step 2: Write Intentionally Incorrect Code

```csharp
// ❌ Common mistake - guessing case sensitivity
using DbUp.SQLite.Helpers;  // SQLite? Sqlite? sqlite?

var upgrader = DeployChanges.To
    .SQLiteDatabase(connectionString);  // Also guessing method name
```

#### Step 3: Observe Build Error

```text
error CS0234: The type or namespace name 'SQLite' does not exist in the namespace 'DbUp'
```

#### Step 4: Resolve with HandMirror

**Prompt 1 - Explore Namespaces:**

```text
Use inspect_nuget_package to check the namespaces in the DbUp-SQLite package
```

**Expected Result:**

```text
Namespaces:
  -  (1 types)              ← Extension methods in root namespace!
  - DbUp.Sqlite (5 types)   ← Sqlite, not SQLite
  - DbUp.Sqlite.Helpers (3 types)
```

**Prompt 2 - Find Extension Methods:**

```text
Use search_nuget_types to search for Extensions pattern in DbUp-SQLite package
```

**Expected Result:**

```text
[static class] SqliteExtensions  ← In root namespace
```

**Prompt 3 - Verify Exact API:**

```text
Use inspect_nuget_package_type to check the SqliteExtensions type in DbUp-SQLite
```

**Expected Result:**

```text
[method] SqliteDatabase(SupportedDatabases, String)  ← Not SQLiteDatabase!
[method] JournalToSqliteTable(UpgradeEngineBuilder, String)
```

#### Step 5: Write Correct Code

```csharp
// ✅ Correct API verified with HandMirror
using DbUp.Sqlite.Helpers;  // lowercase 'l'

var upgrader = DeployChanges.To
    .SqliteDatabase(connectionString);  // lowercase 'l'
```

### Verification Checklist

- [ ] Found exact namespace without web search
- [ ] Verified case sensitivity (SQLite vs Sqlite)
- [ ] Located extension methods (root namespace)
- [ ] Build succeeded

---

## 🔧 Scenario 2: Resolving Build Errors

### Objective: Build Error Diagnosis

Diagnose and resolve .NET build errors using HandMirror tools.

### Test Error Codes

| Error | Description | Test Method |
| ------- | ------------- | ------------- |
| CS0246 | Type/namespace not found | Incorrect using statement |
| CS1061 | Member not defined | Calling non-existent method |
| CS0234 | Type not in namespace | Wrong namespace path |
| NU1605 | Package downgrade detected | Version conflict |

### Steps: Error Resolution

#### Step 1: Request Error Code Explanation

```text
Use explain_build_error to explain the CS0234 error
```

#### Step 2: Search for Related Package

```text
Use find_package_by_type to find which package provides the 'SqliteConnection' type
```

#### Step 3: Analyze Package

```text
Use inspect_nuget_package to analyze the Microsoft.Data.Sqlite package
```

### Verification Checklist: Error Resolution

- [ ] Error code explanation accuracy
- [ ] Solution suggestions provided
- [ ] Related package recommendations

---

## 📊 Scenario 3: Comparing Package Versions

### Objective: Version Comparison

Identify API changes when upgrading packages.

### Test Case

Compare Newtonsoft.Json 12.x → 13.x

### Steps: Version Comparison

#### Step 1: Check Version List

```text
Use get_nuget_package_versions to show version list for Newtonsoft.Json
```

#### Step 2: Compare Two Versions

```text
Use the compare_nuget_versions prompt to compare Newtonsoft.Json 12.0.3 and 13.0.3
```

#### Step 3: Check Specific Type Changes

```text
Use inspect_nuget_package_type to compare JsonConvert type 
between Newtonsoft.Json 12.0.3 and 13.0.3
```

### Verification Checklist: Version Comparison

- [ ] Identified newly added methods
- [ ] Identified removed/changed methods
- [ ] Detected breaking changes

---

## 🔌 Scenario 4: Native Interop Analysis

### Objective: Native Interop

Analyze native dependencies in NuGet packages.

### Test Packages for Scenario 4

- **SkiaSharp** - Cross-platform 2D graphics library
- **SQLitePCLRaw.core** - SQLite native bindings

### Steps: Native Interop

#### Step 1: List Native Libraries

```text
Use inspect_nuget_native_libs to check native libraries in 
SkiaSharp.NativeAssets.Win32 package
```

#### Step 2: Check Native Function Exports

```text
Use inspect_nuget_native_exports to check libSkiaSharp.dll functions 
in SkiaSharp.NativeAssets.Win32
```

#### Step 3: Verify P/Invoke Signatures

```text
Use inspect_native_dependencies to check P/Invoke declarations in SkiaSharp.dll
```

### Verification Checklist: Native Interop

- [ ] Native DLL list confirmed
- [ ] Export function list confirmed
- [ ] P/Invoke signatures confirmed

---

## 📈 Expected Results and Evaluation Criteria

### Success Criteria

| Metric | Traditional Method | HandMirror | Improvement |
| -------- | ------------------- | ------------ | ------------- |
| API Discovery Time | 5-10 min | 30 sec - 1 min | 80%+ |
| Build Trial & Error | 3-5 attempts | 0-1 attempts | 80%+ |
| Accuracy | Guess-based | Assembly-based | 100% |

### Evaluation Checklist

#### Functional Verification

- [ ] `inspect_nuget_package` - Package analysis successful
- [ ] `search_nuget_types` - Type search successful
- [ ] `inspect_nuget_package_type` - Type details successful
- [ ] `explain_build_error` - Error explanation successful

#### Usability Verification

- [ ] Prompt Understanding - Does AI appropriately select tools?
- [ ] Result Readability - Is output easy to understand?
- [ ] Workflow Integration - Natural development flow?

#### Performance Verification

- [ ] Response Time - Under 5 seconds
- [ ] Caching Behavior - Faster response on second call
- [ ] Large Packages - Can handle large packages

---

## 📝 Test Log Template

```markdown
## Test Execution Record

**Date:** YYYY-MM-DD
**Tester:** 
**Version:** HandMirror vX.X.X

### Scenario 1: Unfamiliar NuGet Package
- Package: DbUp-SQLite 6.0.1
- Result: ✅ Pass / ❌ Fail
- Duration: 
- Notes:

### Scenario 2: Build Error Resolution
- Error Code: CS0234
- Result: ✅ Pass / ❌ Fail
- Notes:

### Scenario 3: Package Version Comparison
- Package: Newtonsoft.Json
- Result: ✅ Pass / ❌ Fail
- Notes:

### Scenario 4: Native Interop
- Package: SkiaSharp
- Result: ✅ Pass / ❌ Fail
- Notes:

### Overall Assessment
- 
```

---

## 🔗 Related Documents

- [README.md](../README.md) - Project overview
- [TestFlight/](../TestFlight/) - Test project code

---

*HandMirror - Look before you code* 🪞
