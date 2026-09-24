# 依赖、字体与素材

## 本轮新增：Fluent UI System Icons

课表的“本周、添加课程、搜索”图标来自 [Microsoft Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons)，分别对应 `assets/Calendar/SVG/ic_fluent_calendar_24_regular.svg`、`assets/Add/SVG/ic_fluent_add_24_regular.svg`、`assets/Search/SVG/ic_fluent_search_24_regular.svg`。使用原图路径，填充色由 `#212121` 调整为 `#8298B2`。上游 MIT 版权与全文保存在 `src/Zheli.DesignSystem/Assets/Fluent/LICENSE`，发布时与资源一起复制。其他依赖许可仍逐项核对，不能把整个程序概括为 MIT。


## 0.2.3 安装器资源

Inno Setup 固定为 6.5.4，编译器来自 `jrsoftware/issrc` 官方 GitHub release，下载 SHA-256 固定为 `fa73bf47a4da250d185d07561c2bfda387e5e20db77e4570004cf6a133cc10b1`。安装器编译器不包含在源码包中。

简体中文语言文件取自同一版本标签下 `Files/Languages/Unofficial/ChineseSimplified.isl`，保持原文；许可全文保留于 `installer/licenses/Inno-Setup-6.5.4-license.txt`。精确 URL 与资源哈希见 `installer/third-party-sources.json`。按该固定版本许可保留版权、许可和免责声明；未将其标注为 MIT。

Windows 发布阶段从实际 NuGet 缓存复制依赖 license/notice/nuspec，并从 SPDX `license-list-data` 的 `v3.27.0` 标签取得 MIT 与 Apache-2.0 全文，随各程序放入 `ThirdPartyNotices`。此收集流程已在最终 Windows CI 执行通过，不代替正式发行时对所有产物的许可核对。固定版本的上游中文翻译有部分消息缺失，安装器编译会提示并回退英文，记录为当前测试版限制。

## 运行与构建依赖

使用 NuGet 官方源 `https://api.nuget.org/v3/index.json`；版本和传递依赖见各项目 `packages.lock.json`。此源码包不包含下载的 SDK、NuGet 二进制缓存或字体文件。

| 依赖 | 版本/用途 | 来源与许可记录 |
| --- | --- | --- |
| .NET SDK | 10.0.100 基线，可滚动到同主版本较新功能带 | [官方下载](https://dotnet.microsoft.com/download/dotnet/10.0)；依照各 SDK/运行时附带许可 |
| Windows App SDK | 2.5.1，WinUI 3 | [NuGet](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.5.1)；包内 Microsoft Software License Terms，不能将整个发行包笼统标为 MIT |
| Microsoft.Data.Sqlite | 10.0.9 | [NuGet](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.9)，包声明 MIT |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.5，覆盖旧版原生 SQLite 依赖 | [NuGet](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/3.0.5)，包声明 Apache-2.0；传递 SQLite 版本见锁文件 |
| PdfPig | 0.1.16，PDF文字提取 | [NuGet](https://www.nuget.org/packages/PdfPig/0.1.16) / [官方源码](https://github.com/UglyToad/PdfPig)，包声明 Apache-2.0 |

`verification/dependency-licenses.json` 从实际还原的 NuGet 元数据提取每个依赖的许可表达式或包内许可文件名。正式二进制分发需要随包收集这些声明及相应全文，不能只放这一页；当前尚未发布二进制。

## 官方技术参考

- [WinUI 非打包部署](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)
- [Windows App SDK 下载与版本](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)
- [WinUI C# 单实例激活](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-single-instance)
- [DeepSeek 模型列表](https://api-docs.deepseek.com/api/list-models/)
- [DeepSeek 聊天接口](https://api-docs.deepseek.com/api/create-chat-completion/)

实现按这些官方接口编写；没有复制第三方产品界面或图标。设置面板说明 Windows Acrylic 仅为当前材质实现，不把它宣称为 Apple 原版视觉效果。

## 字体与图形

字体优先使用用户电脑已安装的 PingFang SC，回退 Microsoft YaHei UI、Segoe UI。源码包和安装脚本不包含或下载苹方。

私有交付源码包的 `assets/reference/miao-character.jpg` 是用户提供的哲喵形象，`selected-icon-directions.png` 是候选方向图；两者均不包含在本公开仓库或当前安装包中。哲喵选 A、课表选 C 的决定不变。当前桌宠是工程内独立绘制的临时矢量形象，正式图标/角色资产导出仍待完成。
