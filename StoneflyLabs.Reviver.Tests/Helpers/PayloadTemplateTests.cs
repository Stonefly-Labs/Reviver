using StoneflyLabs.Reviver.Console.Helpers;

namespace StoneflyLabs.Reviver.Tests.Helpers;

public sealed class PayloadTemplateTests
{
    [Theory]
    [InlineData("no placeholders", 3, "no placeholders")]
    [InlineData("index={index}",   0, "index=0")]
    [InlineData("index={index}",  42, "index=42")]
    public void Expand_StaticReplacements_ReturnExpected(string template, int index, string expected)
    {
        Assert.Equal(expected, PayloadTemplate.Expand(template, index));
    }

    [Fact]
    public void Expand_GuidPlaceholder_ProducesValidGuid()
    {
        var result = PayloadTemplate.Expand("{guid}", 0);
        Assert.True(Guid.TryParse(result, out _), $"Expected a valid GUID but got: {result}");
    }

    [Fact]
    public void Expand_TimestampPlaceholder_ProducesValidDateTimeOffset()
    {
        var result = PayloadTemplate.Expand("{timestamp}", 0);
        Assert.True(DateTimeOffset.TryParse(result, out _),
            $"Expected a valid DateTimeOffset but got: {result}");
    }

    [Fact]
    public void Expand_DefaultTemplate_ProducesValidJson()
    {
        var result = PayloadTemplate.Expand(PayloadTemplate.Default, 7);
        Assert.True(JsonHelper.IsValid(result, out _),
            $"Default template did not expand to valid JSON: {result}");
    }

    [Fact]
    public void Expand_MultipleIndexReferences_AllReplaced()
    {
        var result = PayloadTemplate.Expand("{index}-{index}", 5);
        Assert.Equal("5-5", result);
    }
}
