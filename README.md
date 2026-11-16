# Workspace Manager

A small Windows 11 tray app that remembers which desktop apps you had open and offers to reopen them when you sign in.

## Features

- **Tray flyout**: left-click the tray icon to open a Windows 11–style panel (acrylic, rounded corners, your accent color).
  - *Saved apps* / *Open now* tabs with app icons and a "Running" badge.
  - **Save** the checked open apps to a profile, **Reopen** the checked saved apps.
  - Create and delete profiles (deleted profiles go to the Recycle Bin).
  - Double-click an app under *Open now* to jump to its window.
- **Right-click menu**: save to / reopen from any profile, toggle *Start with Windows*, exit.
- **Sign-in prompt**: "Welcome back" window listing the apps from last time; untick what you don't want, then *Reopen* or *Not now*.
- **Automatic "Last session" profile**: refreshed every 2 minutes and on shutdown.
- Store apps are relaunched through their AppUserModelID.

## Build and install

Requires the .NET 10 SDK on Windows.

```powershell
dotnet publish -c Release -o "$env:LOCALAPPDATA\Programs\WorkspaceManager"
& "$env:LOCALAPPDATA\Programs\WorkspaceManager\WorkspaceManager.exe"
```

On first run it registers itself under `HKCU\...\Run` (with `--startup`) so it starts with Windows. You can turn that off in Settings.

## Files

| What | Where |
|---|---|
| Profiles | `%USERPROFILE%\.claude\workspaces\<profile>.json` |
| Settings | `%APPDATA%\WorkspaceManager\settings.json` |
| Error log | `%APPDATA%\WorkspaceManager\error.log` |

Profiles use the same JSON format as the companion PowerShell script (`workspace.ps1 save|restore|list [profile]`), so both can read each other's saves.

## Limitations

- Reopens programs, not their contents (e.g. browser tabs are not restored).
- Apps under `C:\Windows` and background shell processes are skipped when saving.
