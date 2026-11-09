#!/usr/bin/env bash
set -euo pipefail

RUNTIME="${1:-linux-x64}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/TwitchRelay.csproj"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet SDK was not found. Install .NET 8 SDK or cross-publish from Windows with publish.ps1 -Runtime linux-x64." >&2
    exit 1
fi

dotnet publish "$PROJECT" \
    -c Release \
    -r "$RUNTIME" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    --nologo

PUBLISH_DIR="$ROOT/bin/Release/net8.0/$RUNTIME/publish"
cp "$ROOT/relay.example.json" "$PUBLISH_DIR/relay.example.json"

if [[ -f "$PUBLISH_DIR/TwitchRelay" ]]; then
    chmod +x "$PUBLISH_DIR/TwitchRelay"
fi

echo
echo "Published to: $PUBLISH_DIR"
echo "Run ./TwitchRelay once to generate relay.json, then edit it and restart."
echo "Keep relay.json and data/ readable only by the service account (chmod 600 / 700)."
