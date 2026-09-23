param([ValidateSet('Debug','Release')][string]$Configuration='Release',[switch]$SkipTests)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if(-not $IsWindows){throw 'Build this script with PowerShell 7 on Windows 11 or a Windows build runner.'}
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:MSBUILDDISABLENODEREUSE='1'
$artifacts=Join-Path $root 'artifacts';New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$logs=Join-Path $artifacts 'logs';New-Item -ItemType Directory -Force -Path $logs | Out-Null
$stage=Join-Path $artifacts ('.publish-'+[Guid]::NewGuid().ToString('N'))
$output=Join-Path $artifacts 'win-x64'
function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if($LASTEXITCODE -ne 0){throw "dotnet failed: $($Arguments -join ' ')"}
}
Start-Transcript -Path (Join-Path $logs 'windows-build.txt') -Force | Out-Null
try {
    if(-not $SkipTests) {
        Invoke-Dotnet -Arguments @('run','--project','tests/Zheli.DeepSeek.Tests/Zheli.DeepSeek.Tests.csproj','-c',$Configuration)
        Invoke-Dotnet -Arguments @('run','--project','tests/Zheli.Tests/Zheli.Tests.csproj','-c',$Configuration,'-p:BuildInParallel=false','-m:1')
    }
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach($app in @('Zheli.CoreHost','Zheli.Timetable.Host','Zheli.Settings','Zheli.Timetable','Zheli.Miao','Zheli.PetHost')) {
        Invoke-Dotnet -Arguments @('publish',"src/$app/$app.csproj",'-c',$Configuration,'-r','win-x64','--self-contained','true',
            '-m:1','-p:BuildInParallel=false','-p:UseSharedCompilation=false','-p:PublishSingleFile=false','-p:WindowsAppSDKSelfContained=true','-o',"$stage/$app")
    }
    & (Join-Path $PSScriptRoot 'collect-notices.ps1') -PublishedRoot $stage
    & (Join-Path $PSScriptRoot 'Test-PublishLayout.ps1') -Path $stage
    foreach($name in @('install-user.ps1','uninstall-user.ps1')){Copy-Item (Join-Path $PSScriptRoot $name) $stage}
    $content=@(Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{Path=[IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/');SHA256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}
    })
    $canonical=($content | ForEach-Object {$_.Path+' '+$_.SHA256}) -join "`n"
    $id=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
    [xml]$props=Get-Content (Join-Path $root 'Directory.Build.props') -Raw
    $version=[string]$props.Project.PropertyGroup.Version
    @{Version=$version;BuildId=$id;TestsPassed=(-not $SkipTests);Configuration=$Configuration;NativeWindowsResources=$true;CreatedUtc=[DateTime]::UtcNow.ToString('o')} |
        ConvertTo-Json | Set-Content (Join-Path $stage 'build-info.json') -Encoding utf8
    "[Build]`nId=$id`nVersion=$version" | Set-Content (Join-Path $stage 'build.ini') -Encoding ascii
    $manifest=@(Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{Path=[IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/');SHA256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}
    })
    $manifest | ConvertTo-Json | Set-Content (Join-Path $stage 'checksums.json') -Encoding utf8
    & (Join-Path $PSScriptRoot 'Test-PublishLayout.ps1') -Path $stage -VerifyChecksums
    # No stale binaries can enter the new payload. Previous successful output is retained.
    if(Test-Path -LiteralPath $output){Move-Item -LiteralPath $output -Destination ($output+'.previous-'+[Guid]::NewGuid().ToString('N'))}
    Move-Item -LiteralPath $stage -Destination $output
    Write-Host "Native Windows publish finished: $output"
} finally {
    if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}
    Stop-Transcript | Out-Null
}
