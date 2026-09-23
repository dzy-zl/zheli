$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if(-not $IsWindows){throw 'The Inno Setup compiler requires Windows.'}
$version='6.5.4'
$sha='fa73bf47a4da250d185d07561c2bfda387e5e20db77e4570004cf6a133cc10b1'
$destination=Join-Path $env:TEMP ('zheli-inno-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $destination | Out-Null
$file=Join-Path $destination 'inno-setup.exe'
try {
    Invoke-WebRequest -Uri "https://github.com/jrsoftware/issrc/releases/download/is-6_5_4/innosetup-$version.exe" -OutFile $file
    if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $sha){throw 'Inno Setup download checksum mismatch.'}
    $install=Join-Path $env:LOCALAPPDATA 'Programs/ZheliBuildTools/InnoSetup-6.5.4'
    $process=Start-Process -FilePath $file -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER',"/DIR=`"$install`"") -Wait -PassThru
    if($process.ExitCode -ne 0){throw "Compiler setup failed: $($process.ExitCode)"}
    $compiler=Join-Path $install 'ISCC.exe'
    if(-not(Test-Path -LiteralPath $compiler)){throw 'Compiler was not installed.'}
    Write-Output $compiler
} finally {Remove-Item -LiteralPath $destination -Recurse -Force}
