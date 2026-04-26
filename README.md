# Reviver

**Interactive TUI for triaging and reprocessing Azure Service Bus dead-letter queues.**

<!-- README last reviewed: 2026-04-26 -->

Reviver connects to an Azure Service Bus namespace, lists every queue and topic subscription that has dead-lettered messages, and lets you inspect, edit, and resend them — or seed a DLQ for load/failure testing.

---

## Features

- Browse all queues and topic subscriptions with DLQ message counts in a single view
- Inspect message metadata, body (auto-formatted if JSON), and application properties
- Edit the message body in your `$EDITOR` before resending
- Add, edit, or remove application properties in-place
- Send to any queue or topic — with the choice to remove from DLQ or keep it there
- Automatic lock renewal every 25 seconds while you work so locks never expire mid-edit
- Seed a DLQ with synthetic messages for testing — usable from the TUI or as a headless CLI command
- Threshold-coloured DLQ counts: yellow (1–5), orange (6–20), red (21+)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Azure CLI — run `az login` before starting; Reviver authenticates with your CLI credential

## Quick start

```bash
az login
dotnet run --project Reviver.Console/Reviver.Console.csproj
```

You'll be prompted for a namespace name or FQDN. Short names are expanded automatically:

```
my-namespace  →  my-namespace.servicebus.windows.net
```

## Usage

### Interactive TUI

```bash
# Launch and prompt for namespace
dotnet run --project Reviver.Console/Reviver.Console.csproj

# Pre-fill namespace, skip the prompt
dotnet run --project Reviver.Console/Reviver.Console.csproj -- -n my-namespace
```

The TUI shows a table of all entities that have DLQ messages. Select one to receive a batch (up to 20), then pick a message to inspect. From the message detail screen you can:

| Action | Effect |
|---|---|
| Edit Body | Opens `$EDITOR` (Notepad on Windows, `nano` elsewhere); validates JSON on save |
| Edit Application Properties | Add, edit, or remove key/value pairs |
| Send to Destination | Sends the (optionally modified) message; choose to remove from DLQ or keep it |
| Discard | Completes the message without resending — permanently removes it from the DLQ |
| Skip | Abandons the lock; message returns to the DLQ |

### Seed command (headless)

```bash
# Seed 50 messages into the "orders" queue DLQ
dotnet run --project Reviver.Console/Reviver.Console.csproj -- \
  seed orders -n my-namespace -c 50 -r "LoadTest"

# Seed into a topic subscription
dotnet run --project Reviver.Console/Reviver.Console.csproj -- \
  seed events/payments -n my-namespace -c 10

# Custom payload template
dotnet run --project Reviver.Console/Reviver.Console.csproj -- \
  seed orders -n my-namespace -p '{"id":"{guid}","seq":{index}}'
```

Payload template placeholders: `{index}`, `{timestamp}`, `{guid}`.

### Version

```bash
dotnet run --project Reviver.Console/Reviver.Console.csproj -- version
```

## Configuration

| Name | Default | Description |
|---|---|---|
| `AZURE_SERVICEBUS_NAMESPACE` | — | Namespace short name or FQDN. Skips the startup prompt when set. |
| `EDITOR` | `notepad.exe` (Windows) / `nano` | Editor launched for body editing. |

## Architecture

Two projects in one solution:

```
StoneFlyLabs.Reviver.sln
├── Reviver.Console/   — app (assembly: reviver)
│   ├── UI/App.cs      — all TUI screens and user interaction
│   ├── Commands/      — CliCommands (Cocona), SeederFlow (TUI seed wizard)
│   ├── Services/      — IServiceBusRepository, ServiceBusService, DlqSession
│   ├── Helpers/       — NamingHelper, PayloadTemplate, JsonHelper (pure, no Azure dep)
│   └── Models/        — EntityInfo, DlqMessage
└── Reviver.Tests/     — xUnit + NSubstitute unit/contract tests
```

`App` receives a `Func<string, IServiceBusRepository>` factory so the Azure layer is fully swappable in tests. `DlqSession` holds the `ServiceBusReceiver` that owns the batch lock — complete/abandon/renew all go through the same session object.

## Development

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run a specific test class
dotnet test Reviver.Tests/Reviver.Tests.csproj --filter "FullyQualifiedName~NamingHelperTests"

# Run a single test
dotnet test Reviver.Tests/Reviver.Tests.csproj \
  --filter "FullyQualifiedName~NamingHelperTests.NormalizeNamespace_ReturnsExpectedFqdn"
```

Tests cover the pure helpers directly and use `NSubstitute` mocks of `IServiceBusRepository` for orchestration tests. `ServiceBusReceivedMessage` is a sealed Azure SDK type — create test instances via `ServiceBusModelFactory.ServiceBusReceivedMessage(...)`.
