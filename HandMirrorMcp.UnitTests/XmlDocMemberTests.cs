using HandMirrorMcp.Services;

namespace HandMirrorMcp.UnitTests;

[TestClass]
public sealed class XmlDocMemberTests
{
    [TestMethod]
    public void Constructor_ShouldInitializeEmptyCollections()
    {
        // Act
        var member = new XmlDocMember { Name = "T:TestType" };

        // Assert
        Assert.IsNotNull(member.Parameters);
        Assert.IsNotNull(member.TypeParameters);
        Assert.IsNotNull(member.Exceptions);
        Assert.IsEmpty(member.Parameters);
        Assert.IsEmpty(member.TypeParameters);
        Assert.IsEmpty(member.Exceptions);
    }

    [TestMethod]
    public void Name_ShouldBeRequired()
    {
        // Arrange & Act
        var member = new XmlDocMember { Name = "M:MyNamespace.MyClass.MyMethod" };

        // Assert
        Assert.AreEqual("M:MyNamespace.MyClass.MyMethod", member.Name);
    }

    [TestMethod]
    public void HasContent_NoContent_ShouldReturnFalse()
    {
        // Arrange
        var member = new XmlDocMember { Name = "T:TestType" };

        // Act & Assert
        Assert.IsFalse(member.HasContent);
    }

    [TestMethod]
    public void HasContent_WithSummary_ShouldReturnTrue()
    {
        // Arrange
        var member = new XmlDocMember
        {
            Name = "T:TestType",
            Summary = "This is a test summary."
        };

        // Act & Assert
        Assert.IsTrue(member.HasContent);
    }

    [TestMethod]
    public void HasContent_WithRemarks_ShouldReturnTrue()
    {
        // Arrange
        var member = new XmlDocMember
        {
            Name = "T:TestType",
            Remarks = "These are remarks."
        };

        // Act & Assert
        Assert.IsTrue(member.HasContent);
    }

    [TestMethod]
    public void HasContent_WithReturns_ShouldReturnTrue()
    {
        // Arrange
        var member = new XmlDocMember
        {
            Name = "M:TestType.Method",
            Returns = "Returns a value."
        };

        // Act & Assert
        Assert.IsTrue(member.HasContent);
    }

    [TestMethod]
    public void HasContent_WithParameters_ShouldReturnTrue()
    {
        // Arrange
        var member = new XmlDocMember { Name = "M:TestType.Method" };
        member.Parameters["param1"] = "The first parameter.";

        // Act & Assert
        Assert.IsTrue(member.HasContent);
    }

    [TestMethod]
    public void HasContent_WithEmptySummary_ShouldReturnFalse()
    {
        // Arrange
        var member = new XmlDocMember
        {
            Name = "T:TestType",
            Summary = ""
        };

        // Act & Assert
        Assert.IsFalse(member.HasContent);
    }

    [TestMethod]
    public void HasContent_WithNullSummary_ShouldReturnFalse()
    {
        // Arrange
        var member = new XmlDocMember
        {
            Name = "T:TestType",
            Summary = null
        };

        // Act & Assert
        Assert.IsFalse(member.HasContent);
    }

    [TestMethod]
    public void Parameters_ShouldAcceptMultipleParameters()
    {
        // Arrange
        var member = new XmlDocMember { Name = "M:TestType.Method" };

        // Act
        member.Parameters["param1"] = "First parameter.";
        member.Parameters["param2"] = "Second parameter.";
        member.Parameters["param3"] = "Third parameter.";

        // Assert
        Assert.HasCount(3, member.Parameters);
        Assert.AreEqual("First parameter.", member.Parameters["param1"]);
        Assert.AreEqual("Second parameter.", member.Parameters["param2"]);
        Assert.AreEqual("Third parameter.", member.Parameters["param3"]);
    }

    [TestMethod]
    public void TypeParameters_ShouldAcceptGenericTypeParams()
    {
        // Arrange
        var member = new XmlDocMember { Name = "T:MyClass`2" };

        // Act
        member.TypeParameters["T"] = "The element type.";
        member.TypeParameters["TResult"] = "The result type.";

        // Assert
        Assert.HasCount(2, member.TypeParameters);
        Assert.AreEqual("The element type.", member.TypeParameters["T"]);
        Assert.AreEqual("The result type.", member.TypeParameters["TResult"]);
    }

    [TestMethod]
    public void Exceptions_ShouldAcceptMultipleExceptions()
    {
        // Arrange
        var member = new XmlDocMember { Name = "M:TestType.Method" };

        // Act
        member.Exceptions["T:System.ArgumentNullException"] = "When the argument is null.";
        member.Exceptions["T:System.InvalidOperationException"] = "When the operation is invalid.";

        // Assert
        Assert.HasCount(2, member.Exceptions);
    }

    [TestMethod]
    public void AllProperties_ShouldBeSettable()
    {
        // Arrange & Act
        var member = new XmlDocMember
        {
            Name = "M:Namespace.Class.Method(System.String)",
            Summary = "Method summary.",
            Remarks = "Method remarks.",
            Returns = "The return value.",
            Example = "var result = Method(\"test\");",
            Value = "Property value description."
        };

        // Assert
        Assert.AreEqual("M:Namespace.Class.Method(System.String)", member.Name);
        Assert.AreEqual("Method summary.", member.Summary);
        Assert.AreEqual("Method remarks.", member.Remarks);
        Assert.AreEqual("The return value.", member.Returns);
        Assert.AreEqual("var result = Method(\"test\");", member.Example);
        Assert.AreEqual("Property value description.", member.Value);
    }

    [TestMethod]
    public void Example_WhenHasExampleButNoOtherContent_ShouldReturnFalseForHasContent()
    {
        // Arrange
        var member = new XmlDocMember
        {
            Name = "T:TestType",
            Example = "var x = new TestType();"
        };

        // Act & Assert
        // HasContent checks Summary, Remarks, Returns, or Parameters, not Example
        Assert.IsFalse(member.HasContent);
    }

    [TestMethod]
    public void Value_WhenHasValueButNoOtherContent_ShouldReturnFalseForHasContent()
    {
        // Arrange
        var member = new XmlDocMember
        {
            Name = "P:TestType.Property",
            Value = "The property value."
        };

        // Act & Assert
        // HasContent doesn't check Value property
        Assert.IsFalse(member.HasContent);
    }
}
