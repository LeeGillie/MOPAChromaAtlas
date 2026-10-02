#!/bin/sh
# Adds MOPAChroma Atlas to your desktop's application menu, for your user only. Nothing needs root.
#
#   ./install-menu-entry.sh            add it (run again after moving this folder)
#   ./install-menu-entry.sh --remove   take it out again
#
# Atlas runs perfectly well without this; it only saves opening a terminal.

set -e
here=$(cd "$(dirname "$0")" && pwd)
apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
entry="$apps/mopachroma-atlas.desktop"

if [ "$1" = "--remove" ]; then
    rm -f "$entry"
    echo "Removed $entry"
    exit 0
fi

if [ ! -x "$here/MOPAChromaAtlas" ]; then
    echo "MOPAChromaAtlas was not found next to this script ($here)." >&2
    exit 1
fi

mkdir -p "$apps"
cat > "$entry" <<DESKTOP
[Desktop Entry]
Type=Application
Name=MOPAChroma Atlas
GenericName=Laser colour mapping
Comment=Find and catalogue the MOPA fiber laser settings that colour metal
Exec="$here/MOPAChromaAtlas"
Icon=$here/mopachroma-atlas.png
Terminal=false
Categories=Graphics;Engineering;
DESKTOP
chmod 644 "$entry"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$apps" >/dev/null 2>&1 || true

echo "MOPAChroma Atlas is now in your application menu."
echo "If you move this folder, run this script again from its new place."
