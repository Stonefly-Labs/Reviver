using System.Diagnostics;
using StoneFlyLabs.Reviver.Commands;
using StoneFlyLabs.Reviver.Helpers;
using StoneFlyLabs.Reviver.Models;
using StoneFlyLabs.Reviver.Services;
using Spectre.Console;

namespace StoneFlyLabs.Reviver.UI;

public sealed class App(Func<string, IServiceBusRepository> repoFactory)
{
    private IServiceBusRepository? _repo;

    private static readonly EntityInfo RefreshSentinel = new("↩  Refresh",  "__refresh__", null, 0);
    private static readonly EntityInfo SeedSentinel    = new("⚡ Seed DLQ", "__seed__",    null, 0);
    private static readonly EntityInfo ExitSentinel    = new("✕  Exit",     "__exit__",    null, 0);

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <param name="presetFqdn">Already-normalised FQDN from CLI flag; null prompts the user.</param>
    public async Task RunAsync(string? presetFqdn = null)
    {
        ShowBanner();

        var fqdn = presetFqdn ?? PromptNamespace();
        _repo = repoFactory(fqdn);

        await using (_repo)
        {
            await MainLoopAsync();
        }

        AnsiConsole.MarkupLine("\n[grey]Goodbye.[/]");
    }

    // ── Banner ────────────────────────────────────────────────────────────────

    private static void ShowBanner()
    {
        AnsiConsole.Write(new FigletText("Reviver").Color(Color.Blue));
        AnsiConsole.MarkupLine("[grey]StoneFlyLabs · Azure Service Bus DLQ Reconciliation[/]");
        AnsiConsole.WriteLine();
    }

    // ── Namespace prompt ──────────────────────────────────────────────────────

    private static string PromptNamespace()
    {
        var env = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_NAMESPACE") ?? string.Empty;

        var prompt = new TextPrompt<string>("[blue]Namespace[/] [grey](name or FQDN)[/]:")
            .Validate(v => string.IsNullOrWhiteSpace(v)
                ? ValidationResult.Error("Required")
                : ValidationResult.Success());

        if (!string.IsNullOrWhiteSpace(env))
            prompt.DefaultValue(env);

        return NamingHelper.NormalizeNamespace(AnsiConsole.Prompt(prompt));
    }

    // ── Main entity-list loop ─────────────────────────────────────────────────

    private async Task MainLoopAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();
            ShowBanner();
            AnsiConsole.MarkupLine($"[grey]Namespace:[/] [blue]{_repo!.NamespaceFqdn}[/]\n");

