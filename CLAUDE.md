# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run tests
dotnet test
dotnet test Reviver.Tests/Reviver.Tests.csproj --filter "FullyQualifiedName~NamingHelperTests"
dotnet test Reviver.Tests/Reviver.Tests.csproj --filter "FullyQualifiedName~NamingHelperTests.NormalizeNamespace_ReturnsExpectedFqdn"
```

### CLI (Cocona-powered)

```bash
# Launch the interactive TUI (default)
dotnet run --project ConsoleApp1/ConsoleApp1.csproj
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- -n myns          # pre-fill namespace, skip prompt

# Seed DLQ messages without the TUI
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- seed <entity> [options]
#   entity:  queue name ("orders") or "topic/subscription" ("events/payments")
#   -n       namespace (or set AZURE_SERVICEBUS_NAMESPACE)
#   -c       message count (default 10)
#   -p       payload template — supports {index}, {timestamp}, {guid}
#   -r       dead-letter reason (default "Reviver.Seeder")
#
# Example:
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- seed orders -n myns -c 100 -r "LoadTest"

# Print version
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- version
```

The tool requires `az login` before running — auth uses `AzureCliCredential`. The target namespace can be pre-set via `AZURE_SERVICEBUS_NAMESPACE` (short name or FQDN; the app normalises either form).

## Architecture

**Two projects** in one solution:
- `ConsoleApp1/` — the app, assembly name `reviver`, root namespace `StoneflyLabs.Reviver`
- `Reviver.Tests/` — xUnit + NSubstitute tests, root namespace `StoneflyLabs.Reviver.Tests`

### Dependency flow

```
Program
  └── App (UI/App.cs)                   — all Spectre.Console TUI screens
        ├── SeederFlow (Commands/)       — DLQ seed wizard
        └── IServiceBusRepository        — injected via Func<string, IServiceBusRepository> factory
              └── ServiceBusService      — concrete Azure SDK impl; also owns DlqSession
```

`App` takes a `Func<string, IServiceBusRepository>` factory (not the concrete type) so it can be constructed with a mock in tests without hitting Azure.

### Key design decisions

**`IServiceBusRepository` / `IDlqSession`** (`Services/IServiceBusRepository.cs`) are the seam everything else depends on. `ServiceBusService` and `DlqSession` (both in `Services/ServiceBusService.cs`) are the only classes that touch the Azure SDK directly.

**`DlqSession`** holds the `ServiceBusReceiver` that locked the batch. Complete/Abandon/RenewLock must go through the same session that received the messages — don't create new receivers for those operations.

**Seeder flow** (`SeedDlqAsync`): sends N messages to the live entity, then receives and calls `DeadLetterMessageAsync` immediately — no waiting for MaxDeliveryCount. For topic targets, only the chosen subscription is dead-lettered; other subscriptions keep the message.

**Lock renewal**: `App` starts a background `RenewLockLoopAsync` (30 s interval) while the user is in the message-detail screen. It is cancelled via `CancellationTokenSource` in the `finally` block.

**No interactive prompts inside `AnsiConsole.Status()`** — all data loading happens inside `Status().StartAsync()`; errors are captured to a local variable and surfaced as prompts after the spinner completes.

### Helpers (pure, no Azure dependency)

- `NamingHelper.NormalizeNamespace` — appends `.servicebus.windows.net` if input has no dot
- `PayloadTemplate.Expand` — replaces `{index}`, `{timestamp}`, `{guid}` in seed payload strings
- `JsonHelper.TryFormat` / `IsValid` — pretty-print or validate JSON strings

These have no external dependencies and are the primary unit-test targets.

### Testing approach

`Reviver.Tests` tests pure helpers directly and uses `NSubstitute` mocks of `IServiceBusRepository` for contract/orchestration tests. `ServiceBusReceivedMessage` is a sealed Azure SDK class — use `ServiceBusModelFactory.ServiceBusReceivedMessage(...)` to create test instances.

When verifying calls on a mock that mix concrete argument values with `Arg.Any<>()` matchers, NSubstitute requires ALL arguments to use matchers (use `Arg.Is()` for concrete values). If that still behaves unexpectedly with optional `CancellationToken` params, inspect `_repo.ReceivedCalls()` directly instead.

Global usings (`Reviver.Tests/GlobalUsings.cs`) pull in `Xunit` and `NSubstitute` for all test files.
