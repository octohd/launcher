DOTNET ?= dotnet
POWERSHELL ?= pwsh
BASH ?= bash

SOLUTION := OctoHD.slnx
APP_PROJECT := src/OctoHD.App/OctoHD.App.csproj
CORE_TEST_PROJECT := tests/OctoHD.Core.Tests/OctoHD.Core.Tests.csproj
APP_TEST_PROJECT := tests/OctoHD.App.Tests/OctoHD.App.Tests.csproj
PUBLISH_SCRIPT := scripts/publish.ps1
XAML_FORMAT_SCRIPT := scripts/format-xaml.ps1
SHELL_SCRIPTS := scripts/package-linux-appimage.sh scripts/package-macos.sh

ACTIONLINT ?= actionlint
SHELLCHECK ?= shellcheck

CONFIGURATION ?= Release
DEV_CONFIGURATION ?= Debug
LINE_COVERAGE_MIN ?= 60
BRANCH_COVERAGE_MIN ?= 45
VERSION ?= 1.0.5
BUILD_NUMBER ?= 1
UPDATE_REPOSITORY ?=
RUNTIME ?=

PUBLISH_ARGS := -Configuration "$(CONFIGURATION)" -Version "$(VERSION)"
ifneq ($(strip $(UPDATE_REPOSITORY)),)
PUBLISH_ARGS += -UpdateRepository "$(UPDATE_REPOSITORY)"
endif

.DEFAULT_GOAL := help

.PHONY: \
	help tools restore build build-debug test coverage format format-check format-xaml format-xaml-check \
	lint lint-workflows lint-shell lint-powershell check ci dev dev-watch run clean \
	build-all build-windows build-linux build-macos \
	build-win-x64 build-win-arm64 build-linux-x64 build-linux-arm64 build-osx-x64 build-osx-arm64 \
	publish-all publish-windows publish-linux publish-macos publish-native \
	publish-win-x64 publish-win-arm64 publish-linux-x64 publish-linux-arm64 publish-osx-x64 publish-osx-arm64 \
	package-linux-x64 package-linux-arm64 package-macos-x64 package-macos-arm64

.NOTPARALLEL: build-all build-windows build-linux build-macos publish-all publish-windows publish-linux publish-macos

help:
	@echo OctoHD Make targets
	@echo Development:
	@echo   make dev                 Start the app in Debug mode
	@echo   make dev-watch           Start with dotnet watch and reliable app restarts
	@echo   make build               Build the solution
	@echo   make build-debug         Build the solution in Debug mode
	@echo   make test                Run all tests
	@echo   make coverage            Generate Cobertura and HTML coverage reports
	@echo   make format              Apply C# and AXAML formatting
	@echo   make format-check        Verify C# and AXAML formatting without changes
	@echo   make lint                Lint workflows, shell scripts, and PowerShell scripts
	@echo   make check               Run the same restore/build/test/format checks as CI
	@echo   make clean               Clean the selected configuration
	@echo Self-contained single-file builds:
	@echo   make build-all           Build all six runtime targets
	@echo   make build-windows       Build Windows x64 and ARM64
	@echo   make build-linux         Build Linux x64 and ARM64
	@echo   make build-macos         Build macOS Intel and Apple Silicon
	@echo   make build-win-x64       Build one runtime; analogous targets exist for all runtimes
	@echo   make publish-native RUNTIME=win-x64
	@echo Native release packages:
	@echo   make package-linux-x64   Build an AppImage on a matching Linux host
	@echo   make package-linux-arm64 Build an ARM64 AppImage on an ARM64 Linux host
	@echo   make package-macos-x64   Build a certificate-free ZIP on an Intel Mac
	@echo   make package-macos-arm64 Build a certificate-free ZIP on an Apple Silicon Mac
	@echo Common overrides:
	@echo   VERSION=0.2.0 CONFIGURATION=Release UPDATE_REPOSITORY=owner/OctoHD
	@echo   BUILD_NUMBER=42 POWERSHELL=pwsh BASH=bash

tools:
	$(DOTNET) tool restore

restore: tools
	$(DOTNET) restore $(SOLUTION)

build:
	$(DOTNET) build $(SOLUTION) --configuration $(CONFIGURATION)

build-debug:
	$(DOTNET) build $(SOLUTION) --configuration Debug

test:
	$(DOTNET) test $(SOLUTION) --configuration $(CONFIGURATION)

