# Builds self-contained single-file MOPAChroma Atlas programs for every desktop target, then
# packs each into the zip a user downloads (dist\MOPAChromaAtlas-<version>-<rid>.zip) with
# packaging\make_bundle.py, the same step the release workflow runs, user guide PDF included.
# Needs Python 3 with "pip install markdown", and Edge or Chrome for the guide PDF.
# Nothing needs to be installed on the target machine: the .NET runtime is inside the program.
# A macOS zip built here is unsigned and will not start on Apple silicon; release those from CI.
#
#   powershell -ExecutionPolicy Bypass -File publish.ps1
#   powershell -ExecutionPolicy Bypass -File publish.ps1 -Rids win-x64,linux-x64

param(
    [string[]] $Rids = @('win-x64', 'win-x86', 'win-arm64',
                         'linux-x64', 'linux-arm64',
                         'osx-x64', 'osx-arm64'),
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'src\MOPAChromaAtlas\MOPAChromaAtlas.csproj'
$out  = Join-Path $root 'publish'
$dist = Join-Path $root 'dist'

$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$python = if (Get-Command py -ErrorAction SilentlyContinue) { 'py' } else { 'python' }
Write-Host "MOPAChroma Atlas $version publish -> $out, zips -> $dist" -ForegroundColor Cyan

$guide = Join-Path $dist 'MOPAChroma-Atlas-User-Guide.pdf'
& $python (Join-Path $root 'docs\build_guide.py') --version $version --out $guide
if ($LASTEXITCODE -ne 0) { throw "building the user guide PDF failed" }

foreach ($rid in $Rids) {
    $dest = Join-Path $out $rid
    Write-Host "`n=== $rid ===" -ForegroundColor Yellow

    dotnet publish $proj `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -o $dest `
        --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $rid" }

    $bin = Join-Path $dest $(if ($rid -like 'win-*') { 'MOPAChromaAtlas.exe' } else { 'MOPAChromaAtlas' })
    & $python (Join-Path $root 'packaging\make_bundle.py') --rid $rid --version $version --binary $bin --guide $guide --dist $dist
    if ($LASTEXITCODE -ne 0) { throw "bundling failed for $rid" }
}

Write-Host "`nDone. Hand a user the zip for their platform from dist\." -ForegroundColor Green
