#!/usr/bin/env bash
set -euo pipefail

# ShellCheck cannot infer that status is assigned before it is expanded by the trap.
# shellcheck disable=SC2154
trap 'status=$?; echo "::error file=scripts/package-macos.sh,line=${LINENO}::Command failed with exit ${status}: ${BASH_COMMAND}"; exit "${status}"' ERR

runtime="${1:-}"
version="${2:-}"
build_number="${3:-1}"

if [[ -z "$runtime" || -z "$version" ]]; then
  echo "Usage: $0 <osx-x64|osx-arm64> <version> [build-number]" >&2
  exit 2
fi

case "$runtime" in
  osx-x64) release_arch="x64" ;;
  osx-arm64) release_arch="arm64" ;;
  *) echo "Unsupported macOS runtime: $runtime" >&2; exit 2 ;;
esac

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.+][0-9A-Za-z.-]+)?$ ]]; then
  echo "Version must be a semantic version without a leading v: $version" >&2
  exit 2
fi
if [[ ! "$build_number" =~ ^[0-9]+$ ]]; then
  echo "Build number must contain digits only: $build_number" >&2
  exit 2
fi

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
app_path="$staging_dir/OctoHD.app"
contents_path="$app_path/Contents"
macos_path="$contents_path/MacOS"
resources_path="$contents_path/Resources"
package_dir="$release_root/packages"
output_path="$package_dir/OctoHD-$version-macos-$release_arch.zip"
bundle_identifier="${OCTOHD_BUNDLE_ID:-st.octowow.octohd}"
bundle_short_version="${version%%[-+]*}"

case "$staging_dir" in
  "$release_root"/*) ;;
  *) echo "Refusing to clean staging path outside $release_root" >&2; exit 1 ;;
esac

rm -rf -- "$staging_dir"
mkdir -p -- "$publish_dir" "$macos_path" "$resources_path" "$package_dir"

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
  echo "Expected exactly one published executable named OctoHD." >&2
  printf 'Found: %s\n' "${published_files[@]}" >&2
  exit 1
fi

cp -- "$publish_dir/OctoHD" "$macos_path/OctoHD"
chmod +x "$macos_path/OctoHD"

icon_source="$repository_root/src/OctoHD.App/Assets/Brand/OctoHD-Icon.png"
iconset_path="$staging_dir/OctoHD.iconset"
mkdir -p -- "$iconset_path"
for size in 16 32 128 256 512; do
  double_size=$((size * 2))
  sips -z "$size" "$size" "$icon_source" --out "$iconset_path/icon_${size}x${size}.png" >/dev/null
  sips -z "$double_size" "$double_size" "$icon_source" --out "$iconset_path/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset_path" -o "$resources_path/OctoHD.icns"

cat > "$contents_path/Info.plist" <<INFO_PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleDisplayName</key>
  <string>OctoHD</string>
  <key>CFBundleExecutable</key>
  <string>OctoHD</string>
  <key>CFBundleIconFile</key>
  <string>OctoHD</string>
  <key>CFBundleIdentifier</key>
  <string>$bundle_identifier</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>OctoHD</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$bundle_short_version</string>
  <key>CFBundleVersion</key>
  <string>$build_number</string>
  <key>LSMinimumSystemVersion</key>
  <string>14.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSHumanReadableCopyright</key>
  <string>Copyright © OctoHD contributors</string>
</dict>
</plist>
INFO_PLIST
plutil -lint "$contents_path/Info.plist"

entitlements_path="$staging_dir/OctoHD.entitlements"
cat > "$entitlements_path" <<'ENTITLEMENTS'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>com.apple.security.cs.allow-jit</key>
  <true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key>
  <true/>
  <key>com.apple.security.cs.allow-dyld-environment-variables</key>
  <true/>
  <key>com.apple.security.cs.disable-library-validation</key>
  <true/>
</dict>
</plist>
ENTITLEMENTS
plutil -lint "$entitlements_path"

codesign --force --options runtime \
  --entitlements "$entitlements_path" \
  --sign - \
  "$macos_path/OctoHD"
codesign --force --options runtime \
  --entitlements "$entitlements_path" \
  --sign - \
  "$app_path"
codesign --verify --deep --strict --verbose=2 "$app_path"

smoke_home="$staging_dir/smoke-home"
smoke_log="$staging_dir/smoke.log"
mkdir -p -- "$smoke_home"
HOME="$smoke_home" "$macos_path/OctoHD" >"$smoke_log" 2>&1 &
smoke_pid=$!
for _ in {1..10}; do
  sleep 1
  if ! kill -0 "$smoke_pid" 2>/dev/null; then
    set +e
    wait "$smoke_pid"
    smoke_status=$?
    set -e
    cat "$smoke_log" >&2
    echo "The signed macOS app exited during its startup smoke test with status $smoke_status." >&2
    exit 1
  fi
done
kill "$smoke_pid"
wait "$smoke_pid" 2>/dev/null || true
echo "Signed macOS app remained running for the 10-second startup smoke test."

rm -f -- "$output_path"
ditto -c -k --sequesterRsrc --keepParent "$app_path" "$output_path"
test -f "$output_path"
echo "Created certificate-free package $output_path"
