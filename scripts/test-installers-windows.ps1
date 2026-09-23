param([Parameter(Mandatory)][string]$InstallerDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if(-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true'){throw 'Run only in a disposable GitHub-hosted Windows job, not a personal profile.'}
$data=Join-Path $env:LOCALAPPDATA 'Zheli'
if(Test-Path -LiteralPath $data){throw 'Existing user data detected. Installer smoke test refused to run.'}
if(Test-Path 'HKCU:\Software\Zheli\Installed'){throw 'Existing installation detected. Test refused.'}
$root=Split-Path $PSScriptRoot -Parent
$logs=Join-Path $root 'artifacts/logs';New-Item -ItemType Directory -Force -Path $logs | Out-Null
$target=Join-Path $env:RUNNER_TEMP ('Zheli-install-smoke-'+[Guid]::NewGuid().ToString('N'))
$build=Get-Content (Join-Path $InstallerDirectory 'build-info.json') -Raw | ConvertFrom-Json
$count=0
function Run-Installer([string]$File,[string[]]$Arguments,[switch]$ExpectFailure) {
    $script:count++
    $log=Join-Path $logs "installer-$script:count.log"
    $process=Start-Process -FilePath $File -ArgumentList ($Arguments+@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$log`"")) -PassThru
    if(-not $process.WaitForExit(180000)){Stop-Process -Id $process.Id;throw 'Installer exceeded three-minute timeout.'}
    if($ExpectFailure){if($process.ExitCode -eq 0){throw 'Installer unexpectedly accepted a blocked operation.'}}
    elseif($process.ExitCode -ne 0){throw "Installer failed: $($process.ExitCode)"}
}
function Install([string]$Product,[switch]$ExpectFailure) {
    $file=Join-Path $InstallerDirectory "Zheli-$Product-$($build.Version)-win-x64-Setup.exe"
    Run-Installer -File $file -Arguments @("/DIR=`"$target`"") -ExpectFailure:$ExpectFailure
}
function Uninstall([string]$Product,[switch]$ExpectFailure) {
    Run-Installer -File (Join-Path $target ".uninstall/$Product/unins000.exe") -Arguments @() -ExpectFailure:$ExpectFailure
}
function Exists([string]$App) {Test-Path -LiteralPath (Join-Path $target "$App/$App.exe")}
function Require([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message};Write-Host "PASS $Message"}
function Stop-TestProcesses {
    # Restrict termination to the unique temporary install directory owned by this test.
    for($i=0;$i -lt 5;$i++) {
        $owned=@(Get-Process -ErrorAction SilentlyContinue | Where-Object {$_.Path -and $_.Path.StartsWith($target+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)})
        foreach($process in $owned){Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue}
        Start-Sleep -Milliseconds 200
    }
}
function Start-App([string]$App,[switch]$NoWindow) {
    $process=Start-Process -FilePath (Join-Path $target "$App/$App.exe") -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 250;$process.Refresh()
        if($process.HasExited){throw "$App exited during startup: $($process.ExitCode)"}
        if($NoWindow -or $process.MainWindowHandle -ne 0){break}
    } while([DateTime]::UtcNow -lt $deadline)
    if(-not $NoWindow -and $process.MainWindowHandle -eq 0){throw "$App did not create a native window."}
    Start-Sleep -Seconds 3;$process.Refresh();Require (-not $process.HasExited) "$App startup survival"
    $mutex=[Threading.Mutex]::OpenExisting('Local\Zheli.Applications.Running');$mutex.Dispose()
    Stop-TestProcesses
}
Start-Transcript -Path (Join-Path $logs 'installer-smoke.txt') -Force | Out-Null
try {
    New-Item -ItemType Directory -Path $data | Out-Null
    $canary=Join-Path $data 'installer-smoke-canary.txt';'preserve user data' | Set-Content $canary
    Install Timetable
    Require ((Exists 'Zheli.Timetable') -and (Exists 'Zheli.Settings') -and (Exists 'Zheli.CoreHost')) 'independent timetable installer provisions common services'
    Start-App 'Zheli.Settings';Start-App 'Zheli.Timetable'
    Install Miao
    Require ((Exists 'Zheli.Miao') -and (Exists 'Zheli.PetHost')) 'independent Miao installer installed'
    Start-App 'Zheli.Miao';Start-App 'Zheli.PetHost' -NoWindow
    Install Timetable
    Require ((Get-Content $canary -Raw).Trim() -eq 'preserve user data') 'same-build repair preserves user data'
    $marker=Join-Path $target 'build.ini';$original=[IO.File]::ReadAllBytes($marker)
    try {"[Build]`nId=other-build" | Set-Content $marker;Install Timetable -ExpectFailure}
    finally {[IO.File]::WriteAllBytes($marker,$original)}
    Uninstall Settings -ExpectFailure
    Require ((Exists 'Zheli.Settings') -and (Exists 'Zheli.CoreHost')) 'dependent applications block common-service removal'
    Uninstall Timetable
    Require ((-not(Exists 'Zheli.Timetable')) -and (Exists 'Zheli.Miao') -and (Exists 'Zheli.CoreHost')) 'timetable removal preserves Miao and common services'
    Start-App 'Zheli.Miao'
    Uninstall Miao
    Require ((-not(Exists 'Zheli.Miao')) -and (Exists 'Zheli.Settings')) 'Miao removal preserves settings'
    Uninstall Settings
    Require ((-not(Exists 'Zheli.Settings')) -and (-not(Exists 'Zheli.CoreHost'))) 'last common-service uninstall removes programs'
    Require ((Get-Content $canary -Raw).Trim() -eq 'preserve user data') 'all uninstalls preserve user data'
    Write-Host 'PASS installer smoke suite. Visual quality, full IPC flows and personal-machine acceptance remain separate.'
} finally {Stop-TestProcesses;Stop-Transcript | Out-Null}
