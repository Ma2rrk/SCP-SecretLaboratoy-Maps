param(
    [int]$MaxSeed = 60
)

$ErrorActionPreference = 'Stop'
$sampleDir = Join-Path $PSScriptRoot '..\.tools\samples'
New-Item -ItemType Directory -Path $sampleDir -Force | Out-Null
$seen = @{
    LightContainment = @{}
    HeavyContainment = @{}
    Entrance = @{}
}

foreach ($file in Get-ChildItem -LiteralPath $sampleDir -Filter '*.json') {
    try {
        $sample = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        foreach ($zone in $seen.Keys) {
            $seen[$zone][[int]$sample.atlasIndex.$zone] = $true
        }
    } catch {
        Write-Warning "Invalid sample: $($file.Name)"
    }
}

for ($seed = 1; $seed -le $MaxSeed; $seed++) {
    $path = Join-Path $sampleDir "$seed.json"
    if (Test-Path -LiteralPath $path) { continue }
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri "https://scpslmaps.fxdyj.com/api.php?seed=$seed" -TimeoutSec 90
        $sample = $response.Content | ConvertFrom-Json
        if ($sample.seed -ne $seed -or -not $sample.atlasIndex) { throw 'Unexpected response' }
        [IO.File]::WriteAllText($path, $response.Content, [Text.Encoding]::UTF8)
        foreach ($zone in $seen.Keys) { $seen[$zone][[int]$sample.atlasIndex.$zone] = $true }
        Write-Output "seed=$seed LCZ=$($sample.atlasIndex.LightContainment) HCZ=$($sample.atlasIndex.HeavyContainment) EZ=$($sample.atlasIndex.Entrance)"
    } catch {
        Write-Warning "seed=$seed failed: $_"
    }
}

foreach ($zone in $seen.Keys) {
    Write-Output "$zone indices: $(($seen[$zone].Keys | Sort-Object) -join ',')"
}
