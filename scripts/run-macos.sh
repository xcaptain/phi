#!/usr/bin/env bash
#
# Dev convenience: package the macOS .app (fast framework-dependent Debug
# publish) and open it, so the Dock / Cmd-Tab icon from AppIcon.icns shows
# up while iterating.
#
# `dotnet run` on macOS launches the bare binary, which has no .app bundle —
# macOS then shows a generic icon in the Dock and Cmd-Tab. The Dock icon
# only comes from Contents/Resources/AppIcon.icns inside the bundle.
#
# Usage:
#   scripts/run-macos.sh        # package + open dist/Phi.app
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

PHI_DEV=1 "$REPO_ROOT/scripts/package-macos.sh" "dev"

echo "==> Opening $REPO_ROOT/dist/Phi.app (Dock / Cmd-Tab icon should now show)"
open "$REPO_ROOT/dist/Phi.app"
