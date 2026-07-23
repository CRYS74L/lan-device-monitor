$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$projectPath = Join-Path $projectRoot 'src\LanDeviceMonitor.App\LanDeviceMonitor.App.csproj'
$projectXml = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $projectPath)
$applicationName = [string]$projectXml.Project.PropertyGroup.AssemblyName
$outputDirectory = Join-Path (Join-Path $projectRoot 'artifacts') $applicationName

$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

if (Test-Path -LiteralPath $localDotnet) {
    $env:DOTNET_ROOT = Split-Path -Parent $localDotnet
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}

dotnet publish $projectPath `
    --configuration Release `
    --no-restore `
    --output $outputDirectory `
    -p:UseSharedCompilation=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Output "Published to: $outputDirectory"
