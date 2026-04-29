using Cocona;
using StoneflyLabs.Reviver.Console.Commands;

namespace StoneflyLabs.Reviver.Console;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        await CoconaApp.RunAsync<CliCommands>(args);
    }
}
