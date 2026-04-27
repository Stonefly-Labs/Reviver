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

    private static readonly EntityInfo RefreshSentinel = new("↺  Refresh",   "__refresh__", null, 0);
    private static readonly EntityInfo SeedSentinel    = new("⚡ Seed DLQ",  "__seed__",    null, 0);
    private static readonly EntityInfo ExitSentinel    = new("✕  Exit",      "__exit__",    null, 0);

    // ── Entry point ───────────────────────────────────────────────────────────

    public async Task RunAsync(string? presetFqdn = null)
    {
        ShowBanner();

        var fqdn = presetFqdn ?? PromptNamespace();
        _repo = repoFactory(fqdn);

        await using (_repo)
        {
            await MainLoopAsync();
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[grey]Goodbye[/]").RuleStyle("grey dim"));
        AnsiConsole.WriteLine();
    }

    // ── Banner ────────────────────────────────────────────────────────────────

    private static void ShowBanner()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new FigletText("Reviver").Color(Color.DeepSkyBlue1));
        AnsiConsole.Write(
            new Rule("[deepskyblue1]StoneFlyLabs[/] [grey]· Azure Service Bus DLQ Reconciliation[/]")
                .RuleStyle("deepskyblue1 dim"));
        AnsiConsole.WriteLine();
    }

    // ── Namespace prompt ──────────────────────────────────────────────────────

    private static string PromptNamespace()
    {
        var env = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_NAMESPACE") ?? string.Empty;

        AnsiConsole.Write(new Rule("[deepskyblue1 dim] Connect [/]").RuleStyle("deepskyblue1 dim"));
        AnsiConsole.WriteLine();

        var prompt = new TextPrompt<string>("[deepskyblue1]Namespace[/] [grey](name or FQDN)[/]:")
            .Validate(v => string.IsNullOrWhiteSpace(v)
                ? ValidationResult.Error("Required")
                : ValidationResult.Success());

        if (!string.IsNullOrWhiteSpace(env))
            prompt.DefaultValue(env);

        AnsiConsole.WriteLine();
        return NamingHelper.NormalizeNamespace(AnsiConsole.Prompt(prompt));
    }

    // ── Main entity-list loop ─────────────────────────────────────────────────

    private async Task MainLoopAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();
            ShowBanner();
            AnsiConsole.MarkupLine($"[grey]  Connected:[/] [deepskyblue1 bold]{_repo!.NamespaceFqdn}[/]\n");

            List<EntityInfo>? entities = null;
            Exception? loadErr = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.BouncingBar)
                .SpinnerStyle(Style.Parse("deepskyblue1"))
                .StartAsync("[grey]Loading entities…[/]", async _ =>
                {
                    try   { entities = await _repo.GetEntitiesWithDlqMessagesAsync(); }
                    catch (Exception ex) { loadErr = ex; }
                });

            if (loadErr is not null)
            {
                AnsiConsole.MarkupLine($"[red bold]✗ Error:[/] {Markup.Escape(loadErr.Message)}");
                AnsiConsole.WriteLine();
                if (!AnsiConsole.Confirm("[grey]Retry?[/]")) return;
                continue;
            }

            if (entities!.Count == 0)
            {
                AnsiConsole.Write(new Rule("[green bold] ✓ All Clear [/]").RuleStyle("green"));
                AnsiConsole.MarkupLine("\n[green]No DLQ messages — everything looks healthy.[/]\n");

                var idle = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[grey]What next?[/]")
                        .HighlightStyle(Style.Parse("deepskyblue1 bold"))
                        .AddChoices("↺  Refresh", "⚡ Seed DLQ", "✕  Exit"));
                if (idle.StartsWith('✕')) return;
                if (idle.StartsWith('⚡')) await new SeederFlow(_repo).RunAsync();
                continue;
            }

            RenderEntityTable(entities);

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<EntityInfo>()
                    .Title("[grey]Select entity to process:[/]")
                    .PageSize(20)
                    .HighlightStyle(Style.Parse("deepskyblue1 bold"))
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
        if (e == SeedSentinel)    return "[gold1]⚡ Seed DLQ[/]";
        if (e == RefreshSentinel) return "[grey]↺  Refresh[/]";
        if (e == ExitSentinel)    return "[grey]✕  Exit[/]";

        var icon = e.IsQueue ? "[deepskyblue1]≡[/]" : "[gold1]⬡[/]";
        var countColor = e.DlqMessageCount switch
        {
            <= 5  => "yellow",
            <= 20 => "orange1",
            _     => "red"
        };
        return $"{icon} {Markup.Escape(e.DisplayName)}  [grey]([/][{countColor} bold]{e.DlqMessageCount} DLQ[/][grey])[/]";
    }

    private static void RenderEntityTable(List<EntityInfo> entities)
    {
        var total = entities.Sum(e => e.DlqMessageCount);

        AnsiConsole.MarkupLine(
            $"  [grey]Found [/][deepskyblue1 bold]{entities.Count}[/]" +
            $"[grey] entit{(entities.Count == 1 ? "y" : "ies")} · [/]" +
            $"[red bold]{total}[/][grey] dead-lettered message{(total == 1 ? "" : "s")}[/]\n");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.DeepSkyBlue1)
            .AddColumn(new TableColumn("[bold]Entity[/]"))
            .AddColumn(new TableColumn("[bold]Type[/]").Centered())
            .AddColumn(new TableColumn("[bold]DLQ[/]").RightAligned());

        foreach (var e in entities)
        {
            var typeLabel = e.IsQueue ? "[deepskyblue1]≡ Queue[/]" : "[gold1]⬡ Topic/Sub[/]";
            var countLabel = e.DlqMessageCount switch
            {
                <= 5  => $"[yellow]{e.DlqMessageCount}[/]",
                <= 20 => $"[orange1 bold]{e.DlqMessageCount}[/]",
                _     => $"[red bold]{e.DlqMessageCount}[/]"
            };
            table.AddRow(Markup.Escape(e.DisplayName), typeLabel, countLabel);
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
            .Spinner(Spinner.Known.BouncingBar)
            .SpinnerStyle(Style.Parse("deepskyblue1"))
            .StartAsync($"[grey]Receiving from[/] [deepskyblue1]{Markup.Escape(entity.DisplayName)}[/] [grey]DLQ…[/]", async _ =>
            {
                try   { session = await _repo!.OpenDlqSessionAsync(entity, maxMessages: 20); }
                catch (Exception ex) { err = ex; }
            });

        if (err is not null)
        {
            AnsiConsole.MarkupLine($"[red bold]✗ Error:[/] {Markup.Escape(err.Message)}");
            Pause();
            return;
        }

        await using var s = session!;

        if (s.Messages.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]⚠ No messages received — batch empty or held by another consumer.[/]");
            Pause();
            return;
        }

        await ProcessBatchAsync(s);
    }

    private sealed record MsgItem(DlqMessage? Message);

    private async Task ProcessBatchAsync(IDlqSession session)
    {
        var pending = session.Messages.ToList();
        var doneItem = new MsgItem((DlqMessage?)null);

        while (pending.Count > 0)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(
                new Rule($"[deepskyblue1 bold] ◆ {Markup.Escape(session.Entity.DisplayName)} [/]  [grey]{pending.Count} message(s) in batch[/]")
                    .RuleStyle("deepskyblue1 dim"));
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<MsgItem>()
                    .Title("[grey]Select message to inspect:[/]")
                    .PageSize(20)
                    .HighlightStyle(Style.Parse("deepskyblue1 bold"))
                    .UseConverter(item => MsgLabel(item.Message))
                    .AddChoices([.. pending.Select(m => new MsgItem(m)), doneItem]));

            if (choice.Message is null)
            {
                await AbandonAllAsync(session, pending);
                break;
            }

            var msg = choice.Message;
            var action = await ShowMessageDetailAsync(session, msg);

            if (action is MessageAction.Sent or MessageAction.SentKept or MessageAction.Discarded)
            {
                pending.Remove(msg);
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine(action switch
                {
                    MessageAction.Sent     => "[green bold]✓ Sent[/][grey] — removed from DLQ.[/]",
                    MessageAction.SentKept => "[green bold]✓ Sent[/][grey] — message remains in DLQ (lock will expire).[/]",
                    _                      => "[yellow bold]✓ Discarded[/][grey] — removed from DLQ.[/]"
                });
                await Task.Delay(900);
            }
        }

        if (pending.Count == 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[green bold] ✓ Batch Complete [/]").RuleStyle("green"));
            Pause();
        }
    }

    private static string MsgLabel(DlqMessage? m)
    {
        if (m is null) return "[grey]↩  Done — release remaining back to DLQ[/]";

        var reason = m.DeadLetterReason is not null
            ? $"  [red]▸ {Markup.Escape(m.DeadLetterReason)}[/]"
            : string.Empty;

        return $"[white bold]{Markup.Escape(m.MessageId)}[/]  [grey]{m.EnqueuedAt:yyyy-MM-dd HH:mm:ss}[/]{reason}";
    }

    private static async Task AbandonAllAsync(IDlqSession session, IEnumerable<DlqMessage> messages)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.BouncingBar)
            .SpinnerStyle(Style.Parse("grey"))
            .StartAsync("[grey]Releasing locks…[/]", async _ =>
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
                        .Title("[grey]Action:[/]")
                        .PageSize(10)
                        .HighlightStyle(Style.Parse("deepskyblue1 bold"))
                        .AddChoices(
                            "✎  Edit Body",
                            "✎  Edit Application Properties",
                            "▶  Send to Destination",
                            "✗  Discard  (complete without resending)",
                            "↩  Skip  (return to list)"));

                switch (action)
                {
                    case "✎  Edit Body":
                        await EditBodyAsync(message);
                        break;

                    case "✎  Edit Application Properties":
                        EditProperties(message);
                        break;

                    case "▶  Send to Destination":
                        var (sent, removedFromDlq) = await SendFlowAsync(session, message);
                        if (sent)
                            return removedFromDlq ? MessageAction.Sent : MessageAction.SentKept;
                        break;

                    case "✗  Discard  (complete without resending)":
                        if (AnsiConsole.Confirm("[red]Permanently remove from DLQ without resending?[/]"))
                        {
                            await session.CompleteAsync(message);
                            return MessageAction.Discarded;
                        }
                        break;

                    case "↩  Skip  (return to list)":
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
            try { await session.RenewLockAsync(message, ct); }
            catch (OperationCanceledException) { return; }
            catch { /* best-effort */ }

            try { await Task.Delay(TimeSpan.FromSeconds(25), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    // ── Message rendering ─────────────────────────────────────────────────────

    private static void RenderMessageDetail(DlqMessage msg)
    {
        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn();

        AddRow(grid, "Message ID",  $"[bold]{Markup.Escape(msg.MessageId)}[/]");
        AddRow(grid, "Enqueued",    $"[grey]{msg.EnqueuedAt:yyyy-MM-dd HH:mm:ss zzz}[/]");
        AddRow(grid, "Lock Until",  $"[grey]{msg.LockedUntil:yyyy-MM-dd HH:mm:ss zzz}[/]");

        if (msg.DeadLetterReason is not null)
            AddRow(grid, "DLQ Reason",       $"[red bold]{Markup.Escape(msg.DeadLetterReason)}[/]");
        if (msg.DeadLetterErrorDescription is not null)
            AddRow(grid, "DLQ Description",  $"[red]{Markup.Escape(msg.DeadLetterErrorDescription)}[/]");
        if (msg.ContentType is not null)
            AddRow(grid, "Content-Type",     $"[grey]{Markup.Escape(msg.ContentType)}[/]");
        if (msg.Subject is not null)
            AddRow(grid, "Subject",          Markup.Escape(msg.Subject));
        if (msg.CorrelationId is not null)
            AddRow(grid, "Correlation ID",   $"[grey]{Markup.Escape(msg.CorrelationId)}[/]");

        AnsiConsole.Write(new Panel(grid)
            .Header("[deepskyblue1 bold] ◆ Message Details [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.DeepSkyBlue1)
            .Padding(1, 0));

        var isJson = JsonHelper.IsValid(msg.Body, out _);
        var isXml  = !isJson && XmlHelper.IsValid(msg.Body, out _);
        var bodyText  = isJson ? JsonHelper.TryFormat(msg.Body)
                      : isXml  ? XmlHelper.TryFormat(msg.Body)
                      : msg.Body;
        var bodyTitle = isJson ? "[deepskyblue1 bold] ◆ Body · JSON [/]"
                      : isXml  ? "[deepskyblue1 bold] ◆ Body · XML [/]"
                      : "[deepskyblue1 bold] ◆ Body [/]";
        var display   = bodyText.Length > 3000 ? bodyText[..3000] + "\n[grey]… (truncated)[/]" : bodyText;

        AnsiConsole.Write(new Panel(new Markup(Markup.Escape(display)))
            .Header(bodyTitle)
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.DeepSkyBlue1)
            .Padding(1, 0));

        if (msg.ApplicationProperties.Count > 0)
        {
            var propTable = new Table()
                .Border(TableBorder.Simple)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn("[bold]Key[/]"))
                .AddColumn(new TableColumn("[bold]Value[/]"));

            foreach (var (k, v) in msg.ApplicationProperties)
                propTable.AddRow(
                    $"[deepskyblue1]{Markup.Escape(k)}[/]",
                    Markup.Escape(v?.ToString() ?? "[grey](null)[/]"));

            AnsiConsole.Write(new Panel(propTable)
                .Header("[deepskyblue1 bold] ◆ Application Properties [/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.DeepSkyBlue1)
                .Padding(1, 0));
        }

        AnsiConsole.WriteLine();
    }

    private static void AddRow(Grid grid, string label, string value)
        => grid.AddRow($"[grey]{label}[/]", value);

    // ── Body editor ───────────────────────────────────────────────────────────

    private static async Task EditBodyAsync(DlqMessage message)
    {
        var isJson   = JsonHelper.IsValid(message.Body, out _);
        var isXml    = !isJson && XmlHelper.IsValid(message.Body, out _);
        var ext      = isJson ? ".json" : isXml ? ".xml" : ".txt";
        var tempFile = Path.Combine(Path.GetTempPath(), $"reviver-{message.MessageId}{ext}");

        try
        {
            var formatted = isJson ? JsonHelper.TryFormat(message.Body)
                          : isXml  ? XmlHelper.TryFormat(message.Body)
                          : message.Body;
            await File.WriteAllTextAsync(tempFile, formatted);

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
                AnsiConsole.MarkupLine("[yellow]⚠ Body is empty — keeping original.[/]");
                Pause();
                return;
            }

            var trimmed = updated.TrimStart();
            if ((trimmed.StartsWith('{') || trimmed.StartsWith('[')) &&
                !JsonHelper.IsValid(updated, out var jsonErr))
            {
                AnsiConsole.MarkupLine($"[yellow]⚠ Invalid JSON:[/] {Markup.Escape(jsonErr)}");
                if (!AnsiConsole.Confirm("Use it anyway?")) return;
            }
            else if (trimmed.StartsWith('<') &&
                !XmlHelper.IsValid(updated, out var xmlErr))
            {
                AnsiConsole.MarkupLine($"[yellow]⚠ Invalid XML:[/] {Markup.Escape(xmlErr)}");
                if (!AnsiConsole.Confirm("Use it anyway?")) return;
            }

            message.Body = updated;
            AnsiConsole.MarkupLine("[green bold]✓ Body updated.[/]");
            await Task.Delay(700);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red bold]✗ Error:[/] {Markup.Escape(ex.Message)}");
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
            AnsiConsole.Write(
                new Rule("[deepskyblue1 bold] ◆ Application Properties [/]")
                    .RuleStyle("deepskyblue1 dim"));
            AnsiConsole.WriteLine();

            if (message.ApplicationProperties.Count > 0)
            {
                var t = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.DeepSkyBlue1)
                    .AddColumn(new TableColumn("[bold]Key[/]"))
                    .AddColumn(new TableColumn("[bold]Value[/]"));

                foreach (var (k, v) in message.ApplicationProperties)
                    t.AddRow(
                        $"[deepskyblue1]{Markup.Escape(k)}[/]",
                        Markup.Escape(v?.ToString() ?? "[grey](null)[/]"));

                AnsiConsole.Write(t);
            }
            else
            {
                AnsiConsole.MarkupLine("[grey]  (none)[/]");
            }

            AnsiConsole.WriteLine();

            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[grey]Action:[/]")
                    .HighlightStyle(Style.Parse("deepskyblue1 bold"))
                    .AddChoices("✎  Add / Edit property", "✗  Remove property", "↩  Done"));

            switch (action)
            {
                case "✎  Add / Edit property":
                    var key = AnsiConsole.Ask<string>("[deepskyblue1]Key:[/]");
                    var val = AnsiConsole.Ask<string>("[deepskyblue1]Value:[/]");
                    message.ApplicationProperties[key] = val;
                    break;

                case "✗  Remove property":
                    if (message.ApplicationProperties.Count == 0)
                    {
                        AnsiConsole.MarkupLine("[yellow]⚠ Nothing to remove.[/]");
                        break;
                    }
                    var toRemove = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[grey]Remove which property?[/]")
                            .HighlightStyle(Style.Parse("red bold"))
                            .AddChoices(message.ApplicationProperties.Keys));
                    message.ApplicationProperties.Remove(toRemove);
                    break;

                case "↩  Done":
                    return;
            }
        }
    }

    // ── Send flow ─────────────────────────────────────────────────────────────

    private async Task<(bool Success, bool RemovedFromDlq)> SendFlowAsync(IDlqSession session, DlqMessage message)
    {
        List<EntityInfo>? destinations = null;
        Exception? err = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.BouncingBar)
            .SpinnerStyle(Style.Parse("deepskyblue1"))
            .StartAsync("[grey]Loading destinations…[/]", async _ =>
            {
                try   { destinations = await _repo!.GetAllSendDestinationsAsync(); }
                catch (Exception ex) { err = ex; }
            });

        if (err is not null)
        {
            AnsiConsole.MarkupLine($"[red bold]✗ Error loading destinations:[/] {Markup.Escape(err.Message)}");
            Pause();
            return (false, false);
        }

        if (destinations is null || destinations.Count == 0)
        {
            AnsiConsole.MarkupLine("[red bold]✗ No destinations found.[/]");
            Pause();
            return (false, false);
        }

        var defaultDest = destinations.FirstOrDefault(d => d.QueueOrTopicName == session.Entity.SendPath);
        if (defaultDest is not null)
        {
            destinations.Remove(defaultDest);
            destinations.Insert(0, defaultDest);
        }

        var dest = AnsiConsole.Prompt(
            new SelectionPrompt<EntityInfo>()
                .Title("[grey]Send to:[/]")
                .PageSize(25)
                .HighlightStyle(Style.Parse("green bold"))
                .UseConverter(e =>
                {
                    var label = Markup.Escape(e.DisplayName);
                    return defaultDest is not null && e == defaultDest
                        ? $"[green bold]{label}[/]  [grey](original source)[/]"
                        : label;
                })
                .AddChoices(destinations));

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Destination:[/] [green bold]{Markup.Escape(dest.DisplayName)}[/]");
        AnsiConsole.WriteLine();

        var confirm = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[grey]Confirm action:[/]")
                .HighlightStyle(Style.Parse("green bold"))
                .AddChoices(
                    "▶  Send and remove from DLQ",
                    "▶  Send and keep in DLQ",
                    "✕  Cancel"));

        if (confirm.StartsWith('✕'))
            return (false, false);

        var removeDlq     = confirm.Contains("remove");
        bool sendOk       = false;
        bool completeOk   = false;
        Exception? sendErr     = null;
        Exception? completeErr = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.BouncingBar)
            .SpinnerStyle(Style.Parse("green"))
            .StartAsync("[grey]Sending…[/]", async _ =>
            {
                try
                {
                    await _repo!.SendMessageAsync(dest.SendPath, message);
                    sendOk = true;
                }
                catch (Exception ex) { sendErr = ex; }

                if (sendOk && removeDlq)
                {
                    try
                    {
                        await session.CompleteAsync(message);
                        completeOk = true;
                    }
                    catch (Exception ex) { completeErr = ex; }
                }
            });

        if (sendErr is not null)
        {
            AnsiConsole.MarkupLine($"[red bold]✗ Send failed:[/] {Markup.Escape(sendErr.Message)}");
            Pause();
            return (false, false);
        }

        if (completeErr is not null)
        {
            AnsiConsole.MarkupLine("[yellow]⚠ Message sent — but the DLQ lock expired before it could be removed.[/]");
            AnsiConsole.MarkupLine("[grey]  It will re-appear in the DLQ once the lock expires.[/]");
            Pause();
            return (true, false);
        }

        return (true, removeDlq && completeOk);
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

internal enum MessageAction { Sent, SentKept, Discarded, Skipped }
