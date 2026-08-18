[CmdletBinding()]
param(
    [switch]$Check,

    [string[]]$Files = @(
        "src/OctoHD.App/App.axaml",
        "src/OctoHD.App/Views/MainWindow.axaml"
    )
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
$configurationPath = Join-Path $repositoryRoot "Settings.XamlStyler"
$failedFiles = [Collections.Generic.List[string]]::new()
$utf8WithoutBom = [Text.UTF8Encoding]::new($false)

function ConvertTo-NormalizedXaml {
    param([Parameter(Mandatory)][string]$Content)

    if ($Content.Length -gt 0 -and $Content[0] -eq [char]0xFEFF) {
        $Content = $Content.Substring(1)
    }

    return $Content.Replace("`r`n", "`n").Replace("`r", "`n")
}

foreach ($file in $Files) {
    $filePath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $file))
    if (-not $filePath.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "XAML path escapes the repository: $file"
    }

    if (-not [IO.File]::Exists($filePath)) {
        throw "XAML file does not exist: $file"
    }

    $process = [Diagnostics.Process]::new()
    try {
        $process.StartInfo.FileName = "dotnet"
        $process.StartInfo.WorkingDirectory = $repositoryRoot
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.CreateNoWindow = $true
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true
        $process.StartInfo.ArgumentList.Add("tool")
        $process.StartInfo.ArgumentList.Add("run")
        $process.StartInfo.ArgumentList.Add("xstyler")
        $process.StartInfo.ArgumentList.Add("--allow-roll-forward")
        $process.StartInfo.ArgumentList.Add("--")
        $process.StartInfo.ArgumentList.Add("--file")
        $process.StartInfo.ArgumentList.Add($filePath)
        $process.StartInfo.ArgumentList.Add("--ignore")
        $process.StartInfo.ArgumentList.Add("--config")
        $process.StartInfo.ArgumentList.Add($configurationPath)
        $process.StartInfo.ArgumentList.Add("--write-to-stdout")

        $null = $process.Start()
        $formatted = $process.StandardOutput.ReadToEnd()
        $diagnostics = $process.StandardError.ReadToEnd()
        $process.WaitForExit()

        if ($process.ExitCode -ne 0) {
            throw "XAML Styler failed for ${file}:`n$diagnostics"
        }
    }
    finally {
        $process.Dispose()
    }

    $formatted = ConvertTo-NormalizedXaml $formatted
    $current = ConvertTo-NormalizedXaml ([IO.File]::ReadAllText($filePath))
    if ($formatted -ceq $current) {
        Write-Output "PASS $file"
        continue
    }

    if ($Check) {
        $failedFiles.Add($file)
        Write-Output "FAIL $file"
        continue
    }

    [IO.File]::WriteAllText($filePath, $formatted, $utf8WithoutBom)
    Write-Output "FORMATTED $file"
}

if ($failedFiles.Count -gt 0) {
    throw "XAML formatting differs in: $($failedFiles -join ', ')"
}
