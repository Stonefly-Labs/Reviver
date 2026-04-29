using StoneflyLabs.Reviver.Console.Helpers;

namespace StoneflyLabs.Reviver.Tests.Helpers;

public sealed class JsonHelperTests
{
    [Fact]
    public void TryFormat_ValidJson_ReturnsIndented()
    {
        var result = JsonHelper.TryFormat("""{"a":1,"b":2}""");
        Assert.Contains("\n", result);
        Assert.Contains("\"a\"", result);
    }

    [Fact]
    public void TryFormat_InvalidJson_ReturnsOriginal()
    {
        const string raw = "not json at all";
        Assert.Equal(raw, JsonHelper.TryFormat(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryFormat_EmptyOrWhitespace_ReturnsOriginal(string input)
    {
        Assert.Equal(input, JsonHelper.TryFormat(input));
    }

    [Fact]
    public void IsValid_ValidJson_ReturnsTrueAndEmptyError()
    {
        Assert.True(JsonHelper.IsValid("""{"ok":true}""", out var err));
        Assert.Equal(string.Empty, err);
    }

    [Fact]
    public void IsValid_InvalidJson_ReturnsFalseWithError()
    {
        Assert.False(JsonHelper.IsValid("{bad json", out var err));
        Assert.NotEmpty(err);
    }
}
