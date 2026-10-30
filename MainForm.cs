using System.Drawing;
using System.Runtime.InteropServices;

namespace PromptDC;

internal sealed class MainForm : Form
{
    private readonly Color ink = Color.FromArgb(29, 42, 54), muted = Color.FromArgb(101, 116, 128), accent = Color.FromArgb(27, 117, 103);
    private Settings settings = new();
    private readonly TextBox key = new() { UseSystemPasswordChar = true };
    private readonly ComboBox provider = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox model = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox hotkey = new();
    private readonly CheckBox automaticContext = new() { Text = "自动读取聊天上下文（Codex / Harness）", AutoSize = true };
    private readonly Label resultInfo = new() { Text = "原文", ForeColor = Color.FromArgb(101, 116, 128), Dock = DockStyle.Fill };
    private readonly TextBox instructions = new() { Multiline = true, Height = 74, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox original = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly TextBox enhanced = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Label status = new() { AutoSize = false, Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(22, 12, 14, 4) };
    private readonly Button save = new() { Text = "保存偏好", Width = 140, Height = 38 };
    private readonly Button login = new() { Text = "登录（自动验证）", Width = 150, Height = 38 };
    private readonly Button cleanupRetry = new() { Text = "重试清除本机凭据", AutoSize = true, Visible = false };
    private readonly Button logout = new() { Text = "退出登录", Width = 110, Height = 38 };
    private readonly Label loginInfo = new() { AutoSize = true };
    private readonly Label loginHint = new() { Text = "验证成功后进入主界面，保持登录 30 天。", ForeColor = Color.FromArgb(101, 116, 128) };
    private readonly TableLayoutPanel workspace = new() { Dock = DockStyle.Fill, Padding = new Padding(24, 0, 24, 0), ColumnCount = 2, RowCount = 1, Visible = false };
    private readonly Panel loginPage = new() { Dock = DockStyle.Fill, Padding = new Padding(24, 0, 24, 12), Visible = false };
    private bool? showingWorkspace;
    private Size workspaceSize = new(940, 770);
    private readonly System.Windows.Forms.Timer sessionTimer = new() { Interval = 60000 };
    private readonly NotifyIcon tray;
    private readonly Icon applicationIcon = LoadApplicationIcon();
    private readonly InputGuard guard;
    private readonly HttpClient http;
    private readonly Func<Settings> loadSettings;
    private readonly Action<Settings> saveSettings;
    private readonly bool testMode;
    private readonly bool background;
    private CancellationTokenSource? cancellation;
    private bool busy, quitting, resourcesDisposed;
    private readonly Panel preferences = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20), BackColor = Color.White };

    public MainForm(bool background, Func<Settings>? loadSettings = null, Action<Settings>? saveSettings = null,
        HttpClient? client = null, bool testMode = false)
    {
        this.background = background;
        this.loadSettings = loadSettings ?? SettingsStore.Load;
        this.saveSettings = saveSettings ?? (value => SettingsStore.Save(value));
        this.http = client ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        this.testMode = testMode;
        Text = $"PromptDC {Application.ProductVersion.Split('+')[0]} · 自动上下文";
        Font = new Font("Microsoft YaHei UI", 9.5f);
        ForeColor = ink;
        BackColor = Color.FromArgb(244, 247, 248);
        ClientSize = new Size(940, 770);
        MinimumSize = new Size(880, 770);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        Icon = applicationIcon;

        var header = new Panel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(24, 14, 24, 8) };
        header.Controls.Add(new Label { Text = $"v{Application.ProductVersion.Split('+')[0]} · 选中 · 按下 · 增强", Dock = DockStyle.Bottom, Height = 30, ForeColor = muted });
        header.Controls.Add(new Label { Text = "PromptDC", Font = new Font("Segoe UI", 25, FontStyle.Bold), Dock = DockStyle.Top, Height = 48 });
        var columns = workspace;
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
        Add(loginInfo, 52);
        var accountButtons = new FlowLayoutPanel { WrapContents = false };
        accountButtons.Controls.Add(logout); Add(accountButtons, 46);
        Caption("增强模式"); mode.Items.AddRange(["自动识别", "编程优先", "通用表达"]); Add(mode, 34);
        Caption("全局快捷键"); Add(hotkey, 34);
        Add(automaticContext, 34);
        Caption("额外偏好（可选）"); Add(instructions, 82);
        Add(new Label { Text = "每次增强时使用；留空默认精简。\n例：用中文，尽量在 100 字以内。", ForeColor = muted }, 48);
        var buttons = new FlowLayoutPanel { WrapContents = false };
        save.BackColor = accent; save.ForeColor = Color.White; save.FlatStyle = FlatStyle.Flat; save.FlatAppearance.BorderSize = 0;
        buttons.Controls.Add(save); Add(buttons, 46);
        Add(new Label { Text = "增强时选中文字及匹配到的聊天上下文会发送给当前登录的服务商。切换服务商或模型，请先退出登录。", ForeColor = muted }, 84);
        preferences.Controls.Add(fields);
        BuildLoginPage();

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var start = new TabPage("开始使用") { BackColor = Color.White, Padding = new Padding(18) };
        var result = new TabPage("最近结果") { BackColor = Color.White, Padding = new Padding(12) };
        tabs.TabPages.AddRange([start, result]); columns.Controls.Add(tabs, 1, 0);
        var intro = new Label
        {
            Dock = DockStyle.Top, Height = 290,
            Text = "01  在 Codex 或 Harness 官方桌面版本机聊天中选中文字\n\n02  按快捷键，自动读取上下文并增强\n\n03  增强结果自动替换原选区，可用 Ctrl+Z 撤销\n\n默认开启自动上下文。只有当前聊天唯一匹配时才会请求模型。内置示例框可以直接体验基础增强。\n\n等待期间保持原来的选区。若你切换窗口、点击或继续输入，结果会保留在“最近结果”。\n\n最小化保留任务栏；关闭后在托盘运行，双击图标恢复。"
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
        var content = new Panel { Dock = DockStyle.Fill };
        content.Controls.Add(workspace); content.Controls.Add(loginPage);
        Controls.Add(content); Controls.Add(header); Controls.Add(status);

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 PromptDC", null, (_, _) => OpenWindow());
        menu.Items.Add("查看最近结果", null, (_, _) => { tabs.SelectedTab = result; OpenWindow(); });
        menu.Items.Add("取消当前增强", null, (_, _) => cancellation?.Cancel());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出 PromptDC", null, (_, _) => { quitting = true; cancellation?.Cancel(); Close(); });
        tray = new NotifyIcon { Icon = Icon, Text = "PromptDC", Visible = !testMode, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => OpenWindow();
        tray.BalloonTipClicked += (_, _) => { tabs.SelectedTab = result; OpenWindow(); };
        save.Click += (_, _) => SavePreferences(); login.Click += async (_, _) => await Login();
        logout.Click += (_, _) => Logout();
        cleanupRetry.Click += (_, _) => Logout();
        sessionTimer.Tick += (_, _) => { if (!busy) EnsureLoggedIn(); };
        guard = new InputGuard();
        LoadPreferences();
        sessionTimer.Start();
        FormClosing += (_, e) =>
        {
            if (quitting) return;
            if (!LoginSession.IsValid(settings, DateTimeOffset.UtcNow)) { quitting = true; cancellation?.Cancel(); return; }
            e.Cancel = true; Hide(); Notify("已在后台运行", $"选中文字后按 {settings.Hotkey} 即可增强。");
        };
    }

    private void BuildLoginPage()
    {
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3 };
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 0,
            BackColor = Color.White, Padding = new Padding(22) };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control control, int height)
        {
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 0, 0, 6);
            card.Controls.Add(control, 0, card.RowCount++);
        }
        void Caption(string text) => Add(new Label { Text = text, ForeColor = muted, TextAlign = ContentAlignment.BottomLeft }, 28);
        Add(new Label { Text = "登录 PromptDC", Font = new Font(Font.FontFamily, 15, FontStyle.Bold) }, 40);
        Caption("服务商");
        provider.Items.AddRange(ModelProviders.All.Select(p => (object)p.Name).ToArray());
        provider.SelectedIndexChanged += (_, _) =>
        {
            if (provider.SelectedItem is not string name) return;
            model.Items.Clear(); model.Items.AddRange(ModelProviders.Get(name).Models); model.SelectedIndex = 0;
            key.Clear();
        };
        provider.SelectedIndex = 0; Add(provider, 34);
        Caption("模型"); Add(model, 34);
        Caption("API 密钥（API Key）"); Add(key, 34);
        key.PlaceholderText = "请输入所选服务商的 API 密钥";
        login.BackColor = accent; login.ForeColor = Color.White; login.FlatStyle = FlatStyle.Flat;
        login.FlatAppearance.BorderSize = 0;
        Add(login, 44);
        Add(loginHint, 52);
        Add(new Label { Text = "密钥仅加密保存在本机。登录会自动调用所选服务商 API 验证连接，可能计费。", ForeColor = muted }, 52);
        var retryRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        retryRow.Controls.Add(cleanupRetry);
        card.Controls.Add(retryRow, 0, card.RowCount++);
        center.Controls.Add(card, 1, 1); loginPage.Controls.Add(center);
    }

    private static Icon LoadApplicationIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("PromptDC.Assets.PromptDC.ico")
            ?? throw new InvalidOperationException("Missing embedded PromptDC icon.");
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    }

    protected override void OnShown(EventArgs e) { base.OnShown(e); if (background && LoginSession.IsValid(settings, DateTimeOffset.UtcNow)) Hide(); }
    private void OpenWindow() { EnsureLoggedIn(); Show(); WindowState = FormWindowState.Normal; Activate(); }
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
        try
        {
            settings = loadSettings();
            if (!LoginSession.IsValid(settings, DateTimeOffset.UtcNow)
                && (settings.ProtectedKey.Length > 0 || settings.ProtectedSession.Length > 0))
            {
                settings = LoginSession.Clear(settings);
                saveSettings(settings);
            }
        }
        catch (Exception) { warning = "设置读取失败，请重新登录。"; }
        provider.SelectedItem = settings.Provider;
        model.Text = settings.Model; mode.SelectedItem = settings.Mode;
        if (mode.SelectedIndex < 0) mode.SelectedIndex = 0;
        hotkey.Text = settings.Hotkey; instructions.Text = settings.Instructions;
        automaticContext.Checked = settings.AutomaticContext;
        RefreshLoginUI();
        try { Register(settings.Hotkey); SetStatus(warning ?? (LoginSession.IsValid(settings, DateTimeOffset.UtcNow) ? $"已登录 · {settings.Hotkey}" : "未登录，请选择服务商和模型后输入密钥登录。")); }
        catch (Exception e) { SetStatus(e.Message); }
    }
    private void Register(string value)
    {
        var parsed = Native.ParseHotkey(value);
        if (testMode) return;
        Native.UnregisterHotKey(Handle, Native.HotkeyId);
        if (!Native.RegisterHotKey(Handle, Native.HotkeyId, parsed.Modifiers, parsed.Key))
            throw new InvalidOperationException("快捷键已被占用，请换一个组合后保存。");
    }
    private void SavePreferences()
    {
        try
        {
            var next = ReadPreferences();
            try { Register(next.Hotkey); saveSettings(next); }
            catch { Register(settings.Hotkey); throw; }
            settings = next;
            SetStatus($"偏好已保存 · {settings.Hotkey}");
        }
        catch (Exception e) { SetStatus(e is InvalidOperationException ? e.Message : "保存失败，请检查设置文件是否可写。"); }
    }
    private void SetBusy(bool value)
    {
        busy = value; preferences.Enabled = !value; loginPage.Enabled = !value;
        if (!value) { cancellation?.Dispose(); cancellation = null; }
        else cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    }
    private Settings ReadPreferences() => settings with { Mode = mode.Text, Hotkey = hotkey.Text.Trim(),
        Instructions = instructions.Text.Trim(), AutomaticContext = automaticContext.Checked };

    private void RefreshLoginUI()
    {
        var valid = LoginSession.IsValid(settings, DateTimeOffset.UtcNow);
        key.Clear(); key.Enabled = !valid; provider.Enabled = !valid; model.Enabled = !valid;
        login.Enabled = !valid; logout.Enabled = valid;
        AcceptButton = valid ? null : login;
        workspace.Visible = valid; loginPage.Visible = !valid;
        if (showingWorkspace != valid)
        {
            if (showingWorkspace == true && WindowState == FormWindowState.Normal) workspaceSize = ClientSize;
            WindowState = FormWindowState.Normal;
            MinimumSize = valid ? new Size(880, 770) : new Size(520, 640);
            ClientSize = valid ? workspaceSize : new Size(560, 660);
            showingWorkspace = valid;
        }
        Text = $"PromptDC {Application.ProductVersion.Split('+')[0]} · {(valid ? "自动上下文" : "登录")}";
        loginInfo.Text = valid ? $"已登录 {settings.Provider} · {settings.Model}\n有效至 {LoginSession.ExpiresUtc(settings)!.Value.ToLocalTime():yyyy-MM-dd HH:mm}"
            : "未登录 · 验证成功后保持登录 30 天";
    }

    private bool EnsureLoggedIn()
    {
        if (LoginSession.IsValid(settings, DateTimeOffset.UtcNow)) return true;
        if (settings.ProtectedKey.Length > 0 || settings.ProtectedSession.Length > 0)
        {
            settings = LoginSession.Clear(settings);
            RefreshLoginUI();
            try { saveSettings(settings); SetStatus("登录已到期或凭据不可用，请重新登录。"); }
            catch { SetStatus("登录已失效，但配置文件无法写入，请检查文件权限后重新登录。"); }
        }
        return false;
    }

    private void Logout()
    {
        var cleared = LoginSession.Clear(settings);
        var cleanupFailed = false;
        try
        {
            saveSettings(cleared);
            SetStatus("已退出登录，本机保存的密钥和登录凭据已清除。");
        }
        catch { cleanupFailed = true; SetStatus("本次运行已退出登录，但磁盘清除失败；请检查配置文件权限并重试退出登录。"); }
        settings = cleared;
        RefreshLoginUI();
        // Offer disk cleanup retry on the login page after the workspace has been hidden.
        loginHint.Text = cleanupFailed ? "磁盘清除失败，请检查权限并点击下方按钮重试。" : "验证成功后进入主界面，保持登录 30 天。";
        cleanupRetry.Visible = cleanupFailed;
    }

    private async Task Login()
    {
        SetBusy(true); SetStatus("正在验证连接，成功后自动登录…");
        try
        {
            var candidate = ReadPreferences() with { Provider = provider.Text, Model = model.Text.Trim() };
            Native.ParseHotkey(candidate.Hotkey);
            var next = await LoginSession.LoginAsync(candidate, key.Text, new DeepSeekClient(http), cancellation!.Token);
            try { Register(next.Hotkey); saveSettings(next); }
            catch { Register(settings.Hotkey); throw; }
            settings = next;
            cleanupRetry.Visible = false; loginHint.Text = "验证成功后进入主界面，保持登录 30 天。";
            SetStatus("连接成功，已登录；30 天内无需重新输入密钥。");
        }
        catch (Exception e) { SetStatus(e is IOException or UnauthorizedAccessException ? "登录未保存，请检查配置文件写入权限。" : "登录失败：" + ErrorMessage(e)); }
        finally { RefreshLoginUI(); SetBusy(false); }
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x312 && (int)message.WParam == Native.HotkeyId && !busy) _ = EnhanceSelection();
        base.WndProc(ref message);
    }
    private async Task EnhanceSelection()
    {
        if (!EnsureLoggedIn()) { Notify("请先登录", "双击托盘图标，选择服务商和模型，输入密钥并登录。"); return; }
        var targetWindow = Native.GetForegroundWindow();
        var targetEpoch = guard.Epoch;
        SetBusy(true);
        try
        {
            var client = new DeepSeekClient(http);
            ChatContext? context = null;
            // The built-in sample editor remains usable without a chat history.
            var needsContext = settings.AutomaticContext && targetWindow != Handle;
            var composer = needsContext ? await ContextWorker.GetComposerIdentityAsync(targetWindow, cancellation!.Token) : "";
            if (!guard.Matches(targetWindow, targetEpoch)) throw new InvalidOperationException("输入位置已变化，原文未替换。");
            var result = await new SelectionEnhancer(guard).RunAsync(async (text, token) =>
            {
                if (needsContext)
                {
                    SetStatus("正在自动定位当前聊天…");
                    context = await ContextWorker.ResolveAsync(targetWindow, token, composer);
                    if (!guard.Matches(targetWindow, targetEpoch)) throw new InvalidOperationException("输入位置已变化，已停止本次请求。");
                    SetStatus(context.IsNewChat ? "Codex 新聊天 · 暂无历史，仅增强选中文字…"
                        : $"{context.ProviderName} · 已结合 {context.Messages.Count} 条历史消息，正在增强…");
                }
                if (!EnsureLoggedIn()) throw new InvalidOperationException("登录已到期，请重新登录。原文未替换。");
                return await client.EnhanceAsync(text, settings, token, context);
            }, SetStatus, cancellation!.Token, needsContext ? async token =>
            {
                var current = await ContextWorker.ResolveAsync(targetWindow, token, composer);
                return EnsureLoggedIn() && context != null && context.SameConversation(current);
            } : _ => Task.FromResult(EnsureLoggedIn()));
            original.Text = result.Original; enhanced.Text = result.Enhanced;
            resultInfo.Text = context == null ? "原文 · 未使用聊天上下文" : context.IsNewChat ? "原文 · Codex 新聊天，暂无历史" : $"原文 · 已结合 {context.Messages.Count} 条历史消息";
            var contextSummary = context == null ? "未使用上下文" : context.IsNewChat ? "Codex 新聊天 · 仅增强选中文字" : $"{context.ProviderName} · 已使用 {context.Messages.Count} 条上下文";
            SetStatus(result.Replaced ? $"{contextSummary} · 已替换 · Ctrl+Z 可撤销" : $"{contextSummary} · 输入位置已变化，结果已保留");
            Notify(result.Replaced ? "提示词已增强" : "增强完成，未自动替换", result.Replaced ? "已粘贴回原选区，没有发送消息。" : "你在等待期间改变了输入位置。点击此提示查看结果。");
        }
        catch (Exception e) { var message = ErrorMessage(e); SetStatus(message); Notify("本次未完成", message); }
        finally { SetBusy(false); }
    }
    private static string ErrorMessage(Exception e) => e switch
    {
        OperationCanceledException => "请求已取消或超过 90 秒，原文未替换。",
        HttpRequestException => "无法连接所选服务商，请检查网络连接。原文未替换。",
        ExternalException => "剪贴板被其他程序占用，请重试。",
        InvalidOperationException => e.Message,
        _ => "本次增强失败，请重试。"
    };
    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            if (IsHandleCreated) Native.UnregisterHotKey(Handle, Native.HotkeyId);
            quitting = true;
            sessionTimer.Dispose();
            tray.Visible = false; tray.Dispose(); guard.Dispose(); cancellation?.Cancel(); http.Dispose();
            applicationIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
