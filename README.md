# Codex Usage Watch

**Status:** Alerting Utility (`v0.1.0` - unsigned Windows preview)

A lightweight Windows system-tray alerting utility that monitors your Codex weekly allowance and notifies you when **2% or less remains** via a Windows tray alert, system sound, and optional private Telegram bot message.

> **How it differs from [Codex Pacer](https://github.com/egormakarenkov/codex-pacer):** Codex Usage Watch is strictly an **alerting utility**. It never enforces hard limits, never blocks or closes the Codex desktop app, and never redeems resets.

## Telegram setup (local only)

No secrets are stored in this repository (`settings.example.json` shows the schema; real credentials are encrypted locally at `%LOCALAPPDATA%\CodexUsageWatch\settings.dat` via Windows DPAPI).

1. Open **@BotFather** in Telegram (`/newbot`) and copy the bot token.
2. Launch `CodexUsageWatch.exe` (via `install.ps1` or built with `build.ps1`), paste your token into the masked **Telegram bot token** field, and click **1. Prepare Telegram link** to copy your one-time `/start` pairing command.
3. Send that exact `/start` command in a private chat with your bot, then click **2. Finish linking** within 10 minutes.
4. Click **Send test alert** to verify phone delivery.

## Build & test from source

Uses the built-in Windows .NET Framework C# compiler (`csc.exe`) with zero external packages:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

`build.ps1` compiles and runs the 19 behavioral checks in `Tests.cs` and builds `CodexUsageWatch.exe`.

To install per-user Start menu and Startup shortcuts:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

## Validation & limitations

- **Automated tests:** **19/19** behavioral checks passed (`Tests.cs`).
- **Live checks:** Read-only `codex app-server --stdio` probe confirmed 10,080-minute weekly window reading and Windows DPAPI round-trip; UI inspected at native DPI; local Windows test alert invoked.
- **Unverified:** Live end-to-end Telegram delivery awaits a real BotFather token and pairing; Windows sign-in startup was inspected rather than reboot-tested.

See [VERIFICATION.md](VERIFICATION.md) and [DESIGN.md](DESIGN.md) for details.

## License

MIT - see [LICENSE](LICENSE).
