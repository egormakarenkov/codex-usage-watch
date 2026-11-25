# Verification - November 9, 2026

- Native Windows .NET Framework executable compiled successfully using build.ps1.
- 19 behavioral checks passed, covering threshold crossing, duplicate protection across restarts, per-channel delivery, small low-band usage corrections, banked-reset and weekly rearming, primary/secondary weekly fields, core bucket preference, missing/invalid data, adaptive polling, error backoff, and private-chat pairing challenges.
- The small-correction duplicate-alert regression was reproduced by a failing test before fixing the rearming condition; the full suite then passed.
- Read-only live app-server probe returned a valid 10,080-minute weekly window. The executable probe reported 97% remaining at that point and verified Windows DPAPI credential round-trip without any real bot credential.
- Native setup window inspected at the actual Windows display scale: controls were visible, text wrapped, and the current weekly value updated (96% remaining during UI verification).
- Send test alert was invoked with no Telegram credentials configured. The UI reported “Windows test sent. Link Telegram above to test your phone.” This verifies the local notification/sound code path was invoked, not physical sound audibility or OS notification display outside the window.
- Per-user install and Startup shortcut verified. The installed executable matches the deliverable SHA-256. The process is responsive and owns its dedicated Codex app-server child process.
- Telegram end-to-end delivery is pending the user's local token entry, private pairing code, and Send test alert. No Telegram credential was requested in chat. No Telegram message has been sent during verification.
- Backend freshness and smartphone notification delivery are external to the tool; five-second low-band polling is not an end-to-end delivery guarantee. Windows sign-in startup was inspected, not reboot-tested.

The review scope is the complete newly created CodexUsageWatch directory. There was no existing project or Git baseline in this workspace; no commits, publication, Codex configuration changes, or banked-reset consumption were performed.
