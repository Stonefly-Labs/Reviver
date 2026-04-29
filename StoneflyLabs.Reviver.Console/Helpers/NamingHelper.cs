namespace StoneflyLabs.Reviver.Console.Helpers;

public static class NamingHelper
{
    public static string NormalizeNamespace(string input)
    {
        var trimmed = input.Trim();
        return trimmed.Contains('.') ? trimmed : $"{trimmed}.servicebus.windows.net";
    }
}
