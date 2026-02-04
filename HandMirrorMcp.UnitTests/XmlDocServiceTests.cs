using HandMirrorMcp.Services;
using System.Reflection;

namespace HandMirrorMcp.UnitTests;

[TestClass]
public sealed class XmlDocServiceTests
{
    private static string? _testAssemblyPath;
    private static string _tempDir = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        // Use the MSTest.TestFramework assembly which has XML documentation
        var assembly = typeof(TestClassAttribute).Assembly;
        _testAssemblyPath = assembly.Location;
        
        // Create a temp directory for tests
        _tempDir = Path.Combine(Path.GetTempPath(), "XmlDocServiceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }
    
    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void Constructor_WithNonExistentAssembly_ShouldNotThrow()
    {
        // Arrange - Use a path in a valid directory but non-existent file
        var nonExistentPath = Path.Combine(_tempDir, "NonExistent.dll");
        
        // Act
        var service = new XmlDocService(nonExistentPath);

        // Assert
        Assert.IsFalse(service.HasDocumentation, "Should not have documentation for non-existent assembly");
        Assert.IsNull(service.XmlPath, "XmlPath should be null");
    }

    [TestMethod]
    public void HasDocumentation_WithoutXmlFile_ShouldReturnFalse()
    {
        // Arrange
        var tempPath = Path.GetTempFileName();
        File.Move(tempPath, Path.ChangeExtension(tempPath, ".dll"));
        var dllPath = Path.ChangeExtension(tempPath, ".dll");

        try
        {
            // Act
            var service = new XmlDocService(dllPath);

            // Assert
            Assert.IsFalse(service.HasDocumentation, "Should not have documentation without XML file");
        }
        finally
        {
            if (File.Exists(dllPath))
                File.Delete(dllPath);
        }
    }

    [TestMethod]
    public void GetTypeDoc_WithValidType_ShouldReturnNull_WhenNoXmlDoc()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDir, "NonExistent2.dll");
        var service = new XmlDocService(nonExistentPath);

        // Act
        var result = service.GetTypeDoc("System.String");

        // Assert
        Assert.IsNull(result, "Should return null when no XML documentation is loaded");
    }

    [TestMethod]
    public void GetMethodDoc_WithValidMethod_ShouldReturnNull_WhenNoXmlDoc()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDir, "NonExistent3.dll");
        var service = new XmlDocService(nonExistentPath);

        // Act
        var result = service.GetMethodDoc("System.String", "Contains");

        // Assert
        Assert.IsNull(result, "Should return null when no XML documentation is loaded");
    }

    [TestMethod]
    public void GetPropertyDoc_WithValidProperty_ShouldReturnNull_WhenNoXmlDoc()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDir, "NonExistent4.dll");
        var service = new XmlDocService(nonExistentPath);

        // Act
        var result = service.GetPropertyDoc("System.String", "Length");

        // Assert
        Assert.IsNull(result, "Should return null when no XML documentation is loaded");
    }

    [TestMethod]
    public void GetFieldDoc_WithValidField_ShouldReturnNull_WhenNoXmlDoc()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDir, "NonExistent5.dll");
        var service = new XmlDocService(nonExistentPath);

        // Act
        var result = service.GetFieldDoc("System.Int32", "MaxValue");

        // Assert
        Assert.IsNull(result, "Should return null when no XML documentation is loaded");
    }

    [TestMethod]
    public void GetEventDoc_WithValidEvent_ShouldReturnNull_WhenNoXmlDoc()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDir, "NonExistent6.dll");
        var service = new XmlDocService(nonExistentPath);

        // Act
        var result = service.GetEventDoc("System.ComponentModel.BackgroundWorker", "DoWork");

        // Assert
        Assert.IsNull(result, "Should return null when no XML documentation is loaded");
    }

    [TestMethod]
    public void GetConstructorDoc_WithValidConstructor_ShouldReturnNull_WhenNoXmlDoc()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDir, "NonExistent7.dll");
        var service = new XmlDocService(nonExistentPath);

        // Act
        var result = service.GetConstructorDoc("System.String");

        // Assert
        Assert.IsNull(result, "Should return null when no XML documentation is loaded");
    }

    [TestMethod]
    public void GetMethodDoc_WithParameters_ShouldReturnNull_WhenNoXmlDoc()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDir, "NonExistent8.dll");
        var service = new XmlDocService(nonExistentPath);
        var paramTypes = new[] { "System.String", "System.StringComparison" };

        // Act
        var result = service.GetMethodDoc("System.String", "Contains", paramTypes);

        // Assert
        Assert.IsNull(result, "Should return null when no XML documentation is loaded");
    }

    [TestMethod]
    public void GetConstructorDoc_WithParameters_ShouldReturnNull_WhenNoXmlDoc()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDir, "NonExistent9.dll");
        var service = new XmlDocService(nonExistentPath);
        var paramTypes = new[] { "System.Char[]" };

        // Act
        var result = service.GetConstructorDoc("System.String", paramTypes);

        // Assert
        Assert.IsNull(result, "Should return null when no XML documentation is loaded");
    }
}
