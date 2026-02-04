using HandMirrorMcp.Services;

namespace HandMirrorMcp.UnitTests;

[TestClass]
public sealed class PackageAssemblyInfoTests
{
    [TestMethod]
    public void Constructor_ShouldInitializeEmptyDictionaries()
    {
        // Act
        var info = new PackageAssemblyInfo();

        // Assert
        Assert.IsNotNull(info.LibAssemblies);
        Assert.IsNotNull(info.RuntimeAssemblies);
        Assert.IsNotNull(info.RefAssemblies);
        Assert.IsEmpty(info.LibAssemblies);
        Assert.IsEmpty(info.RuntimeAssemblies);
        Assert.IsEmpty(info.RefAssemblies);
    }

    [TestMethod]
    public void HasAnyAssemblies_EmptyInfo_ShouldReturnFalse()
    {
        // Arrange
        var info = new PackageAssemblyInfo();

        // Act & Assert
        Assert.IsFalse(info.HasAnyAssemblies);
    }

    [TestMethod]
    public void HasAnyAssemblies_WithLibAssemblies_ShouldReturnTrue()
    {
        // Arrange
        var info = new PackageAssemblyInfo();
        info.LibAssemblies["net8.0"] = new List<string> { "MyAssembly.dll" };

        // Act & Assert
        Assert.IsTrue(info.HasAnyAssemblies);
    }

    [TestMethod]
    public void HasAnyAssemblies_WithRuntimeAssemblies_ShouldReturnTrue()
    {
        // Arrange
        var info = new PackageAssemblyInfo();
        info.RuntimeAssemblies["win-x64/net8.0"] = new List<string> { "MyAssembly.dll" };

        // Act & Assert
        Assert.IsTrue(info.HasAnyAssemblies);
    }

    [TestMethod]
    public void HasAnyAssemblies_WithRefAssemblies_ShouldReturnTrue()
    {
        // Arrange
        var info = new PackageAssemblyInfo();
        info.RefAssemblies["net8.0"] = new List<string> { "MyAssembly.dll" };

        // Act & Assert
        Assert.IsTrue(info.HasAnyAssemblies);
    }

    [TestMethod]
    public void LibAssemblies_ShouldBeCaseInsensitive()
    {
        // Arrange
        var info = new PackageAssemblyInfo();
        info.LibAssemblies["Net8.0"] = new List<string> { "Assembly1.dll" };

        // Act
        var hasNet80 = info.LibAssemblies.ContainsKey("net8.0");
        var hasNET80 = info.LibAssemblies.ContainsKey("NET8.0");

        // Assert
        Assert.IsTrue(hasNet80, "Should find key with lowercase");
        Assert.IsTrue(hasNET80, "Should find key with uppercase");
    }

    [TestMethod]
    public void RuntimeAssemblies_ShouldBeCaseInsensitive()
    {
        // Arrange
        var info = new PackageAssemblyInfo();
        info.RuntimeAssemblies["Win-X64/Net8.0"] = new List<string> { "Assembly1.dll" };

        // Act
        var hasKey = info.RuntimeAssemblies.ContainsKey("win-x64/net8.0");

        // Assert
        Assert.IsTrue(hasKey, "Should find key with different case");
    }

    [TestMethod]
    public void RefAssemblies_ShouldBeCaseInsensitive()
    {
        // Arrange
        var info = new PackageAssemblyInfo();
        info.RefAssemblies["NetStandard2.0"] = new List<string> { "Assembly1.dll" };

        // Act
        var hasKey = info.RefAssemblies.ContainsKey("netstandard2.0");

        // Assert
        Assert.IsTrue(hasKey, "Should find key with different case");
    }

    [TestMethod]
    public void LibAssemblies_MultipleFrameworks_ShouldStoreAll()
    {
        // Arrange
        var info = new PackageAssemblyInfo();
        info.LibAssemblies["net6.0"] = new List<string> { "Assembly.dll" };
        info.LibAssemblies["net7.0"] = new List<string> { "Assembly.dll" };
        info.LibAssemblies["net8.0"] = new List<string> { "Assembly.dll" };

        // Assert
        Assert.HasCount(3, info.LibAssemblies);
        Assert.IsTrue(info.HasAnyAssemblies);
    }

    [TestMethod]
    public void LibAssemblies_MultipleAssembliesPerFramework_ShouldStoreAll()
    {
        // Arrange
        var info = new PackageAssemblyInfo();
        info.LibAssemblies["net8.0"] = new List<string>
        {
            "Assembly1.dll",
            "Assembly2.dll",
            "Assembly3.dll"
        };

        // Assert
        Assert.HasCount(3, info.LibAssemblies["net8.0"]);
    }
}
