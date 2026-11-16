---
description: Reopen desktop apps saved in a workspace profile
argument-hint: [profile]
allowed-tools: PowerShell, Bash
---
Run this command and show the user the result:

powershell -NoProfile -ExecutionPolicy Bypass -File "$HOME/.claude/scripts/workspace.ps1" restore "$ARGUMENTS"

If $ARGUMENTS is empty, the profile defaults to "default". To see saved profiles, run the same script with `list`.
