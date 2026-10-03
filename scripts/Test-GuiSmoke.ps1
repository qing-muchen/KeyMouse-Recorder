# Run with Windows PowerShell 5.1 to use the Windows UI Automation assemblies.
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $projectRoot "src/MacroRecorder.App/bin/$Configuration/net10.0-windows/win-x64"
$applicationDll = Join-Path $outputDirectory 'MacroRecorder.App.dll'
$localDotnet = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
if (-not (Test-Path -LiteralPath $applicationDll)) { throw 'Build the WPF application before the smoke test.' }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$logDirectory = Join-Path $outputDirectory 'data/logs'
$existingLogs = @(Get-ChildItem -LiteralPath $logDirectory -Filter '*.jsonl' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
$applicationProcess = Start-Process -FilePath $dotnetCommand -ArgumentList @('"' + $applicationDll + '"') -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $statusElement = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        $applicationProcess.Refresh()
        if ($applicationProcess.HasExited) { throw "Application exited early: $($applicationProcess.ExitCode)" }
        if ($applicationProcess.MainWindowHandle -ne [IntPtr]::Zero) {
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($applicationProcess.MainWindowHandle)
            $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'ApplicationStatus')
            $statusElement = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
            if ($null -ne $statusElement) { break }
        }
        Start-Sleep -Milliseconds 100
    }
    if ($null -eq $statusElement) { throw 'The WPF status element did not appear.' }
    if ($statusElement.Current.Name -notlike 'Idle*') { throw 'The application did not start in Idle.' }
    Write-Output "PASS: WPF window and bound status: $($statusElement.Current.Name)"
    foreach ($name in @('macros', 'config', 'logs')) {
        if (-not (Test-Path -LiteralPath (Join-Path $outputDirectory "data/$name") -PathType Container)) { throw "Missing data directory: $name" }
    }
    if (-not $applicationProcess.CloseMainWindow()) { throw 'Could not request a normal window close.' }
    if (-not $applicationProcess.WaitForExit(10000)) { throw 'Application did not exit after closing its window.' }
    if ($applicationProcess.ExitCode -ne 0) { throw "Application exited with code $($applicationProcess.ExitCode)." }

    $newLogs = @(Get-ChildItem -LiteralPath $logDirectory -Filter '*.jsonl' | Where-Object { $_.FullName -notin $existingLogs })
    if ($newLogs.Count -ne 1) { throw 'Expected one session log for this application instance.' }
    $entries = @(Get-Content -LiteralPath $newLogs[0].FullName | ForEach-Object { $_ | ConvertFrom-Json })
    if ($entries.Count -ne 2 -or $entries[0].eventId -ne 'ApplicationStarted' -or $entries[1].eventId -ne 'ApplicationStopped') {
        throw 'The startup/shutdown log sequence did not match.'
    }
    Write-Output 'PASS: local directories, startup/shutdown logs, normal close and exit code 0.'
}
finally {
    if (-not $applicationProcess.HasExited) {
        Stop-Process -Id $applicationProcess.Id -Force -ErrorAction Stop
        Write-Warning 'Smoke test cleanup terminated its own application process after a failure.'
    }
    $applicationProcess.Dispose()
}
