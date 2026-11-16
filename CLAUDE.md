# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Workspace Manager is a Windows 11 tray app (WPF on .NET 10). It saves the desktop apps you have open into named profiles and offers to reopen them when you sign in. The `claude-code/` folder holds a companion PowerShell script and the `/save-apps` and `/reopen-apps` slash commands, which read and write the same profile files.

## Commands

```powershell
# Build
dotnet build src/WorkspaceManager -c Release

# Install / redeploy. The running exe locks its files, so stop it first.
Stop-Process -Name WorkspaceManager -Force -ErrorAction SilentlyContinue
dotnet publish src/WorkspaceManager -c Release -o "$env:LOCALAPPDATA\Programs\WorkspaceManager"

# Run (no args: opens the flyout; --startup: quiet tray start plus the sign-in prompt)
& "$env:LOCALAPPDATA\Programs\WorkspaceManager\WorkspaceManager.exe"
```

There are no tests and no linter. To verify a change, publish, run the app and look at it. A screenshot cropped to the window rect works. The `DwmGetWindowAttribute` frame bounds are attribute 9, and from PowerShell `FindWindow` needs `[NullString]::Value` as the class name, not `$null`.

Unhandled exceptions don't crash the app. They are logged to `%APPDATA%\WorkspaceManager\error.log` and swallowed, so check that file whenever a window fails to appear.

CI (`.github/workflows/build.yml`) publishes on every push. Pushing a `v*` tag also creates a GitHub Release with a zip. Pushing changes to workflow files needs a token with the `workflow` scope (`gh auth refresh -s workflow`).

## Architecture

All source is in `src/WorkspaceManager/`.

- **`App.xaml.cs`** is the process host. It enforces a single instance with the `Local\WorkspaceManager.Singleton` mutex. A second launch sets the `Local\WorkspaceManager.Show` event, and the first instance responds by opening its flyout. It also owns:
  - the tray icon (WinForms `NotifyIcon`)
  - the right-click menu: a WPF `ContextMenu` opened from an off-screen 1×1 host window, so it gets Fluent styling and closes properly
  - the sign-in flow: `--startup` waits 3 s, then `ShowStartupPrompt()`
  - autosave of the `last-session` profile every 2 min and on `SessionEnding`. With `--startup`, autosave only starts after the prompt closes, so the snapshot isn't overwritten before the user answers.
- **`Workspace.cs`** holds the domain logic, with no UI.
  - Window detection: `EnumWindows`, keeping windows that are visible, titled, unowned and not tool windows. Store apps inside `ApplicationFrameHost` are resolved to the child window's process, and cloaked packaged windows are skipped. A noise list plus anything under `C:\Windows` is excluded.
  - Profile JSON load and save.
  - Launching: `ShellExecute` on the `.exe`, or `explorer.exe shell:AppsFolder\<AUMID>` for packaged apps.
- **`Support.cs`** holds the shared pieces:
  - `Settings`: JSON in `%APPDATA%`. `StartWithWindows` maps to the `HKCU\...\Run` value.
  - `Theme`: writes theme-aware `Wm*` brushes into `Application.Resources` at runtime and reads the accent from the registry `AccentPalette`.
  - `FluentWindow`: the base class for both windows. It makes them borderless with `WindowChrome` and sets the DWM backdrop (`Backdrop`: 2 = Mica, 3 = acrylic), rounded corners and dark mode.
  - `IconCache`, `AppItem` (the list row view model) and `ProfileItem`.
- **`FlyoutWindow`** is the tray panel, with a settings view in the same window. **`StartupPrompt`** is the "Welcome back" window. Both use the shared `AppItemTemplate` and styles in `Styles.xaml`.

## Gotchas

- **Resource key collisions crash the app at startup.** `Theme.Apply` puts brushes named `WmText`, `WmCard`, `WmAccent`… straight into `Application.Resources`, and they override merged-dictionary entries with the same key. Style keys must use different names, for example `WmCardStyle`.
- **WPF and WinForms are both referenced and `ImplicitUsings` is off.** Use the `Forms = System.Windows.Forms` alias, and never import `System.Windows.Forms` or `System.Drawing` in WPF files, or you get ambiguous types.
- **Fluent theme:** `ThemeMode="System"` is experimental (WPF0001 is suppressed in the csproj). It styles the stock controls (ComboBox, CheckBox, ScrollViewer, ContextMenu). Windows deriving from `FluentWindow` don't get the implicit `Window` style, which is intentional: their background and backdrop are set by hand.
- **The flyout hides itself on `Deactivated`.** Anything that steals focus closes it, including automated screenshots.
- **Profile format is shared with `claude-code/workspace.ps1`** and lives in `%USERPROFILE%\.claude\workspaces\<name>.json`.
  - Files are written as UTF-8 **with BOM** so Windows PowerShell 5.1 reads non-ASCII window titles correctly.
  - `Load()` tolerates `Apps` being a single object rather than an array.
  - `DisplayName` and `Aumid` are optional fields.
- **Security checks are documented in `SECURITY.md`. Keep the two in sync.**
  - Profile names must match `IsValidName` (`^\w[\w\-\.]*$`, at most 64 characters).
  - AUMIDs must match `IsValidAumid`.
  - Only fully qualified `.exe` paths are loaded or launched.
- **This clone sets `http.sslBackend=schannel`** in its local git config, because something on the machine intercepts TLS and git's OpenSSL backend rejects the certificate.
