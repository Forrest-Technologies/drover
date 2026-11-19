#!/usr/bin/env bash
set -euo pipefail

publish_dir=${1:?publish directory is required}
output_dir=${2:?output directory is required}
version=${3:?version is required}
status=${4:-unsigned}
signing_identity=${5:-}

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "version must have the form 1.2.3" >&2
  exit 1
fi
if [[ ! -x "$publish_dir/Drover.Native" ]]; then
  echo "the publish directory does not contain the Drover.Native executable" >&2
  exit 1
fi

case "$(uname -m)" in
  arm64) architecture=arm64 ;;
  x86_64) architecture=x64 ;;
  *) echo "unsupported macOS architecture" >&2; exit 1 ;;
esac

temp=$(mktemp -d "${TMPDIR:-/tmp}/drover-macos-package.XXXXXX")
trap 'rm -r "$temp"' EXIT
app="$temp/Drover.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish_dir"/. "$app/Contents/MacOS/"
chmod 0755 "$app/Contents/MacOS/Drover.Native"
install -m 0644 src/Shared/Web/icon-512.png "$app/Contents/Resources/drover.png"

cat > "$app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDisplayName</key><string>Drover</string>
  <key>CFBundleExecutable</key><string>Drover.Native</string>
  <key>CFBundleIconFile</key><string>drover.png</string>
  <key>CFBundleIdentifier</key><string>com.forresttechnologies.drover.client</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>Drover</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF

if [[ -n "$signing_identity" ]]; then
  while IFS= read -r -d '' file; do
    if file -b "$file" | grep -q 'Mach-O'; then
      codesign --force --options runtime --timestamp --sign "$signing_identity" "$file"
    fi
  done < <(find "$app/Contents/MacOS" -type f -print0)
  codesign --force --options runtime --timestamp --sign "$signing_identity" "$app"
  codesign --verify --deep --strict --verbose=2 "$app"
fi

mkdir -p "$output_dir"
dmg="$output_dir/drover-macos-$architecture-$version-$status.dmg"
hdiutil create -quiet -volname Drover -srcfolder "$app" -ov -format UDZO "$dmg"
printf '%s\n' "$dmg"
