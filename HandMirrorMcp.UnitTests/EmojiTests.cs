using HandMirrorMcp.Constants;

namespace HandMirrorMcp.UnitTests;

[TestClass]
public sealed class EmojiTests
{
    #region Constant Value Tests

    [TestMethod]
    [DataRow("\u2705", nameof(Emoji.CheckMark))]
    [DataRow("\u274C", nameof(Emoji.CrossMark))]
    [DataRow("\u26A0\uFE0F", nameof(Emoji.Warning))]
    [DataRow("\U0001F4E6", nameof(Emoji.Package))]
    [DataRow("\U0001F4C1", nameof(Emoji.Folder))]
    [DataRow("\U0001F4C4", nameof(Emoji.File))]
    [DataRow("\u2699\uFE0F", nameof(Emoji.Gear))]
    [DataRow("\U0001F50D", nameof(Emoji.MagnifyingGlass))]
    public void EmojiConstants_ShouldHaveCorrectValues(string expected, string propertyName)
    {
        var actual = typeof(Emoji).GetField(propertyName)?.GetValue(null) as string;
        Assert.AreEqual(expected, actual, $"{propertyName} should have correct value");
    }

    #endregion

    #region Severity Method Tests

    [TestMethod]
    public void Severity_Critical_ShouldReturnRedCircle()
    {
        // Act
        var result = Emoji.Severity("CRITICAL");

        // Assert
        Assert.AreEqual(Emoji.RedCircle, result);
    }

    [TestMethod]
    public void Severity_CriticalLowerCase_ShouldReturnRedCircle()
    {
        // Act
        var result = Emoji.Severity("critical");

        // Assert
        Assert.AreEqual(Emoji.RedCircle, result);
    }

    [TestMethod]
    public void Severity_High_ShouldReturnOrangeCircle()
    {
        // Act
        var result = Emoji.Severity("HIGH");

        // Assert
        Assert.AreEqual(Emoji.OrangeCircle, result);
    }

    [TestMethod]
    public void Severity_Moderate_ShouldReturnYellowCircle()
    {
        // Act
        var result = Emoji.Severity("MODERATE");

        // Assert
        Assert.AreEqual(Emoji.YellowCircle, result);
    }

    [TestMethod]
    public void Severity_Medium_ShouldReturnYellowCircle()
    {
        // Act
        var result = Emoji.Severity("MEDIUM");

        // Assert
        Assert.AreEqual(Emoji.YellowCircle, result);
    }

    [TestMethod]
    public void Severity_Low_ShouldReturnGreenCircle()
    {
        // Act
        var result = Emoji.Severity("LOW");

        // Assert
        Assert.AreEqual(Emoji.GreenCircle, result);
    }

    [TestMethod]
    public void Severity_Unknown_ShouldReturnWhiteCircle()
    {
        // Act
        var result = Emoji.Severity("UNKNOWN");

        // Assert
        Assert.AreEqual(Emoji.WhiteCircle, result);
    }

    [TestMethod]
    public void Severity_EmptyString_ShouldReturnWhiteCircle()
    {
        // Act
        var result = Emoji.Severity("");

        // Assert
        Assert.AreEqual(Emoji.WhiteCircle, result);
    }

    #endregion

    #region DiagnosticSeverity Method Tests

    [TestMethod]
    public void DiagnosticSeverity_Error_ShouldReturnCrossMark()
    {
        // Act
        var result = Emoji.DiagnosticSeverity("Error");

        // Assert
        Assert.AreEqual(Emoji.CrossMark, result);
    }

    [TestMethod]
    public void DiagnosticSeverity_Warning_ShouldReturnWarning()
    {
        // Act
        var result = Emoji.DiagnosticSeverity("Warning");

        // Assert
        Assert.AreEqual(Emoji.Warning, result);
    }

    [TestMethod]
    public void DiagnosticSeverity_Info_ShouldReturnInfo()
    {
        // Act
        var result = Emoji.DiagnosticSeverity("Info");

        // Assert
        Assert.AreEqual(Emoji.Info, result);
    }

    [TestMethod]
    public void DiagnosticSeverity_Unknown_ShouldReturnBulb()
    {
        // Act
        var result = Emoji.DiagnosticSeverity("Unknown");

        // Assert
        Assert.AreEqual(Emoji.Bulb, result);
    }

    [TestMethod]
    public void DiagnosticSeverity_EmptyString_ShouldReturnBulb()
    {
        // Act
        var result = Emoji.DiagnosticSeverity("");

        // Assert
        Assert.AreEqual(Emoji.Bulb, result);
    }

