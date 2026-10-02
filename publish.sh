#!/usr/bin/env bash
# Builds self-contained single-file MOPAChroma Atlas programs for every desktop target, then
# packs each into the zip a user downloads (dist/MOPAChromaAtlas-<version>-<rid>.zip) with
# packaging/make_bundle.py, the same step the release workflow runs, user guide PDF included.
# Needs Python 3 with "pip install markdown", and Chrome or Chromium for the guide PDF.
# A macOS zip built anywhere except a Mac is unsigned and will not start on Apple silicon.
#
#   ./publish.sh
#   ./publish.sh win-x64 linux-x64

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$ROOT/src/MOPAChromaAtlas/MOPAChromaAtlas.csproj"
OUT="$ROOT/publish"
DIST="$ROOT/dist"

if [ "$#" -gt 0 ]; then RIDS=("$@")
else RIDS=(win-x64 win-x86 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64); fi

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props" | head -1)"
echo "MOPAChroma Atlas $VERSION publish -> $OUT, zips -> $DIST"

GUIDE="$DIST/MOPAChroma-Atlas-User-Guide.pdf"
python3 "$ROOT/docs/build_guide.py" --version "$VERSION" --out "$GUIDE"

for rid in "${RIDS[@]}"; do
    echo; echo "=== $rid ==="
    dotnet publish "$PROJ" -c Release -r "$rid" --self-contained true \
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true -p:DebugType=none \
        -o "$OUT/$rid" --nologo -v quiet
    bin="$OUT/$rid/MOPAChromaAtlas"
    case "$rid" in win-*) bin="$bin.exe" ;; esac
    python3 "$ROOT/packaging/make_bundle.py" --rid "$rid" --version "$VERSION" --binary "$bin" --guide "$GUIDE" --dist "$DIST"
done

echo; echo "Done. Hand a user the zip for their platform from dist/."
