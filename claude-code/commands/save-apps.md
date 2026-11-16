---
description: Save currently open desktop apps to a workspace profile
argument-hint: [profile]
allowed-tools: PowerShell, Bash
---
Run this command and show the user the result:

powershell -NoProfile -ExecutionPolicy Bypass -File "$HOME/.claude/scripts/workspace.ps1" save "$ARGUMENTS"

If $ARGUMENTS is empty, the profile defaults to "default".
