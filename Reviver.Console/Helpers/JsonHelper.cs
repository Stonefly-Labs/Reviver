using System.Text.Json;

namespace StoneFlyLabs.Reviver.Console.Helpers;

public static class JsonHelper
{
    private static readonly JsonSerializerOptions PrettyOptions = new() { WriteIndented = true };

    public static string TryFormat(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        try
        {
            var doc = JsonDocument.Parse(input);
            return JsonSerializer.Serialize(doc, PrettyOptions);
        }
        catch
        {
            return input;
        }
    }

    public static bool IsValid(string input, out string error)
    {
        try
        {
            JsonDocument.Parse(input);
            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
