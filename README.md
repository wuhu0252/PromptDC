# PromptDC

[English](README.en.md) · [GitHub 仓库](https://github.com/wuhu0252/PromptDC) · [下载发布版](https://github.com/wuhu0252/PromptDC/releases/latest)

Windows 提示词增强工具：在输入框中选中文字，按 **Ctrl+Alt+E**，用所选服务商增强后替换原选区。不会按 Enter 或提交聊天消息。支持自动读取 Codex 和已适配的 DeepSeek Harness 官方 Windows 桌面版本机聊天上下文。

当前版本为 **0.3.6**，包含精简提示词、多服务商独立登录页、30 天登录状态、Harness 上下文适配和应用图标。

## 当前源码的登录流程

未登录时先显示独立登录页，主界面暂不显示：选择服务商 → 选择或填写模型 → 输入 API 密钥 → 点击“登录（自动验证）” → 验证成功后跳转到主界面。主界面只保留登录信息、“退出登录”和增强偏好，不显示密钥输入框。退出登录或到期后返回登录页；有效期内重启直接进入主界面。程序自动发起一次真实生成请求（可能计费），只有 HTTP 成功、JSON 有效、`finish_reason=stop` 且最终内容非空才保存并登录；失败不会保存本次密钥或进入已登录状态。登录验证只发送固定测试文本，不读取聊天历史。

| 服务商 | 预填模型 | 官方接口说明 |
| --- | --- | --- |
| DeepSeek | `deepseek-flash`，可选 `deepseek-v4-pro` | 沿用原接口与参数 |
| Kimi | `kimi-k3.1-pro`，可选 `kimi-k2.6` | [Chat Completions](https://platform.kimi.com/docs/api/chat) |
| 豆包 | `doubao-seed-2-1-pro-260915` | [火山方舟文本生成](https://docs.volcengine.com/docs/ark/text-generation?lang=en) |
| 智谱 | `glm-4.7` | [官方示例](https://docs.bigmodel.cn/cn/guide/models/text/glm-4.7)，使用标准模型 API Key |
| OpenAI (GPT) | `gpt-5.4-mini` | [Chat Completions](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create) |

模型框允许手动填写官方模型 ID；豆包也可填写账户已开通的推理接入点 ID。是否有权限以实际验证为准。这里使用各家官方标准 API，不支持网页会员登录、Coding Plan 专用接口或自定义中转地址。当前只保持一个服务商的登录；切换服务商或模型需退出后重新验证，切换时会清空输入框。

密钥输入始终使用密码掩码，移除“显示 API Key”开关；已保存的密钥不会填回输入框。登录成功后清空输入框并隐藏整个登录页。点击“保存偏好”只保存增强模式、快捷键、自动上下文和额外偏好，不登录、不延长有效期。

登录成功起固定 **30 天**有效，重启保留，使用和保存偏好均不续期。启动、每分钟及增强请求/粘贴前检查有效性，到期停止使用并清除保存的密钥和登录凭据，需重新登录。退出登录同样清除二者，保留其他偏好；若磁盘不可写会明确提示清除失败并允许重试，不能保证下一次启动的磁盘状态已清除。

保存位置仍是 `%LOCALAPPDATA%\PromptDC\settings.json`，新增字段 `Provider` 默认 `DeepSeek`、`ProtectedSession` 默认空字符串（未登录）。`ProtectedKey` 保持原字段，默认空字符串。Key 与登录票据分别用当前 Windows 用户 DPAPI 加密后保存为 Base64；票据绑定服务商、模型、密钥摘要与 UTC 登录时间，有效期由代码固定为 30 天，没有明文到期时间设置。JSON 通过临时文件原子替换。损坏、其他 Windows 用户或不匹配的票据不能登录。旧配置没有验证凭据，需要重新输入 Key 验证，首次启动会清除旧密钥，其他偏好保留。

原配置字段及默认值：`Model=deepseek-flash`、`Mode=自动识别`、`Hotkey=Ctrl+Alt+E`、`Instructions=""`、`AutomaticContext=true`；旧 `StartWithWindows` 继续忽略。

## 下载与使用

1. 打开[最新发布页](https://github.com/wuhu0252/PromptDC/releases/latest)，在 **Assets** 下载 **`PromptDC-0.3.6-win-x64.zip`** 和 `SHA256SUMS.txt`。GitHub 自动生成的 **Source code** 压缩包不是运行包。
2. 全部解压，进入 `PromptDC` 文件夹，双击 **`PromptDC.exe`**。保留旁边全部 DLL 和子目录；运行包自带 .NET，无需安装 SDK、运行时或 Git。
3. 在独立登录页选择服务商和模型，填写自己的 **API 密钥**，点击 **“登录（自动验证）”**。验证会产生一次真实 API 调用；失败时按提示检查 Key、账户或网络。
4. 登录成功后进入主界面；按需调整增强模式、快捷键、自动上下文及额外偏好，点击 **“保存偏好”**。
5. 在聊天输入框选中文字，按 **Ctrl+Alt+E** 后松开，保持窗口、焦点和选区，等待替换。可用 **Ctrl+Z** 撤销；软件内置示例框可测试基础增强。

PowerShell 校验命令：`Get-FileHash .\PromptDC-0.3.6-win-x64.zip -Algorithm SHA256`，与校验文件比较。

浏览器等未适配应用可取消勾选“自动读取聊天上下文（Codex / Harness）”并保存，此时只增强选中文字。

最小化后保留任务栏图标；关闭窗口隐藏到系统托盘，双击托盘图标恢复，右键菜单退出。升级前退出旧版本。失败会提示原因并保留原文；已生成但未粘贴的内容可在“最近结果”复制，仅保存在当前进程内存中。

## 功能与配置

- 支持自动识别、编程优先、通用表达模式、额外偏好、自定义模型和全局快捷键。
- 默认精简改写、合并重复要求：短请求优先一段 1～3 句，通常不超过 120 个中文字符；复杂请求通常不超过原文长度的 1.5 倍。这是模型的编辑目标，不会截断结果，保留明确要求和必要细节优先。
- “额外偏好”是每次增强都会附带的改写要求，例如“用中文，尽量在 100 字以内，不加标题”。填写后点击“保存偏好”生效；留空也默认精简。它约束增强后的提示词，不直接设置接收任务的 AI 的回复风格；单次任务内容请写在选中文字中。
- 自动上下文默认开启，必须唯一匹配当前输入框、聊天区域与本机记录。标题、输入框数量和修改时间不能单独证明当前会话。
- DeepSeek 接口 `https://api.deepseek.com/chat/completions`；该服务商默认 `deepseek-flash`，关闭 thinking、非流式、temperature 0.3、max_tokens 8192。输入上限 24,000 UTF-16 字符，请求超时 90 秒。
- 例如“这个项目的位置在哪，我要怎么启动”：增强器要求接收任务的 AI 查阅当前对话和工作区，不凭空补充项目名或路径。PromptDC 不扫描项目源码，也不验证历史记录中的路径是否仍存在。

| 配置或数据 | 定位方式 |
| --- | --- |
| PromptDC 设置 | `%LOCALAPPDATA%\PromptDC\settings.json`；软件内保存，Key 使用当前 Windows 用户的 DPAPI 加密 |
| Codex 数据 | `CODEX_HOME`；未设置时为当前用户目录下 `.codex` |
| Harness 数据 | `DSH_HOME`；未设置时为当前用户目录下 `.dsh` |

默认目录随用户适配，无开发者本机路径。环境变量应指向应用实际数据目录，支持绝对路径、`~`、`~/`、`~\`，拒绝相对路径；修改后重启 PromptDC。普通用户一般只需填写 Key。DPAPI 配置不能跨机器通用；运行包不含 settings.json 或任何 Key。旧配置中未使用的 `StartWithWindows` 会被忽略，下次保存不再写入。

## 上下文与隐私边界

快捷键触发后，识别前台应用与焦点输入框，沿 UI Automation Raw View 祖先定位其对话区域，避开侧栏、搜索框和内置页面输入框。隔离子进程读取 UI 与记录，单次最多 15 秒；请求只传焦点选项与临时输入框标识。粘贴前复核会话、输入框、选区和输入状态。歧义、窗口切换或同窗口焦点变化时停止自动替换。

**Codex**：从 ProseMirror、ComposerLayoutBody 和 `group/thread-scroll-layout` 祖先限定区域，兼容非最大化窗口。读取数据目录最新编号的 `state_*.sqlite`，通过 Windows `winsqlite3` 只读访问，匹配未归档的 vscode/cli 本机聊天及 `sessions` / `archived_sessions` 日志；不使用继承的 `CODEX_THREAD_ID` 猜测会话。已适配界面：26.1023.6123.0。

**Codex 尚未发送的首条消息**：新建页的 `group/home-composer-layout` 与已有聊天区域不同。确认焦点输入框属于此页面后，以临时 UI 输入框身份进行前后复核，明确显示“新聊天，暂无历史”，仅发送选中文字；不读取同项目的其他聊天，也不把项目名当作会话标识。首条消息发送并进入正式聊天后，恢复按索引与日志核对，仍需足够的用户/助手历史证据。切页、换输入框或草稿转为正式会话时停止替换；无法确认的新建页同样保留原文。无新增设置。

**DeepSeek Harness**：校验进程文件名、产品名和版本，目前仅接受 Windows **0.2.1-rc.3**。从 `dsh-app://app/` 页面及输入框/正文祖先定位区域，匹配数据目录中的 v4 `jsonl` / `jsonl.zstd`。读取 source=user 消息与已完成轮次最后的纯文字助手回复，排除凭据、运行时、技能、工具调用和推理。需要用户文字（归一化后至少 2 字符）和助手片段（每段至少 8 字符，合计至少 32 字符，并支持同一条回复）共同作证，一个有完整回复的对话即可。相同记录或分叉无法唯一匹配时失败；按界面结构排除草稿，保留与草稿同文的历史证据。

Codex 从每份候选日志末尾分块向前读取，找到最近 20 条用户消息/助手最终回复即停止（各算一条，不是 20 个聊天）；所有候选仍参与近期证据匹配，重复或证据不足时失败。不会按日志完整大小拒绝；单份实际读取上限 32 MB，全部候选实际读取总上限 64 MB，工具密集导致达到上限时仍安全停止。最多核对 200 条候选记录。Harness 读取方式不变：单文件、单次解压和窗口各上限 32 MB、累计解压 64 MB。上下文最多最近 20 条、共 20,000 UTF-16 字符、单条 5,000 字符。部分常见凭据格式及当前 API Key 会从历史上下文脱敏，但不能保证覆盖全部敏感内容。选中文字与匹配历史会发送给所选服务商；选中文字按用户输入发送，请自行检查敏感内容。

## 技术栈与目录

C# / .NET 8 / Windows Forms；WPF 引用用于 UI Automation。Windows API 负责热键、剪贴板、焦点保护和 DPAPI。`ZstdSharp.Port 0.8.8` 负责 Zstandard 解压；发布脚本使用 PowerShell。

| 文件 | 职责 |
| --- | --- |
| `Program.cs` | 入口、单实例和测试/诊断命令分流 |
| `MainForm.cs` | 设置、托盘、任务栏与增强流程 |
| `SelectionEnhancer.cs` | 选区获取、增强、复核、替换与剪贴板恢复 |
| `Native.cs` | Win32、热键与输入/焦点保护 |
| `Settings.cs` | 默认配置、原子保存与 DPAPI |
| `DeepSeekClient.cs` / `ModelProviders.cs` | 改写规则、各服务商请求参数与响应校验 |
| `LoginSession.cs` | 连接验证、加密登录票据、30 天有效性和凭据清除 |
| `LoginTests.cs` / `LoginUiTests.cs` | 登录状态与各服务商模拟 HTTP 测试、隔离配置的登录界面冒烟 |
| `GlobalUsings.cs` | 共享命名空间导入 |
| `Context/ContextWorker.cs` | 上下文类型、进程协议、超时与诊断 |
| `Context/ComposerTarget.cs` | 输入框所属区域、身份及共享类名匹配 |
| `Context/WindowSnapshot.cs` | Codex 窗口与 UIA 采集 |
| `Context/CodexContext.cs` | Codex 本机记录与可见证据匹配 |
| `Context/LocalSqlite.cs` | SQLite 只读访问 |
| `Context/DataDirectories.cs` | 可移植数据目录与环境变量解析 |
| `Context/ChatText.cs` | 共用归一化、上下文预算和凭据脱敏 |
| `Context/HarnessWindow.cs` | Harness 版本、页面、区域识别与采集 |
| `Context/HarnessContext.cs` | Harness v4/压缩记录解析与唯一匹配 |
| `SelfTests.cs` | 自检入口、配置/Key/API 模拟测试 |
| `Context/ContextTests.cs` | Codex、脱敏、预算、歧义测试 |
| `Context/HarnessTests.cs` | Harness 解析、压缩、证据和失败边界测试 |
| `Context/WindowAndPathTests.cs` | 输入框结构与数据目录测试 |
| `SmokeTests.cs` | 自有窗口的真实热键/剪贴板/焦点测试 |
| `Context/HarnessSmokeTests.cs` | 指定真实 Harness 窗口的选区与匹配测试 |
| `EnhancementRegression.cs` | 显式真实 API 语义回归，使用合成背景 |
| `PromptDC.csproj` / `app.manifest` | 版本、依赖、构建和普通用户权限 |
| `Assets/PromptDC.png` / `Assets/PromptDC.ico` | 透明图标原图、多尺寸 Windows 图标；EXE、窗口、任务栏和托盘共用，配置见 csproj 的 `ApplicationIcon` 与嵌入资源 |
| `scripts/convert-icon.ps1` | 从 PNG 重新生成多尺寸 ICO，详细说明及生成提示词见 `Assets/README.md` |
| `scripts/publish.ps1` | Windows x64 目录发布 |
| `scripts/package-release.ps1` | 全新发布、检查、ZIP 与 SHA-256 |
| `.gitignore` | 排除凭据、设置、依赖和产物 |
| `THIRD_PARTY_NOTICES.md` | 第三方组件许可 |

## 从源码运行与验证

需要 Windows x64 和 **.NET 8 SDK**，在仓库根目录执行：

```powershell
dotnet restore
dotnet run --project .\PromptDC.csproj
```

构建与不调用真实 API 的验证：

```powershell
dotnet build .\PromptDC.csproj -c Release
& .\bin\Release\net8.0-windows\PromptDC.exe --self-test
& .\bin\Release\net8.0-windows\PromptDC.exe --smoke-test
```

报告写入当前工作目录 `test-results`，失败退出码非 0。SmokeTests 需要交互桌面与可用的 `Ctrl+Alt+9`，测试时保持自有测试窗口前台。自检使用合成数据/模拟 HTTP；原生冒烟测试只操作自己的输入框并恢复剪贴板。新增登录界面测试注入合成设置和模拟 HTTP，不读取或改写真实配置，覆盖验证后保存、失败不保存、重启、退出、旧配置、到期与写入失败；登录页截图为 `test-results/login-page.png`，主界面截图为 `test-results/login-ui.png`；检查登录页与主界面互斥显示，成功才跳转，失败留在登录页，退出和运行期间到期均返回登录页。

可显式运行 `--diagnose-context <HWND>`（只读诊断）、`--smoke-harness <HWND>`（操作指定真实输入框，先保存草稿）。`--verify-enhancement` 使用本机 Key 进行真实、计费的 API 语义回归；合成背景不能代替真实聊天定位验证。诊断输出不应提交。

## 构建运行包

```powershell
.\scripts\publish.ps1
.\scripts\package-release.ps1
```

前者默认输出 `dist/PromptDC/PromptDC.exe` 与完整依赖，自带运行时；`-FrameworkDependent` 要求已安装匹配的 .NET Desktop Runtime，不适合通用“只填 Key”包。两脚本都支持 `-OutputDirectory`。

后者按 csproj 版本输出 `dist/releases/PromptDC-0.3.6-win-x64.zip` 与 `SHA256SUMS.txt`，使用全新临时目录，检查运行时/必要文件，拒绝配置、凭据、调试与开发文件，携带两份 README 和第三方许可，最后清理本次临时目录。已有附件不覆盖。ZIP 包含完整 `PromptDC/`；脚本不会创建标签或上传 Release。

`bin`、`obj`、`dist`、`test-results` 是忽略的本地产物，构建排除发布和测试输出目录。分发前检查 ZIP、校验值、自检与原生回归，再创建对应版本的 Release 并上传 ZIP/校验文件。仓库不得包含 API Key、settings.json、.env 或聊天记录。

## 待补充与兼容性

- 仍需在无 SDK/运行时的干净 Windows x64 环境验证启动；开发机验证不能代替此项。
- 其他 Codex/Harness 界面版本和系统需单独验证；Harness 未验证版本会拒绝自动读取。
- 项目开源许可证待维护者选择，尚未添加 LICENSE；第三方许可见 `THIRD_PARTY_NOTICES.md`。
