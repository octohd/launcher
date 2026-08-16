# Releasing OctoHD

OctoHD releases are created exclusively by `.github/workflows/release.yml`. Pushing a semantic version tag such as `v1.0.0` runs tests, builds all supported platform packages, validates them, creates checksums and provenance attestations, and publishes a GitHub Release.

## Release downloads

Every stable release contains exactly these platform packages plus `SHA256SUMS.txt`:

- `OctoHD-<version>-windows-x64.exe`
- `OctoHD-<version>-windows-arm64.exe`
- `OctoHD-<version>-linux-x64.AppImage`
- `OctoHD-<version>-linux-arm64.AppImage`
- `OctoHD-<version>-macos-x64.zip`
- `OctoHD-<version>-macos-arm64.zip`

Tags containing a suffix, for example `v1.0.0-beta.1`, are published as GitHub prereleases.

## Self-updater contract

Every CI build embeds the current `GITHUB_REPOSITORY` value as the updater's public release source. Release jobs also pass the tag version into the assemblies, so no separate updater URL or version secret is required. Local builds without `GITHUB_REPOSITORY` do not contact an update endpoint.

At startup, released builds query GitHub's latest stable Release and select the exact asset for the running OS and CPU architecture. The package is downloaded in the background and must match GitHub's `sha256:` asset digest before `pending-v1.json` is written. Deferred updates are applied by a temporary copy of the current single-file executable before the next UI launch. macOS extraction uses `ditto`, and the extracted app bundle must pass `codesign --verify --deep --strict` to validate its anonymous ad-hoc code seal before replacement.

Do not rename or omit any of the six platform assets listed above: their names are part of the updater protocol. A release version must be greater than the version baked into the running application. Prereleases are intentionally excluded from the stable auto-update channel.

## Certificate-free distribution

Releases require no Azure account, Apple Developer membership, signing certificates, notarization credentials, or GitHub Actions secrets.

Windows executables are distributed without Authenticode. Microsoft Defender SmartScreen can therefore show **Windows protected your PC**, and managed devices may block execution according to their organization policy.

The macOS job applies an anonymous ad-hoc code seal with `codesign --sign -`. This contains no certificate or verified publisher identity, but preserves the required .NET JIT entitlement and lets the updater reject structurally modified app bundles. The app is not submitted to Apple and is not notarized, so Gatekeeper can require the user to approve it under **System Settings → Privacy & Security → Open Anyway** after the first launch attempt.

The optional GitHub Actions variable `OCTOHD_BUNDLE_ID` selects the macOS bundle identifier. If omitted, packaging uses `st.octowow.octohd`.

Platform trust warnings are expected and must be documented with each release. Users should download OctoHD only from the canonical GitHub repository and can validate each package with the published SHA-256 checksums and GitHub provenance attestation.

## Linux AppImage

Linux packages are built on native x64 and ARM64 GitHub-hosted runners. `scripts/package-linux-appimage.sh` uses the pinned `linuxdeploy` release `1-alpha-20251107-1` and verifies the downloaded tool against a hard-coded SHA-256 digest before execution.

Users normally need to mark a downloaded AppImage as executable once:

```bash
chmod +x OctoHD-*-linux-*.AppImage
```

## Publishing a release

1. Ensure the `build` workflow succeeds on the intended release commit.
2. Update the application version/release notes as needed.
3. Create and push an annotated semantic version tag:

```bash
git tag -a v1.0.0 -m "OctoHD 1.0.0"
git push origin v1.0.0
```

4. The `release` workflow publishes the GitHub Release only after every platform package succeeds.
5. Download at least one package per platform from the Release page and perform a clean-machine smoke test before announcing it.

Consumers can verify checksums with `SHA256SUMS.txt` and verify GitHub provenance with:

```bash
gh attestation verify <downloaded-file> --repo <owner>/<repository>
```

No external release credentials are required. Do not add unrelated credentials or private keys to the repository or workflow files.
