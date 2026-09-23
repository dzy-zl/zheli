param([switch]$Live)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
$project='tests/Zheli.DeepSeek.Tests/Zheli.DeepSeek.Tests.csproj'
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Offline client checks failed.' }
if ($Live) {
    if (-not $IsWindows) { throw 'Live mode reads Windows Credential Manager. Run on Windows with PowerShell 7.' }
    Write-Host 'Live test uses the key saved in Zheli Settings; sends two synthetic prompts, which may incur API charges.'
    & dotnet run --project $project -c Release --no-build -- --live
    if ($LASTEXITCODE -ne 0) { throw 'Live checks failed. See the redacted failure category above.' }
}
