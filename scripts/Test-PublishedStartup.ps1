param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This smoke test requires Windows.' }
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$startedAt = Get-Date
$applicationProcess = Start-Process -FilePath $executablePath -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        $applicationProcess.Refresh()
        if ($applicationProcess.HasExited) {
            throw "Application exited before showing its configuration window (code $($applicationProcess.ExitCode))."
        }
        if ($applicationProcess.MainWindowHandle -ne [IntPtr]::Zero -and
            $applicationProcess.MainWindowTitle -eq 'Duel Companion · Configuration') {
            $ready = $applicationProcess.WaitForInputIdle(5000)
            if ($ready) { break }
        }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) { throw 'Configuration window did not become ready before the startup timeout.' }

    # Catch delayed dispatcher/layout failures after the first visible window.
    Start-Sleep -Seconds 2
    $applicationProcess.Refresh()
    if ($applicationProcess.HasExited -or $applicationProcess.MainWindowHandle -eq [IntPtr]::Zero -or
        -not $applicationProcess.Responding) {
        throw 'Application failed after creating its configuration window.'
    }
    Write-Host 'Published executable started successfully; configuration window is visible and responsive.'
    if (-not $applicationProcess.CloseMainWindow() -or -not $applicationProcess.WaitForExit(10000)) {
        throw 'Application did not close cleanly.'
    }
    if ($applicationProcess.ExitCode -ne 0) { throw "Application closed with code $($applicationProcess.ExitCode)." }
    Write-Host 'Published executable closed cleanly with exit code 0.'
}
catch {
    $failure = $_
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = '.NET Runtime'; StartTime = $startedAt } -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -like '*YuGiOhOverlay.UI.exe*' } |
        Select-Object -First 1 -ExpandProperty Message |
        ForEach-Object { Write-Host $_ }
    throw $failure
}
finally {
    $applicationProcess.Refresh()
    if (-not $applicationProcess.HasExited) {
        $applicationProcess.Kill()
        $applicationProcess.WaitForExit(5000) | Out-Null
    }
    $applicationProcess.Dispose()
}
