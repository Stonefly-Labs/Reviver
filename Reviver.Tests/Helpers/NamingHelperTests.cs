using StoneFlyLabs.Reviver.Helpers;

namespace StoneFlyLabs.Reviver.Tests.Helpers;

public sealed class NamingHelperTests
{
    [Theory]
    [InlineData("myns",                               "myns.servicebus.windows.net")]
    [InlineData("myns.servicebus.windows.net",        "myns.servicebus.windows.net")]
    [InlineData("  myns  ",                           "myns.servicebus.windows.net")]
    [InlineData("myns.custom.domain",                 "myns.custom.domain")]
    [InlineData("  myns.servicebus.windows.net  ",    "myns.servicebus.windows.net")]
    public void NormalizeNamespace_ReturnsExpectedFqdn(string input, string expected)
    {
        Assert.Equal(expected, NamingHelper.NormalizeNamespace(input));
    }
}
