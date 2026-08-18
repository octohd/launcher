$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$requiredVersion = [version]'1.25.0'
$availableModule = Get-Module -ListAvailable -Name PSScriptAnalyzer |
    Where-Object Version -GE $requiredVersion |
    Sort-Object Version -Descending |
    Select-Object -First 1

if ($null -eq $availableModule) {
    throw "PSScriptAnalyzer $requiredVersion or newer is required. Run: Install-Module PSScriptAnalyzer -Scope CurrentUser"
}

Import-Module $availableModule.Path -Force
Invoke-ScriptAnalyzer `
    -Path $PSScriptRoot `
    -Recurse `
    -Severity Error, Warning `
    -EnableExit
