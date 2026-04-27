using StoneFlyLabs.Reviver.Console.Helpers;
using StoneFlyLabs.Reviver.Console.Models;
using StoneFlyLabs.Reviver.Console.Services;
using Spectre.Console;

namespace StoneFlyLabs.Reviver.Console.Commands;

public sealed class SeederFlow(IServiceBusRepository repo)
{
    public async Task RunAsync()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(
            new Rule("[gold1 bold] ⚡ Seed DLQ [/]")
                .RuleStyle("gold1 dim"));
        AnsiConsole.WriteLine();

        List<EntityInfo>? entities = null;
        Exception? loadErr = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.BouncingBar)
            .SpinnerStyle(Style.Parse("gold1"))
            .StartAsync("[grey]Loading entities…[/]", async _ =>
            {
                try   { entities = await repo.GetAllEntitiesAsync(); }
                catch (Exception ex) { loadErr = ex; }
            });

        if (loadErr is not null)
        {
            AnsiConsole.MarkupLine($"[red bold]✗ Error:[/] {Markup.Escape(loadErr.Message)}");
            Pause();
            return;
        }

        if (entities is null || entities.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]⚠ No queues or topic subscriptions found.[/]");
            Pause();
            return;
        }

        var entity = AnsiConsole.Prompt(
            new SelectionPrompt<EntityInfo>()
                .Title("[grey]Target entity:[/]")
                .PageSize(20)
                .HighlightStyle(Style.Parse("gold1 bold"))
                .UseConverter(e =>
                {
                    var icon = e.IsQueue ? "[deepskyblue1]≡[/]" : "[gold1]⬡[/]";
                    return $"{icon} {Markup.Escape(e.DisplayName)}";
                })
                .AddChoices(entities));

        if (!entity.IsQueue)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                "[grey]ℹ  Messages sent to a topic reach[/] [bold]all[/] [grey]subscriptions." +
                " Only the selected subscription will be dead-lettered.[/]");
        }

        AnsiConsole.WriteLine();

        var count = AnsiConsole.Prompt(
            new TextPrompt<int>("[grey]Number of messages:[/]")
                .DefaultValue(10)
                .Validate(n => n is >= 1 and <= 1000
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Must be 1–1000")));

        var payloadTemplate = AnsiConsole.Prompt(
            new TextPrompt<string>("[grey]Payload template:[/]")
                .DefaultValue(PayloadTemplate.Default)
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(payloadTemplate))
            payloadTemplate = PayloadTemplate.Default;

        AnsiConsole.MarkupLine($"\n[grey]Placeholders: {{index}}, {{timestamp}}, {{guid}}[/]");
        AnsiConsole.MarkupLine($"[grey]Preview →[/] {Markup.Escape(PayloadTemplate.Expand(payloadTemplate, 0))}\n");

        var dlqReason = AnsiConsole.Prompt(
            new TextPrompt<string>("[grey]Dead-letter reason:[/]")
                .DefaultValue("Reviver.Seeder"));

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[grey] Summary [/]").RuleStyle("grey dim"));
        AnsiConsole.MarkupLine(
            $"\n  Seed [gold1 bold]{count}[/] message(s) → " +
            $"[gold1 bold]{Markup.Escape(entity.DisplayName)}[/] DLQ" +
            $"  [grey]reason:[/] [gold1]{Markup.Escape(dlqReason)}[/]\n");

        if (!AnsiConsole.Confirm("[grey]Proceed?[/]"))
            return;

        AnsiConsole.WriteLine();

        Exception? seedErr = null;

        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn(Spinner.Known.BouncingBar))
            .StartAsync(async ctx =>
            {
                var sendTask = ctx.AddTask("[gold1]Sending[/]", maxValue: count);
                var dlqTask  = ctx.AddTask("[red]Dead-lettering[/]", maxValue: count);

                var sendProgress = new Progress<int>(n => sendTask.Value = n);
                var dlqProgress  = new Progress<int>(n => dlqTask.Value  = n);

                try
                {
                    await repo.SeedDlqAsync(
                        entity, count, payloadTemplate, dlqReason,
                        sendProgress, dlqProgress);
                }
                catch (Exception ex)
                {
                    seedErr = ex;
                }
            });

        AnsiConsole.WriteLine();

        if (seedErr is not null)
            AnsiConsole.MarkupLine($"[red bold]✗ Seeding failed:[/] {Markup.Escape(seedErr.Message)}");
        else
            AnsiConsole.MarkupLine($"[green bold]✓ {count} message(s) seeded to DLQ.[/]");

        Pause();
    }

    private static void Pause()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Prompt(new TextPrompt<string>("[grey]Press Enter to continue…[/]")
            .AllowEmpty()
            .HideDefaultValue());
    }
}
