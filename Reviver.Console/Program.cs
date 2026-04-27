using Cocona;
using StoneFlyLabs.Reviver.Console.Commands;

namespace StoneFlyLabs.Reviver.Console;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        await CoconaApp.RunAsync<CliCommands>(args);
    }
}
