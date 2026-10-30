using System.Net;
using System.Reflection;

namespace PromptDC;

// Uses injected synthetic settings/HTTP; never reads or writes the user's credentials.
internal static class LoginUiTests
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("login UI masks input, saves only after validation, restores session and logs out", async () =>
        {
            Settings stored = new();
            var writes = 0;
            var handler = new Handler();
            using (var form = Create(() => stored, value => { stored = value; writes++; }, handler))
            {
                form.Show();
                AssertPage(form, loggedIn: false);
                Capture(form, "login-page.png");
                var input = Field<TextBox>(form, "key");
                SelfTests.Assert(input.UseSystemPasswordChar && input.Text == "", "input exposes key");
                SelfTests.Assert(!Descendants(form).OfType<CheckBox>().Any(c => c.Text.Contains("显示 API")), "reveal control exists");
                input.Text = "synthetic-ui-test-key";
                var task = InvokeAsync(form, "Login");
                SelfTests.Assert(writes == 0 && stored.ProtectedKey == "" && !task.IsCompleted, "saved before verification");
                AssertPage(form, loggedIn: false);
                handler.Complete(200);
                await task;
                SelfTests.Assert(writes == 1 && LoginSession.IsValid(stored, DateTimeOffset.UtcNow), "login not persisted");
                AssertPage(form, loggedIn: true);
                SelfTests.Assert(input.Text == "" && input.UseSystemPasswordChar && !input.Enabled
                    && !Field<ComboBox>(form, "model").Enabled && Field<Button>(form, "logout").Enabled, "logged-in UI incorrect");
                var session = stored.ProtectedSession;
                Invoke(form, "SavePreferences");
                SelfTests.Assert(stored.ProtectedSession == session, "saving preferences extended login");
                Capture(form, "login-ui.png");
            }
            using (var restarted = Create(() => stored, value => stored = value, new Handler()))
            {
                restarted.Show();
                AssertPage(restarted, loggedIn: true);
                SelfTests.Assert(Field<TextBox>(restarted, "key").Text == "" && !Field<Button>(restarted, "login").Enabled, "restart requires login or echoes key");
                Invoke(restarted, "Logout");
                AssertPage(restarted, loggedIn: false);
                SelfTests.Assert(stored.ProtectedKey == "" && stored.ProtectedSession == ""
                    && Field<Button>(restarted, "login").Enabled && Field<TextBox>(restarted, "key").Enabled, "logout did not clear credentials");
            }
        });
        await check("login UI rejects failed connection without persisting credentials", async () =>
        {
            Settings stored = new(); var writes = 0;
            var handler = new Handler();
            using var form = Create(() => stored, _ => writes++, handler);
            form.Show();
            Field<TextBox>(form, "key").Text = "synthetic-ui-test-key";
            var task = InvokeAsync(form, "Login"); handler.Complete(401); await task;
            AssertPage(form, loggedIn: false);
            SelfTests.Assert(writes == 0 && Field<TextBox>(form, "key").Text == "" && Field<Button>(form, "login").Enabled
                && Field<Label>(form, "status").Text.Contains("登录失败"), "failed login persisted or misleading status");
        });
        await check("login UI clears legacy and expired credentials on startup", async () =>
        {
            var handler = new Handler(); handler.Complete(200);
            using var http = new HttpClient(handler);
            var expired = await LoginSession.LoginAsync(new(), "synthetic-ui-test-key", new(http), default,
                () => DateTimeOffset.UtcNow.AddDays(-31));
            foreach (var old in new[] { expired, new Settings { ProtectedKey = Secret.Protect("synthetic-ui-test-key") } })
            {
                var stored = old;
                using var form = Create(() => stored, value => stored = value, new Handler());
                form.Show();
                AssertPage(form, loggedIn: false);
                SelfTests.Assert(stored.ProtectedKey == "" && stored.ProtectedSession == "" && Field<Button>(form, "login").Enabled
                    && Field<TextBox>(form, "key").Text == "", "legacy/expired credential reused");
            }
        });
        await check("login UI never enters logged-in state when saving fails", async () =>
        {
            var handler = new Handler();
            using var form = Create(() => new(), _ => throw new IOException("synthetic write failure"), handler);
            form.Show();
            Field<TextBox>(form, "key").Text = "synthetic-ui-test-key";
            var task = InvokeAsync(form, "Login"); handler.Complete(200); await task;
            AssertPage(form, loggedIn: false);
            SelfTests.Assert(Field<Button>(form, "login").Enabled && Field<Label>(form, "status").Text.Contains("未保存"), "save failure logged in");
        });
        await check("switching providers clears typed credentials and selects the corresponding models", () =>
        {
            using var form = Create(() => new(), _ => { }, new Handler());
            foreach (var provider in ModelProviders.All.Reverse())
            {
                Field<TextBox>(form, "key").Text = "synthetic-ui-test-key";
                Field<ComboBox>(form, "provider").SelectedItem = provider.Name;
                SelfTests.Assert(Field<TextBox>(form, "key").Text == "" && Field<ComboBox>(form, "model").Text == provider.Models[0], "provider switch retained credentials/model");
            }
            return Task.CompletedTask;
        });
        await check("logout write failure invalidates memory and allows disk cleanup retry", async () =>
        {
            var handler = new Handler(); handler.Complete(200);
            using var http = new HttpClient(handler);
            var stored = await LoginSession.LoginAsync(new(), "synthetic-ui-test-key", new(http), default);
            var failWrites = true;
            using var form = Create(() => stored, value => { if (failWrites) throw new IOException(); stored = value; }, new Handler());
            form.Show();
            Invoke(form, "Logout");
            AssertPage(form, loggedIn: false);
            SelfTests.Assert(!LoginSession.IsValid(Field<Settings>(form, "settings"), DateTimeOffset.UtcNow)
                && Field<Button>(form, "cleanupRetry").Visible && Field<Label>(form, "status").Text.Contains("磁盘清除失败"), "logout failure not reported or session active");
            failWrites = false; Invoke(form, "Logout");
            SelfTests.Assert(stored.ProtectedKey == "" && stored.ProtectedSession == "" && !Field<Button>(form, "logout").Enabled, "cleanup retry failed");
        });
        await check("session expiry returns from workspace to the login page without losing preferences", async () =>
        {
            var handler = new Handler(); handler.Complete(200);
            using var http = new HttpClient(handler);
            var stored = await LoginSession.LoginAsync(new() { Instructions = "keep my preference" }, "synthetic-ui-test-key", new(http), default);
            using var form = Create(() => stored, value => stored = value, new Handler());
            form.Show(); AssertPage(form, loggedIn: true);
            var expiryHandler = new Handler(); expiryHandler.Complete(200);
            using var expiryHttp = new HttpClient(expiryHandler);
            var expired = await LoginSession.LoginAsync(stored, "synthetic-ui-test-key", new(expiryHttp), default,
                () => DateTimeOffset.UtcNow.AddDays(-31));
            typeof(MainForm).GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, expired);
            SelfTests.Assert(Invoke(form, "EnsureLoggedIn") is false, "expired session still accepted");
            AssertPage(form, loggedIn: false);
            SelfTests.Assert(stored.ProtectedKey == "" && stored.ProtectedSession == "" && stored.Instructions == "keep my preference", "expiry did not clear credentials or lost preferences");
        });
        await check("background startup still shows the login page if authentication is required", () =>
        {
            using var form = new MainForm(true, () => new(), _ => { }, new HttpClient(new Handler()), testMode: true);
            form.Show();
            SelfTests.Assert(form.Visible, "background startup hid required login");
            AssertPage(form, loggedIn: false);
            form.Close();
            SelfTests.Assert(form.IsDisposed, "closing login page left app running");
            return Task.CompletedTask;
        });
    }

    private static void AssertPage(MainForm form, bool loggedIn)
    {
        SelfTests.Assert(Field<Panel>(form, "loginPage").Visible == !loggedIn
            && Field<TableLayoutPanel>(form, "workspace").Visible == loggedIn, "login and workspace pages not mutually exclusive");
        SelfTests.Assert(Field<TextBox>(form, "key").Visible == !loggedIn
            && Field<Button>(form, "logout").Visible == loggedIn, "authentication controls on wrong page");
    }
    private static void Capture(MainForm form, string name)
    {
        Directory.CreateDirectory("test-results");
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.GetFullPath(Path.Combine("test-results", name)));
    }

    private static MainForm Create(Func<Settings> load, Action<Settings> save, Handler handler) =>
        new(false, load, save, new HttpClient(handler), testMode: true);
    private static T Field<T>(MainForm form, string name) => (T)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
    private static object? Invoke(MainForm form, string name) => typeof(MainForm).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
    private static Task InvokeAsync(MainForm form, string name) => (Task)Invoke(form, name)!;
    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private sealed class Handler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(int status) => completion.SetResult(new HttpResponseMessage((HttpStatusCode)status)
        { Content = new StringContent("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"验证通过\"}}]}") });
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => completion.Task.WaitAsync(cancellationToken);
    }
}
