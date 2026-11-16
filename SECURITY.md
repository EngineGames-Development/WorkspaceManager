# Security

## Reporting a vulnerability

Please **don't open a public issue** for security problems. Use GitHub's
[private vulnerability reporting](../../security/advisories/new) for this repository instead.
I'll reply as soon as I can and credit you in the fix unless you'd rather stay anonymous.

Only the latest release is supported.

## Security review (v1.0.0)

A manual review of the whole codebase. It covers how the app runs, what data it stores, what it launches, and how that input is checked.

### Profile

| Property | Status |
|---|---|
| Runs as | Current user, **no elevation**, no services, no drivers |
| Network access | **None.** The app makes no network connections and has no telemetry. |
| Third-party packages | **None.** It uses only the .NET 10 / Windows Desktop base libraries. |
| Persistence | One `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value. Optional, can be removed from the UI. |
| Files written | `%USERPROFILE%\.claude\workspaces\*.json`, `%APPDATA%\WorkspaceManager\settings.json`, `error.log` |
| Process access | `PROCESS_QUERY_LIMITED_INFORMATION` only (reads the image path and AUMID). It never reads memory or injects into other processes. |

### Trust boundary

Profiles are plain JSON files in your user folder, and **reopening a profile starts the programs listed in it.**
Anything that can write to your profile folder can already run code as you, so this doesn't give an attacker more power. It does mean a profile file from someone else should be treated like an executable.

### Findings and fixes

| # | Finding | Severity | Status |
|---|---|---|---|
| 1 | **Argument injection through `Aumid`.** Store apps are started as `explorer.exe shell:AppsFolder\<Aumid>`. A crafted profile could put spaces or quotes in `Aumid` to pass extra arguments to Explorer. | Low | **Fixed.** The AUMID must match `^[\w.-]+_[a-z0-9]+![\w.-]+$` both when loading and when launching. |
| 2 | **Non-executable or relative paths in profiles.** A profile could point to a document, a URL handler or a relative path that `ShellExecute` resolves against the working directory. | Low | **Fixed.** Only fully qualified paths ending in `.exe` are loaded or launched. |
| 3 | **Profile name used as a file name.** The names come from the UI and from `settings.json` (`StartupProfile`, `LastProfile`). | Low | **Fixed.** Names must match `^\w[\w.-]*$` (64 characters max, no separators, no leading dot), and `Load()` checks this too. |
| 4 | **Display name can be spoofed.** A crafted profile can show the name *Google Chrome* for a different `.exe`. | Info | **Mitigated.** Hover over any app in the lists to see its full path. |
| 5 | **Window titles stored in plain text.** Titles can include document names or page titles. They are saved unencrypted in your profile folder. | Info | Accepted. Only your user account (and admins) can read the files. Turn off *Remember my apps automatically* if you don't want the automatic *Last session* file. |
| 6 | **Single-instance objects** (`Local\WorkspaceManager.Singleton` mutex and `.Show` event). Another program in the same session could take them first to block startup, or signal the flyout to open. | Info | Accepted. These are session-local and only reachable by code already running as you. |
| 7 | **Run key quoting.** An unquoted path with spaces could launch the wrong program. | — | Not affected. The path is quoted: `"…\WorkspaceManager.exe" --startup`. |
| 8 | **JSON parsing.** | — | Not affected. Uses `System.Text.Json` (`JsonDocument` / POCO) with no type-name handling or polymorphic deserialization. |
| 9 | **Deleting profiles.** | — | Not affected. Files go to the Recycle Bin via `FileSystem.DeleteFile(..., SendToRecycleBin)`, and only after an inline confirmation. |

### Hardening ideas (not implemented)

- Sign release binaries with Authenticode.
- Optionally warn before reopening an `.exe` from a user-writable location (`%TEMP%`, `Downloads`).
