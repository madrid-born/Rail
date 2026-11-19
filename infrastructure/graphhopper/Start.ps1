param(
    [string]$JavaPath,
    [string]$DataPath = (Join-Path $PSScriptRoot 'data\tehran-region.osm.pbf'),
    [string]$Heap = '1g'
)
$ErrorActionPreference = 'Stop'
if (-not $JavaPath) {
    $portableJava = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'runtime') -Directory -Filter 'jdk-*' -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'bin\java.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    $JavaPath = if ($portableJava) { $portableJava } else { 'java' }
}
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'graphhopper.jar'))) { throw 'Download GraphHopper 11.1 as graphhopper.jar first; see README.md.' }
if (-not (Test-Path -LiteralPath $DataPath -PathType Leaf)) { throw "OSM PBF not found: $DataPath. Pass -DataPath or see README.md." }
$DataPath = (Resolve-Path -LiteralPath $DataPath).Path
Push-Location $PSScriptRoot
try {
    & $JavaPath '-Xms128m' "-Xmx$Heap" "-Ddw.graphhopper.datareader.file=$DataPath" '-jar' 'graphhopper.jar' 'server' 'config.yml'
    if ($LASTEXITCODE -ne 0) { throw "GraphHopper exited with code $LASTEXITCODE." }
}
finally { Pop-Location }
