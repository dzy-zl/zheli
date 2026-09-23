$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Split-Path $PSScriptRoot -Parent
$temp=Join-Path ([IO.Path]::GetTempPath()) ('zheli-packaging-'+[Guid]::NewGuid().ToString('N'))
$passed=0;$failed=0
function Test([string]$Name,[scriptblock]$Body) {
    try {& $Body;$script:passed++;Write-Host "PASS $Name"}
    catch {$script:failed++;Write-Host "FAIL $Name : $($_.Exception.Message)"}
}
function Require([bool]$Condition){if(-not $Condition){throw 'Assertion failed'}}
function Write-Fixture([string]$Relative,[byte[]]$Bytes) {
    $file=Join-Path $temp $Relative;New-Item -ItemType Directory -Force -Path (Split-Path $file) | Out-Null
    [IO.File]::WriteAllBytes($file,$Bytes)
}
function Manifest {
    $manifest=@(Get-ChildItem -LiteralPath $temp -Recurse -File | Where-Object {$_.Name -ne 'checksums.json'} | ForEach-Object {
        @{Path=[IO.Path]::GetRelativePath($temp,$_.FullName).Replace('\','/');SHA256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}
    })
    $manifest | ConvertTo-Json | Set-Content (Join-Path $temp 'checksums.json')
}
function Validate {& (Join-Path $root 'scripts/Test-PublishLayout.ps1') -Path $temp -VerifyChecksums | Out-Null}
function Reject([scriptblock]$Body) {$rejected=$false;try{& $Body}catch{$rejected=$true};Require $rejected}
try {
    Get-ChildItem $root -Recurse -Filter '*.ps1' | ForEach-Object {
        $file=$_
        Test "PowerShell parser: $($file.Name)" {
            $tokens=$null;$errors=$null
            [void][Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
            if($errors.Count){throw ($errors.Message -join '; ')}
        }
    }
    foreach($app in @('Zheli.Settings','Zheli.Timetable','Zheli.Miao','Zheli.PetHost','Zheli.CoreHost','Zheli.Timetable.Host')) {
        foreach($name in @("$app.exe","$app.dll","$app.deps.json","$app.runtimeconfig.json",'coreclr.dll','hostfxr.dll','hostpolicy.dll')){Write-Fixture "$app/$name" ([byte[]](77,90,0,0))}
        if($app -in @('Zheli.Settings','Zheli.Timetable','Zheli.Miao')) {Write-Fixture "$app/Microsoft.UI.Xaml.dll" ([byte[]](1));Write-Fixture "$app/resources.pri" ([byte[]](1))}
    }
    Manifest
    Test 'complete synthetic publish layout accepted' {Validate}
    Test 'missing native resource rejected' {
        Remove-Item (Join-Path $temp 'Zheli.Settings/resources.pri');Reject {Validate};Write-Fixture 'Zheli.Settings/resources.pri' ([byte[]](1));Manifest
    }
    Test 'non-Windows entry point rejected' {
        Write-Fixture 'Zheli.Miao/Zheli.Miao.exe' ([byte[]](127,69,76,70));Manifest;Reject {Validate};Write-Fixture 'Zheli.Miao/Zheli.Miao.exe' ([byte[]](77,90,0,0));Manifest
    }
    Test 'modified binary rejected' {Write-Fixture 'Zheli.Miao/Zheli.Miao.dll' ([byte[]](1,2,3));Reject {Validate};Manifest}
    Test 'unlisted extra file rejected' {Write-Fixture 'unexpected.txt' ([byte[]](1));Reject {Validate};Remove-Item (Join-Path $temp 'unexpected.txt');Manifest}
    Test 'duplicate manifest entry rejected' {
        $m=@(Get-Content (Join-Path $temp 'checksums.json') -Raw | ConvertFrom-Json);@($m)+@($m[0]) | ConvertTo-Json | Set-Content (Join-Path $temp 'checksums.json');Reject {Validate};Manifest
    }
    Test 'parent traversal rejected' {
        $m=@(Get-Content (Join-Path $temp 'checksums.json') -Raw | ConvertFrom-Json);$m[0].Path='../outside';$m | ConvertTo-Json | Set-Content (Join-Path $temp 'checksums.json');Reject {Validate};Manifest
    }
    Test 'absolute manifest path rejected' {
        $m=@(Get-Content (Join-Path $temp 'checksums.json') -Raw | ConvertFrom-Json);$m[0].Path=[IO.Path]::GetFullPath($temp);$m | ConvertTo-Json | Set-Content (Join-Path $temp 'checksums.json');Reject {Validate};Manifest
    }
    Test 'alternate path spelling cannot conceal missing file' {
        $m=@(Get-Content (Join-Path $temp 'checksums.json') -Raw | ConvertFrom-Json)
        $m[0].Path=$m[1].Path.Replace('/','//');$m[0].SHA256=$m[1].SHA256
        $m | ConvertTo-Json | Set-Content (Join-Path $temp 'checksums.json');Reject {Validate};Manifest
    }
    if(-not $IsWindows) {
        Test 'symlink payload rejected' {
            New-Item -ItemType SymbolicLink -Path (Join-Path $temp 'link') -Target (Join-Path $temp 'Zheli.Miao/Zheli.Miao.dll') | Out-Null
            Reject {Validate};Remove-Item (Join-Path $temp 'link')
        }
    }
} finally {if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Recurse -Force}}
Write-Host "RESULT: $passed passed, $failed failed; script and synthetic packaging checks only"
if($failed){exit 1}
