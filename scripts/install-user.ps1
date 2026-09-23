param([ValidateSet('All','Timetable','Miao','Settings')][string]$App='All')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if(-not $IsWindows){throw 'Run on Windows with PowerShell 7.'}
if(Test-Path 'HKCU:\Software\Zheli\Installed\Settings'){throw 'A managed installer is present. Use its setup EXE to add or repair applications.'}
$source=$PSScriptRoot
$target=Join-Path $env:LOCALAPPDATA 'Programs/Zheli'
$components=@('Zheli.CoreHost','Zheli.Settings')
if($App -in @('All','Timetable')) {$components+=@('Zheli.Timetable','Zheli.Timetable.Host')}
if($App -in @('All','Miao')) {$components+=@('Zheli.Miao','Zheli.PetHost')}
$manifestPath=Join-Path $source 'checksums.json'
if(-not(Test-Path $manifestPath)){throw 'Missing build checksum manifest.'}
$manifest=Get-Content $manifestPath -Raw | ConvertFrom-Json
$toInstall=@()
foreach($component in $components) {
    if(-not(Test-Path (Join-Path $source "$component/$component.exe"))){throw "Missing component $component"}
    # Never stop unrelated processes or overwrite files in use.
    $entries=@($manifest | Where-Object {$_.Path.StartsWith($component+[IO.Path]::DirectorySeparatorChar) -or $_.Path.StartsWith($component+'/')})
    $actual=@(Get-ChildItem (Join-Path $source $component) -File -Recurse)
    if($entries.Count -eq 0 -or $entries.Count -ne $actual.Count){throw "Incomplete manifest for $component"}
    foreach($file in $entries) {
        $path=[IO.Path]::GetFullPath((Join-Path $source $file.Path))
        if(-not $path.StartsWith([IO.Path]::GetFullPath($source)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid manifest path.'}
        if((Get-FileHash $path -Algorithm SHA256).Hash -ne $file.SHA256){throw "Checksum mismatch: $($file.Path)"}
    }
    $installed=Join-Path $target $component
    if(Test-Path $installed) {
        if(@(Get-ChildItem $installed -File -Recurse).Count -ne $entries.Count){throw "Different version of $component installed. Versioned upgrades are not supported yet."}
        foreach($file in $entries) {
            $existing=Join-Path $target $file.Path
            if(-not(Test-Path $existing) -or (Get-FileHash $existing -Algorithm SHA256).Hash -ne $file.SHA256){throw "Different version of $component installed. Existing files were not changed."}
        }
        # Identical shared components can be reused when adding another app later.
    } else {
        if(Get-Process -Name $component -ErrorAction SilentlyContinue){throw "Close $component before installing."}
        $toInstall+=$component
    }
}
New-Item -ItemType Directory $target -Force | Out-Null
$staging=Join-Path $target ('.install-'+[Guid]::NewGuid().ToString('N'))
$added=@()
try {
    New-Item -ItemType Directory $staging | Out-Null
    foreach($component in $toInstall){Copy-Item (Join-Path $source $component) (Join-Path $staging $component) -Recurse}
    foreach($component in $toInstall){Move-Item (Join-Path $staging $component) (Join-Path $target $component);$added+=$component}
} catch {
    # Remove only directories created by this attempt; existing installations and data stay intact.
    foreach($component in $added){Remove-Item (Join-Path $target $component) -Recurse -Force}
    throw
} finally {
    if(Test-Path $staging){Remove-Item $staging -Recurse -Force}
}
$menu=Join-Path ([Environment]::GetFolderPath('Programs')) '哲里'
New-Item -ItemType Directory $menu -Force | Out-Null
$shell=New-Object -ComObject WScript.Shell
$names=@{'Zheli.Settings'='哲里设置';'Zheli.Timetable'='哲里课表';'Zheli.Miao'='哲喵'}
foreach($component in $components) {
    if($names.ContainsKey($component)) {
        $shortcut=$shell.CreateShortcut((Join-Path $menu ($names[$component]+'.lnk')))
        $shortcut.TargetPath=Join-Path $target "$component/$component.exe"
        $shortcut.WorkingDirectory=Join-Path $target $component
        $shortcut.Save()
    }
}
Write-Host 'Installed for the current user. No administrator privileges, startup entries or desktop shortcuts were added.'
Write-Host 'Launch 哲里设置 from the Start menu. This is an unsigned development build, not a certified release.'
