namespace StoneflyLabs.Reviver.Console.Helpers;

public static class PayloadTemplate
{
    public const string Default =
        """{"index": {index}, "source": "reviver-seeder", "timestamp": "{timestamp}", "id": "{guid}"}""";

    public static string Expand(string template, int index) =>
        template
            .Replace("{index}", index.ToString())
            .Replace("{timestamp}", DateTimeOffset.UtcNow.ToString("O"))
            .Replace("{guid}", Guid.NewGuid().ToString());
}
