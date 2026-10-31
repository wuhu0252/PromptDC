# PromptDC

[简体中文](README.md) · [GitHub repository](https://github.com/wuhu0252/PromptDC) · [Download a release](https://github.com/wuhu0252/PromptDC/releases/latest)

A Windows prompt enhancer: select text in an input field and press **Ctrl+Alt+E** to enhance it with your chosen provider and replace the selection. It does not press Enter or submit chat messages. Automatic context supports local chats in Codex and the adapted official DeepSeek Harness Windows desktop build.

The current version is **0.3.7**, with concise rewrites, a separate multi-provider login page, 30-day saved sessions, Harness context support and an application icon.

## Download and use

1. Open the [latest release](https://github.com/wuhu0252/PromptDC/releases/latest) and download **`PromptDC-0.3.7-win-x64.zip`** and `SHA256SUMS.txt` from **Assets**. GitHub's automatically generated **Source code** archives are not application packages.
2. Extract everything, open the `PromptDC` folder and double-click **`PromptDC.exe`**. Keep all adjacent DLLs and subdirectories. The package includes .NET; no SDK, runtime installation or Git is needed.
3. Choose a provider and model on the separate login page, enter your **API key**, then click **“登录（自动验证）”** (Login). This makes a real API call; follow the error message if verification fails.
4. After successful login, adjust the mode, hotkey, automatic context and extra preferences as needed, then click **“保存偏好”** (Save preferences).
5. Select text in the chat input, press and release **Ctrl+Alt+E**, and keep the window, focus and selection unchanged. **Ctrl+Z** can undo the replacement. The built-in sample field can test basic enhancement.

PowerShell checksum command: `Get-FileHash .\PromptDC-0.3.7-win-x64.zip -Algorithm SHA256`; compare with the checksum file.

For browsers and unsupported applications, uncheck “自动读取聊天上下文（Codex / Harness）” (Automatic chat context) and save. Only selected text is enhanced.

Minimizing keeps the taskbar button. Closing hides the window to the system tray; double-click its icon to restore, or exit through its context menu. Exit the old version before upgrading. Failures report a reason and preserve the original. Generated text that was not pasted is available in “最近结果” (Recent result), only in the current process memory.

## Features and settings

- Modes: automatic detection, coding first and general expression; optional preferences, custom model and global hotkey.
- Rewrites are concise by default and merge repeated requirements. Short requests aim for one paragraph of 1–3 sentences (usually within 120 Chinese characters); complex requests aim for at most 1.5 times the source length. These are model editing targets, not output truncation limits; explicit requirements and necessary details take priority.
- Preserve necessary terminology and translate informal requests into precise technical language. Add implicit technical goals or handling requirements only when directly supported by the intent or confirmed context and useful for clarity or execution. Avoid jargon padding, unrequested implementation choices and presenting hypotheses as facts.
- “额外偏好” (Extra preferences) supplies rewrite instructions on every enhancement, such as “Use Chinese, aim for 100 characters, no headings”. Click Save preferences to apply; leaving it blank still uses concise defaults. This controls the rewritten prompt, not the receiving AI's response settings. Put one-off tasks in the selected text.
- Automatic context defaults to enabled and requires a unique match of the current editor, conversation area and local records. Titles, input counts and modification times alone cannot establish the current conversation.
- Fixed endpoint: `https://api.deepseek.com/chat/completions`; default `deepseek-flash`, thinking disabled, non-streaming, temperature 0.3, max_tokens 8192. Input limit: 24,000 UTF-16 characters; request timeout: 90 seconds.
- For “Where is this project and how do I start it?”, the enhancer asks the receiving AI to inspect the current conversation and workspace, without inventing names or paths. PromptDC does not scan project source or verify that recorded paths still exist.

| Setting or data | Location |
| --- | --- |
| PromptDC settings | `%LOCALAPPDATA%\PromptDC\settings.json`; saved in the app, with the Key encrypted using the current Windows user's DPAPI |
| Codex data | `CODEX_HOME`, or `.codex` under the current user's home when unset |
| Harness data | `DSH_HOME`, or `.dsh` under the current user's home when unset |

Defaults adapt to the current user without developer-specific paths. Environment variables must point to the application's actual data directory; absolute paths, `~`, `~/` and `~\` are supported, while relative paths are rejected. Restart PromptDC after changes. Most users only need their Key. DPAPI settings are not portable across machines; packages contain neither settings.json nor any Key. The unused legacy `StartWithWindows` field is ignored and omitted on the next save.

## Context and privacy boundaries

After the hotkey, PromptDC identifies the foreground application and focused editor, following UI Automation Raw View ancestors to its conversation area. This avoids sidebar, search and embedded-page inputs. An isolated worker reads UI and local records with a 15-second deadline; requests contain only focus options and an ephemeral editor identity. Conversation, editor, selection and input state are checked again before pasting. Ambiguity, window switches or focus changes within the same window prevent replacement.

**Codex**: scopes the area through ProseMirror, ComposerLayoutBody and `group/thread-scroll-layout` ancestors, including restored windows. It reads the highest-numbered `state_*.sqlite` using Windows `winsqlite3` in read-only mode, matching unarchived vscode/cli local chats and their `sessions` / `archived_sessions` logs. It does not guess from inherited `CODEX_THREAD_ID`. Adapted UI version: 26.1023.6123.0.

**Codex before the first message is sent**: the new-chat page uses `group/home-composer-layout`, distinct from a stored conversation. After verifying that the focused editor belongs to that page, an ephemeral UI editor identity is revalidated before pasting. The app explicitly reports a new chat with no history and sends only the selected text; it never reads another chat in the same project or uses the project name as a conversation ID. Once the first message creates a stored conversation, index/log matching resumes and still requires sufficient user/assistant evidence. Page changes, editor switches and draft-to-thread transitions prevent pasting. Unrecognized new-chat pages also preserve the original. No settings were added.

**DeepSeek Harness**: checks process filename, product name and version; currently only Windows **0.2.1-rc.3** is accepted. It locates the `dsh-app://app/` page and editor/body ancestors, matching v4 `jsonl` / `jsonl.zstd` data records. It reads source=user messages and the last text-only assistant reply of completed turns, excluding credentials, runtimes, skills, tool calls and reasoning. User text (at least 2 normalized characters) and assistant fragments (at least 8 characters each, at least 32 in total supporting one reply) are both required. One conversation with a completed reply can suffice. Indistinguishable records or forks fail; drafts are excluded structurally, preserving identical historical evidence.

Codex reads each candidate log backwards in blocks, stopping after the latest 20 user messages/assistant final replies (each counts as one message, not 20 chats). All candidates still participate in recent-evidence matching; ambiguity or insufficient evidence fails. Full file sizes no longer cause rejection. Actual reads are limited to 32 MB per candidate and 64 MB overall; tool-heavy logs can still hit these limits and fail safely. At most 200 candidates are checked. Harness reading is unchanged: file, decompression and window limits are 32 MB each, with 64 MB total decompression. Context contains at most the latest 20 messages, 20,000 UTF-16 characters overall and 5,000 per message. Some common credential formats and the current API Key are redacted from history, but not every sensitive value can be detected. Selected text and matched history are sent to DeepSeek; selected text is sent as supplied, so review it for sensitive content.

## Stack and files

C# / .NET 8 / Windows Forms; WPF references provide UI Automation. Windows APIs handle hotkeys, clipboard, focus protection and DPAPI. `ZstdSharp.Port 0.8.8` handles Zstandard decompression; release scripts use PowerShell.

| File | Responsibility |
| --- | --- |
| `Program.cs` | Entry point, single instance and test/diagnostic command routing |
| `MainForm.cs` | Settings, tray, taskbar and enhancement flow |
| `SelectionEnhancer.cs` | Selection capture, enhancement, revalidation, replacement and clipboard restoration |
| `Native.cs` | Win32, hotkeys and input/focus protection |
| `Settings.cs` | Defaults, atomic saves and DPAPI |
| `DeepSeekClient.cs` | Editing rules, API requests and response validation |
| `GlobalUsings.cs` | Shared namespace imports |
| `Context/ContextWorker.cs` | Context types, process protocol, timeout and diagnostics |
| `Context/ComposerTarget.cs` | Editor ownership, identity and shared class matching |
| `Context/WindowSnapshot.cs` | Codex window detection and UIA capture |
| `Context/CodexContext.cs` | Match Codex local records against visible evidence |
| `Context/LocalSqlite.cs` | Read-only SQLite access |
| `Context/DataDirectories.cs` | Portable data directories and environment variable resolution |
| `Context/ChatText.cs` | Shared normalization, context budgets and credential redaction |
| `Context/HarnessWindow.cs` | Harness version, page and region detection/capture |
| `Context/HarnessContext.cs` | Harness v4/compressed parsing and unique matching |
| `SelfTests.cs` | Self-test entry point, settings/Key/simulated API tests |
| `Context/ContextTests.cs` | Codex, redaction, budgets and ambiguity tests |
| `Context/HarnessTests.cs` | Harness parsing, compression, evidence and failure boundaries |
| `Context/WindowAndPathTests.cs` | Editor structure and data directory tests |
| `SmokeTests.cs` | Real hotkey/clipboard/focus tests in its own window |
| `Context/HarnessSmokeTests.cs` | Selection and matching tests for a specified real Harness window |
| `EnhancementRegression.cs` | Explicit real-API semantic regression with synthetic history |
| `PromptDC.csproj` / `app.manifest` | Version, dependencies, build and ordinary-user permissions |
| `scripts/publish.ps1` | Windows x64 directory publishing |
| `scripts/package-release.ps1` | Fresh publishing, checks, ZIP and SHA-256 |
| `.gitignore` | Exclude credentials, settings, dependencies and outputs |
| `THIRD_PARTY_NOTICES.md` | Third-party component licenses |

## Run and verify from source

Requires Windows x64 and the **.NET 8 SDK**. From the repository root:

```powershell
dotnet restore
dotnet run --project .\PromptDC.csproj
```

Build and checks that do not call the real API:

```powershell
dotnet build .\PromptDC.csproj -c Release
& .\bin\Release\net8.0-windows\PromptDC.exe --self-test
& .\bin\Release\net8.0-windows\PromptDC.exe --smoke-test
```

Reports are written to `test-results` under the working directory; failures return a nonzero exit code. SmokeTests requires an interactive desktop and available `Ctrl+Alt+9`; keep its own test window foreground. Self-tests use synthetic data/simulated HTTP. Native smoke tests only edit their own fields and restore the clipboard.

Explicit options include `--diagnose-context <HWND>` (read-only diagnosis) and `--smoke-harness <HWND>` (edits the specified real input; save its draft first). `--verify-enhancement` uses the local Key for real, billable API semantic regression; synthetic history does not replace real conversation-targeting checks. Diagnostic output should not be committed.

## Build an application package

```powershell
.\scripts\publish.ps1
.\scripts\package-release.ps1
```

The first produces `dist/PromptDC/PromptDC.exe` and complete dependencies, including the runtime. `-FrameworkDependent` requires an installed matching .NET Desktop Runtime and is unsuitable for a general “only enter your Key” package. Both scripts accept `-OutputDirectory`.

The second uses the csproj version to produce `dist/releases/PromptDC-0.3.7-win-x64.zip` and `SHA256SUMS.txt`, publishes into fresh staging, checks runtime/required files, rejects settings, credentials, debugging/development files, includes both READMEs and third-party notices, and finally removes its staging directory. Existing attachments are never overwritten. The ZIP contains the complete `PromptDC/` folder; the script does not create tags or upload a Release.

`bin`, `obj`, `dist` and `test-results` are ignored local outputs; release and test outputs are excluded from source compilation. Before distribution, check ZIP contents, checksums, self-tests and native regression, then create the matching Release and upload ZIP/checksum files. The repository must not contain API Keys, settings.json, .env or chat records.

## Outstanding details and compatibility

- Startup still needs verification on clean Windows x64 without an SDK/runtime; development-machine checks cannot establish this result.
- Other Codex/Harness UI versions and systems need separate verification; unverified Harness versions are rejected for automatic context.
- The project license awaits the maintainer's choice; no LICENSE has been added. Third-party licenses are in `THIRD_PARTY_NOTICES.md`.


## Login in the current source checkout

The app first shows a separate login page when signed out. Select DeepSeek, Kimi, Doubao, Zhipu or OpenAI (GPT), choose/edit a model, enter its API key and click Login (automatic verification). Only successful verification opens the main workspace; it contains login status, Logout and preferences, with no API-key entry controls. Failed login stays on the login page; logout or expiry returns to it. A valid saved session opens the workspace directly after restarting. A real generation request may incur charges. Only a successful HTTP response with valid JSON, finish_reason=stop and nonempty final text creates a login. The fixed verification text never includes chat history. Official standard API endpoints are used; web subscriptions, Coding Plan endpoints and custom proxies are not configured.

Keys are always masked during entry and never reloaded into the UI. Both ProtectedKey and the new ProtectedSession are encrypted with Windows current-user DPAPI in `%LOCALAPPDATA%\PromptDC\settings.json`. Provider defaults to DeepSeek; ProtectedSession defaults to an empty string. The encrypted session binds provider, model, key hash and UTC login time. Login lasts exactly 30 days from successful verification, survives restarts, and is never renewed by use or saving preferences. Startup, a minute timer and enhancement checks enforce expiry. Logout/expiry clears the saved key and session, retaining preferences. A disk write failure is reported explicitly. Legacy credentials require re-entry and verification; they do not count as a login.

LoginTests extends --self-test with simulated provider requests and session boundaries. LoginUiTests extends --smoke-test with synthetic settings and mocked HTTP, covering login, failure, restart, logout, migration, expiration and save failure without accessing real credentials.
