param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Intermediate', 'Release')]
    [string]$Mode
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $scriptDir 'ZapretManager.csproj'
$runtime = 'win-x64'
$buildStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$tempBuildRoot = Join-Path $env:TEMP "ZapretManagerBuild\$Mode\$buildStamp"
$publishDir = Join-Path $tempBuildRoot 'publish'

if (-not (Test-Path -LiteralPath $projectFile)) {
    throw "Project file not found: $projectFile"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK was not found in PATH.'
}

[xml]$projectXml = Get-Content -LiteralPath $projectFile
$propertyGroup = @($projectXml.Project.PropertyGroup) | Where-Object { $_.DisplayVersion } | Select-Object -First 1
$displayVersion = $propertyGroup.DisplayVersion
if ([string]::IsNullOrWhiteSpace($displayVersion)) {
    throw 'DisplayVersion not found in ZapretManager.csproj.'
}

$intermediateSuffix = -join ([char[]](0x041F, 0x0440, 0x043E, 0x043C, 0x0435, 0x0436, 0x0443, 0x0442, 0x043E, 0x0447, 0x043D, 0x0430, 0x044F, 0x0020, 0x0432, 0x0435, 0x0440, 0x0441, 0x0438, 0x044F))

if ($Mode -eq 'Intermediate') {
    $targetRoot = Join-Path $scriptDir 'IntermediateBuilds'
    $folderName = "ZapretManager $displayVersion $intermediateSuffix"
} else {
    $targetRoot = Join-Path $scriptDir 'Release'
    $folderName = "ZapretManager $displayVersion"
}

$targetDir = Join-Path $targetRoot $folderName
$targetExe = Join-Path $targetDir 'ZapretManager.exe'

try {
    Write-Host ''
    Write-Host "[1/4] Publishing portable single-file build $displayVersion..."
    dotnet publish $projectFile -c Release -r $runtime --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet publish failed.'
    }

    Write-Host ''
    Write-Host "[2/4] Preparing $Mode folder..."
    New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
    if (Test-Path -LiteralPath $targetDir) {
        Remove-Item -LiteralPath $targetDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $publishDir 'ZapretManager.exe') -Destination $targetExe -Force

    Write-Host ''
    Write-Host "[3/4] Build files:"
    Get-ChildItem -LiteralPath $targetDir -File |
        Sort-Object Name |
        Select-Object Name, @{ Name = 'SizeMB'; Expression = { [math]::Round($_.Length / 1MB, 2) } } |
        Format-Table -AutoSize

    Write-Host ''
    Write-Host "[4/4] Build ready:"
    Write-Host $targetDir
}
finally {
    if (Test-Path -LiteralPath $tempBuildRoot) {
        Remove-Item -LiteralPath $tempBuildRoot -Recurse -Force
    }
}
