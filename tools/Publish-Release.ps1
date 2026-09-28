[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '1.0.0-rc.2',

    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactsRoot = Join-Path $repositoryRoot 'artifacts\release'
$packageName = "NetOpsTerminal-$Version-win-x64"
$publishRoot = Join-Path $artifactsRoot $packageName
$zipPath = Join-Path $artifactsRoot "$packageName.zip"
$hashPath = "$zipPath.sha256"
$localDotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
$dotnetPath = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & $dotnetPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$resolvedArtifactsRoot = [System.IO.Path]::GetFullPath($artifactsRoot)
$resolvedPublishRoot = [System.IO.Path]::GetFullPath($publishRoot)
if (-not $resolvedPublishRoot.StartsWith(
    $resolvedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The publish path resolved outside the repository release-artifact directory.'
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
if (Test-Path -LiteralPath $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

foreach ($existingPackage in @($zipPath, $hashPath)) {
    if (Test-Path -LiteralPath $existingPackage) {
        Remove-Item -LiteralPath $existingPackage -Force
    }
}

Push-Location $repositoryRoot
try {
    Invoke-DotNet @('restore', 'NetOpsTerminal.sln', '--runtime', 'win-x64', '-p:PublishReadyToRun=true')
    Invoke-DotNet @('build', 'NetOpsTerminal.sln', '-c', 'Release', '--no-restore')
    if (-not $SkipTests) {
        Invoke-DotNet @('test', 'NetOpsTerminal.sln', '-c', 'Release', '--no-build')
    }

    Invoke-DotNet @(
        'publish',
        'src/NetOpsTerminal.App/NetOpsTerminal.App.csproj',
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '--no-restore',
        '-p:PublishProfile=win-x64',
        "-p:Version=$Version",
        '--output', $publishRoot)

    $executablePath = Join-Path $publishRoot 'NetOpsTerminal.exe'
    if (-not (Test-Path -LiteralPath $executablePath)) {
        throw "The expected release executable was not published: $executablePath"
    }

    $documentationRoot = Join-Path $publishRoot 'documentation'
    New-Item -ItemType Directory -Path $documentationRoot -Force | Out-Null
    Copy-Item -LiteralPath 'README.md','SECURITY.md','THIRD_PARTY_NOTICES.md' -Destination $documentationRoot
    if ($Version -eq '1.0.0') {
        Copy-Item -LiteralPath 'docs\USER_AGREEMENT.md' -Destination $documentationRoot
    }
    $releaseGuide = if ($Version -eq '1.0.0') {
        'docs\RELEASE_V1.md'
    } else {
        'docs\RELEASE_CANDIDATE.md'
    }
    Copy-Item -LiteralPath $releaseGuide -Destination $documentationRoot

    $packageInventory = & $dotnetPath list 'src/NetOpsTerminal.App/NetOpsTerminal.App.csproj' package --include-transitive --format json
    if ($LASTEXITCODE -ne 0) {
        throw 'The resolved package inventory could not be generated.'
    }

    $packageInventory |
        Set-Content -LiteralPath (Join-Path $documentationRoot 'package-inventory.json') -Encoding utf8

    $manifest = Get-ChildItem -LiteralPath $publishRoot -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            [pscustomobject]@{
                Path = $_.FullName.Substring($publishRoot.Length).TrimStart('\', '/')
                Length = $_.Length
                Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        }
    $manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $publishRoot 'manifest.json') -Encoding utf8

    Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    "$zipHash  $([System.IO.Path]::GetFileName($zipPath))" |
        Set-Content -LiteralPath $hashPath -Encoding ascii

    Write-Host "Release folder: $publishRoot"
    Write-Host "Release archive: $zipPath"
    Write-Host "SHA256: $zipHash"
}
finally {
    Pop-Location
}
