# PromptDC

Windows 桌面提示词增强工具。选中输入框中的文字，按 **Ctrl+Alt+E**，松开按键并等待，即可使用 DeepSeek 增强并直接粘贴回原选区。不会发送 Enter，不会自动提交聊天消息。

项目使用 **C# / .NET 8 / Windows Forms**，通过 Windows API 注册全局快捷键并检测输入状态，通过 HTTPS 调用 DeepSeek Chat Completions 接口。发布脚本使用 PowerShell，项目没有声明第三方 NuGet 包依赖。

## 主要功能

- 全局快捷键触发，可在设置中修改快捷键。
- 增强后直接粘贴替换选中的原文，不需要在不同软件间手动搬运。
- 提供“自动识别”“编程优先”“通用表达”三种模式，可填写额外编辑偏好。
- 提供 API Key、模型配置和连接测试。
- 关闭或最小化窗口后在系统托盘继续运行，托盘菜单支持取消请求和退出。
- 请求期间输入位置变化时暂停自动替换，最近原文和结果可在窗口中复制。
- 使用 Windows DPAPI 加密保存 API Key，并尽量恢复操作前的剪贴板内容。

## 环境要求与安装运行

- Windows 桌面环境；发布脚本的目标架构是 Windows x64。
- 从源码运行或构建需要 .NET 8 SDK（支持 Windows 桌面开发）。
- 使用增强与连接测试功能，需要网络和有效的 DeepSeek API Key，调用费用由用户自己的账户承担。
- 程序通过 Windows Forms 和 Windows 原生 API 实现，当前源码不支持直接在 macOS 或 Linux 上运行。

```powershell
git clone https://github.com/wuhu0252/PromptDC.git
cd PromptDC
dotnet build -c Release
dotnet run -c Release
```

也可以按下方“开发与打包”章节生成本机可运行版本。Git 仓库只包含源码和文档，`dist/` 等构建产物不提交；首次克隆后需要运行或构建项目。预编译安装包的发布计划待补充。

## 使用

1. 使用 `dotnet run -c Release` 启动，或打包后打开 `dist/PromptDC/PromptDC.exe`。
2. 在窗口内输入自己的 DeepSeek API Key，点击“保存并启用”。不需要将 Key 发给开发者。
3. 可点击“测试连接”，它会产生一次小额 API 调用。
4. 在窗口右侧示例框、Codex 或浏览器聊天输入框里选中文字，按快捷键，松开后保持选区，等待替换。
5. 增强偏了可尝试原编辑器的 `Ctrl+Z`；也可在“最近结果”复制原文。

关闭或最小化窗口后在系统托盘继续运行。双击托盘图标打开窗口；右键菜单可查看结果、取消请求或退出。没有设置开机自启。

## 使用示例

在程序右侧示例框，或支持标准复制粘贴的聊天输入框中输入：

```text
帮我排查登录接口偶尔返回 500 的问题，先看看项目现有代码，别改接口返回格式。
```

选中这段文字，按 **Ctrl+Alt+E** 并松开。程序将选中文字交给模型编辑，并在输入位置未变化且结果完整时粘贴替换原文。增强后的具体文本由模型返回，可能随输入、模型及编辑偏好变化。

如果等待期间切换了窗口或修改了选区，可从托盘打开“最近结果”复制增强内容。程序没有自动读取项目代码；示例中的“先看看项目现有代码”是发送给后续编程 AI 的任务要求。

## 目录结构

```text
PromptDC/
├── PromptDC.csproj         # .NET 8 Windows Forms 项目配置
├── Program.cs              # 程序入口、单实例与启动参数
├── MainForm.cs             # 设置界面、托盘与请求调度
├── Native.cs               # Windows API、快捷键和输入状态检测
├── SelectionEnhancer.cs    # 选区复制、核对、替换和剪贴板恢复
├── DeepSeekClient.cs       # 模型请求、提示词编辑规则和响应检查
├── Settings.cs             # 本地配置与 DPAPI 密钥加密
├── SelfTests.cs            # 使用模拟 HTTP 的逻辑测试
├── SmokeTests.cs           # 独立测试窗口中的输入流程测试
├── app.manifest            # 普通用户运行权限声明
├── scripts/
│   └── publish.ps1         # Windows x64 发布脚本
├── .gitignore              # 本机配置、敏感文件与构建产物排除规则
└── README.md
```

