param([string]$PublishDirectory,[string]$Compiler)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if(-not $IsWindows){throw 'Build installers on Windows. Linux managed output is not a distributable payload.'}
$root=Split-Path $PSScriptRoot -Parent
if(-not $PublishDirectory){$PublishDirectory=Join-Path $root 'artifacts/win-x64'}
$PublishDirectory=(Resolve-Path -LiteralPath $PublishDirectory).Path
& (Join-Path $PSScriptRoot 'Test-PublishLayout.ps1') -Path $PublishDirectory -VerifyChecksums
$build=Get-Content (Join-Path $PublishDirectory 'build-info.json') -Raw | ConvertFrom-Json
if(-not $build.TestsPassed -or -not $build.NativeWindowsResources -or $build.Configuration -ne 'Release'){throw 'Installers require a tested Release build with native Windows resources.'}
if($build.Version -notmatch '^\d+\.\d+\.\d+$' -or $build.BuildId -notmatch '^[a-fA-F0-9]{64}$'){throw 'Invalid build metadata.'}
if(-not $Compiler){$Compiler=Join-Path $env:LOCALAPPDATA 'Programs/ZheliBuildTools/InnoSetup-6.5.4/ISCC.exe'}
if(-not(Test-Path -LiteralPath $Compiler)){throw 'Run scripts/install-inno-compiler.ps1 first, or pass -Compiler.'}
# ISCC /? exits 1 before loading the engine; launcher PE metadata has no engine version.
# Compile an in-memory, no-output script to query the engine actually loaded.
$probe="[Setup]`nAppName=Zheli compiler check`nAppVersion=0`nCreateAppDir=no`nUninstallable=no`nOutput=no"
$compilerIdentity=($probe | & $Compiler '/O-' '-' 2>&1 | Out-String)
if($LASTEXITCODE -ne 0 -or $compilerIdentity -notmatch '(?im)^\s*Compiler engine version:\s+[^\r\n]*\b6\.5\.4(?:\s|$)'){
    throw "Use the pinned Inno Setup 6.5.4 compiler engine. Reported identity: $compilerIdentity"
}
$output=Join-Path $root ('artifacts/installers-'+$build.BuildId.Substring(0,12))
if(Test-Path -LiteralPath $output){throw 'Installer output already exists. Choose a fresh build or move the prior output aside.'}
New-Item -ItemType Directory -Path $output | Out-Null
$commonName="Zheli-Settings-$($build.Version)-win-x64-Setup.exe"
foreach($product in @('Settings','Timetable','Miao')) {
    $arguments=@("/DProduct=$product","/DAppVersion=$($build.Version)","/DBuildId=$($build.BuildId)","/DPayload=$PublishDirectory","/O$output")
    if($product -ne 'Settings'){$arguments+=@("/DCommonSetup=$(Join-Path $output $commonName)","/DCommonName=$commonName")}
    & $Compiler @arguments (Join-Path $root 'installer/Zheli.iss')
    if($LASTEXITCODE -ne 0){throw "Installer compilation failed: $product"}
}
$records=@(Get-ChildItem -LiteralPath $output -Filter '*.exe' -File | ForEach-Object {[pscustomobject]@{Name=$_.Name;SHA256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}})
if($records.Count -ne 3){throw 'Expected three independent installers.'}
$records | ConvertTo-Json | Set-Content (Join-Path $output 'SHA256.json') -Encoding utf8
$build | ConvertTo-Json | Set-Content (Join-Path $output 'build-info.json') -Encoding utf8
Write-Output $output