            List<EntityInfo>? entities = null;
            Exception? loadErr = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("blue"))
                .StartAsync("Loading entities…", async _ =>
                {
                    try   { entities = await _repo.GetEntitiesWithDlqMessagesAsync(); }
                    catch (Exception ex) { loadErr = ex; }
                });

            if (loadErr is not null)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(loadErr.Message)}");
                AnsiConsole.WriteLine();
                if (!AnsiConsole.Confirm("Retry?")) return;
                continue;
            }

            if (entities!.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]✓ No DLQ messages — everything is clean![/]");
                AnsiConsole.WriteLine();

                var idle = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("What now?")
                        .AddChoices("Refresh", "Seed DLQ", "Exit"));

                if (idle == "Exit") return;
                if (idle == "Seed DLQ") await new SeederFlow(_repo).RunAsync();
                continue;
            }

            RenderEntityTable(entities);

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<EntityInfo>()
                    .Title("Select entity to process:")
                    .PageSize(20)
                    .HighlightStyle(Style.Parse("blue bold"))
                    .UseConverter(EntityLabel)
                    .AddChoices([.. entities, SeedSentinel, RefreshSentinel, ExitSentinel]));

            if (choice == ExitSentinel) return;

            if (choice == SeedSentinel)
            {
                await new SeederFlow(_repo).RunAsync();
                continue;
            }

            if (choice == RefreshSentinel) continue;

            await ProcessEntityAsync(choice);
        }
    }

    private static string EntityLabel(EntityInfo e)
    {
        if (e == SeedSentinel)    return "[yellow]⚡ Seed DLQ[/]";
        if (e == RefreshSentinel) return "[grey]↩  Refresh[/]";
        if (e == ExitSentinel)    return "[grey]✕  Exit[/]";

        var type = e.IsQueue ? "[cyan]Queue[/]" : "[yellow]Topic/Sub[/]";
        return $"{Markup.Escape(e.DisplayName)}  [grey]({type}[grey] · [/][red]{e.DlqMessageCount}[/][grey] DLQ)[/]";
    }

    private static void RenderEntityTable(List<EntityInfo> entities)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .AddColumn(new TableColumn("[bold]Entity[/]"))
            .AddColumn(new TableColumn("[bold]Type[/]").Centered())
            .AddColumn(new TableColumn("[bold]DLQ[/]").RightAligned());

        foreach (var e in entities)
        {
            table.AddRow(
                Markup.Escape(e.DisplayName),
                e.IsQueue ? "[cyan]Queue[/]" : "[yellow]Topic/Sub[/]",
                $"[red bold]{e.DlqMessageCount}[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    // ── Receive batch and hand off ────────────────────────────────────────────

    private async Task ProcessEntityAsync(EntityInfo entity)
    {
        IDlqSession? session = null;
        Exception? err = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync($"Receiving from {entity.DisplayName} DLQ…", async _ =>
            {
                try   { session = await _repo!.OpenDlqSessionAsync(entity, maxMessages: 20); }
                catch (Exception ex) { err = ex; }
            });

        if (err is not null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(err.Message)}");
            Pause();
            return;
        }

        await using var s = session!;

        if (s.Messages.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No messages received (batch empty or held by another consumer).[/]");
            Pause();
            return;
        }

        await ProcessBatchAsync(s);
    }

    // Wrapper because SelectionPrompt<T> requires T : notnull
    private sealed record MsgItem(DlqMessage? Message);

    private async Task ProcessBatchAsync(IDlqSession session)
    {
        var pending = session.Messages.ToList();
        var doneItem = new MsgItem((DlqMessage?)null);

        while (pending.Count > 0)
        {
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine($"[blue bold]DLQ:[/] {Markup.Escape(session.Entity.DisplayName)}  " +
                                   $"[grey]({pending.Count} message(s) in batch)[/]\n");

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<MsgItem>()
                    .Title("Select message:")
                    .PageSize(20)
                    .HighlightStyle(Style.Parse("blue bold"))
                    .UseConverter(item => MsgLabel(item.Message))
                    .AddChoices([.. pending.Select(m => new MsgItem(m)), doneItem]));

            if (choice.Message is null)
            {
                await AbandonAllAsync(session, pending);
                break;
            }

            var msg = choice.Message;
            var action = await ShowMessageDetailAsync(session, msg);

            if (action is MessageAction.Sent or MessageAction.Discarded)
            {
                pending.Remove(msg);
                AnsiConsole.MarkupLine(action == MessageAction.Sent
                    ? "[green]✓ Sent and removed from DLQ.[/]"
                    : "[yellow]✓ Discarded from DLQ.[/]");
                await Task.Delay(900);
            }
        }

        if (pending.Count == 0)
        {
            AnsiConsole.MarkupLine("\n[green]✓ Batch complete![/]");
            Pause();
        }
    }

    private static string MsgLabel(DlqMessage? m)
    {
        if (m is null) return "[grey]↩  Done (abandon remaining back to DLQ)[/]";

        var reason = m.DeadLetterReason is not null
            ? $"  [red]{Markup.Escape(m.DeadLetterReason)}[/]"
            : string.Empty;

        return $"[white]{Markup.Escape(m.MessageId)}[/]  [grey]{m.EnqueuedAt:yyyy-MM-dd HH:mm:ss}[/]{reason}";
    }

    private static async Task AbandonAllAsync(IDlqSession session, IEnumerable<DlqMessage> messages)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Releasing locks…", async _ =>
            {
                foreach (var m in messages)
                {
                    try { await session.AbandonAsync(m); }
                    catch { /* best-effort */ }
                }
            });
    }

    // ── Message detail + action loop ──────────────────────────────────────────

    private async Task<MessageAction> ShowMessageDetailAsync(IDlqSession session, DlqMessage message)
    {
        using var renewCts = new CancellationTokenSource();
        var renewTask = RenewLockLoopAsync(session, message, renewCts.Token);

        try
        {
            while (true)
            {
                AnsiConsole.Clear();
                RenderMessageDetail(message);

                var action = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Action:")
                        .PageSize(10)
                        .AddChoices(
                            "Edit Body",
                            "Edit Application Properties",
                            "Send to Destination",
                            "Discard  (complete without resending)",
                            "Skip  (return to list)"));

                switch (action)
                {
                    case "Edit Body":
                        await EditBodyAsync(message);
                        break;

                    case "Edit Application Properties":
                        EditProperties(message);
                        break;

                    case "Send to Destination":
                        if (await SendFlowAsync(session, message))
                            return MessageAction.Sent;
                        break;

                    case "Discard  (complete without resending)":
                        if (AnsiConsole.Confirm("[red]Permanently remove from DLQ without resending?[/]"))
                        {
                            await session.CompleteAsync(message);
                            return MessageAction.Discarded;
                        }
                        break;

                    case "Skip  (return to list)":
                        return MessageAction.Skipped;
                }
            }
        }
        finally
        {
            await renewCts.CancelAsync();
            try { await renewTask; } catch (OperationCanceledException) { }
        }
    }

    private static async Task RenewLockLoopAsync(IDlqSession session, DlqMessage message, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            if (!ct.IsCancellationRequested)
            {
                try { await session.RenewLockAsync(message, ct); }
                catch { /* expired or cancelled */ }
            }
        }
    }

    // ── Message rendering ─────────────────────────────────────────────────────

    private static void RenderMessageDetail(DlqMessage msg)
    {
        var grid = new Grid().AddColumn().AddColumn();
        AddRow(grid, "Message ID",  msg.MessageId);
        AddRow(grid, "Enqueued",    msg.EnqueuedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        AddRow(grid, "Lock Until",  msg.LockedUntil.ToString("yyyy-MM-dd HH:mm:ss zzz"));

        if (msg.DeadLetterReason is not null)
            AddRow(grid, "DLQ Reason", $"[red]{Markup.Escape(msg.DeadLetterReason)}[/]");
        if (msg.DeadLetterErrorDescription is not null)
            AddRow(grid, "DLQ Description", $"[red]{Markup.Escape(msg.DeadLetterErrorDescription)}[/]");
        if (msg.ContentType is not null)
            AddRow(grid, "Content-Type", msg.ContentType);
        if (msg.Subject is not null)
            AddRow(grid, "Subject", msg.Subject);
        if (msg.CorrelationId is not null)
            AddRow(grid, "Correlation ID", msg.CorrelationId);

        AnsiConsole.Write(new Panel(grid)
            .Header("[blue bold] Message [/]")
            .Border(BoxBorder.Rounded));

        var bodyText = JsonHelper.TryFormat(msg.Body);
        var display  = bodyText.Length > 3000 ? bodyText[..3000] + "\n[grey]… (truncated)[/]" : bodyText;

        AnsiConsole.Write(new Panel(new Markup(Markup.Escape(display)))
            .Header("[blue bold] Body [/]")
            .Border(BoxBorder.Rounded));

        if (msg.ApplicationProperties.Count > 0)
        {
            var propTable = new Table()
                .Border(TableBorder.Simple)
                .AddColumn(new TableColumn("[bold]Key[/]"))
                .AddColumn(new TableColumn("[bold]Value[/]"));

            foreach (var (k, v) in msg.ApplicationProperties)
                propTable.AddRow(Markup.Escape(k), Markup.Escape(v?.ToString() ?? "(null)"));

            AnsiConsole.Write(new Panel(propTable)
                .Header("[blue bold] Application Properties [/]")
                .Border(BoxBorder.Rounded));
        }

        AnsiConsole.WriteLine();
    }

    private static void AddRow(Grid grid, string label, string value)
        => grid.AddRow($"[bold]{label}[/]", value);

    // ── Body editor ───────────────────────────────────────────────────────────

    private static async Task EditBodyAsync(DlqMessage message)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"reviver-{message.MessageId}.json");

        try
        {
            await File.WriteAllTextAsync(tempFile, JsonHelper.TryFormat(message.Body));

            var editor = Environment.GetEnvironmentVariable("EDITOR")
                ?? (OperatingSystem.IsWindows() ? "notepad.exe" : "nano");

            AnsiConsole.MarkupLine($"\n[grey]Opening [bold]{editor}[/] — save and close to continue…[/]\n");

            var proc = Process.Start(new ProcessStartInfo(editor, $"\"{tempFile}\"")
            {
                UseShellExecute = OperatingSystem.IsWindows()
            });

            if (proc is not null)
                await proc.WaitForExitAsync();

            var updated = (await File.ReadAllTextAsync(tempFile)).Trim();

            if (string.IsNullOrWhiteSpace(updated))
            {
                AnsiConsole.MarkupLine("[yellow]Body is empty — keeping original.[/]");
                Pause();
                return;
            }

            if ((updated.StartsWith('{') || updated.StartsWith('[')) &&
                !JsonHelper.IsValid(updated, out var jsonErr))
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] Invalid JSON — {Markup.Escape(jsonErr)}");
                if (!AnsiConsole.Confirm("Use it anyway?")) return;
            }

            message.Body = updated;
            AnsiConsole.MarkupLine("[green]✓ Body updated.[/]");
            await Task.Delay(700);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            Pause();
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    // ── Properties editor ─────────────────────────────────────────────────────

    private static void EditProperties(DlqMessage message)
    {
        while (true)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[blue bold] Application Properties [/]").RuleStyle("blue"));
            AnsiConsole.WriteLine();

            if (message.ApplicationProperties.Count > 0)
            {
                var t = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Blue)
                    .AddColumn(new TableColumn("[bold]Key[/]"))
                    .AddColumn(new TableColumn("[bold]Value[/]"));

                foreach (var (k, v) in message.ApplicationProperties)
                    t.AddRow(Markup.Escape(k), Markup.Escape(v?.ToString() ?? "(null)"));

                AnsiConsole.Write(t);
            }
            else
            {
                AnsiConsole.MarkupLine("[grey](none)[/]");
            }

            AnsiConsole.WriteLine();

            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Action:")
                    .AddChoices("Add / Edit property", "Remove property", "↩  Done"));

            switch (action)
            {
                case "Add / Edit property":
                    var key = AnsiConsole.Ask<string>("Key:");
                    var val = AnsiConsole.Ask<string>("Value:");
                    message.ApplicationProperties[key] = val;
                    break;

                case "Remove property":
                    if (message.ApplicationProperties.Count == 0)
                    {
                        AnsiConsole.MarkupLine("[yellow]Nothing to remove.[/]");
                        break;
                    }
                    var toRemove = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("Remove which property?")
                            .AddChoices(message.ApplicationProperties.Keys));
                    message.ApplicationProperties.Remove(toRemove);
                    break;

                case "↩  Done":
                    return;
            }
        }
    }

    // ── Send flow ─────────────────────────────────────────────────────────────

    private async Task<bool> SendFlowAsync(IDlqSession session, DlqMessage message)
    {
        List<EntityInfo>? destinations = null;
        Exception? err = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Loading destinations…", async _ =>
            {
                try   { destinations = await _repo!.GetAllSendDestinationsAsync(); }
                catch (Exception ex) { err = ex; }
            });

        if (err is not null)
        {
            AnsiConsole.MarkupLine($"[red]Error loading destinations:[/] {Markup.Escape(err.Message)}");
            Pause();
            return false;
        }

        if (destinations is null || destinations.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No destinations found.[/]");
            Pause();
            return false;
        }

        // Bubble the originating source to top
        var defaultDest = destinations.FirstOrDefault(d => d.QueueOrTopicName == session.Entity.SendPath);
        if (defaultDest is not null)
        {
            destinations.Remove(defaultDest);
            destinations.Insert(0, defaultDest);
        }

        var dest = AnsiConsole.Prompt(
            new SelectionPrompt<EntityInfo>()
                .Title("Send to:")
                .PageSize(25)
                .HighlightStyle(Style.Parse("green bold"))
                .UseConverter(e =>
                {
                    var label = Markup.Escape(e.DisplayName);
                    return defaultDest is not null && e == defaultDest
                        ? $"[green]{label}[/]  [grey](original source — default)[/]"
                        : label;
                })
                .AddChoices(destinations));

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"Destination: [green bold]{Markup.Escape(dest.DisplayName)}[/]");

        if (!AnsiConsole.Confirm("Confirm send?"))
            return false;

        bool success = false;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("green"))
            .StartAsync("Sending…", async _ =>
            {
                try
                {
                    await _repo!.SendMessageAsync(dest.SendPath, message);
                    await session.CompleteAsync(message);
                    success = true;
                }
                catch (Exception ex)
                {
                    err = ex;
                }
            });

        if (err is not null)
        {
            AnsiConsole.MarkupLine($"[red]Send failed:[/] {Markup.Escape(err.Message)}");
            Pause();
        }

        return success;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void Pause()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Prompt(new TextPrompt<string>("[grey]Press Enter to continue…[/]")
            .AllowEmpty()
            .HideDefaultValue());
    }
}

internal enum MessageAction { Sent, Discarded, Skipped }
