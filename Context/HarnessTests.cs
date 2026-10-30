using System.Net;
using System.Text;
using System.Text.Json;
using ZstdSharp;

namespace PromptDC;

internal static class HarnessTests
{
    private const string User = "你好";
    private const string Reply = "你好，我可以协助你理解项目代码、排查问题，并结合已经确认的要求说明项目位置及启动步骤。";
    internal static void Register(Action<string, Action> check)
    {
        check("Harness resolves compressed histories under two different default user homes", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "PromptDC-portable-harness-" + Guid.NewGuid());
            try
            {
                foreach (var name in new[] { "user-a", "other 用户" })
                {
                    var user = Path.Combine(root, name);
                    var home = DataDirectories.ResolveHome(null, user, ".dsh", "DSH_HOME");
                    using (var fixture = new Fixture(home))
                    {
                        fixture.Add("session-a", Rows("session-a"), compressed: true);
                        SelfTests.Assert(HarnessContext.Resolve(Snapshot(), home).StorageIdentity.StartsWith(home, StringComparison.Ordinal), "wrong user's storage root");
                    }
                    Directory.Delete(user);
                }
            }
            finally { if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root); }
        });
        check("Harness uniquely matches actual messages in concatenated Zstandard frames", () =>
        {
            using var fixture = new Fixture();
            fixture.Add("session-a", Rows("session-a"), compressed: true);
            fixture.Add("session-b", Rows("session-b", answer: "这是另一份记录，不能作为当前对话的背景，哪怕它的标题也叫问候。"));
            var context = HarnessContext.Resolve(Snapshot(), fixture.Home);
            SelfTests.Assert(context.Provider == "harness" && context.ThreadId == "session-a" && context.Messages.Count == 2, "wrong conversation");
            SelfTests.Assert(context.StorageIdentity.EndsWith("session.v4.jsonl.zstd"), "missing storage identity");
        });
        check("Harness refuses identical histories and conflicting evidence across conversations", () =>
        {
            using var fixture = new Fixture(); fixture.Add("session-a", Rows("session-a")); fixture.Add("session-b", Rows("session-b"));
            Reject(() => HarnessContext.Resolve(Snapshot(), fixture.Home));
            File.Delete(fixture.File("session-b"));
            fixture.Add("session-b", Rows("session-b", user: "请处理另外一个项目中的数据库连接问题。", answer: "这是另外一个会话的回复，必须保留在它自己的上下文中，不能与其他对话混合。"));
            Reject(() => HarnessContext.Resolve(new([.. Snapshot().Fragments,
                new("user", "请处理另外一个项目中的数据库连接问题。")], true), fixture.Home));
        });
        check("Harness matches one completed conversation when selected text repeats history", () =>
        {
            using var fixture = new Fixture(); fixture.Add("session-a", Rows("session-a"), compressed: true);
            var repeated = Snapshot() with { Fragments = [.. Snapshot().Fragments, new("user", User), new("input", User)] };
            var result = HarnessContext.Resolve(repeated, fixture.Home);
            SelfTests.Assert(result.ThreadId == "session-a" && result.Messages.Count == 2, "repeated history was lost or composer text was exported");
            fixture.Add("session-b", Rows("session-b"));
            Reject(() => HarnessContext.Resolve(Snapshot(), fixture.Home));
        });
        check("Harness rejects unscoped, assistant-only and composer-only evidence", () =>
        {
            using var fixture = new Fixture(); fixture.Add("session-a", Rows("session-a"));
            Reject(() => HarnessContext.Resolve(Snapshot() with { ConversationScoped = false }, fixture.Home));
            Reject(() => HarnessContext.Resolve(new([new("assistant", Reply)], true), fixture.Home));
            Reject(() => HarnessContext.Resolve(new([new("input", User), new("assistant", Reply)], true), fixture.Home));
        });
        check("Harness excludes runtime context, skill text, tools, reasoning and unfinished replies", () =>
        {
            var rows = Rows("session-a");
            rows.Insert(2, Event("user/message", new { content = Blocks("private environment"), role = "user", id = "runtime", source = new { kind = "runtime-context" } }));
            rows.Insert(3, Event("user/message", new { content = Blocks("private skills"), role = "user", id = "skills", source = new { kind = "skill-catalog" } }));
            rows.Insert(4, Event("tool/result", new { text = "private tool output" }));
            rows.Add(Event("assistant/message", new { turn = 2, message = new { role = "assistant", content = Blocks("unfinished text") } }));
            var parsed = HarnessContext.ParseLog(Encode(rows), FakePath("session-a"));
            SelfTests.Assert(parsed.Messages.Count == 2 && parsed.Messages[0].Text == User && parsed.Messages[1].Text == Reply, "non-chat content exported");
        });
        check("Harness exports only the last text-only reply in a completed turn", () =>
        {
            var rows = Rows("session-a"); rows.RemoveAt(rows.Count - 1);
            rows.Add(Event("assistant/message", new { turn = 1, message = new { role = "assistant",
                content = new object[] { new { type = "text", text = "intermediate progress" }, new { type = "tool-call", name = "shell" } } } }));
            rows.Add(Event("turn/end", new { turn = 1, reason = new { kind = "completed" } }));
            SelfTests.Assert(HarnessContext.ParseLog(Encode(rows), FakePath("session-a")).Messages.Count == 1, "intermediate reply treated as final");
            rows = Rows("session-a"); rows[^1] = Event("turn/end", new { turn = 1, reason = new { kind = "aborted" } });
            SelfTests.Assert(HarnessContext.ParseLog(Encode(rows), FakePath("session-a")).Messages.Count == 1, "aborted reply exported");
        });
        check("Harness fails closed when any candidate is damaged or its identity disagrees", () =>
        {
            using var fixture = new Fixture(); fixture.Add("session-a", Rows("session-a")); fixture.Add("session-b", Rows("session-b"));
            File.WriteAllText(fixture.File("session-b"), "{broken");
            Reject(() => HarnessContext.Resolve(Snapshot(), fixture.Home));
            Reject(() => HarnessContext.ParseLog(Encode(Rows("session-other")), FakePath("session-a")));
            Reject(() => HarnessContext.ParseLog(Encoding.UTF8.GetBytes("{\"type\":\"session\"}"), FakePath("session-a")));
        });
        check("Harness rejects unsupported newer generations and multiple current encodings", () =>
        {
            using var fixture = new Fixture(); fixture.Add("session-a", Rows("session-a"));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.File("session-a"))!, "session.v5.jsonl"), "new format");
            Reject(() => HarnessContext.FindLogs(fixture.Home));
            File.Delete(Path.Combine(Path.GetDirectoryName(fixture.File("session-a"))!, "session.v5.jsonl"));
            File.WriteAllText(fixture.File("session-a") + ".zstd", "another encoding");
            Reject(() => HarnessContext.FindLogs(fixture.Home));
        });
        check("Harness refuses unsupported history rewrites and duplicate user message ids", () =>
        {
            var rows = Rows("session-a");
            rows.Add(Event("user/message", new { content = Blocks("edited"), role = "user", id = "u1", source = new { kind = "user" } }, "replace"));
            Reject(() => HarnessContext.ParseLog(Encode(rows), FakePath("session-a")));
            rows[^1] = Event("user/message", new { content = Blocks("duplicate"), role = "user", id = "u1", source = new { kind = "user" } });
            Reject(() => HarnessContext.ParseLog(Encode(rows), FakePath("session-a")));
        });
        check("Harness refuses oversized or truncated compressed records", () =>
        {
            using var fixture = new Fixture(); fixture.Add("session-a", Rows("session-a"), compressed: true);
            var file = fixture.File("session-a", true);
            var bytes = File.ReadAllBytes(file); File.WriteAllBytes(file, bytes[..^6]);
            Reject(() => HarnessContext.ReadLog(file));
            using (var stream = new FileStream(file, FileMode.Create)) stream.SetLength(32L * 1024 * 1024 + 1);
            Reject(() => HarnessContext.ReadLog(file));
        });
        check("Harness refuses a decompression bomb before allocating an unbounded transcript", () =>
        {
            using var fixture = new Fixture(); fixture.Add("session-a", Rows("session-a"), compressed: true);
            using var compressor = new Compressor();
            File.WriteAllBytes(fixture.File("session-a", true), compressor.Wrap(new byte[32 * 1024 * 1024 + 1]).ToArray());
            Reject(() => HarnessContext.ReadLog(fixture.File("session-a", true)));
        });
        check("conversation revalidation distinguishes providers and Harness storage roots", () =>
        {
            var a = new ChatContext("same-id", "", "", [], "") { Provider = "harness", StorageIdentity = "C:\\one\\session.v4.jsonl" };
            SelfTests.Assert(a.SameConversation(a with { Title = "renamed" }), "rename invalidated identity");
            SelfTests.Assert(!a.SameConversation(a with { Provider = "codex" })
                && !a.SameConversation(a with { StorageIdentity = "C:\\other\\session.v4.jsonl" })
                && !a.SameConversation(a with { ThreadId = "different" }), "different conversation accepted");
        });
        check("Harness API background has correct source, masks Key and preserves last input", () => ApiTest().GetAwaiter().GetResult());
    }

    private static async Task ApiTest()
    {
        var handler = new Handler(); using var http = new HttpClient(handler);
        var context = new ChatContext("private-id", "private-title", "C:\\project\\test-key", [new("user", "这个项目怎么启动 test-key")], "")
        { Provider = "harness", StorageIdentity = "private-store" };
        await new DeepSeekClient(http).EnhanceAsync("这个项目怎么启动", new Settings { ProtectedKey = Secret.Protect("test-key") }, default, context);
        using var body = JsonDocument.Parse(handler.Body);
        var messages = body.RootElement.GetProperty("messages");
        using var history = JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
        SelfTests.Assert(history.RootElement.GetProperty("workspace").GetProperty("source").GetString()!.StartsWith("DeepSeek Harness"), "wrong provenance");
        SelfTests.Assert(messages[2].GetProperty("content").GetString() == "这个项目怎么启动", "current input changed");
        SelfTests.Assert(history.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("这个项目怎么启动"), "overlapping historical message was removed");
        SelfTests.Assert(!handler.Body.Contains("test-key") && !handler.Body.Contains("private-id") && !handler.Body.Contains("private-store"), "private metadata/key sent");
    }
    private sealed class Handler : HttpMessageHandler
    {
        public string Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Body = await request.Content!.ReadAsStringAsync(token);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"请说明项目启动步骤。\"}}]}") };
        }
    }
    private static HarnessSnapshot Snapshot() => new([new("user", User), new("assistant", Reply)], true);
    private static object[] Blocks(string text) => [new { type = "text", text }];
    private static JsonElement Event(string type, object data, string? surfaceOp = null) => JsonSerializer.SerializeToElement(new { type, data, surfaceOp });
    private static List<JsonElement> Rows(string id, string user = User, string answer = Reply) =>
    [
        JsonSerializer.SerializeToElement(new { type = "session", version = 4, id, cwd = "C:\\example-project" }),
        Event("user/message", new { content = Blocks(user), source = new { kind = "user" }, role = "user", id = "u1" }, "append"),
        Event("assistant/message", new { turn = 1, message = new { role = "assistant",
            content = new object[] { new { type = "reasoning", text = "private reasoning" }, new { type = "text", text = answer } } } }, "append"),
        Event("turn/end", new { turn = 1, reason = new { kind = "completed" } })
    ];
    private static byte[] Encode(List<JsonElement> rows) => Encoding.UTF8.GetBytes(string.Join("\n", rows.Select((row, index) =>
    {
        if (index == 0) return row.GetRawText();
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.GetRawText())!;
        values["seq"] = JsonSerializer.SerializeToElement(index - 1);
        if (values["surfaceOp"].ValueKind == JsonValueKind.Null) values.Remove("surfaceOp");
        return JsonSerializer.Serialize(values);
    })) + "\n");
    private static string FakePath(string id) => Path.Combine(Path.GetTempPath(), id, "session.v4.jsonl");
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("unsafe Harness data accepted");
    }
    private sealed class Fixture : IDisposable
    {
        public string Home { get; }
        public Fixture(string? home = null) { Home = home ?? Path.Combine(Path.GetTempPath(), "PromptDC-harness-test-" + Guid.NewGuid()); }
        public string File(string id, bool compressed = false) => Path.Combine(Home, "sessions", "--test-project--", id, "session.v4.jsonl" + (compressed ? ".zstd" : ""));
        public void Add(string id, List<JsonElement> rows, bool compressed = false)
        {
            var file = File(id, compressed); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var bytes = Encode(rows);
            if (compressed)
            {
                // Harness writes independent checksummed frames in one concatenated file.
                using var compressor = new Compressor();
                compressor.SetParameter(ZstdSharp.Unsafe.ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
                var middle = bytes.Length / 2;
                bytes = [.. compressor.Wrap(bytes.AsSpan(0, middle)).ToArray(), .. compressor.Wrap(bytes.AsSpan(middle)).ToArray()];
            }
            System.IO.File.WriteAllBytes(file, bytes);
        }
        public void Dispose()
        {
            if (!Directory.Exists(Home)) return;
            // This exact random temporary test root was created by this fixture.
            foreach (var file in Directory.EnumerateFiles(Home, "*", SearchOption.AllDirectories)) System.IO.File.Delete(file);
            foreach (var directory in Directory.EnumerateDirectories(Home, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length)) Directory.Delete(directory);
            Directory.Delete(Home);
        }
    }
}
