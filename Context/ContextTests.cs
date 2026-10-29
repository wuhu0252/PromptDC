using System.Runtime.InteropServices;
using System.Text.Json;

namespace PromptDC;

internal static class ContextTests
{
    public static void Register(Action<string, Action> check)
    {
        check("context uniquely matches message evidence, independent of title and ordering", () =>
        {
            using var fixture = new Fixture();
            fixture.Add("old-a", Messages());
            fixture.Add("newer-b", [Messages()[0], new("assistant", "这是另外一个聊天的最终回复，与当前界面没有关系。")]);
            var result = CodexContext.Resolve(Snapshot(), fixture.Home, "待优化的输入");
            SelfTests.Assert(result.ThreadId == "old-a" && result.Messages.Count == 2, "wrong chat chosen");
        });
        check("context rejects indistinguishable forked chats", () =>
        {
            using var fixture = new Fixture(); fixture.Add("a", Messages()); fixture.Add("b", Messages());
            Reject(() => CodexContext.Resolve(Snapshot(), fixture.Home, ""));
        });
        check("context rejects title-only, unscoped, editable, and draft-only evidence", () =>
        {
            using var fixture = new Fixture(); fixture.Add("a", Messages());
            Reject(() => CodexContext.Resolve(new(0, 0, "", [Text("同名聊天标题")]), fixture.Home, ""));
            Reject(() => CodexContext.Resolve(Snapshot() with { ConversationScoped = false }, fixture.Home, ""));
            var editable = Snapshot() with { Texts = Snapshot().Texts.Select(t => t with { Excluded = "input" }).ToList() };
            Reject(() => CodexContext.Resolve(editable, fixture.Home, ""));
            Reject(() => CodexContext.Resolve(Snapshot(), fixture.Home, Messages()[0].Text));
        });
        check("context uses loaded offscreen conversation messages and distinguishes shared fork prefixes", () =>
        {
            using var fixture = new Fixture();
            var current = Messages();
            current.Add(new("user", "这个分支还需要保持现有数据库表结构，不新增任何字段，请继续处理登录异常问题。"));
            fixture.Add("current", current); fixture.Add("shared-prefix", Messages());
            var snapshot = new WindowSnapshot(0, 0, "", current.Select(m => Text(m.Text) with { Offscreen = true }).ToList(), true);
            SelfTests.Assert(CodexContext.Resolve(snapshot, fixture.Home, "").ThreadId == "current", "shared prefix prevented accurate match");
        });
        check("context excludes exec and subagent records from interactive chat candidates", () =>
        {
            using var fixture = new Fixture(); fixture.Add("interactive", Messages()); fixture.Add("exec-copy", Messages(), "exec");
            fixture.Add("missing-guardian", Messages(), "subagent", false);
            SelfTests.Assert(CodexContext.Resolve(Snapshot(), fixture.Home, "").ThreadId == "interactive", "non-chat records affected matching");
        });
        check("context refuses missing local candidate and path traversal", () =>
        {
            using var fixture = new Fixture(); fixture.Add("a", Messages()); fixture.Add("missing", Messages(), "vscode", false);
            Reject(() => CodexContext.Resolve(Snapshot(), fixture.Home, ""));
            Reject(() => CodexContext.ValidateRolloutPath(fixture.Home, Path.Combine(fixture.Home, "..", "outside.jsonl")));
        });
        check("transcript excludes tools, system instructions, reasoning and progress messages", () =>
        {
            using var fixture = new Fixture();
            var file = Path.Combine(fixture.Home, "sessions", "filter.jsonl");
            File.WriteAllLines(file,
            [
                Line("system", "do not export"), Line("developer", "do not export"),
                Line("user", "<environment_context>metadata</environment_context>"),
                Line("user", "<in-app-browser-context>ambient</in-app-browser-context>\n## My request:\n真正的用户需求"),
                Line("assistant", "progress", "commentary"), Line("assistant", "secret reasoning", "analysis"),
                Line("assistant", "最终可见回答", "final_answer"),
                "{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call_output\",\"output\":\"do not export\"}}",
                "{\"type\":\"response_item\""
            ]);
            var messages = CodexContext.ReadMessages(file);
            SelfTests.Assert(messages.Count == 2 && messages[0].Text == "真正的用户需求" && messages[1].Text == "最终可见回答", "non-chat items exported");
        });
        check("history bounds and redaction preserve recent messages", () =>
        {
            var credential = "sk-" + new string('x', 30);
            var source = Enumerable.Range(0, 30).Select(i => new ChatMessage("user", i + new string('文', 1000))).ToList();
            source.Add(new("assistant", "最后一条 " + credential));
            var limited = CodexContext.LimitContext(source, 4000);
            SelfTests.Assert(limited.Count <= 20 && limited.Sum(x => x.Text.Length) <= 4050, "context budget exceeded");
            SelfTests.Assert(limited[^1].Text.Contains("最后一条") && !limited[^1].Text.Contains(credential), "recent content lost or credential leaked");
        });
    }
    private static List<ChatMessage> Messages() =>
    [
        new("user", "请检查登录接口偶尔返回服务器错误的问题，保持现有接口返回格式，并且先了解当前项目代码。"),
        new("assistant", "已经确认本次只排查登录接口的服务器错误，先检查异常日志和调用链，保持已有返回结构，不增加其他功能。")
    ];
    private static WindowText Text(string text) => new("ControlType.Text", text, "", 100, 100, 500, 80, false);
    private static WindowSnapshot Snapshot() => new(0, 0, "", Messages().Select(m => Text(m.Text)).ToList(), true);
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("unsafe context was accepted");
    }
    internal static string Line(string role, string text, string? phase = null) => JsonSerializer.Serialize(new
    {
        type = "response_item", payload = new { type = "message", role, phase,
            content = new[] { new { type = role == "user" ? "input_text" : "output_text", text } } }
    });
    private sealed class Fixture : IDisposable
    {
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string name, out nint db, int flags, nint vfs);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_exec(nint db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, nint callback, nint context, nint error);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close_v2(nint db);
        public string Home { get; } = Path.Combine(Path.GetTempPath(), "PromptDC-fixture-" + Guid.NewGuid());
        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Home, "sessions"));
            Sql("CREATE TABLE threads(id TEXT, title TEXT, cwd TEXT, rollout_path TEXT, archived INTEGER, source TEXT)");
        }
        private void Sql(string sql)
        {
            SelfTests.Assert(sqlite3_open_v2(Path.Combine(Home, "state_5.sqlite"), out var db, 6, 0) == 0, "fixture open failed");
            try { SelfTests.Assert(sqlite3_exec(db, sql, 0, 0, 0) == 0, "fixture SQL failed"); }
            finally { sqlite3_close_v2(db); }
        }
        public void Add(string id, List<ChatMessage> messages, string source = "vscode", bool writeFile = true)
        {
            var path = Path.Combine(Home, "sessions", id + ".jsonl");
            if (writeFile) File.WriteAllLines(path, messages.Select(m => Line(m.Role, m.Text, m.Role == "assistant" ? "final_answer" : null)));
            static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
            Sql($"INSERT INTO threads VALUES ({Quote(id)}, '同名标题', 'test-project', {Quote(path)}, 0, {Quote(source)})");
        }
        public void Dispose()
        {
            var sessions = Path.Combine(Home, "sessions");
            foreach (var file in Directory.EnumerateFiles(sessions)) File.Delete(file);
            Directory.Delete(sessions);
            File.Delete(Path.Combine(Home, "state_5.sqlite"));
            Directory.Delete(Home);
        }
    }
}
