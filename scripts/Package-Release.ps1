param([string]$OutputDirectory = 'artifacts/releases')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content (Join-Path $projectRoot 'crt.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+([.-][A-Za-z0-9.-]+)?$') {
    throw 'Invalid project version.'
}
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot $OutputDirectory
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$releaseName = "CRT-Overlay-v$version-win-x64"
$zipPath = Join-Path $OutputDirectory "$releaseName.zip"
$hashPath = "$zipPath.sha256"
if ((Test-Path -LiteralPath $zipPath) -or (Test-Path -LiteralPath $hashPath)) {
    throw 'Release output already exists. Choose a different -OutputDirectory.'
}
$stagingRoot = Join-Path $projectRoot ('artifacts/release-staging/' + [guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $stagingRoot $releaseName
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Push-Location $projectRoot
try {
    $sdk = & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'Unable to locate the required .NET SDK.' }
    & dotnet publish crt.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o (Join-Path $packageRoot 'app')
    if ($LASTEXITCODE -ne 0) { throw 'Release publish failed. No archive was produced.' }
    $files = @('Start.bat', 'Start-GameBoy.bat', 'Start-MasterSystem.bat', 'Start-PSX.bat',
        'Start-Rolling.bat', 'Start-Sega.bat', 'Start-SNES.bat', 'Stop.bat', 'Editor.bat',
        'README.md', 'README.ru.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'CONTRIBUTING.md', 'CHANGELOG.md')
    foreach ($file in $files) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination (Join-Path $packageRoot $file)
    }
    $dataFiles = @(
        'presets/balanced.json', 'presets/gameboy.json', 'presets/mastersystem.json',
        'presets/megadrive-composite.json', 'presets/megadrive.json', 'presets/philips-a51eal155x.json',
        'presets/psx-composite.json', 'presets/psx.json', 'presets/rolling.json',
        'presets/snes.json', 'presets/soft.json', 'presets/strong.json',
        'Profiles/philips-a51eal155x.json',
        'docs/ARCHITECTURE.md', 'docs/BUILDING.md', 'docs/CRT-RESEARCH.md', 'docs/PARAMETERS.md',
        'docs/ru/ARCHITECTURE.md', 'docs/ru/BUILDING.md', 'docs/ru/CHANGELOG.md',
        'docs/ru/CONTRIBUTING.md', 'docs/ru/CRT-RESEARCH.md', 'docs/ru/PARAMETERS.md',
        'licenses/ILLink-MIT.txt', 'licenses/ILLink-NOTICES.txt', 'licenses/SharpGen-MIT.txt',
        'licenses/System.Text.Json-MIT.txt', 'licenses/System.Text.Json-NOTICES.txt',
        'licenses/Vortice.Mathematics-MIT.txt', 'licenses/Vortice.Windows-MIT.txt'
    )
    foreach ($file in $dataFiles) {
        $destination = Join-Path $packageRoot $file
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $destination
    }
    [ordered]@{
        product = 'CRT Overlay'
        version = $version
        sdk = ([string]$sdk).Trim()
        configuration = 'Release'
        runtimeIdentifier = 'win-x64'
        selfContained = $false
        requiredRuntime = '.NET Runtime 9 x64'
        builtUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageRoot 'BUILD-INFO.json') -Encoding UTF8
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $releaseName.zip" | Set-Content -LiteralPath $hashPath -Encoding ASCII
    Write-Output "Release: $zipPath"
    Write-Output "SHA256:  $hashPath"
    Write-Output "Staging: $packageRoot"
} finally {
    Pop-Location
}
