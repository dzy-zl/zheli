# 验证记录与版本边界

## 0.2.3 本轮结果

Windows 最终验证已完成：运行 [35932215812](https://github.com/dzy-zl/zheli/actions/runs/35932215812)，提交 `a2bdb280224edcb1dae5a085626251eebe795527`，Windows Server 2025 x64、SDK 10.0.100。96 项核心、28 项离线客户端、19 项打包检查通过；原生发布、三个安装器编译、窗口/桌宠启动及共存卸载流程通过。机器可读记录见 `windows-ci.json`。下面的准备阶段和 0.2.2 日志保留为历史，不能与本次运行混用。

## 0.2.3 安装器准备阶段历史记录

环境：Linux x64，PowerShell 7.5.3。`packaging-tests.txt` 记录 20 项通过、0 项失败，包括 10 个脚本解析检查和 10 项合成载荷校验。合成文件只用于验证打包规则，不是真实应用程序。

`installer-preparation.json` 记录提交 GitHub 前的状态，当时尚未进行 Windows 构建。该阻塞现已解除，以本页开头的 Windows 最终记录为准。下表及其日志属于 0.2.2 或更早版本，不能据此认定 0.2.3 已通过相同测试。

复测本轮脚本检查：`pwsh -NoProfile -File tests/Packaging.Tests.ps1`。Windows 跳过 Linux 符号链接检查，预期 19 项；实际通过数以运行日志为准。

## 0.2.2 历史记录（以下“本轮”均指 0.2.2）

2026-09-23，开发版 0.2.2，Linux x64，.NET SDK 10.0.100。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 核心逻辑、SQLite、备份、草稿、帧协议、多格式提取、索引、权限、桌宠位置与手势 | 99 通过，0 失败 | core-tests.txt |
| 生产 DeepSeek 客户端离线传输测试 | 28 通过，0 失败 | deepseek-offline.txt |
| 真实 DeepSeek API | 保留 0.2.1 的模型列表、两条合成问答通过记录；本轮未重复调用 | deepseek-live.txt |
| 六个应用/服务的 Windows 目标托管代码 | 全部编译成功，0 警告、0 错误 | managed-summary.json 与 managed-*.txt |
| NuGet 直接及传递依赖已知漏洞检查 | 保留 0.2.0 检查记录；本轮没有新增或升级第三方依赖 | dependency-audit.txt |
| 完整 WinUI 资源生成 | 上一轮已确认 Linux 无法执行 Windows MakePri.exe，本轮未重复执行 | windows-resource-blocked-on-linux.txt（0.1.0历史记录） |
| Windows UI、安装、IPC 身份、凭据管理器 | 未执行；Linux 实网测试不能代替 Windows 集成验收 | docs/WINDOWS-ACCEPTANCE.md |

核心测试命令：

```text
dotnet run --project tests/Zheli.Tests/Zheli.Tests.csproj -c Release -p:BuildInParallel=false -m:1
```

托管编译检查对六个入口分别执行以下命令，仅为检查 C# 代码及 API 引用。跳过 Windows 资源和自包含打包的参数**不能用于发行或证明软件已可运行**：

```text
dotnet build src/<项目>/<项目>.csproj -m:1 -p:BuildInParallel=false -p:WindowsAppSDKSelfContained=false -p:AppxGeneratePriEnabled=false -p:AppxGeneratePrisForPortableLibrariesEnabled=false -r win-x64 -v minimal
```

依赖检查：

```text
dotnet list Zheli.slnx package --vulnerable --include-transitive --no-restore
```

未报告已知漏洞不等于安全审计通过。0.1.0已修复过旧SQLite原生依赖问题，0.2.0新增PdfPig并检查依赖；0.2.1/0.2.2依赖版本保持不变，保留当时的漏洞检查与许可证元数据，不将旧记录称为本轮重新审计。PowerShell在本环境未安装，构建/安装/卸载脚本尚未执行。Windows发行构建请使用正式的`scripts/build-windows.ps1`，不携带上面的资源跳过参数。

本轮核心测试与客户端测试共 127 项通过。新增 20 项覆盖桌宠默认位置、负坐标屏幕、DPI 尺寸变化、工作区变化、显示器回退、边缘约束、鼠标抖动、拖动/点击分离、丢失捕获和状态持久化。窗口消息、原生鼠标事件、托盘和实际动画仍需 Windows 验收。

独立客户端项目没有 NuGet 包依赖，直接链接生产源码；在受限环境恢复依赖时使用官方 NuGet 包的本地缓存源，并关闭恢复阶段的在线审计，不修改工程的默认官方源。0.2.1 的真实 API 测试仅包含两条合成问题：课程名称/教室与文件片段/来源编号；密钥临时输入，没有包含在日志和交付包中。没有使用真实课程、电脑文件或既有聊天历史。

0.2.0 新增27项测试：六类格式提取/来源定位、PPT关系顺序、Excel缓存值、Word段落、PDF空页提示、DTD/外部关系限制、索引持久化、同长度同时间戳修改识别、撤销/禁读子目录、私密目录优先、发送前源文件变化拒绝、异常文档与取消。当前 Windows 核心测试跳过3项Linux符号链接测试，预期96项，另有28项客户端测试；本轮没有替用户在Windows上执行。
