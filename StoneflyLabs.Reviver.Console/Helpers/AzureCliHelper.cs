using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StoneflyLabs.Reviver.Console.Helpers;

internal static class AzureCliHelper
{
    public static async Task<List<string>> GetServiceBusNamespacesAsync()
    {
        var json = await RunAzAsync("servicebus namespace list --query \"[].serviceBusEndpoint\" -o json");
        if (json is null) return [];

        try
        {
            var endpoints = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return endpoints
                .Select(e => Uri.TryCreate(e.Trim(), UriKind.Absolute, out var uri) ? uri.Host : string.Empty)
                .Where(h => !string.IsNullOrEmpty(h))
                .Order()
                .ToList();
        }
        catch { return []; }
    }

    public static async Task<List<AzureSubscription>> GetSubscriptionsAsync()
    {
        var json = await RunAzAsync("account list -o json");
        if (json is null) return [];

        try { return JsonSerializer.Deserialize<List<AzureSubscription>>(json) ?? []; }
        catch { return []; }
    }

    public static async Task<AzureSubscription?> GetCurrentSubscriptionAsync()
    {
        var json = await RunAzAsync("account show -o json");
        if (json is null) return null;

        try { return JsonSerializer.Deserialize<AzureSubscription>(json); }
        catch { return null; }
    }

    public static async Task<bool> SetSubscriptionAsync(string subscriptionId)
    {
        var result = await RunAzAsync($"account set --subscription {subscriptionId}");
        return result is not null;
    }

    private static async Task<string?> RunAzAsync(string args)
    {
        try
        {
            var psi = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("cmd.exe", $"/c az {args}")
                : new ProcessStartInfo("az", args);

            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError  = true;
            psi.UseShellExecute        = false;
            psi.CreateNoWindow         = true;

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            return proc.ExitCode == 0 ? output.Trim() : null;
        }
        catch { return null; }
    }
}

internal sealed record AzureSubscription(
    [property: JsonPropertyName("id")]        string Id,
    [property: JsonPropertyName("name")]      string Name,
    [property: JsonPropertyName("isDefault")] bool IsDefault,
    [property: JsonPropertyName("state")]     string State
);
