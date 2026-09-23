# 三个测试版安装包

当前状态：0.2.3 三个 EXE 已生成，Windows 原生发布和安装启动/卸载自动测试通过。[下载安装包](https://github.com/dzy-zl/zheli/actions/runs/35932215812/artifacts/10781757593)，[查看通过记录](https://github.com/dzy-zl/zheli/actions/runs/35932215812)。这是未签名测试版，不能视为正式发行。

下载 ZIP 后解压，依次运行 Settings、Timetable、Miao 安装器，或只运行所需软件的安装器。安装后通过开始菜单的“哲里”文件夹启动。第一次安装无需打开任何哲里程序，全部安装完再启动。

## 使用 GitHub Windows 构建

1. 准备哲里专用仓库，建议私有。将本目录内容放到仓库根目录，确保 `.github/workflows/windows-build.yml` 一并提交；不要再套一层 `zheli-native` 文件夹。
2. 推送到 `main` 或 `build/任意名称` 会触发 `Windows test installers`。也可在 Actions 页手动运行；默认分支必须已包含工作流。
3. 工作流在 Windows runner 安装 .NET SDK 10.0.100，执行打包校验、核心测试与客户端离线测试，发布完整 Windows 原生资源。
4. 下载并验证固定版本 Inno 编译器，生成三个安装器，再执行安装、启动、共存与卸载测试。无需 API 密钥，测试不访问个人文件。
5. 全部通过后，在对应运行页面下载 `Zheli-Windows-x64-Test-Installers`。失败时下载 `Zheli-Windows-Diagnostics`，修复后重跑；不能将失败构建视为成品。

安装器目录包含 `Zheli-Settings-0.2.3-win-x64-Setup.exe`、`Zheli-Timetable-0.2.3-win-x64-Setup.exe`、`Zheli-Miao-0.2.3-win-x64-Setup.exe`、`SHA256.json` 和构建元数据。产物保留 14 天，下载后另行保存。流水线的 45 分钟是超时上限，不是交付时间承诺。

## 在 Windows 本机构建

需要 Windows 11 x64、PowerShell 7、.NET SDK 10.0.100，以及 NuGet/GitHub 网络访问。在源码根目录依次运行：

```powershell
pwsh -NoProfile -File .\tests\Packaging.Tests.ps1
pwsh -NoProfile -File .\scripts\build-windows.ps1
pwsh -NoProfile -File .\scripts\install-inno-compiler.ps1
pwsh -NoProfile -File .\scripts\build-installers.ps1
```

必须逐条成功再运行下一条。原生发布位于 `artifacts/win-x64`，安装器位于 `artifacts/installers-<构建编号前12位>`。安装器构建拒绝跳过测试的发布、非 Release 发布及校验失败的文件。重复编译同一构建前，先另存已有安装器输出；脚本不覆盖它。

`test-installers-windows.ps1` 只允许一次性的 GitHub Windows runner，且发现已有哲里数据或安装记录就拒绝运行。不要在个人电脑伪造 runner 环境变量来运行它；个人电脑按 WINDOWS-ACCEPTANCE.md 手工验收。

## 安装与卸载行为

- 安装到当前用户 `%LOCALAPPDATA%\Programs\Zheli`，无需管理员权限。业务数据仍在 `%LOCALAPPDATA%\Zheli`。
- 课表、哲喵可分别安装；缺少公共组件时，自动调用内置的哲里设置安装器。哲里桌面不参与。
- 哲里设置拥有设置 UI 与 CoreHost；课表拥有课表 UI 与 Timetable.Host；哲喵拥有对话 UI 与 PetHost。
- 每个软件有自己的 Windows 卸载记录。同一构建可以重装；不同构建暂须先卸载旧程序，数据保留。
- 安装/卸载前退出所有哲里软件与后台服务。当前服务尚无统一退出入口，可在任务管理器确认 CoreHost、Timetable.Host、PetHost 已结束，或重启后先执行安装/卸载。安装器不会强制结束它们。
- 卸载顺序为课表和哲喵在前，哲里设置在后。卸载保留数据和凭据；不将卸载解释为清空个人资料。
- 旧便携安装目录需按旧方式移除程序后再安装；不允许直接覆盖未知目录。

## 验证范围

自动测试检查前置组件安装、三个窗口启动、桌宠进程存活、同构建重装、跨构建拒绝、卸载依赖保护及数据文件保留。启动几秒无退出不等于业务流程或视觉验收通过。

仍需 Windows 11、2560×1600、150% 缩放实机检查，完成 IPC/凭据/权限、课表业务、备份与异常恢复验收。安装器暂未签名，最终图标和角色资产尚未完成；正式发行标准见 STATUS.md。
