$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$projectPath = Join-Path $projectRoot 'src\LanDeviceMonitor.App\LanDeviceMonitor.App.csproj'
$projectXml = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $projectPath)
$applicationName = [string]$projectXml.Project.PropertyGroup.AssemblyName
$artifactsDirectory = Join-Path $projectRoot 'artifacts'
$outputDirectory = Join-Path $artifactsDirectory $applicationName
$runtimeIdentifier = 'win-x64'

$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

if (Test-Path -LiteralPath $localDotnet) {
    $env:DOTNET_ROOT = Split-Path -Parent $localDotnet
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}

dotnet restore $projectPath `
    --runtime $runtimeIdentifier `
    --ignore-failed-sources `
    --disable-parallel `
    -p:NuGetAudit=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

if (Test-Path -LiteralPath $outputDirectory) {
    $resolvedArtifacts = [System.IO.Path]::GetFullPath($artifactsDirectory)
    $resolvedOutput = [System.IO.Path]::GetFullPath($outputDirectory)
    if (-not $resolvedOutput.StartsWith(
        $resolvedArtifacts + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean publish directory outside artifacts: $resolvedOutput"
    }

    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

dotnet publish $projectPath `
    --configuration Release `
    --runtime $runtimeIdentifier `
    --self-contained true `
    --no-restore `
    --output $outputDirectory `
    -p:UseSharedCompilation=false `
    -p:WindowsAppSDKSelfContained=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$requiredRuntimeFiles = @(
    'hostfxr.dll',
    'hostpolicy.dll',
    'coreclr.dll',
    'System.Private.CoreLib.dll'
)

foreach ($requiredRuntimeFile in $requiredRuntimeFiles) {
    $requiredPath = Join-Path $outputDirectory $requiredRuntimeFile
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Self-contained publish is missing required runtime file: $requiredRuntimeFile"
    }
}

Write-Output "Published self-contained $runtimeIdentifier app to: $outputDirectory"
