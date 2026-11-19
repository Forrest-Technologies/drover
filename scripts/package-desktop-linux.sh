#!/usr/bin/env bash
set -euo pipefail

publish_dir=${1:?publish directory is required}
output_dir=${2:?output directory is required}
version=${3:?version is required}
status=${4:-unsigned}

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "version must have the form 1.2.3" >&2
  exit 1
fi
if [[ ! -x "$publish_dir/Drover.Native" ]]; then
  echo "the publish directory does not contain the Drover.Native executable" >&2
  exit 1
fi

temp=$(mktemp -d "${TMPDIR:-/tmp}/drover-linux-package.XXXXXX")
trap 'rm -r "$temp"' EXIT
root="$temp/root"
install -d "$root/DEBIAN" "$root/opt/drover" "$root/usr/bin" "$root/usr/share/applications" "$root/usr/share/icons/hicolor/512x512/apps"
cp -R "$publish_dir"/. "$root/opt/drover/"
chmod 0755 "$root/opt/drover/Drover.Native"
ln -s /opt/drover/Drover.Native "$root/usr/bin/drover"
install -m 0644 src/Shared/Web/icon-512.png "$root/usr/share/icons/hicolor/512x512/apps/drover.png"

cat > "$root/DEBIAN/control" <<EOF
Package: drover
Version: $version
Section: utils
Priority: optional
Architecture: amd64
Maintainer: Forrest Technologies
Depends: libfontconfig1, libgl1, libx11-6, libxcursor1, libxi6, libxinerama1, libxrandr2
Description: Native client for a self-hosted Drover server
EOF

cat > "$root/usr/share/applications/drover.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Drover
Comment=Connect to a self-hosted Drover server
Exec=drover
Icon=drover
Terminal=false
Categories=Network;
EOF

mkdir -p "$output_dir"
package="$output_dir/drover-linux-x64-$version-$status.deb"
dpkg-deb --root-owner-group --build "$root" "$package"
dpkg-deb --info "$package" >/dev/null
printf '%s\n' "$package"
