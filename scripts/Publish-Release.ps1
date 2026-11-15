[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    $revision = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source revision.' }
    $shortRevision = $revision.Substring(0, 8)
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $packageName = "SLMapsOverlay-$stamp-$shortRevision-win-x64"
    $outputRoot = Join-Path $projectRoot 'artifacts'
    $packageDirectory = Join-Path $outputRoot $packageName
    $publishDirectory = Join-Path $projectRoot "obj/release-publish/$packageName"
    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

    & dotnet publish SLMapsOverlay.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false `
        -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

    $runtimeFiles = @(
        'SLMapsOverlay.exe',
        'map-generation-data.json',
        'map-generation-data-14.2.9.json',
        'room.txt',
        'overlay.json',
        'scpsl-seed-detector.bat'
    )
    foreach ($name in $runtimeFiles) {
        $source = Join-Path $publishDirectory $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Required runtime file missing: $name"
        }
        Copy-Item -LiteralPath $source -Destination $packageDirectory
    }
    # Fail if a future publish adds an external runtime dependency to this single-file build.
    $unexpected = Get-ChildItem -LiteralPath $publishDirectory -File |
        Where-Object { $_.Name -notin $runtimeFiles -and $_.Name -ne 'generated-map-224593721.json' }
    if ($unexpected) { throw "Review additional publish files: $($unexpected.Name -join ', ')" }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'research/15.0-beta.md') `
        -Destination (Join-Path $packageDirectory '15.0-beta-notes.md')

    $manifest = [ordered]@{
        application = 'SLMapsOverlay'
        sourceCommit = $revision
        builtAt = (Get-Date).ToString('o')
        platform = 'win-x64'
        selfContained = $true
        mapVersions = @('14.2.9', '15.0 Beta')
        defaultMapVersion = '15.0 Beta'
        files = @(Get-ChildItem -LiteralPath $packageDirectory -File | Sort-Object Name | ForEach-Object {
            [ordered]@{
                name = $_.Name
                size = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        })
    }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText((Join-Path $packageDirectory 'release-info.json'),
        ($manifest | ConvertTo-Json -Depth 6), $utf8)

    $archivePath = Join-Path $outputRoot "$packageName.zip"
    Compress-Archive -LiteralPath $packageDirectory -DestinationPath $archivePath -CompressionLevel Optimal
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$archivePath.sha256", "$archiveHash  $packageName.zip`n", $utf8)
    Write-Output "Archive: $archivePath"
    Write-Output "SHA256: $archiveHash"
    Write-Output "Source: $revision"
}
finally {
    Pop-Location
}
