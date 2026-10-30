# PromptDC application icon

- `PromptDC.png`: original generated artwork, with transparent background.
- `PromptDC.ico`: Windows icon containing 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixel RGBA frames.
- Regenerate the ICO from the PNG with `./scripts/convert-icon.ps1` (PowerShell on Windows).
- `PromptDC.csproj` sets `ApplicationIcon` for the executable and embeds the same ICO as `PromptDC.Assets.PromptDC.ico`.
- `MainForm.cs` loads that resource for the window, taskbar and notification area.
- Both publishing scripts inherit the project configuration; no external icon file is required at runtime.

Artwork generated with the built-in imagegen tool. Generation prompt:

```text
undefined
```
