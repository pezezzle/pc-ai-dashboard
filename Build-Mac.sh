#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
verify=false
for argument in "$@"; do
    case "$argument" in
        --verify) verify=true ;;
        *) echo "Usage: ./Build-Mac.sh [--verify]" >&2; exit 2 ;;
    esac
done
if [[ "$(uname -s)" != Darwin ]]; then echo "Build-Mac.sh requires macOS." >&2; exit 1; fi
architecture="$(uname -m)"
case "$architecture" in arm64) runtime=osx-arm64 ;; x86_64) runtime=osx-x64 ;; *) exit 1 ;; esac
npm ci
npm run build
dotnet publish src/Dashboard.Agent -c Release -r "$runtime" --self-contained true -o artifacts/mac-agent
swift build --package-path src/Dashboard.Mac -c release
binary_directory="$(swift build --package-path src/Dashboard.Mac -c release --show-bin-path)"
app="$PWD/artifacts/mac/AI Dashboard.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources/Agent" "$app/Contents/Resources/Web" "$app/Contents/Resources/Providers"
cp "$binary_directory/DashboardMac" "$app/Contents/MacOS/AI Dashboard"
cp src/Dashboard.Mac/Info.plist "$app/Contents/Info.plist"
cp assets/branding/dashboard-icon.icns "$app/Contents/Resources/"
cp assets/branding/dashboard-mark.png "$app/Contents/Resources/"
# Sync generated resources so rebuilding cannot leave stale application files.
rsync -a --delete artifacts/mac-agent/ "$app/Contents/Resources/Agent/"
rsync -a --delete ui/web/ "$app/Contents/Resources/Web/"
codesign --force --deep --sign - "$app"
if $verify; then
    dotnet run --project tests/PcAiDashboard.Checks -c Release
    swift test --package-path src/Dashboard.Mac
    npx playwright test
    codesign --verify --deep --strict "$app"
fi
echo "Ready: $app"
echo "Start: open \"$app\""
