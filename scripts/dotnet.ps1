# Keep SDK state and caches inside this checkout. No persistent environment changes.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$savedEnvironment = @{}
$taskEnvironment = @{
    DOTNET_CLI_HOME = (Join-Path $projectRoot '.tools/cli-home')
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    DOTNET_NOLOGO = '1'
    NUGET_PACKAGES = (Join-Path $projectRoot '.nuget/packages')
    NUGET_HTTP_CACHE_PATH = (Join-Path $projectRoot '.nuget/http-cache')
    NUGET_PLUGINS_CACHE_PATH = (Join-Path $projectRoot '.nuget/plugins-cache')
    TEMP = (Join-Path $projectRoot '.tools/temp')
    TMP = (Join-Path $projectRoot '.tools/temp')
}
if (Test-Path -LiteralPath $localDotnet) {
    $taskEnvironment.DOTNET_ROOT = Split-Path -Parent $localDotnet
}
New-Item -ItemType Directory -Force -Path $taskEnvironment.TEMP | Out-Null
try {
    foreach ($name in $taskEnvironment.Keys) {
        $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $taskEnvironment[$name], 'Process')
    }
    & $dotnetCommand @args
    $commandExitCode = $LASTEXITCODE
}
finally {
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
    }
}
exit $commandExitCode
