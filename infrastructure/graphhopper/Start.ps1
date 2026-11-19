param(
    [string]$JavaPath,
    [string]$DataPath = (Join-Path $PSScriptRoot '..\..\Rail.Resource\Data\tehran-region-261029.osm.pbf'),
    [string]$JarPath = (Join-Path $PSScriptRoot '..\..\Rail.Resource\Data\graphhopper-web-11.1.jar'),
    [string]$Heap = '1g'
)
$ErrorActionPreference = 'Stop'
if (-not $JavaPath) {
    $portableJava = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'runtime') -Directory -Filter 'jdk-*' -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'bin\java.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    $JavaPath = if ($portableJava) { $portableJava } else { 'java' }
}
if (-not (Test-Path -LiteralPath $JarPath -PathType Leaf)) { throw "GraphHopper JAR not found: $JarPath. Pass -JarPath or see README.md." }
$JarPath = (Resolve-Path -LiteralPath $JarPath).Path
if (-not (Test-Path -LiteralPath $DataPath -PathType Leaf)) { throw "OSM PBF not found: $DataPath. Pass -DataPath or see README.md." }
$DataPath = (Resolve-Path -LiteralPath $DataPath).Path
Push-Location $PSScriptRoot
try {
    & $JavaPath '-Xms128m' "-Xmx$Heap" "-Ddw.graphhopper.datareader.file=$DataPath" '-jar' $JarPath 'server' 'config.yml'
    if ($LASTEXITCODE -ne 0) { throw "GraphHopper exited with code $LASTEXITCODE." }
}
finally { Pop-Location }
