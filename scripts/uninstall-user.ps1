[CmdletBinding(SupportsShouldProcess=$true,ConfirmImpact='High')]
param()
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'Run on Windows with PowerShell 7.'}
if(Test-Path 'HKCU:\Software\Zheli\Installed\Settings'){throw 'Use Windows Installed Apps to uninstall the managed packages; remove Settings last.'}
$target=Join-Path $env:LOCALAPPDATA 'Programs/Zheli'
$known=@('Zheli.CoreHost','Zheli.Timetable.Host','Zheli.Settings','Zheli.Timetable','Zheli.Miao','Zheli.PetHost')
foreach($name in $known){if(Get-Process -Name $name -ErrorAction SilentlyContinue){throw "Close $name first. User data will be retained."}}
if($PSCmdlet.ShouldProcess($target,'Move installed program components to a recoverable sibling folder (keep all user data)')) {
    if(Test-Path $target){Move-Item $target ($target+'.uninstalled-'+(Get-Date -Format yyyyMMdd-HHmmss))}
    $menu=Join-Path ([Environment]::GetFolderPath('Programs')) '哲里'
    if(Test-Path $menu){Move-Item $menu ($menu+'.uninstalled-'+(Get-Date -Format yyyyMMdd-HHmmss))}
    Write-Host 'Program folders and shortcuts moved aside. User data and backups were retained.'
}
