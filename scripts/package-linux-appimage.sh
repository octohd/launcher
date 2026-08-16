#!/usr/bin/env bash
set -euo pipefail

runtime="${1:-}"
version="${2:-}"

if [[ -z "$runtime" || -z "$version" ]]; then
  echo "Usage: $0 <linux-x64|linux-arm64> <version>" >&2
  exit 2
fi

case "$runtime" in
  linux-x64)
    appimage_arch="x86_64"
    linuxdeploy_asset="linuxdeploy-x86_64.AppImage"
    linuxdeploy_sha256="c20cd71e3a4e3b80c3483cef793cda3f4e990aca14014d23c544ca3ce1270b4d"
    release_arch="x64"
    ;;
  linux-arm64)
    appimage_arch="aarch64"
    linuxdeploy_asset="linuxdeploy-aarch64.AppImage"
    linuxdeploy_sha256="620095110d693282b8ebeb244a95b5e911cf8f65f76c88b4b47d16ae6346fcff"
    release_arch="arm64"
    ;;
  *)
    echo "Unsupported Linux runtime: $runtime" >&2
    exit 2
    ;;
esac

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd -- "$script_dir/.." && pwd)"
if [[ "${GITHUB_ACTIONS:-}" == "true" && -z "${GITHUB_REPOSITORY:-}" ]]; then
  echo "GITHUB_REPOSITORY is required in CI for the self-update channel." >&2
  exit 1
fi
project_path="$repository_root/src/OctoHD.App/OctoHD.App.csproj"
release_root="$repository_root/releases"
staging_dir="$release_root/staging-$runtime"
publish_dir="$staging_dir/publish"
app_dir="$staging_dir/OctoHD.AppDir"
package_dir="$release_root/packages"
tools_dir="$release_root/tools"
linuxdeploy_path="$tools_dir/$linuxdeploy_asset"
output_path="$package_dir/OctoHD-$version-linux-$release_arch.AppImage"

case "$staging_dir" in
  "$release_root"/*) ;;
  *) echo "Refusing to clean staging path outside $release_root" >&2; exit 1 ;;
esac

rm -rf -- "$staging_dir"
mkdir -p -- "$publish_dir" "$app_dir/usr/lib/octohd" "$app_dir/usr/bin" "$package_dir" "$tools_dir"

dotnet publish "$project_path" \
  --configuration Release \
  --runtime "$runtime" \
  --self-contained true \
  -p:Version="$version" \
  -p:IncludeSourceRevisionInInformationalVersion=false \
  -p:OctoHDUpdateRepository="${GITHUB_REPOSITORY:-}" \
  -p:PublishAot=false \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugSymbols=false \
  -p:DebugType=None \
  --output "$publish_dir"

published_files=("$publish_dir"/*)
if [[ ${#published_files[@]} -ne 1 || "$(basename -- "${published_files[0]}")" != "OctoHD" ]]; then
  echo "Expected exactly one published executable named OctoHD inside the AppImage." >&2
  printf 'Found: %s\n' "${published_files[@]}" >&2
  exit 1
fi

cp -a -- "$publish_dir/." "$app_dir/usr/lib/octohd/"
chmod +x -- "$app_dir/usr/lib/octohd/OctoHD"

cat > "$app_dir/usr/bin/OctoHD" <<'APP_RUNNER'
#!/bin/sh
set -eu
app_dir="${APPDIR:-$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)}"
exec "$app_dir/usr/lib/octohd/OctoHD" "$@"
APP_RUNNER
chmod +x -- "$app_dir/usr/bin/OctoHD"

cat > "$staging_dir/octohd.desktop" <<DESKTOP_FILE
[Desktop Entry]
Type=Application
Name=OctoHD
Comment=OctoWoW HD patch launcher
Exec=OctoHD
Icon=octohd
Terminal=false
Categories=Game;Utility;
StartupNotify=true
X-AppImage-Version=$version
DESKTOP_FILE

cp -- "$repository_root/src/OctoHD.App/Assets/Brand/OctoHD-Icon.png" "$staging_dir/octohd.png"

linuxdeploy_url="https://github.com/linuxdeploy/linuxdeploy/releases/download/1-alpha-20251107-1/$linuxdeploy_asset"
if [[ ! -f "$linuxdeploy_path" ]]; then
  curl --fail --location --retry 3 --proto '=https' --tlsv1.2 \
    "$linuxdeploy_url" --output "$linuxdeploy_path"
fi

echo "$linuxdeploy_sha256  $linuxdeploy_path" | sha256sum --check --status
chmod +x -- "$linuxdeploy_path"

rm -f -- "$output_path"
export ARCH="$appimage_arch"
export OUTPUT="$output_path"
export APPIMAGE_EXTRACT_AND_RUN=1
"$linuxdeploy_path" \
  --appdir "$app_dir" \
  --desktop-file "$staging_dir/octohd.desktop" \
  --icon-file "$staging_dir/octohd.png" \
  --output appimage

test -f "$output_path"
chmod +x -- "$output_path"
echo "Created $output_path"
