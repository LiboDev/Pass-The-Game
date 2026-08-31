# Claude Code Stop hook: refresh the running Unity editor, wait for compilation, report Console errors.
# Talks to Assets/Editor/ClaudeBridge.cs through Library/ClaudeBridge/.
#   Hook mode : reads the hook JSON on stdin; exit 2 + stderr blocks the stop while errors remain (capped).
#   -Manual   : plain command for mid-task checks. Exit 0 = clean, 1 = errors, 3 = editor/bridge unavailable.
param([switch]$Manual)

$ErrorActionPreference = 'Stop'
$project      = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$bridgeDir    = Join-Path $project 'Library\ClaudeBridge'
$request      = Join-Path $bridgeDir 'refresh.request'
$result       = Join-Path $bridgeDir 'result.json'
$stateFile    = Join-Path $bridgeDir 'hook-state.json'
$timeoutSec   = 150   # stay under the hook timeout in .claude/settings.json
$pokeAfterSec = 15    # request untouched this long => bridge not loaded; focus Unity so Auto Refresh imports it
$maxAttempts  = 3     # blocked stops allowed on an unchanged set of errors

$hook = $null
if (-not $Manual) {
    try { $raw = [Console]::In.ReadToEnd(); if ($raw) { $hook = $raw | ConvertFrom-Json } } catch { }
}

function Skip($msg) {
    if ($Manual) { Write-Output "Unity refresh unavailable: $msg"; exit 3 }
    Write-Output "Unity refresh skipped: $msg"
    exit 0
}

# --- is the editor open on this project? ---------------------------------------------------------
if (-not (Test-Path (Join-Path $project 'Temp\UnityLockfile'))) { Skip 'Unity editor is not running for this project.' }
$projectName = Split-Path $project -Leaf
$windows = @(Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 })
$editor = $windows | Where-Object { $_.MainWindowTitle -like "*$projectName*" } | Select-Object -First 1
if (-not $editor) { $editor = $windows | Select-Object -First 1 }
if (-not $editor) { Skip 'no Unity editor window found (stale Temp/UnityLockfile?).' }

# --- ask the bridge for a refresh and wait for result.json ---------------------------------------
New-Item -ItemType Directory -Force -Path $bridgeDir | Out-Null
Remove-Item $result -Force -ErrorAction SilentlyContinue
Set-Content -Path $request -Value 'refresh' -Encoding ascii

$sw = [Diagnostics.Stopwatch]::StartNew()
$poked = $false
while (-not (Test-Path $result)) {
    if ($sw.Elapsed.TotalSeconds -gt $timeoutSec) {
        Remove-Item $request -Force -ErrorAction SilentlyContinue
        Skip "bridge did not answer within ${timeoutSec}s. Is Assets/Editor/ClaudeBridge.cs imported and compiling? Is a modal dialog open in Unity?"
    }
    if (-not $poked -and $sw.Elapsed.TotalSeconds -gt $pokeAfterSec -and (Test-Path $request)) {
        $poked = $true   # never picked up => bridge probably not imported yet; Auto Refresh runs when Unity gains focus
        try { (New-Object -ComObject WScript.Shell).AppActivate($editor.Id) | Out-Null } catch { }
    }
    Start-Sleep -Milliseconds 250
}

$r = $null
foreach ($i in 1..10) {
    try { $r = Get-Content $result -Raw -ErrorAction Stop | ConvertFrom-Json; break } catch { Start-Sleep -Milliseconds 100 }
}
if (-not $r) { Skip 'result.json could not be read.' }

# --- summarise -----------------------------------------------------------------------------------
$errList = @($r.errors)
$lines = @()
if ($r.note) { $lines += "Note: $($r.note)" }
$lines += "Unity refresh: refreshed=$($r.refreshed) recompiled=$($r.recompiled) errors=$($r.errorCount) warnings=$($r.warningCount)"
if ($errList.Count -gt 0) { $lines += ($errList | ForEach-Object { "  - $_" }) }
$summary = $lines -join "`n"

if ([int]$r.errorCount -eq 0) {
    Remove-Item $stateFile -Force -ErrorAction SilentlyContinue
    Write-Output $summary
    exit 0
}
if ($Manual) { Write-Output $summary; exit 1 }

# --- block the stop so Claude fixes the errors; cap retries on an unchanged error set ------------
$sha  = [Security.Cryptography.SHA1]::Create()
$hash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($errList -join "`n"))))) -replace '-', ''
$session = ''
if ($hook -and $hook.session_id) { $session = [string]$hook.session_id }
$attempts = 0
if (Test-Path $stateFile) {
    try {
        $s = Get-Content $stateFile -Raw | ConvertFrom-Json
        if ($s.hash -eq $hash -and $s.session -eq $session) { $attempts = [int]$s.attempts }
    } catch { }
}
if ($attempts -ge $maxAttempts) {
    @{ systemMessage = "Unity Console still has $($r.errorCount) error(s) after $attempts fix attempts:`n$summary" } | ConvertTo-Json -Compress | Write-Output
    exit 0
}
@{ hash = $hash; session = $session; attempts = $attempts + 1 } | ConvertTo-Json -Compress | Set-Content -Path $stateFile -Encoding ascii
[Console]::Error.WriteLine("The Unity editor was refreshed and its Console shows $($r.errorCount) error(s) (fix attempt $($attempts + 1) of $maxAttempts). Fix them, then finish again.`n$summary")
exit 2
