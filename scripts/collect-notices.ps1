param([Parameter(Mandatory)][string]$PublishedRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Split-Path $PSScriptRoot -Parent
$cache=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $HOME '.nuget/packages'}
$notices=Join-Path $PublishedRoot 'notices-staging'
New-Item -ItemType Directory -Path $notices | Out-Null
$packages=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
Get-ChildItem (Join-Path $root 'src') -Filter project.assets.json -Recurse | ForEach-Object {
    $assets=Get-Content $_.FullName -Raw | ConvertFrom-Json -AsHashtable
    foreach($lib in $assets.libraries.Values){if($lib.type -eq 'package'){[void]$packages.Add($lib.path)}}
    foreach($framework in $assets.project.frameworks.Values){
        if($framework.ContainsKey('downloadDependencies')){foreach($dep in $framework.downloadDependencies){[void]$packages.Add(($dep.name.ToLowerInvariant()+'/'+$dep.version.Trim('[]').Split(',')[0]))}}
    }
}
foreach($package in $packages) {
    $dir=Join-Path $cache $package
    if(-not(Test-Path -LiteralPath $dir)){throw "License source package missing: $package"}
    $target=Join-Path $notices ($package.Replace('/','-'));New-Item -ItemType Directory -Path $target | Out-Null
    Get-ChildItem -LiteralPath $dir -File -Recurse | Where-Object {$_.Name -match '(?i)(license|notice|copying)' -or $_.Extension -eq '.nuspec'} | ForEach-Object {
        $relative=[IO.Path]::GetRelativePath($dir,$_.FullName)
        $dest=Join-Path $target $relative;New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $dest
    }
}
foreach($license in @('MIT','Apache-2.0')) {
    Invoke-WebRequest -Uri "https://raw.githubusercontent.com/spdx/license-list-data/v3.27.0/text/$license.txt" -OutFile (Join-Path $notices "$license.txt")
}
@{Sources=@('https://github.com/spdx/license-list-data/tree/v3.27.0/text','NuGet package license/notice files and nuspec metadata');Packages=@($packages | Sort-Object)} |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $notices 'sources.json') -Encoding utf8
foreach($app in @('Zheli.Settings','Zheli.Timetable','Zheli.Miao','Zheli.PetHost','Zheli.CoreHost','Zheli.Timetable.Host')) {
    Copy-Item -LiteralPath $notices -Destination (Join-Path $PublishedRoot "$app/ThirdPartyNotices") -Recurse
}
Remove-Item -LiteralPath $notices -Recurse -Force
