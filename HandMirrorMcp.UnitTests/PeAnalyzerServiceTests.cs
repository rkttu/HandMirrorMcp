using HandMirrorMcp.Services;

namespace HandMirrorMcp.UnitTests;

[TestClass]
public sealed class PeAnalyzerServiceTests
{
    [TestMethod]
    public void IsSupported_OnWindows_ShouldReturnTrue()
    {
        // Arrange & Act
        var isSupported = PeAnalyzerService.IsSupported;

        // Assert
        if (OperatingSystem.IsWindows())
        {
            Assert.IsTrue(isSupported, "Should be supported on Windows");
        }
        else
        {
            Assert.IsFalse(isSupported, "Should not be supported on non-Windows");
        }
    }

    [TestMethod]
    public void AnalyzePeFile_NonExistentFile_ShouldReturnNull()
    {
        // Arrange
        var filePath = "C:\\NonExistent\\File.dll";

        // Act
        var result = PeAnalyzerService.AnalyzePeFile(filePath);

        // Assert
        Assert.IsNull(result, "Should return null for non-existent file");
    }

    [TestMethod]
    public void AnalyzePeFile_NonPeFile_ShouldReturnNull()
    {
        // Arrange - Create a temp file that is not a PE file
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, "This is not a PE file");

            // Act
            var result = PeAnalyzerService.AnalyzePeFile(tempPath);

            // Assert
            Assert.IsNull(result, "Should return null for non-PE file");
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [TestMethod]
    [TestCategory("Windows")]
    public void AnalyzePeFile_SystemDll_ShouldReturnValidResult()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("This test is Windows-only");
            return;
        }

        // Arrange - Use a system DLL that should exist on Windows
        var kernel32Path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "kernel32.dll");

        if (!File.Exists(kernel32Path))
        {
            Assert.Inconclusive($"System DLL not found: {kernel32Path}");
            return;
        }

        // Act
        var result = PeAnalyzerService.AnalyzePeFile(kernel32Path);

        // Assert
        Assert.IsNotNull(result, "Should return result for valid PE file");
        Assert.AreEqual("kernel32.dll", result.FileName, StringComparer.OrdinalIgnoreCase);
        Assert.IsTrue(result.IsDll, "kernel32.dll should be a DLL");
    }

    [TestMethod]
    [TestCategory("Windows")]
    public void AnalyzePeFile_SystemDll_ShouldHaveExports()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("This test is Windows-only");
            return;
        }

        // Arrange
        var kernel32Path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "kernel32.dll");

        if (!File.Exists(kernel32Path))
        {
            Assert.Inconclusive($"System DLL not found: {kernel32Path}");
            return;
        }

        // Act
        var result = PeAnalyzerService.AnalyzePeFile(kernel32Path);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Exports, "kernel32.dll should have exports");
        Assert.IsNotEmpty(result.Exports, "kernel32.dll should have exported functions");
        
        // Check for well-known exports
        Assert.IsTrue(result.Exports.Any(e => e.Name == "GetLastError"),
            "kernel32.dll should export GetLastError");
    }

    [TestMethod]
    [TestCategory("Windows")]
    public void AnalyzePeFile_ManagedDll_ShouldReturnValidResult()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("This test is Windows-only");
            return;
        }

        // Arrange - Use the test assembly itself
        var testAssemblyPath = typeof(PeAnalyzerServiceTests).Assembly.Location;

        if (!File.Exists(testAssemblyPath))
        {
            Assert.Inconclusive($"Test assembly not found: {testAssemblyPath}");
            return;
        }

        // Act
        var result = PeAnalyzerService.AnalyzePeFile(testAssemblyPath);

        // Assert
        Assert.IsNotNull(result, "Should return result for managed assembly");
        Assert.IsNotNull(result.FileName, "FileName should not be null");
        Assert.IsTrue(result.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), 
            "Test assembly file should have .dll extension");
    }

    [TestMethod]
    public void AnalyzePeFile_EmptyPath_ShouldReturnNull()
    {
        // Act
        var result = PeAnalyzerService.AnalyzePeFile("");

        // Assert
        Assert.IsNull(result, "Should return null for empty path");
    }

    [TestMethod]
    [TestCategory("Windows")]
    public void AnalyzePeFile_ExeFile_ShouldIdentifyAsNotDll()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("This test is Windows-only");
            return;
        }

        // Arrange - Use notepad.exe which should exist on Windows
        var notepadPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "notepad.exe");

        if (!File.Exists(notepadPath))
        {
            Assert.Inconclusive($"Notepad not found: {notepadPath}");
            return;
        }

        // Act
        var result = PeAnalyzerService.AnalyzePeFile(notepadPath);

        // Assert
        Assert.IsNotNull(result, "Should return result for valid PE file");
        Assert.IsFalse(result.IsDll, "notepad.exe should not be a DLL");
    }
}
