# PromptDC

当前版本：**0.2.1**。窗口标题会显示版本号；新版替换完成后状态栏会显示“已使用 N 条上下文”或“未使用上下文”。

Windows 桌面提示词增强工具。选中输入框中的文字，按 **Ctrl+Alt+E**，松开按键并等待，即可使用 DeepSeek 增强并直接粘贴回原选区。默认在 Codex 桌面本机聊天中自动定位对话、读取近期上下文。不会发送 Enter，不会自动提交聊天消息。

项目使用 **C# / .NET 8 / Windows Forms**，通过 Windows API 注册全局快捷键并检测输入状态，通过 HTTPS 调用 DeepSeek Chat Completions 接口。发布脚本使用 PowerShell，项目没有声明第三方 NuGet 包依赖。

## 直接下载使用（无需开发环境）

1. 打开 [最新发布页](https://github.com/wuhu0252/PromptDC/releases/latest)，在 **Assets** 中下载 **`PromptDC-0.2.1-win-x64.zip`**。这是自带 .NET 运行时的 Windows x64 免安装包，无需安装 .NET、SDK 或 Git。
2. 将 ZIP **全部解压**到一个目录，打开其中的 `PromptDC` 文件夹，双击 **`PromptDC.exe`**。不要从压缩包预览窗口直接运行，也不要单独移动 exe；旁边的 DLL 和子目录需要一同保留。
3. 在窗口内填写自己的 **DeepSeek API Key**，点击 **“保存并启用”**。模型和快捷键有默认值，通常无需修改配置文件。
4. 点击 **“测试连接”**；该操作会使用你的账户产生一次 API 调用。失败时按软件提示检查 Key、账户或网络。
5. 在 Codex 桌面本机聊天输入框中选中文字，按默认快捷键 **Ctrl+Alt+E**，松开并保持选区，等待自动替换。首次也可在软件内置示例框中体验基础增强。

在浏览器输入框中使用时，先取消勾选“自动读取 Codex 当前聊天上下文”，再点击“保存并启用”；此时只增强选中文字，不读取浏览器聊天历史。关闭软件窗口会隐藏到托盘，右键托盘图标选择退出；升级前请先退出旧版本。

**不要将 GitHub 自动生成的 `Source code (zip)` / `Source code (tar.gz)` 当作运行包**，它们只有源码，需要自行构建。运行包不包含任何用户的 API Key 或 `settings.json`；每位用户需在自己的软件窗口内配置 Key。

发布页同时提供 `SHA256SUMS.txt`。可在下载目录使用 PowerShell 校验 ZIP，并与校验文件中的值比较：

```powershell
Get-FileHash .\PromptDC-0.2.1-win-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

发布包自带 .NET 核心与桌面运行时，依然依赖 Windows 系统组件。当前验证环境为 Windows 11 x64；尚未在没有任何开发环境的全新 Windows 系统上验证，其他 Windows 版本的兼容性待补充确认。

## 主要功能

- 全局快捷键触发，可在设置中修改快捷键。
- 自动核对 Codex 当前聊天区域与本机记录，唯一匹配后结合近期历史增强提示词。
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

也可以按下方“开发与打包”章节生成本机可运行版本。Git 仓库只包含源码和文档，`dist/` 等构建产物不提交；普通用户请使用上方的 GitHub Release 附件。

## 使用

1. 使用 `dotnet run -c Release` 启动，或打包后打开 `dist/PromptDC/PromptDC.exe`。
2. 在窗口内输入自己的 DeepSeek API Key，点击“保存并启用”。不需要将 Key 发给开发者。
3. 可点击“测试连接”，它会产生一次小额 API 调用。
4. 在 Codex 桌面本机聊天输入框里选中文字，按快捷键，松开后保持选区，等待替换。内置示例框可以体验不带上下文的基础增强。
5. 增强偏了可尝试原编辑器的 `Ctrl+Z`；也可在“最近结果”复制原文。

关闭或最小化窗口后在系统托盘继续运行。双击托盘图标打开窗口；右键菜单可查看结果、取消请求或退出。没有设置开机自启。

## 自动聊天上下文

设置中的“自动读取 Codex 当前聊天上下文”默认开启，使用时无需手动选择会话或复制聊天记录。

1. 读取触发快捷键时的 Codex 前台窗口，通过 Windows UI Automation 查找当前聊天内容布局。
2. 读取该聊天区域中已加载的文字，排除输入框、侧栏及按钮内容。
3. 只读查询 `$CODEX_HOME/state_*.sqlite`；未设置 `CODEX_HOME` 时使用 `%USERPROFILE%/.codex/`。仅核对未归档的本机 `vscode` / `cli` 交互聊天。
4. 将用户消息与助手最终回复的文字片段和本机 JSONL 记录进行精确核对；不会按最近更新时间、窗口标题或当前进程的 `CODEX_THREAD_ID` 选择聊天。
5. 仅在候选唯一时，将最近最多 20 条、合计最多 20,000 字符的实际消息作为背景交给 DeepSeek，单条最多 5,000 字符。工具输出、推理、进度消息和环境元数据不作为背景。
6. 模型完成后再次核对聊天及选区。定位不明确、窗口变化或核对失败时保留原文；已生成的结果可在“最近结果”复制。

匹配在本机完成，不向模型发送其他候选聊天。本地记录只读访问；定位在独立进程运行，15 秒未完成即终止。匹配会话记录的工作目录也会作为背景提供给模型，并标注其来源及未经文件扫描验证的限制。历史中常见凭据格式及已配置的当前 API Key 会在发送背景时遮盖，格式检测不能保证识别任意敏感信息。

**兼容范围：**目前适配并实测 Codex Windows 桌面 `26.1021.3705.0` 的本机聊天读取。适配器依赖界面中的 `group/thread-scroll-layout` 类标记；Codex 更新可能需要调整。没有聊天历史、聊天记录无法读取、完全相同的分叉内容或界面未加载足够消息时，可能无法定位。云端、SSH/WSL 远程聊天和浏览器上下文尚未适配。最多核对 200 个交互聊天，单份记录上限 32 MB，本次核对总量上限 64 MB，超限会停止操作。

需要在浏览器中使用原有的基础增强时，可关闭并保存“自动读取 Codex 当前聊天上下文”；关闭后只发送选中文字和编辑偏好，不会获取浏览器聊天历史。

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
├── GlobalUsings.cs         # 桌面项目公共命名空间
├── Context/
│   ├── WindowSnapshot.cs   # Codex 聊天区域的 UI Automation 读取
│   ├── LocalSqlite.cs      # Windows 内置 SQLite 的只读访问
│   ├── CodexContext.cs     # 聊天匹配、历史筛选与背景限制
│   ├── ContextWorker.cs    # 独立定位进程、超时和诊断
│   └── ContextTests.cs     # 合成聊天数据与匹配回归测试
├── SelfTests.cs            # 使用模拟 HTTP 的逻辑测试
├── SmokeTests.cs           # 独立测试窗口中的输入流程测试
├── EnhancementRegression.cs # 可选的真实接口提示词回归
├── app.manifest            # 普通用户运行权限声明
├── scripts/
│   ├── publish.ps1         # Windows x64 发布脚本
│   └── package-release.ps1 # 全新目录打包与 SHA-256 校验文件
├── .gitignore              # 本机配置、敏感文件与构建产物排除规则
└── README.md
```

## 行为与限制

- 默认自动识别任务，可切换“编程优先”或“通用表达”，也可填写额外编辑偏好。
- 请求期间若按键、点击、滚动或切换窗口，不再自动替换；结果保留在“最近结果”。鼠标移动不影响等待。
- 替换前会重新复制选区并核对原文。请保持文字选中，且避免等待期间编辑。
- 使用标准 Ctrl+C / Ctrl+V，适用于支持这套操作的可编辑输入框。Codex、ChatGPT、DeepSeek 输入框需在用户机器上用真实 Key 实测；不承诺任意自定义编辑器都兼容。
- 必须选中**可编辑输入框**中的文字；在网页正文选中文字无法保证粘贴到原处。部分编辑器在未选中文字时 Ctrl+C 会复制整行，因此不要省略选择步骤。
- 自动上下文开启时会读取匹配到的 Codex 本机聊天历史，模型会收到经过筛选的近期消息。不会自动读取项目源码或截图，历史也不等于完整仓库上下文。
- 24,000 字符输入上限，90 秒请求超时。API 失败、空结果或截断结果不会替换原文。
- 只统计物理输入和前台窗口变化以避免过期替换，不记录按键内容。剪贴板在读取和粘贴后尽量恢复；若其他程序已写入新内容，则保留新内容。远程桌面、剪贴板管理器、慢速自定义编辑器可能影响这一机制。
- 使用普通用户权限运行。高权限目标软件可能阻止模拟按键。

## 数据与模型

选中文字、编辑偏好及开启自动上下文时匹配到的近期聊天背景发送至 `https://api.deepseek.com/chat/completions`。历史以独立的背景数据消息传递，编辑器只改写最后一条当前输入。API 调用费用由用户自己的 DeepSeek 账户承担，带上下文通常比仅传原文消耗更多输入 token。默认 `deepseek-flash`，关闭思考以缩短等待；可在设置中修改模型。

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
dotnet run -c Release -- --diagnose-context
```

测试报告写入 `test-results/`。self-test 使用模拟 HTTP 接口；smoke-test 会弹出自己的测试窗口，使用独立快捷键 `Ctrl+Alt+9` 验证实际快捷键、选区复制、替换、撤销、焦点切换和失败保护，不调用真实 API。测试快捷键使用与默认热键相同的 Ctrl+Alt 修饰键，避免加入 Shift 后干扰输入法切换。运行时请暂时不要操作键鼠；若测试快捷键被占用，请先退出占用它的程序。

`--diagnose-context` 只诊断当前前台 Codex 窗口，走与实际使用相同的独立进程定位流程，不复制/粘贴、不调用模型。报告 `test-results/context-diagnostic.json` 包含匹配结果、聊天 ID、数量和耗时等元数据，不保存聊天正文或 API Key。运行时请让目标 Codex 聊天保持在前台。

`dotnet run -c Release -- --verify-enhancement` 会使用本机保存的 Key 对“这个项目的位置在哪，我要怎么启动”做真实接口回归（分别无背景、带固定项目背景），产生实际 API 调用费用。只发送固定的公开项目事实，不读取实际聊天历史，结果保存到被 Git 忽略的 `test-results/enhancement-regression.json`。该检查仅覆盖常见错误模式，输出质量仍需人工审阅；返回 401 时需要在软件内更新有效 Key。

```powershell
./scripts/publish.ps1
```

默认输出携带 .NET 运行时的 Windows x64 目录版，复制整个 `dist/PromptDC/` 即可使用。可加 `-FrameworkDependent` 输出较小版本，但目标电脑需要 .NET 8 Desktop Runtime。

发布 GitHub Release 附件使用：

```powershell
./scripts/package-release.ps1
```

该脚本从 `PromptDC.csproj` 读取版本，在全新暂存目录构建自带运行时的程序，检查必要运行文件及禁止携带的配置/调试文件，然后生成 `dist/releases/PromptDC-0.2.1-win-x64.zip` 和 `dist/releases/SHA256SUMS.txt`。它不会读取本机用户设置，也不会覆盖已存在的发布附件；重复构建可通过 `-OutputDirectory dist/releases-check` 选择新目录。标签采用 `v0.2.1`，与项目版本对应，ZIP 和校验文件作为附件上传，构建目录不提交到 Git。

`publish.ps1` 也支持 `-OutputDirectory` 指定输出目录；只有默认的自带运行时模式用于普通用户 Release，`-FrameworkDependent` 不用于该发布包。

入口：`Program.cs`；设置界面与托盘：`MainForm.cs`；热键及输入状态：`Native.cs`；选区事务：`SelectionEnhancer.cs`；模型请求：`DeepSeekClient.cs`。

## 当前待确认事项

- 当前 Codex 窗口的聊天定位与上下文读取已做本机诊断；使用真实 API Key 的“选区 → 上下文 → 模型 → 替换”完整流程仍待用户实测。浏览器上下文适配待后续实现。
- 项目尚未添加 `LICENSE`，许可证类型待仓库所有者决定；公开仓库本身不代表已授予开源许可。
- 全新 Windows 环境（未安装 .NET/SDK）的启动验证、其他 Windows 版本兼容性及使用截图待补充。
