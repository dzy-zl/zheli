param([Parameter(Mandatory)][string]$Path,[switch]$VerifyChecksums)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath($Path)
if(((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Publish root is a link or reparse point.'}
$apps=@('Zheli.Settings','Zheli.Timetable','Zheli.Miao','Zheli.PetHost','Zheli.CoreHost','Zheli.Timetable.Host')
$files=@(Get-ChildItem -LiteralPath $root -Recurse -Force)
if ($files | Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0}) { throw 'Publish output contains links or reparse points.' }
foreach($app in $apps) {
    $dir=Join-Path $root $app
    foreach($name in @("$app.exe","$app.dll","$app.deps.json","$app.runtimeconfig.json",'coreclr.dll','hostfxr.dll','hostpolicy.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $dir $name) -PathType Leaf)) { throw "Missing published file: $app/$name" }
    }
    $exe=[IO.File]::OpenRead((Join-Path $dir "$app.exe"))
    try { if($exe.ReadByte() -ne 77 -or $exe.ReadByte() -ne 90) { throw "Not a Windows executable: $app" } } finally {$exe.Dispose()}
    if($app -in @('Zheli.Settings','Zheli.Timetable','Zheli.Miao')) {
        if(-not(Test-Path -LiteralPath (Join-Path $dir 'Microsoft.UI.Xaml.dll'))) {throw "WinUI runtime missing: $app"}
        if(@(Get-ChildItem -LiteralPath $dir -Recurse -Filter '*.pri' -File).Count -eq 0){throw "Native PRI resources missing: $app"}
    }
}
if($VerifyChecksums) {
    $manifest=@(Get-Content -LiteralPath (Join-Path $root 'checksums.json') -Raw | ConvertFrom-Json)
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($entry in $manifest) {
        $relative=[string]$entry.Path
        if([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or $relative.Contains('\') -or $relative.Split('/') -contains '..' -or $relative.Split('/') -contains '.' -or $relative.Split('/') -contains '' -or $relative.Contains(':')){throw 'Invalid checksum path.'}
        if(-not $seen.Add($relative)){throw 'Duplicate checksum entry.'}
        $file=[IO.Path]::GetFullPath((Join-Path $root $relative))
        if(-not $file.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Checksum path escaped root.'}
        if($entry.SHA256 -notmatch '^[0-9a-fA-F]{64}$' -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.SHA256){throw "Checksum mismatch: $relative"}
    }
    $actual=@($files | Where-Object {-not $_.PSIsContainer -and $_.FullName -ne (Join-Path $root 'checksums.json')})
    if($actual.Count -ne $seen.Count){throw 'Publish manifest is incomplete.'}
    foreach($file in $actual){if(-not $seen.Contains([IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/'))){throw 'Publish manifest omitted an actual file.'}}
}
Write-Host 'Publish layout validated. This does not replace Windows runtime testing.'
