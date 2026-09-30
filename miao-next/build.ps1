param(
    [string]$SourceDir = (Join-Path $PSScriptRoot '_upstream'),
    [switch]$Package
)

$ErrorActionPreference = 'Stop'
$upstreamUrl = 'https://github.com/sjmsltx/deepseek-desktop-pet.git'
$upstreamCommit = '07bfe7863f961877fff450f4e6dc58d3b0e369f0'

if (Test-Path $SourceDir) {
    throw "工作目录已存在：$SourceDir。请传入一个新的 -SourceDir，避免覆盖现有源码或个人资料。"
}
git clone --filter=blob:none --no-checkout $upstreamUrl $SourceDir
if ($LASTEXITCODE -ne 0) { throw '上游克隆失败' }
git -C $SourceDir -c advice.detachedHead=false checkout --detach $upstreamCommit
if ($LASTEXITCODE -ne 0) { throw '固定上游提交检出失败' }
if ((git -C $SourceDir rev-parse HEAD).Trim() -ne $upstreamCommit) {
    throw '上游提交校验不一致'
}

$pluginSrc = Join-Path $PSScriptRoot 'plugins/theme_zheli_ceramic'
$pluginDst = Join-Path $SourceDir 'plugins/theme_zheli_ceramic'
Copy-Item $pluginSrc $pluginDst -Recurse

$cat = Join-Path $PSScriptRoot 'assets/cat-sitting.png'
if (-not (Test-Path $cat)) { throw "缺少桌宠立绘：$cat" }
foreach ($role in @('flash', 'pro')) {
    $sprites = @(Get-ChildItem (Join-Path $SourceDir "assets/$role") -Filter "${role}_*.png" -File)
    if ($sprites.Count -eq 0) { throw "上游找不到 $role 立绘" }
    foreach ($sprite in $sprites) {
        Copy-Item $cat $sprite.FullName -Force
    }
    Write-Host "哲喵立绘已用于 $role 的 $($sprites.Count) 个状态路径"
}

function Replace-Once([string]$Path, [string]$Old, [string]$New) {
    $text = [System.IO.File]::ReadAllText($Path)
    $first = $text.IndexOf($Old, [System.StringComparison]::Ordinal)
    if ($first -lt 0 -or $text.IndexOf($Old, $first + $Old.Length, [System.StringComparison]::Ordinal) -ge 0) {
        throw "固定上游语句缺失或不唯一：$Path | $Old"
    }
    $updated = $text.Substring(0, $first) + $New + $text.Substring($first + $Old.Length)
    [System.IO.File]::WriteAllText($Path, $updated, [System.Text.UTF8Encoding]::new($false))
}

$ui = Join-Path $SourceDir 'desktop_pet.py'
Copy-Item (Join-Path $PSScriptRoot 'overlay/zheli_shell.py') (Join-Path $SourceDir 'zheli_shell.py')
Replace-Once $ui '    pet = PetWidget()' "    pet = PetWidget()`n    from zheli_shell import install`n    install(pet)"
Replace-Once $ui '    def toggle_chat_panel(self):' "    def toggle_chat_panel(self):`n        if hasattr(self, '_zheli_shell'):`n            shell = self._zheli_shell`n            return shell.close() if shell.isVisible() else shell.present('quick')"
Replace-Once $ui "self._saved_theme = str(cfg.get('theme') or 'default')" "self._saved_theme = str(cfg.get('theme') or 'theme_zheli_ceramic')"
Replace-Once $ui "str(getattr(self, '_saved_theme', 'default') or 'default')" "str(getattr(self, '_saved_theme', 'theme_zheli_ceramic') or 'theme_zheli_ceramic')"
Replace-Once $ui '    app = QApplication(sys.argv)' "    app = QApplication(sys.argv)`n    app.setFont(QFont('PingFang SC'))"

$models = Join-Path $SourceDir 'model_registry.py'
Replace-Once $models "'display_name': 'V4 Flash'," "'display_name': '哲喵 · 快聊',"
Replace-Once $models "'display_name': 'V4 Pro'," "'display_name': '哲喵 · 深思',"
Replace-Once $models "'浅蓝和服 · 快言快语'" "'圆润蓝白猫 · 快捷对话'"
Replace-Once $models "'深蓝女仆 · 深思熟虑'" "'圆润蓝白猫 · 深度思考'"

$stage = Join-Path $SourceDir 'tools/stage_portable.py'
Replace-Once $stage "'使用说明.txt', 'config.example.json', 'models.json.example']" "'使用说明.txt', 'config.example.json', 'models.json.example', 'LICENSE']"
Write-Host "已装配上游 $upstreamCommit + 哲喵主题及素材"

if ($Package) {
    $env:DP_PYTHON = (Get-Command python -ErrorAction Stop).Source
    & (Join-Path $SourceDir 'tools/build_portable.ps1') -Version '2.9.1-zheli-preview.2'
    if ($LASTEXITCODE -ne 0) { throw '上游便携包构建或自检失败' }
}
