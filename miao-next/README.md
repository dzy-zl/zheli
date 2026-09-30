# 哲喵 · 奶油陶瓷预览版

这个目录是哲喵重构的独立实验线。它以 [DeepSeek 桌宠助手](https://github.com/sjmsltx/deepseek-desktop-pet) 的 `07bfe7863f961877fff450f4e6dc58d3b0e369f0` 提交为运行底座，使用其桌宠、对话、记忆、工具、主题插件和 Windows 便携包构建。上游代码采用 MIT 许可，原版权声明见 [LICENSE.upstream](LICENSE.upstream)。旧 `src/Zheli.Miao` 不参与此版构建。

## 已定的视觉方向

- 角色：用户提供的圆润蓝白猫，粉脸颊与肉垫、棕色手绘轮廓；待机立绘使用透明图，保持上游的呼吸与拖动交互。
- 面板：奶油色陶瓷底、暖棕字、浅蓝搪瓷色的交互强调，聊天气泡和输入槽有清楚的前后层次。
- 字体：系统安装的苹方（`PingFang SC`）；系统无此字体时由 Qt 回退。
- 基准环境：Windows 11，2560 × 1600，150% 缩放。

Preview 2 将上游聊天组件移入独立窗口：单击桌宠打开快捷对话，点击“展开完整窗口”显示左侧导航、中间对话、右侧上游信息栏；“收起”返回桌宠。两种对话模式共用原有输入、历史、附件及发送处理，切换不复制对话，也不丢弃草稿。完整窗口支持系统边框缩放。趴卧猫美术仍未完成，当前继续使用已交付的待机立绘。

## 复用与改动

`build.ps1` 会拉取固定的上游提交，把 `plugins/theme_zheli_ceramic/plugin.json` 和 `assets/cat-sitting.png` 覆盖到它现有的扩展位置，并对默认主题、苹方和角色显示名做几处受固定提交约束的替换。上游其余功能直接使用原项目。安装或更新时不覆盖用户的 `config.json`、`models.json` 或记忆文件。

本预览包会把同一张待机立绘放到上游已列出的两套角色状态图路径，避免切表情时突然出现上游人物；所以当前表情变化还没有独立逐帧美术。API Key 不进入仓库或构建产物，首次启动后通过程序设置填写。

## 本地构建

Windows PowerShell 7、Git、Python 3.12 环境：

```powershell
pwsh -File .\miao-next\build.ps1
python -m pip install -r .\miao-next\_upstream\requirements.txt 'pyinstaller>=6.21,<7' pywin32 edge-tts
python .\miao-next\tests\test_shell.py
pwsh -File .\miao-next\_upstream\tools\build_portable.ps1 -Version 2.9.1-zheli-preview.2
```

`build.ps1` 首次执行会在 `miao-next/_upstream` 拉取上游源码。更方便的方式是在此分支的 GitHub Actions 运行 `Miao Next Windows`，它会自动安装依赖并生成 `Zheli-Miao-Preview` 便携包产物。构建脚本在找不到预期的上游语句时直接失败，不会在未知的新版本上静默套用替换。

### 已知限制

- 当前是未签名便携版，不是安装器；未在用户的 Windows 11 实机做交互验收。
- 与旧 .NET 哲里课表、设置和跨应用协议尚未打通；本阶段先验证开源底座与外观。
- 上游工具、MCP 和自修改能力仍按上游项目提供。使用文件或系统操作前请在程序中核对其确认与权限设置。
