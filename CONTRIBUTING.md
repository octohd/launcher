# Contributing to OctoHD

Thanks for helping improve OctoHD. Bug fixes, accessibility improvements, platform compatibility work, tests, and focused UX refinements are welcome.

## Before opening a pull request

- Search existing issues and pull requests to avoid duplicate work.
- Keep each change focused and explain the user-facing reason for it.
- Use English for UI text, documentation, commit messages, and issue content.
- Never commit game files, MPQ archives, credentials, certificates, tokens, personal paths, or private patch-source URLs.
- Be respectful and constructive in every project interaction.

For substantial behavior or UI changes, open an issue first so the approach can be discussed before implementation.

## Development setup

OctoHD requires the .NET 10 SDK. GNU Make provides the recommended shortcuts; PowerShell 7 is required for self-contained publish commands.

```text
make dev
make check
make build-win-x64 VERSION=1.0.0
```

Run `make help` to list all available development, test, and platform packaging commands.

You can also run the app directly:

```powershell
dotnet restore .\OctoHD.slnx
dotnet run --project .\src\OctoHD.App\OctoHD.App.csproj
```

Native AppImage and macOS bundle targets must run on their respective operating systems with the required platform tools installed.

## Quality requirements

Before submitting a pull request, run:

```text
make check
```

This restores dependencies, builds the Release configuration, runs the test suite, and verifies formatting. Add or update tests whenever behavior changes.

## Pull requests

- Describe what changed and why.
- Link the related issue when one exists.
- Call out platform-specific behavior and any manual verification performed.
- Keep generated build output out of Git.
- Confirm that your contribution contains no third-party assets without compatible redistribution terms.

Maintainer release procedures live in [docs/releasing.md](docs/releasing.md).
