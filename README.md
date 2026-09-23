# 哲里 · Windows 原生开发版 0.2.3

**三个测试版安装包已生成并通过 Windows 自动验证。** [下载安装包 ZIP（约 594 MB，需登录 GitHub）](https://github.com/dzy-zl/zheli/actions/runs/35932215812/artifacts/10781757593)。包含哲里设置、哲里课表、哲喵三个 EXE 安装器，以及 SHA-256 校验清单。此产物保留至 2026-10-07，之后可重新运行构建。

0.2.3 完成六个程序的 Windows 原生发布、三个安装器编译，以及安装/启动/共存卸载自动检查。96 项核心测试、28 项客户端离线测试、19 项打包检查通过。[通过记录](https://github.com/dzy-zl/zheli/actions/runs/35932215812)对应提交 `a2bdb280224edcb1dae5a085626251eebe795527`。构建与安装方法见 [安装包构建](docs/INSTALLER-BUILD.md)。

0.2.2 补齐桌宠位置与隐藏状态记忆、跨屏缩放后的可见范围恢复、拖动与点击区分，以及呼吸/眨眼/耳朵/尾巴轻量动画。降低动态效果、远程桌面或软件渲染时使用静态角色。详见 [桌宠使用与验收](docs/PET-DESKTOP.md)。最终角色资产与 Windows 实机视觉验收仍待完成。

0.2.1 已完成真实 DeepSeek 模型列表与两条合成问答联调，修复超时误报为主动停止，并补齐分类错误提示。完整记录和复测方法见 [DeepSeek 联调](docs/DEEPSEEK-TESTING.md)。密钥不包含在源码包内；Windows 凭据存储和原生界面仍须实机验收。

这是哲里设置、哲里课表、哲喵三个独立软件的源码工程。已实现真实本地数据保存与应用间接口，不是网页演示；目前仍是开发版本，尚未达到正式发行条件。

GitHub 的 Code → Download ZIP 下载的是源码；请使用上面的安装包链接。自动验证运行于 Windows Server 2025，不代替你电脑上的 Windows 11、150% DPI 视觉与完整业务验收。安装器未签名，正式图标和角色资产尚未完成；请阅读 [实现与验收状态](docs/STATUS.md)。

## 在 Windows 11 上构建

需要 x64 Windows 11、.NET 10 SDK、PowerShell 7、网络连接以还原 NuGet 依赖。推荐安装支持 .NET 10 的 Visual Studio 与 Windows 应用开发工具。工程通过 NuGet 使用 Windows App SDK，不需要下载本包以外的字体。

在解压后的源码根目录运行：

```powershell
pwsh -File .\scripts\build-windows.ps1
```

脚本先运行核心测试，再发布六个执行程序到 `artifacts\win-x64`。三个主软件分别附带所需后台组件，发布目录必须保持同级关系。脚本不跳过 Windows 资源生成，也不使用本轮 Linux 检查的跳过参数。

构建成功后，可直接在发布目录打开 `Zheli.Settings\Zheli.Settings.exe`。生成独立安装器：

```powershell
pwsh -File .\scripts\install-inno-compiler.ps1
pwsh -File .\scripts\build-installers.ps1
```

输出到 `artifacts/installers-<构建编号前12位>`。课表或哲喵安装器会在缺少公共组件时自动安装内置的哲里设置，不依赖哲里桌面；设置窗口无需常驻。三个安装记录分别维护，卸载课表不删除哲喵。哲里设置须在依赖它的应用卸载后再卸载，业务数据保留。不同构建暂不允许原地覆盖。安装器尚未签名。旧 `install-user.ps1` 仅保留给便携开发目录，不可与新安装器混用。

首次使用：

1. 打开哲里设置，选择主题、玻璃通透度、强调色、字号和减少动态效果。
2. 打开哲里课表，创建学期，明确第 1 周的周一、实际开学日及每天作息。
3. 点击空白节次录课；默认只添加查看中的这一周。重复课程需要填写周次并取消“仅这一次”。
4. 在设置中分别授权哲喵本地查询、课程单次修改、云端发送。默认均关闭。
5. 需要 AI 对话时，在设置中填入自己的 DeepSeek 密钥，获取并选择可用模型。密钥只进入 Windows 凭据管理器。
6. 哲喵可显示桌宠；点击对话，拖动移动，右键或托盘菜单管理。`Ctrl+Alt+Space` 唤起对话。
7. 在哲里设置 → 文件知识库中添加文件夹。默认只允许本地读取；私密目录始终禁止发送内容。打开哲喵，点击“本地文件搜索”，使用关键词检索 PDF、DOCX、XLSX、PPTX、TXT 和 Markdown。
8. 需要 AI 根据资料回答时，单独允许相应文件夹云端发送，检索后勾选“本次选择文件片段”，发送时逐条预览并选择最多 6 个片段。不会自动上传整个文件夹或整份文件。详见 [知识库使用与边界](docs/KNOWLEDGE.md)。

## 项目结构

| 项目 | 职责 |
| --- | --- |
| Zheli.Settings | 统一外观、课表显示、哲喵连接与授权、课表备份恢复 |
| Zheli.Timetable | 课表窗口、手动录课、搜索、详情与单次变动 |
| Zheli.Miao | 独立会话、DeepSeek 客户端、本地文本搜索与课表操作入口 |
| Zheli.PetHost | 透明桌宠窗口、托盘、快捷键；属于哲喵安装组件 |
| Zheli.CoreHost | 全局设置数据库的唯一服务所有者 |
| Zheli.Timetable.Host | 课表数据库的唯一服务所有者，主窗口关闭后也可响应查询 |
| Zheli.DesignSystem | 三个窗口共用的原生控件、字体、玻璃、主题与窗口框架 |
| Zheli.Domain / Contracts | 课程规则、版本化操作、稳定接口协议 |
| Zheli.Storage / Bridge / Platform | SQLite、备份、进程通信、凭据与本机能力 |

设置软件是统一配置入口；组件库作为共享源码项目和程序集供应用引用，不要求显示设置窗口才能运行。三个主窗口各自只有一个实例，重复启动应转交给原窗口，此行为待 Windows 验收。

## 本地数据

数据位于 `%LOCALAPPDATA%\Zheli`，程序位于 `%LOCALAPPDATA%\Programs\Zheli`。两者分离；卸载脚本保留全部业务数据和备份。窗口位置与草稿使用独立 JSON 文件，不是业务数据库。

业务数据库采用 SQLite WAL、事务、版本检查和操作日志。课程共享信息、安排历史版本、单次变动在领域模型中分离。当前以带版本的聚合 JSON 存入 SQLite，尚未拆为规范化课程表；大量会话的日志空间治理仍待实现。

加密课表备份使用 `.zhelibackup` 格式。恢复会先检查密码、完整性和课程规则，再创建本机保护快照并事务替换课表。它不包含 DeepSeek 密钥，也不跨应用恢复。备份密码遗失无法恢复。普通本机 SQLite 快照未加密。

## 验证与继续开发

```powershell
dotnet run --project tests/Zheli.Tests/Zheli.Tests.csproj -c Release
```

0.2.2 的 Linux 核心测试历史结果见 `verification/core-tests.txt`；0.2.3 本轮打包检查见 `verification/packaging-tests.txt`，两者不能混作同一版本验收。Windows 核心测试会跳过依赖 Linux 符号链接的 3 项。图形界面验收步骤见 [WINDOWS-ACCEPTANCE.md](docs/WINDOWS-ACCEPTANCE.md)，接口和数据设计见 [ARCHITECTURE.md](docs/ARCHITECTURE.md)。

用户提供的哲喵形象和此前图标方向图仅保留在私有交付源码包，未上传到本公开仓库。当前桌宠使用临时矢量实现；尚未把已选哲喵 A、课表 C 制作为正式多尺寸图标。未打包苹方字体。依赖与素材来源见 [THIRD-PARTY.md](docs/THIRD-PARTY.md)。
