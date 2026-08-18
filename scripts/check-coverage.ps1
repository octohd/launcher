[CmdletBinding()]
param(
    [ValidateRange(0, 100)]
    [double]$LineThreshold = 60,

    [ValidateRange(0, 100)]
    [double]$BranchThreshold = 45,

    [string]$ReportPath = "coverage/report/Cobertura.xml"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$resolvedReportPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $ReportPath))
if (-not [IO.File]::Exists($resolvedReportPath)) {
    throw "Coverage report does not exist: $resolvedReportPath"
}

[xml]$report = [IO.File]::ReadAllText($resolvedReportPath)
$lineCoverage = [double]::Parse(
    $report.coverage."line-rate",
    [Globalization.CultureInfo]::InvariantCulture) * 100
$branchCoverage = [double]::Parse(
    $report.coverage."branch-rate",
    [Globalization.CultureInfo]::InvariantCulture) * 100

Write-Output ("Coverage: {0:N1}% lines, {1:N1}% branches" -f $lineCoverage, $branchCoverage)
if ($lineCoverage -lt $LineThreshold) {
    throw "Line coverage $lineCoverage% is below the required $LineThreshold%."
}

if ($branchCoverage -lt $BranchThreshold) {
    throw "Branch coverage $branchCoverage% is below the required $BranchThreshold%."
}