    #endregion

    #region Tree Markers Tests

    [TestMethod]
    [DataRow("\u251C\u2500", nameof(Emoji.TreeBranch))]
    [DataRow("\u2514\u2500", nameof(Emoji.TreeCorner))]
    [DataRow("\u2502", nameof(Emoji.TreeVertical))]
    [DataRow("\u2022", nameof(Emoji.Bullet))]
    public void TreeMarkers_ShouldHaveCorrectValues(string expected, string propertyName)
    {
        var actual = typeof(Emoji).GetField(propertyName)?.GetValue(null) as string;
        Assert.AreEqual(expected, actual, $"{propertyName} should have correct value");
    }

    #endregion

    #region All Constants Are Not Null Or Empty Tests

    [TestMethod]
    [DataRow(nameof(Emoji.CheckMark))]
    [DataRow(nameof(Emoji.CrossMark))]
    [DataRow(nameof(Emoji.Warning))]
    [DataRow(nameof(Emoji.Info))]
    [DataRow(nameof(Emoji.Question))]
    [DataRow(nameof(Emoji.Bulb))]
    [DataRow(nameof(Emoji.Fire))]
    [DataRow(nameof(Emoji.Sparkles))]
    [DataRow(nameof(Emoji.Star))]
    [DataRow(nameof(Emoji.Celebration))]
    public void StatusIndicators_ShouldNotBeNullOrEmpty(string propertyName)
    {
        var value = typeof(Emoji).GetField(propertyName)?.GetValue(null) as string;
        Assert.IsFalse(string.IsNullOrEmpty(value), $"{propertyName} should not be null or empty");
    }

    [TestMethod]
    [DataRow(nameof(Emoji.RedCircle))]
    [DataRow(nameof(Emoji.OrangeCircle))]
    [DataRow(nameof(Emoji.YellowCircle))]
    [DataRow(nameof(Emoji.GreenCircle))]
    [DataRow(nameof(Emoji.WhiteCircle))]
    public void SeverityIndicators_ShouldNotBeNullOrEmpty(string propertyName)
    {
        var value = typeof(Emoji).GetField(propertyName)?.GetValue(null) as string;
        Assert.IsFalse(string.IsNullOrEmpty(value), $"{propertyName} should not be null or empty");
    }

    [TestMethod]
    [DataRow(nameof(Emoji.Package))]
    [DataRow(nameof(Emoji.Folder))]
    [DataRow(nameof(Emoji.FolderOpen))]
    [DataRow(nameof(Emoji.File))]
    [DataRow(nameof(Emoji.FileText))]
    [DataRow(nameof(Emoji.Clipboard))]
    [DataRow(nameof(Emoji.Books))]
    [DataRow(nameof(Emoji.Book))]
    [DataRow(nameof(Emoji.Scroll))]
    [DataRow(nameof(Emoji.Gear))]
    [DataRow(nameof(Emoji.Wrench))]
    [DataRow(nameof(Emoji.Hammer))]
    [DataRow(nameof(Emoji.HammerAndWrench))]
    [DataRow(nameof(Emoji.MagnifyingGlass))]
    [DataRow(nameof(Emoji.MagnifyingGlassLeft))]
    [DataRow(nameof(Emoji.Link))]
    [DataRow(nameof(Emoji.Pin))]
    [DataRow(nameof(Emoji.Pushpin))]
    [DataRow(nameof(Emoji.Key))]
    [DataRow(nameof(Emoji.Lock))]
    [DataRow(nameof(Emoji.Unlock))]
    public void ObjectsAndTools_ShouldNotBeNullOrEmpty(string propertyName)
    {
        var value = typeof(Emoji).GetField(propertyName)?.GetValue(null) as string;
        Assert.IsFalse(string.IsNullOrEmpty(value), $"{propertyName} should not be null or empty");
    }

    [TestMethod]
    [DataRow(nameof(Emoji.Computer))]
    [DataRow(nameof(Emoji.Desktop))]
    [DataRow(nameof(Emoji.Globe))]
    [DataRow(nameof(Emoji.Shuffle))]
    [DataRow(nameof(Emoji.Ruler))]
    public void ComputingEmojis_ShouldNotBeNullOrEmpty(string propertyName)
    {
        var value = typeof(Emoji).GetField(propertyName)?.GetValue(null) as string;
        Assert.IsFalse(string.IsNullOrEmpty(value), $"{propertyName} should not be null or empty");
    }

    #endregion
}
