param(
    [Parameter(Position = 0)][ValidateSet('save', 'restore', 'list')][string]$Action = 'list',
    [Parameter(Position = 1)][string]$ProfileName = 'default'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProfileName)) { $ProfileName = 'default' }
$ProfileName = $ProfileName.Trim()
if ($ProfileName -notmatch '^[\w\-\.]+$') { throw "Invalid profile name '$ProfileName' (use letters, digits, - _ .)" }

$dir = Join-Path $HOME '.claude\workspaces'
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
$file = Join-Path $dir "$ProfileName.json"

$noise = @(
    'TextInputHost', 'ApplicationFrameHost', 'SystemSettings', 'ShellExperienceHost',
    'StartMenuExperienceHost', 'SearchHost', 'SearchApp', 'LockApp', 'explorer',
    'NVIDIA Overlay', 'GameBar', 'GameBarFTServer', 'Widgets', 'WidgetBoard',
    'SecurityHealthSystray', 'CompPkgSrv', 'dwm', 'ctfmon', 'powershell', 'pwsh',
    'WindowsTerminal', 'OpenConsole', 'conhost', 'RuntimeBroker', 'PhoneExperienceHost'
)

function Get-OpenApps {
    $seen = @{}
    Get-Process | Where-Object {
        $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -and $noise -notcontains $_.ProcessName
    } | ForEach-Object {
        $path = $null
        try { $path = $_.Path } catch {}
        if (-not $path -or -not (Test-Path $path)) { return }
        if ($path -like "$env:WINDIR\*") { return }
        $key = $path.ToLower()
        if ($seen.ContainsKey($key)) { return }
        $seen[$key] = $true
        [pscustomobject]@{
            Name  = $_.ProcessName
            Title = $_.MainWindowTitle
            Path  = $path
        }
    }
}

switch ($Action) {
    'save' {
        $apps = @(Get-OpenApps)
        $data = [pscustomobject]@{
            Profile = $ProfileName
            SavedAt = (Get-Date).ToString('s')
            Apps    = $apps
        }
        $data | ConvertTo-Json -Depth 4 | Out-File -Encoding utf8 $file
        Write-Output "Saved $($apps.Count) app(s) to profile '$ProfileName' ($file):"
        $apps | Format-Table Name, Title -AutoSize | Out-String -Width 200
    }
    'restore' {
        if (-not (Test-Path $file)) { throw "Profile '$ProfileName' not found at $file" }
        $data = Get-Content -Raw $file | ConvertFrom-Json
        $running = @{}
        Get-Process | ForEach-Object { try { if ($_.Path) { $running[$_.Path.ToLower()] = $true } } catch {} }
        foreach ($app in @($data.Apps)) {
            if ($running.ContainsKey($app.Path.ToLower())) {
                Write-Output "SKIP     $($app.Name) (already running)"
            } elseif (-not (Test-Path $app.Path)) {
                Write-Output "MISSING  $($app.Name) ($($app.Path))"
            } else {
                try {
                    Start-Process -FilePath $app.Path -WorkingDirectory (Split-Path $app.Path)
                    Write-Output "LAUNCH   $($app.Name)"
                } catch { Write-Output "FAILED   $($app.Name): $($_.Exception.Message)" }
            }
        }
    }
    'list' {
        $files = Get-ChildItem $dir -Filter *.json -ErrorAction SilentlyContinue
        if (-not $files) { Write-Output "No saved profiles in $dir"; break }
        foreach ($f in $files) {
            $d = Get-Content -Raw $f.FullName | ConvertFrom-Json
            Write-Output ("{0,-15} {1,3} app(s)  saved {2}" -f $f.BaseName, @($d.Apps).Count, $d.SavedAt)
        }
    }
}

