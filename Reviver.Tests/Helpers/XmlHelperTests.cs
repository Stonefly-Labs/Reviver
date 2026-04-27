using StoneFlyLabs.Reviver.Console.Helpers;

namespace StoneFlyLabs.Reviver.Tests.Helpers;

public sealed class XmlHelperTests
{
    [Fact]
    public void TryFormat_ValidXml_ReturnsIndented()
    {
        var result = XmlHelper.TryFormat("<root><child>value</child></root>");
        Assert.Contains("\n", result);
        Assert.Contains("<child>", result);
    }

    [Fact]
    public void TryFormat_InvalidXml_ReturnsOriginal()
    {
        const string raw = "not xml at all";
        Assert.Equal(raw, XmlHelper.TryFormat(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryFormat_EmptyOrWhitespace_ReturnsOriginal(string input)
    {
        Assert.Equal(input, XmlHelper.TryFormat(input));
    }

    [Fact]
    public void IsValid_ValidXml_ReturnsTrueAndEmptyError()
    {
        Assert.True(XmlHelper.IsValid("<root><item id=\"1\"/></root>", out var err));
        Assert.Equal(string.Empty, err);
    }

    [Fact]
    public void IsValid_InvalidXml_ReturnsFalseWithError()
    {
        Assert.False(XmlHelper.IsValid("<root><unclosed>", out var err));
        Assert.NotEmpty(err);
    }

    [Fact]
    public void IsValid_Json_ReturnsFalse()
    {
        Assert.False(XmlHelper.IsValid("""{"a":1}""", out _));
    }
}
