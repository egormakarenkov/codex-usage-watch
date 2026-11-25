# Codex Usage Watch

Approved in chat on November 9, 2026: a Windows background monitor with a tray icon, Windows notification and sound, Telegram alert at 2% or less weekly allowance remaining, adaptive polling (five seconds near the threshold), persistent duplicate suppression and rearming after reset, startup at Windows sign-in, and a test button. No automatic resets or task interruption.

Implementation: dependency-free C# / .NET Framework WinForms application. Read usage through a dedicated stdio Codex app-server using the current user's existing sign-in. Match the `codex` bucket and a 10,080-minute window in either primary or secondary. Poll every 30 seconds above 10% remaining, five seconds at or below 10%, and back off on errors. The app-server consumes no model turns. Backend freshness, sleep/offline periods, carrier-independent Telegram delivery and phone notification settings limit alert latency.

Persist bot credentials with Windows DPAPI for the current user; never log them. Pair only a private chat that sends an unpredictable, locally displayed `/start` code. Check for an existing webhook and refuse to disrupt it. Recommend a dedicated bot. No public listener, webhook server, hosting subscription, or Telegram Premium is needed. Secrets stay on this PC except HTTPS requests to Telegram. Usage alert text contains remaining allowance and reset time, not task content.

Alert state is persisted per weekly window and delivery channel. Crossing below the threshold triggers local notification/sound and Telegram independently. Repeated low readings or application restarts do not repeat successful alerts. A new window or recovery above 2% remaining rearms the monitor. Small usage corrections within the low band do not rearm it. Failures appear in the UI; Telegram errors retry with backoff. An uncertain send can cause a duplicate on retry because Telegram sendMessage has no caller idempotency key. Never silently classify missing or stale usage as healthy.

Delivery: local executable, source, tests, build and install scripts, setup guide. Install under LOCALAPPDATA/Programs/CodexUsageWatch and a per-user Startup shortcut. No changes to Codex configuration or binaries, commits, or publication.

References: https://learn.chatgpt.com/docs/app-server ; https://core.telegram.org/bots/api ; https://core.telegram.org/bots/features#botfather
