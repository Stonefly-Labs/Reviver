using Cocona;
using StoneFlyLabs.Reviver.Commands;

namespace StoneFlyLabs.Reviver;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        await CoconaApp.RunAsync<CliCommands>(args);
    }
}