## 行为与限制

- 默认自动识别任务，可切换“编程优先”或“通用表达”，也可填写额外编辑偏好。
- 请求期间若按键、点击、滚动或切换窗口，不再自动替换；结果保留在“最近结果”。鼠标移动不影响等待。
- 替换前会重新复制选区并核对原文。请保持文字选中，且避免等待期间编辑。
- 使用标准 Ctrl+C / Ctrl+V，适用于支持这套操作的可编辑输入框。Codex、ChatGPT、DeepSeek 输入框需在用户机器上用真实 Key 实测；不承诺任意自定义编辑器都兼容。
- 必须选中**可编辑输入框**中的文字；在网页正文选中文字无法保证粘贴到原处。部分编辑器在未选中文字时 Ctrl+C 会复制整行，因此不要省略选择步骤。
- 不获取聊天历史、仓库文件或截图。模型只看到选中文字与编辑偏好，不会自动理解整个项目。
- 24,000 字符输入上限，90 秒请求超时。API 失败、空结果或截断结果不会替换原文。
- 只统计物理输入和前台窗口变化以避免过期替换，不记录按键内容。剪贴板在读取和粘贴后尽量恢复；若其他程序已写入新内容，则保留新内容。远程桌面、剪贴板管理器、慢速自定义编辑器可能影响这一机制。
- 使用普通用户权限运行。高权限目标软件可能阻止模拟按键。

## 数据与模型

选中文字发送至 `https://api.deepseek.com/chat/completions`。API 调用费用由用户自己的 DeepSeek 账户承担。默认 `deepseek-flash`，关闭思考以缩短等待；可在设置中修改模型。

API Key 通过 Windows DPAPI 在当前用户范围加密，配置保存在 `%LOCALAPPDATA%/PromptDC/settings.json`。最近原文和增强结果只保存在内存中，退出即清空。没有遥测、远端配置服务或明文 Key 日志。

不要提交或分享 API Key、`.env` 或本机 `settings.json`，即使配置中的 Key 已加密。仓库的忽略规则已覆盖这些文件以及构建产物、测试输出和常见证书文件。API Key 应在软件设置界面输入，无需写入源码。

接口依据：[DeepSeek 快速开始](https://api-docs.deepseek.com/)、[Chat Completions](https://api-docs.deepseek.com/api/create-chat-completion/)。

## 开发与打包

需要 Windows 和 .NET 8 SDK，无第三方 NuGet 依赖。

```powershell
dotnet build -c Release
dotnet run -c Release
dotnet run -c Release -- --self-test
dotnet run -c Release -- --smoke-test
```

测试报告写入 `test-results/`。self-test 使用模拟 HTTP 接口；smoke-test 会弹出自己的测试窗口，验证实际快捷键、选区复制、替换、撤销、焦点切换和失败保护，不调用真实 API。运行时请暂时不要操作键鼠，也不要同时运行另一个 PromptDC。

```powershell
./scripts/publish.ps1
```

默认输出携带 .NET 运行时的 Windows x64 目录版，复制整个 `dist/PromptDC/` 即可使用。可加 `-FrameworkDependent` 输出较小版本，但目标电脑需要 .NET 8 Desktop Runtime。

入口：`Program.cs`；设置界面与托盘：`MainForm.cs`；热键及输入状态：`Native.cs`；选区事务：`SelectionEnhancer.cs`；模型请求：`DeepSeekClient.cs`。

## 当前待确认事项

- Codex、ChatGPT 和 DeepSeek 输入框的具体兼容版本，以及使用真实 API Key 的端到端验证结果，待实际测试后补充。
- 项目尚未添加 `LICENSE`，许可证类型待仓库所有者决定；公开仓库本身不代表已授予开源许可。
- 预编译安装包、版本发布方式和截图待补充。
