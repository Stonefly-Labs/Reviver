using Cocona;
using System.ComponentModel;
using StoneFlyLabs.Reviver.Helpers;
using StoneFlyLabs.Reviver.Models;
using StoneFlyLabs.Reviver.Services;
using StoneFlyLabs.Reviver.UI;
using Spectre.Console;

namespace StoneFlyLabs.Reviver.Commands;

public sealed class CliCommands
{
    // ── reviver [-n <namespace>] ──────────────────────────────────────────────

    [PrimaryCommand]
    [Description("Launch the interactive TUI (default when no command is given)")]
    public async Task RunAsync(
        [Option('n', Description = "Namespace name or FQDN (overrides AZURE_SERVICEBUS_NAMESPACE)")]
        string? @namespace = null)
    {
        var fqdn = @namespace is not null
            ? NamingHelper.NormalizeNamespace(@namespace)
            : null; // App will prompt if null

        await new App(ns => new ServiceBusService(ns)).RunAsync(fqdn);
    }

    // ── reviver seed ──────────────────────────────────────────────────────────

    [Command("seed")]
    [Description("Seed messages directly into a DLQ without launching the TUI")]
    public async Task SeedAsync(
        [Argument(Description = "Entity: queue name or 'topic/subscription'")]
        string entity,
        [Option('n', Description = "Namespace name or FQDN (or set AZURE_SERVICEBUS_NAMESPACE)")]
        string? @namespace = null,
        [Option('c', Description = "Number of messages to seed")]
        int count = 10,
        [Option('p', Description = "Payload template — supports {index}, {timestamp}, {guid}")]
        string? payload = null,
        [Option('r', Description = "Dead-letter reason")]
        string reason = "Reviver.Seeder")
    {
        var fqdn = ResolveNamespace(@namespace);
        var entityInfo = ParseEntityArg(entity);
        var template = payload ?? PayloadTemplate.Default;

        AnsiConsole.MarkupLine(
            $"[grey]Namespace:[/] [blue]{fqdn}[/]  " +
            $"[grey]Entity:[/] [yellow]{Markup.Escape(entityInfo.DisplayName)}[/]  " +
            $"[grey]Count:[/] [yellow]{count}[/]  " +
            $"[grey]Reason:[/] [yellow]{Markup.Escape(reason)}[/]\n");

        AnsiConsole.MarkupLine(
            $"[grey]Payload preview (index=0):[/] {Markup.Escape(PayloadTemplate.Expand(template, 0))}\n");

        await using var svc = new ServiceBusService(fqdn);

        Exception? err = null;

        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var sendTask = ctx.AddTask("[yellow]Sending[/]",    maxValue: count);
                var dlqTask  = ctx.AddTask("[red]Dead-lettering[/]", maxValue: count);

                try
                {
                    await svc.SeedDlqAsync(
                        entityInfo, count, template, reason,
                        new Progress<int>(n => sendTask.Value = n),
                        new Progress<int>(n => dlqTask.Value  = n));
                }
                catch (Exception ex)
                {
                    err = ex;
                }
            });

        AnsiConsole.WriteLine();

        if (err is not null)
        {
            AnsiConsole.MarkupLine($"[red]Seed failed:[/] {Markup.Escape(err.Message)}");
            throw new CommandExitedException(1);
        }

        AnsiConsole.MarkupLine($"[green]✓ {count} message(s) seeded to DLQ.[/]");
    }

    // ── reviver version ───────────────────────────────────────────────────────

    [Command("version")]
    [Description("Print version and build information")]
    public void VersionCommand()
    {
        var ver = typeof(CliCommands).Assembly.GetName().Version?.ToString(3) ?? "unknown";

        AnsiConsole.Write(new FigletText("Reviver").Color(Color.Blue));
        AnsiConsole.MarkupLine($"[bold]Version:[/] [blue]{ver}[/]");
        AnsiConsole.MarkupLine($"[bold]Runtime:[/] {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        AnsiConsole.MarkupLine($"[bold]Purpose:[/] StoneFlyLabs · Azure Service Bus DLQ Reconciliation");
        AnsiConsole.MarkupLine($"[bold]Auth:[/]    Azure CLI credential ([grey]az login[/])");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Resolves --namespace flag → env var → error.</summary>
    private static string ResolveNamespace(string? flag)
    {
        var raw = flag ?? Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_NAMESPACE");

        if (string.IsNullOrWhiteSpace(raw))
        {
            AnsiConsole.MarkupLine("[red]Namespace required:[/] use [bold]-n <namespace>[/] or set [bold]AZURE_SERVICEBUS_NAMESPACE[/].");
            throw new CommandExitedException(1);
        }

        return NamingHelper.NormalizeNamespace(raw);
    }

    /// <summary>Parses "queueName" or "topic/subscription" into an EntityInfo.</summary>
    private static EntityInfo ParseEntityArg(string s)
    {
        var slash = s.IndexOf('/');

        if (slash < 0)
            return new EntityInfo($"[Q] {s}", s, null, 0);

        var topic = s[..slash];
        var sub   = s[(slash + 1)..];
        return new EntityInfo($"[T] {topic} → {sub}", topic, sub, 0);
    }
}