coverage: tools
	$(POWERSHELL) -NoLogo -NoProfile -Command "if (Test-Path -LiteralPath '$(CURDIR)/coverage') { Remove-Item -LiteralPath '$(CURDIR)/coverage' -Recurse -Force }"
	$(DOTNET) restore $(SOLUTION) --locked-mode
	$(DOTNET) build $(SOLUTION) --configuration $(CONFIGURATION) --no-restore -p:PublishAot=false
	$(DOTNET) test $(CORE_TEST_PROJECT) --configuration $(CONFIGURATION) --no-build -p:PublishAot=false -- --results-directory "$(CURDIR)/coverage" --coverlet --coverlet-file-prefix core --coverlet-output-format cobertura
	$(DOTNET) test $(APP_TEST_PROJECT) --configuration $(CONFIGURATION) --no-build -p:PublishAot=false -- --results-directory "$(CURDIR)/coverage" --coverlet --coverlet-file-prefix app --coverlet-output-format cobertura
	$(DOTNET) tool run reportgenerator --allow-roll-forward -- -reports:"coverage/*.coverage.cobertura.*.xml" -targetdir:"coverage/report" -reporttypes:"Html;Cobertura;MarkdownSummaryGithub"
	$(POWERSHELL) -NoLogo -NoProfile -File scripts/check-coverage.ps1 -LineThreshold $(LINE_COVERAGE_MIN) -BranchThreshold $(BRANCH_COVERAGE_MIN)

format: tools
	$(DOTNET) format $(SOLUTION)
	$(POWERSHELL) -NoLogo -NoProfile -File $(XAML_FORMAT_SCRIPT)

format-check: tools
	$(DOTNET) format $(SOLUTION) --verify-no-changes
	$(POWERSHELL) -NoLogo -NoProfile -File $(XAML_FORMAT_SCRIPT) -Check

format-xaml: tools
	$(POWERSHELL) -NoLogo -NoProfile -File $(XAML_FORMAT_SCRIPT)

format-xaml-check: tools
	$(POWERSHELL) -NoLogo -NoProfile -File $(XAML_FORMAT_SCRIPT) -Check

lint: lint-workflows lint-shell lint-powershell

lint-workflows:
	$(ACTIONLINT)

lint-shell:
	$(SHELLCHECK) $(SHELL_SCRIPTS)

lint-powershell:
	$(POWERSHELL) -NoLogo -NoProfile -File scripts/lint-powershell.ps1

check ci: tools
	$(DOTNET) restore $(SOLUTION) --locked-mode
	$(DOTNET) build $(SOLUTION) --configuration Release --no-restore -p:PublishAot=false
	$(DOTNET) test $(SOLUTION) --configuration Release --no-build -p:PublishAot=false
	$(DOTNET) format $(SOLUTION) --verify-no-changes --no-restore
	$(POWERSHELL) -NoLogo -NoProfile -File $(XAML_FORMAT_SCRIPT) -Check

dev run:
	$(DOTNET) run --project $(APP_PROJECT) --configuration $(DEV_CONFIGURATION)

dev-watch:
	$(DOTNET) watch --no-hot-reload --project $(APP_PROJECT) run --configuration $(DEV_CONFIGURATION)

clean:
	$(DOTNET) clean $(SOLUTION) --configuration $(CONFIGURATION)

build-all: publish-all
build-windows: publish-windows
build-linux: publish-linux
build-macos: publish-macos
build-win-x64: publish-win-x64
build-win-arm64: publish-win-arm64
build-linux-x64: publish-linux-x64
build-linux-arm64: publish-linux-arm64
build-osx-x64: publish-osx-x64
build-osx-arm64: publish-osx-arm64

publish-all: publish-windows publish-linux publish-macos

publish-windows: publish-win-x64 publish-win-arm64

publish-linux: publish-linux-x64 publish-linux-arm64

publish-macos: publish-osx-x64 publish-osx-arm64

publish-win-x64:
	$(POWERSHELL) -NoLogo -NoProfile -File $(PUBLISH_SCRIPT) -Runtime win-x64 $(PUBLISH_ARGS)

publish-win-arm64:
	$(POWERSHELL) -NoLogo -NoProfile -File $(PUBLISH_SCRIPT) -Runtime win-arm64 $(PUBLISH_ARGS)

publish-linux-x64:
	$(POWERSHELL) -NoLogo -NoProfile -File $(PUBLISH_SCRIPT) -Runtime linux-x64 $(PUBLISH_ARGS)

publish-linux-arm64:
	$(POWERSHELL) -NoLogo -NoProfile -File $(PUBLISH_SCRIPT) -Runtime linux-arm64 $(PUBLISH_ARGS)

publish-osx-x64:
	$(POWERSHELL) -NoLogo -NoProfile -File $(PUBLISH_SCRIPT) -Runtime osx-x64 $(PUBLISH_ARGS)

publish-osx-arm64:
	$(POWERSHELL) -NoLogo -NoProfile -File $(PUBLISH_SCRIPT) -Runtime osx-arm64 $(PUBLISH_ARGS)

publish-native:
	$(POWERSHELL) -NoLogo -NoProfile -File $(PUBLISH_SCRIPT) -Runtime "$(RUNTIME)" $(PUBLISH_ARGS) -NativeAot

package-linux-x64:
	$(BASH) scripts/package-linux-appimage.sh linux-x64 "$(VERSION)"

package-linux-arm64:
	$(BASH) scripts/package-linux-appimage.sh linux-arm64 "$(VERSION)"

package-macos-x64:
	$(BASH) scripts/package-macos.sh osx-x64 "$(VERSION)" "$(BUILD_NUMBER)"

package-macos-arm64:
	$(BASH) scripts/package-macos.sh osx-arm64 "$(VERSION)" "$(BUILD_NUMBER)"
