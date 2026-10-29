using System.Drawing;
using System.Runtime.InteropServices;

namespace PromptDC;

internal sealed class MainForm : Form
{
    private readonly Color ink = Color.FromArgb(29, 42, 54), muted = Color.FromArgb(101, 116, 128), accent = Color.FromArgb(27, 117, 103);
    private Settings settings = new();
    private readonly TextBox key = new() { UseSystemPasswordChar = true };
    private readonly ComboBox model = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox hotkey = new();
    private readonly CheckBox automaticContext = new() { Text = "自动读取 Codex 当前聊天上下文", AutoSize = true };
    private readonly Label resultInfo = new() { Text = "原文", ForeColor = Color.FromArgb(101, 116, 128), Dock = DockStyle.Fill };
    private readonly TextBox instructions = new() { Multiline = true, Height = 74, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox original = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly TextBox enhanced = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Label status = new() { AutoSize = false, Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(22, 12, 14, 4) };
    private readonly Button save = new() { Text = "保存并启用", Width = 140, Height = 38 };
    private readonly Button test = new() { Text = "测试连接", Width = 110, Height = 38 };
    private readonly NotifyIcon tray;
    private readonly InputGuard guard;
    private readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly bool background;
    private CancellationTokenSource? cancellation;
    private bool busy, quitting;
    private readonly Panel preferences = new() { Dock = DockStyle.Fill, Padding = new Padding(20), BackColor = Color.White };

    public MainForm(bool background)
    {
        this.background = background;
        Text = $"PromptDC {Application.ProductVersion.Split('+')[0]} · 自动上下文";
        Font = new Font("Microsoft YaHei UI", 9.5f);
        ForeColor = ink;
        BackColor = Color.FromArgb(244, 247, 248);
        ClientSize = new Size(940, 770);
        MinimumSize = new Size(880, 770);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;

        var header = new Panel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(24, 14, 24, 8) };
        header.Controls.Add(new Label { Text = $"v{Application.ProductVersion.Split('+')[0]} · 选中 · 按下 · 增强", Dock = DockStyle.Bottom, Height = 30, ForeColor = muted });
        header.Controls.Add(new Label { Text = "PromptDC", Font = new Font("Segoe UI", 25, FontStyle.Bold), Dock = DockStyle.Top, Height = 48 });
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 0, 24, 0), ColumnCount = 2, RowCount = 1 };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        preferences.Margin = new Padding(0, 0, 14, 0);
        columns.Controls.Add(preferences, 0, 0);
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control control, int height)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, 0, 0, 6);
            fields.Controls.Add(control, 0, fields.RowCount++);
        }
        void Caption(string text) => Add(new Label { Text = text, TextAlign = ContentAlignment.BottomLeft, ForeColor = muted }, 28);
        Add(new Label { Text = "让工具随时待命", Font = new Font(Font, FontStyle.Bold) }, 30);
        Caption("DeepSeek API Key"); Add(key, 34);
        var reveal = new CheckBox { Text = "显示 API Key", AutoSize = true };
        reveal.CheckedChanged += (_, _) => key.UseSystemPasswordChar = !reveal.Checked;
        Add(reveal, 28);
        Caption("模型"); model.Items.AddRange(["deepseek-flash", "deepseek-v4-pro"]); Add(model, 34);
        Caption("增强模式"); mode.Items.AddRange(["自动识别", "编程优先", "通用表达"]); Add(mode, 34);
        Caption("全局快捷键"); Add(hotkey, 34);
        Add(automaticContext, 34);
        Caption("额外偏好（可选）"); Add(instructions, 82);
        var buttons = new FlowLayoutPanel { WrapContents = false };
        save.BackColor = accent; save.ForeColor = Color.White; save.FlatStyle = FlatStyle.Flat; save.FlatAppearance.BorderSize = 0;
        buttons.Controls.AddRange([save, test]); Add(buttons, 46);
        Add(new Label { Text = "Key 仅加密保存在本机。选中文字及匹配到的聊天上下文会发送给 DeepSeek。", ForeColor = muted }, 64);
        preferences.Controls.Add(fields);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var start = new TabPage("开始使用") { BackColor = Color.White, Padding = new Padding(18) };
        var result = new TabPage("最近结果") { BackColor = Color.White, Padding = new Padding(12) };
        tabs.TabPages.AddRange([start, result]); columns.Controls.Add(tabs, 1, 0);
        var intro = new Label
        {
            Dock = DockStyle.Top, Height = 290,
            Text = "01  填写 Key，保存并启用\n\n02  在 Codex 本机聊天的输入框中选中文字\n\n03  按快捷键，自动读取上下文并增强\n\n默认开启自动上下文。只有当前聊天唯一匹配时才会请求模型。内置示例框可以直接体验基础增强。\n\n等待期间保持原来的选区。若你切换窗口、点击或继续输入，结果会保留在“最近结果”。\n\n关闭窗口后在托盘继续运行；双击托盘图标可打开。"
        };
        var sandbox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = "帮我排查登录接口偶尔返回 500 的问题，先看看项目现有代码，别改接口返回格式。" };
        var sandboxCaption = new Label { Text = "试一试：选中下面的文字，再按快捷键", Dock = DockStyle.Top, Height = 32, ForeColor = accent };
        var sandboxPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) };
        sandboxPanel.Controls.Add(sandbox); sandboxPanel.Controls.Add(sandboxCaption);
        start.Controls.Add(sandboxPanel); start.Controls.Add(intro);
        var resultLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        resultLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        resultLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); resultLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        resultLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); resultLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        resultLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        resultLayout.Controls.Add(resultInfo, 0, 0); resultLayout.Controls.Add(original, 0, 1);
        resultLayout.Controls.Add(new Label { Text = "增强结果（仅在本次运行中保留）", AutoSize = true, ForeColor = muted }, 0, 2); resultLayout.Controls.Add(enhanced, 0, 3);
        var copyButtons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var copyResult = new Button { Text = "复制增强结果", AutoSize = true };
        var copyOriginal = new Button { Text = "复制原文", AutoSize = true };
        copyResult.Click += (_, _) => CopyText(enhanced.Text); copyOriginal.Click += (_, _) => CopyText(original.Text);
        copyButtons.Controls.AddRange([copyResult, copyOriginal]); resultLayout.Controls.Add(copyButtons, 0, 4); result.Controls.Add(resultLayout);
        Controls.Add(columns); Controls.Add(header); Controls.Add(status);

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开设置", null, (_, _) => OpenWindow());
        menu.Items.Add("查看最近结果", null, (_, _) => { tabs.SelectedTab = result; OpenWindow(); });
        menu.Items.Add("取消当前增强", null, (_, _) => cancellation?.Cancel());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出 PromptDC", null, (_, _) => { quitting = true; cancellation?.Cancel(); Close(); });
        tray = new NotifyIcon { Icon = Icon, Text = "PromptDC", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => OpenWindow();
        tray.BalloonTipClicked += (_, _) => { tabs.SelectedTab = result; OpenWindow(); };
        save.Click += (_, _) => SavePreferences(); test.Click += async (_, _) => await TestConnection();
        guard = new InputGuard();
        LoadPreferences();
        FormClosing += (_, e) => { if (!quitting) { e.Cancel = true; Hide(); Notify("已在后台运行", $"选中文字后按 {settings.Hotkey} 即可增强。"); } };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
    }

    protected override void OnShown(EventArgs e) { base.OnShown(e); if (background) Hide(); }
    private void OpenWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void SetStatus(string message) { status.Text = message; tray.Text = ("PromptDC · " + message)[..Math.Min(63, 11 + message.Length)]; }
    private void Notify(string title, string message) { tray.ShowBalloonTip(3500, title, message, ToolTipIcon.Info); }
    private void CopyText(string text)
    {
        if (text.Length == 0) return;
        try { Clipboard.SetText(text); SetStatus("已复制。"); } catch (ExternalException) { SetStatus("剪贴板正忙，请重试。"); }
    }
    private void LoadPreferences()
    {
        string? warning = null;
        try { settings = SettingsStore.Load(); key.Text = settings.GetKey(); }
        catch (Exception) { warning = "设置读取失败，请重新填写 API Key 并保存。"; }
        model.Text = settings.Model; mode.SelectedItem = settings.Mode;
        if (mode.SelectedIndex < 0) mode.SelectedIndex = 0;
        hotkey.Text = settings.Hotkey; instructions.Text = settings.Instructions;
        automaticContext.Checked = settings.AutomaticContext;
        try { Register(settings.Hotkey); SetStatus(warning ?? $"已就绪 · {settings.Hotkey} · {(key.Text.Length > 0 ? "选中文字即可增强" : "请填写 API Key 并保存")}"); }
        catch (Exception e) { SetStatus(e.Message); }
    }
    private void Register(string value)
    {
        var parsed = Native.ParseHotkey(value);
        Native.UnregisterHotKey(Handle, Native.HotkeyId);
        if (!Native.RegisterHotKey(Handle, Native.HotkeyId, parsed.Modifiers, parsed.Key))
            throw new InvalidOperationException("快捷键已被占用，请换一个组合后保存。");
    }
    private void SavePreferences()
    {
        try
        {
            if (key.Text.Trim().Length == 0 || key.Text.Trim().Any(char.IsWhiteSpace)) throw new InvalidOperationException("请填写有效的 API Key（不能含空白字符）。");
            if (string.IsNullOrWhiteSpace(model.Text)) throw new InvalidOperationException("请填写模型名称。");
            var next = settings with { ProtectedKey = Secret.Protect(key.Text.Trim()), Model = model.Text.Trim(), Mode = mode.Text, Hotkey = hotkey.Text.Trim(), Instructions = instructions.Text.Trim(), AutomaticContext = automaticContext.Checked };
            try { Register(next.Hotkey); SettingsStore.Save(next); }
            catch { Register(settings.Hotkey); throw; }
            settings = next;
            SetStatus($"已保存 · {settings.Hotkey} · 选中文字即可增强");
        }
        catch (Exception e) { SetStatus(e is InvalidOperationException ? e.Message : "保存失败，请检查设置文件是否可写。"); }
    }
    private void SetBusy(bool value)
    {
        busy = value; preferences.Enabled = !value;
        if (!value) { cancellation?.Dispose(); cancellation = null; }
        else cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    }
    private async Task TestConnection()
    {
        SavePreferences();
        if (!status.Text.StartsWith("已保存")) return;
        SetBusy(true); SetStatus("正在测试 DeepSeek 连接…");
        try
        {
            await new DeepSeekClient(http).EnhanceAsync("请帮我把这段说明写清楚。", settings, cancellation!.Token);
            SetStatus("连接成功。现在可以在输入框选中文字，按快捷键试用。");
        }
        catch (Exception e) { SetStatus(ErrorMessage(e)); }
        finally { SetBusy(false); }
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x312 && (int)message.WParam == Native.HotkeyId && !busy) _ = EnhanceSelection();
        base.WndProc(ref message);
    }
    private async Task EnhanceSelection()
    {
        if (key.Text.Length == 0) { Notify("请先配置 API Key", "双击托盘图标，填写 DeepSeek API Key 并保存。"); return; }
        var targetWindow = Native.GetForegroundWindow();
        var targetEpoch = guard.Epoch;
        SetBusy(true);
        try
        {
            var client = new DeepSeekClient(http);
            ChatContext? context = null;
            // The built-in sample editor remains usable without a Codex history.
            var needsContext = settings.AutomaticContext && targetWindow != Handle;
            var result = await new SelectionEnhancer(guard).RunAsync(async (text, token) =>
            {
                if (needsContext)
                {
                    SetStatus("正在自动定位 Codex 当前聊天…");
                    context = await ContextWorker.ResolveAsync(targetWindow, text, token);
                    if (!guard.Matches(targetWindow, targetEpoch)) throw new InvalidOperationException("输入位置已变化，已停止本次请求。");
                    SetStatus($"已结合 {context.Messages.Count} 条历史消息，正在增强…");
                }
                return await client.EnhanceAsync(text, settings, token, context);
            }, SetStatus, cancellation!.Token, needsContext ? async token =>
            {
                var current = await ContextWorker.ResolveAsync(targetWindow, "", token);
                return context != null && current.ThreadId == context.ThreadId;
            } : null);
            original.Text = result.Original; enhanced.Text = result.Enhanced;
            resultInfo.Text = context == null ? "原文 · 未使用聊天上下文" : $"原文 · 已结合 {context.Messages.Count} 条历史消息";
            var contextSummary = context == null ? "未使用上下文" : $"已使用 {context.Messages.Count} 条上下文";
            SetStatus(result.Replaced ? $"{contextSummary} · 已替换 · Ctrl+Z 可撤销" : $"{contextSummary} · 输入位置已变化，结果已保留");
            Notify(result.Replaced ? "提示词已增强" : "增强完成，未自动替换", result.Replaced ? "已粘贴回原选区，没有发送消息。" : "你在等待期间改变了输入位置。点击此提示查看结果。");
        }
        catch (Exception e) { var message = ErrorMessage(e); SetStatus(message); Notify("本次未完成", message); }
        finally { SetBusy(false); }
    }
    private static string ErrorMessage(Exception e) => e switch
    {
        OperationCanceledException => "请求已取消或超过 90 秒，原文未替换。",
        HttpRequestException => "无法连接 DeepSeek，请检查网络连接。原文未替换。",
        ExternalException => "剪贴板被其他程序占用，请重试。",
        InvalidOperationException => e.Message,
        _ => "本次增强失败，请重试。"
    };
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Native.UnregisterHotKey(Handle, Native.HotkeyId);
            tray.Visible = false; tray.Dispose(); guard.Dispose(); cancellation?.Cancel(); http.Dispose();
        }
        base.Dispose(disposing);
    }
}
