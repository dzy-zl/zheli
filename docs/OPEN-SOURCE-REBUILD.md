# 哲里重构：开源项目筛选与复用记录

更新：2026-09-24。当前仓库仍为 0.2.3 原生试验版。本记录以用户确定的 Windows 11、16:10、150% 缩放、本地数据、独立安装和跨应用互通为边界。项目提供参考与可复用模块，不代表现有哲里已经具备对应功能。

## 对照项目

| 哲里目标 | 上游项目 | 已确认许可 / 技术栈 | 具体借鉴方式 | 当前决策 |
| --- | --- | --- | --- | --- |
| 课表、上课提醒、临时调课 | [ClassIsland](https://github.com/ClassIsland/ClassIsland) | 主程序 [GPL-3.0](https://github.com/ClassIsland/ClassIsland/blob/master/LICENSE.txt)；Avalonia/.NET，重点面向班级屏幕 | 学习“当前课程 / 下一节 / 倒计时 / 临时调整”的交互流程。大学周次、单双周、个人课程模型沿用哲里领域层 | 只参考功能，不复制代码、图标或界面 |
| 哲里日程：收集箱、任务、重复、专注 | [Super Productivity](https://github.com/super-productivity/super-productivity) | [MIT](https://github.com/super-productivity/super-productivity/blob/master/LICENSE)；TypeScript / Angular / Electron | 评估任务状态、子任务、项目、时间块和本地持久化；日程单独安装，通过版本化协议向桌面入口提供摘要 | 候选代码来源；先设计数据映射，禁止直接覆盖现有数据库 |
| 哲里桌面：快捷唤起和全局搜索 | [Flow Launcher](https://github.com/Flow-Launcher/Flow.Launcher) | [MIT](https://github.com/Flow-Launcher/Flow.Launcher/blob/dev/LICENSE)；WPF/.NET | 借鉴搜索结果排序、快捷键、插件接口、应用和文件启动模式；明确评估 WPF 与 WinUI 3 的桥接成本 | 候选代码来源，不能把完整启动器直接塞进设置程序 |
| 原生窗口和托盘 | [WinUIEx](https://github.com/dotMorten/WinUIEx) | [MIT](https://github.com/dotMorten/WinUIEx/blob/main/LICENSE)；WinUI 3 | 比较窗口状态恢复、窗口管理、托盘支持与现有 ShellWindow / PetHost 的代码后分模块替换 | 候选依赖，待 Windows 实测 |
| 原生控件和状态展示 | [Windows Community Toolkit](https://github.com/CommunityToolkit/Windows) | [MIT](https://github.com/CommunityToolkit/Windows/blob/main/License.md)；有 WinUI 3 控件 | 使用成熟控件构建设置表单、空状态、加载状态和窄窗口响应布局，逐页取代直接堆砌按钮的代码 | 候选依赖，需固定包版本和锁文件 |
| 导航和操作图标 | [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) | [MIT](https://github.com/microsoft/fluentui-system-icons/blob/main/LICENSE)；SVG | 已引入 Add、Search、Calendar 三枚 24px 图标，并保留完整许可文本；颜色调整为适合双主题的 #8298B2 | **本分支已落地** |

## 本分支的实际改动

- 从 Microsoft 的 Fluent UI System Icons 引入三枚 SVG，改变填充色，并把上游 MIT 文本和资源一起随程序发布。
- 共用 Ui.IconButton 显示图标与可读文字，保留辅助功能名称与工具提示；课表的“本周、添加课程、搜索”先接入。
- 这是视觉和资源复用的第一步，不能把三个图标宣称为一次“彻底重构”。现有页面的信息密度、导航层级、课表卡片和哲喵会话仍需重做。

## 后续工程顺序

1. **先定体验基线。** 在 Windows 11 的 2560×1600 / 150% 实机截取课表、设置、哲喵的真实界面，标出可用宽度、滚动、焦点与空/错误状态；以用户反馈而非 CI 的“进程能启动”作为验收。
2. **重建共享设计系统。** 以真实 WinUI 控件封装导航、工具栏、表单、对话框、卡片、响应布局和主题令牌。替换目前压缩后截取前两个汉字、窄窗口横向滚动的临时方案。
3. **先完成课表的日常主流程。** 周视图 → 当前/下一节 → 快捷添加 → 单次调课 → 冲突提示 → 撤销；保留领域模型、事务和备份能力，逐屏对照 ClassIsland 的信息呈现方式，但重新设计适合大学个人使用的交互。
4. **按独立程序建立哲里日程、哲里桌面。** 先定义任务/提醒和搜索的协议与数据迁移，再评估 Super Productivity、Flow Launcher 可移植的模块。哲喵优先重做会话和权限确认，现有 DeepSeek 接口继续由凭据库管理。
5. **每次移植保留出处。** 固定上游提交或包版本、原许可/版权、改动清单与对应测试；GPL 等强 copyleft 项目不得未经整体许可方案审查就拼入当前仓库。

## 验收门槛

Windows 构建与测试通过后仍要做真实 Windows 11 视觉和键盘操作验收，包括 150% 缩放、窄窗口、亮/暗主题、中文长文本、无网启动、离线数据迁移、安装/卸载及失败恢复。CI 当前无法证明设计效果已达标。
