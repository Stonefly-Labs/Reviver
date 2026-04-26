using StoneFlyLabs.Reviver.Helpers;
using StoneFlyLabs.Reviver.Models;
using StoneFlyLabs.Reviver.Services;
using Spectre.Console;

namespace StoneFlyLabs.Reviver.Commands;

public sealed class SeederFlow(IServiceBusRepository repo)
{
    public async Task RunAsync()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[yellow bold]Seed DLQ[/]").RuleStyle("yellow"));
        AnsiConsole.WriteLine();

        // Load all entities
        List<EntityInfo>? entities = null;
        Exception? loadErr = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("yellow"))
            .StartAsync("Loading entities…", async _ =>
            {
                try   { entities = await repo.GetAllEntitiesAsync(); }
                catch (Exception ex) { loadErr = ex; }
            });

        if (loadErr is not null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(loadErr.Message)}");
            Pause();
            return;
        }

        if (entities is null || entities.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No queues or topic subscriptions found.[/]");
            Pause();
            return;
        }

        // Entity selection
        var entity = AnsiConsole.Prompt(
            new SelectionPrompt<EntityInfo>()
                .Title("Target entity:")
                .PageSize(20)
                .HighlightStyle(Style.Parse("yellow bold"))
                .UseConverter(e => Markup.Escape(e.DisplayName))
                .AddChoices(entities));

        if (!entity.IsQueue)
        {
            AnsiConsole.MarkupLine(
                "[grey]Note: messages sent to a topic are delivered to ALL subscriptions. " +
                "Only the selected subscription will be dead-lettered.[/]\n");
        }

        // Seed config
        var count = AnsiConsole.Prompt(
            new TextPrompt<int>("Number of messages to seed:")
                .DefaultValue(10)
                .Validate(n => n is >= 1 and <= 1000
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Must be 1–1000")));

        var payloadTemplate = AnsiConsole.Prompt(
            new TextPrompt<string>("Payload template:")
                .DefaultValue(PayloadTemplate.Default)
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(payloadTemplate))
            payloadTemplate = PayloadTemplate.Default;

        AnsiConsole.MarkupLine($"\n[grey]Placeholders available: {{index}}, {{timestamp}}, {{guid}}[/]");
        AnsiConsole.MarkupLine($"[grey]Preview (index=0):[/] {Markup.Escape(PayloadTemplate.Expand(payloadTemplate, 0))}\n");

        var dlqReason = AnsiConsole.Prompt(
            new TextPrompt<string>("Dead-letter reason:")
                .DefaultValue("Reviver.Seeder"));

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]Summary:[/] seed [yellow]{count}[/] messages → " +
                               $"[yellow]{Markup.Escape(entity.DisplayName)}[/] DLQ " +
                               $"with reason [yellow]{Markup.Escape(dlqReason)}[/]");
        AnsiConsole.WriteLine();

        if (!AnsiConsole.Confirm("Proceed?"))
            return;

        AnsiConsole.WriteLine();

        // Run with progress bars
        Exception? seedErr = null;

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
                var sendTask = ctx.AddTask("[yellow]Sending[/]", maxValue: count);
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
            AnsiConsole.MarkupLine($"[red]Seeding failed:[/] {Markup.Escape(seedErr.Message)}");
        else
            AnsiConsole.MarkupLine($"[green]✓ {count} message(s) seeded to DLQ.[/]");

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
