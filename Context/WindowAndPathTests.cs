namespace PromptDC;

internal static class WindowAndPathTests
{
    internal static void Register(Action<string, Action> check)
    {
        check("Codex new-chat home composer is distinct from stored-thread composers and lookalikes", () =>
        {
            SelfTests.Assert(ComposerTarget.IsComposerChain("codex", ["ProseMirror", "_ComposerLayoutBody_1q7o3_2",
                "relative", "group/home-composer-layout", "@container/left-panel"]), "verified home editor rejected");
            SelfTests.Assert(!ComposerTarget.IsComposerChain("codex", ["searchInput", "_ComposerLayoutBody_x", "group/home-composer-layout"])
                && !ComposerTarget.IsComposerChain("codex", ["ProseMirror", "group/home-composer-layout"])
                && !ComposerTarget.IsComposerChain("codex", ["ProseMirror", "_ComposerLayoutBody_x", "group/home-composer-layout", "group/thread-scroll-layout"])
                && !ComposerTarget.IsComposerChain("harness", ["ProseMirror", "_ComposerLayoutBody_x", "group/home-composer-layout"]), "lookalike or mixed ownership accepted");
        });
        check("a new-chat draft never revalidates as a stored conversation or another draft editor", () =>
        {
            var draft = new ChatContext("codex-draft:editor-one", "", "", [], "") { IsNewChat = true, ComposerIdentity = "draft:editor-one" };
            SelfTests.Assert(draft.SameConversation(draft with { Evidence = "rechecked" })
                && !draft.SameConversation(draft with { IsNewChat = false })
                && !draft.SameConversation(draft with { ThreadId = "stored-id" })
                && !draft.SameConversation(draft with { ComposerIdentity = "draft:editor-two" }), "draft transition or editor switch accepted");
        });
        check("Codex binds the focused composer through raw compact and expanded layout ancestors", () =>
        {
            string[] compact = ["ProseMirror ProseMirror-focused", "_ComposerLayoutBody_hash_2", "relative", "group/thread-scroll-layout relative h-full"];
            string[] expanded = ["ProseMirror", "_ComposerLayoutBody_other_2", "contents", "footer", "thread-scroll-container", "group/thread-scroll-layout"];
            SelfTests.Assert(ComposerTarget.IsComposerChain("codex", compact) && ComposerTarget.IsComposerChain("codex", expanded), "layout wrappers changed composer ownership");
            SelfTests.Assert(!ComposerTarget.IsComposerChain("codex", ["searchInput", "group/thread-scroll-layout"])
                && !ComposerTarget.IsComposerChain("codex", ["ProseMirror", "browser-editor", "group/thread-scroll-layout"])
                && !ComposerTarget.IsComposerChain("codex", ["ProseMirror", "group/thread-scroll-layout", "_ComposerLayoutBody_hash_2"]), "lookalike editor accepted");
        });
        check("Codex accepts current CSS-module composer names in stored and new-chat layouts", () =>
        {
            string[] thread = ["ProseMirror ProseMirror-focused", "flex", "ComposerLayoutBody-uBBf1A LegacyExpandableBody-UZJPeV",
                "relative", "thread-scroll-container", "group/thread-scroll-layout relative h-full"];
            string[] draft = ["ProseMirror", "ComposerLayoutBody-uBBf1A", "contents", "group/home-composer-layout", "@container/left-panel"];
            SelfTests.Assert(ComposerTarget.IsComposerChain("codex", thread) && ComposerTarget.IsComposerChain("codex", draft),
                "current CSS-module composer or raw-view wrappers rejected");
        });
        check("Codex current composer matching rejects ChatGPT request cards and malformed owners", () =>
        {
            string[][] rejected =
            [
                ["ProseMirror", "@container/request-card", "thread-scroll-container", "group/thread-scroll-layout"],
                ["ProseMirror", "ComposerLayoutBody-", "group/thread-scroll-layout"],
                ["ProseMirror", "OtherComposerLayoutBody-uBBf1A", "group/thread-scroll-layout"],
                ["ProseMirror", "_OtherComposerLayoutBody_hash_2", "group/thread-scroll-layout"],
                ["searchInput", "ComposerLayoutBody-uBBf1A", "group/thread-scroll-layout"],
                ["ProseMirror", "group/thread-scroll-layout", "ComposerLayoutBody-uBBf1A"],
                ["ProseMirror", "ComposerLayoutBody-uBBf1A", "group/home-composer-layout", "group/thread-scroll-layout"],
                ["ProseMirror", "ComposerLayoutBody-uBBf1A", "group/thread-scroll-layout", "group/home-composer-layout"]
            ];
            foreach (var chain in rejected)
                SelfTests.Assert(!ComposerTarget.IsComposerChain("codex", chain), "non-Codex or malformed composer accepted: " + string.Join(" > ", chain));
        });
        check("Harness composer ownership excludes a separate search field in the same window", () =>
        {
            SelfTests.Assert(ComposerTarget.IsComposerChain("harness", ["GRgSLq_input", "GRgSLq_grow", "an3WzG_composerSeat", "an3WzG_body"]), "composer rejected");
            SelfTests.Assert(!ComposerTarget.IsComposerChain("harness", ["GAkraW_searchInput", "GAkraW_search", "an3WzG_body"])
                && !ComposerTarget.IsComposerChain("harness", ["GRgSLq_input", "an3WzG_body"]), "unowned input accepted");
        });
        check("revalidation rejects another editor even when window, provider and conversation match", () =>
        {
            var original = new ChatContext("same-chat", "", "", [], "") { ComposerIdentity = "editor-one" };
            SelfTests.Assert(original.SameConversation(original with { Title = "new title" })
                && !original.SameConversation(original with { ComposerIdentity = "editor-two" }), "editor swap ignored");
        });
        check("unavailable Codex history continues with no stored or visible text exported as context", () =>
        {
            var snapshot = new WindowSnapshot([new("ControlType.Text", "当前聊天正文", 300, 30, false)], true);
            var fallback = CodexHistoryFallback.Resolve(snapshot, "editor", "scope", () =>
                throw new IOException("private storage path and error details"));
            SelfTests.Assert(fallback.HistoryUnavailable && !fallback.IsNewChat && fallback.Messages.Count == 0
                && fallback.Title == "" && fallback.Project == "" && fallback.ComposerIdentity == "editor"
                && fallback.Evidence == "本次未使用历史，仅增强选中文字"
                && !fallback.ThreadId.Contains("当前聊天正文") && !fallback.StorageIdentity.Contains("private"),
                "selection-only fallback leaked history or error details");
        });
        check("selection-only revalidation binds exact scoped transcript, composer and fallback state", () =>
        {
            var text = new WindowText("ControlType.Text", "当前聊天正文。", 300, 30, false);
            var snapshot = new WindowSnapshot([text], true);
            ChatContext Fallback(WindowSnapshot state, string editor = "editor", string scope = "scope") =>
                CodexHistoryFallback.Resolve(state, editor, scope, () => throw new InvalidOperationException("history unavailable"));
            var original = Fallback(snapshot);
            SelfTests.Assert(original.SameConversation(Fallback(snapshot with { Texts = [text with { Width = 250, Offscreen = true }] }))
                && !original.SameConversation(Fallback(snapshot with { Texts = [text with { Text = "当前聊天正文！" }] }))
                && !original.SameConversation(Fallback(snapshot, "other-editor"))
                && !original.SameConversation(Fallback(snapshot, scope: "other-scope"))
                && !original.SameConversation(original with { HistoryUnavailable = false })
                && !original.SameConversation(original with { IsNewChat = true }),
                "fallback identity accepted a conversation/editor/state change");
        });
        check("selection-only fingerprint excludes draft, groups, buttons and navigation text", () =>
        {
            WindowText Text(string content, string type = "ControlType.Text", string excluded = "") => new(type, content, 300, 30, false, excluded);
            var transcript = Text("实际聊天正文");
            var before = new WindowSnapshot([transcript, Text("编辑前草稿", excluded: "input"),
                Text("包含草稿的聚合名称", "ControlType.Group"), Text("按钮文字", excluded: "button"), Text("菜单文字", excluded: "navigation")], true);
            var after = new WindowSnapshot([transcript, Text("编辑后草稿", excluded: "input"),
                Text("修改后的聚合名称", "ControlType.Group"), Text("新按钮文字", excluded: "button"), Text("新菜单文字", excluded: "navigation")], true);
            ChatContext Fallback(WindowSnapshot state) => CodexHistoryFallback.Resolve(state, "editor", "scope", () => throw new IOException());
            SelfTests.Assert(Fallback(before).SameConversation(Fallback(after)), "draft or non-transcript controls changed the fingerprint");
            SelfTests.Assert(!Fallback(new([Text("ab"), Text("c")], true)).SameConversation(Fallback(new([Text("a"), Text("bc")], true))),
                "transcript fragments were ambiguously concatenated");
        });
        check("Codex history fallback preserves matched history and never swallows cancellation or unverified scope", () =>
        {
            var snapshot = new WindowSnapshot([], true);
            var matched = new ChatContext("stored-thread", "stored-title", "stored-project", [new("user", "stored-message")], "matched");
            var actual = CodexHistoryFallback.Resolve(snapshot, "editor", "scope", () => matched);
            SelfTests.Assert(!actual.HistoryUnavailable && actual.ThreadId == matched.ThreadId
                && actual.Messages.SequenceEqual(matched.Messages) && actual.ComposerIdentity == "editor", "valid history was degraded");
            var cancelled = false;
            try { CodexHistoryFallback.Resolve(snapshot, "editor", "scope", () => throw new OperationCanceledException()); }
            catch (OperationCanceledException) { cancelled = true; }
            SelfTests.Assert(cancelled, "cancellation became fallback success");
            foreach (var (state, editor, scope) in new[]
            {
                (snapshot with { ConversationScoped = false }, "editor", "scope"),
                (snapshot, "", "scope"), (snapshot, "editor", "")
            })
            {
                var called = false;
                try { CodexHistoryFallback.Resolve(state, editor, scope, () => { called = true; return matched; }); }
                catch (InvalidOperationException) { SelfTests.Assert(!called, "unverified state reached history resolver"); continue; }
                throw new Exception("unverified state accepted for fallback");
            }
        });
        check("data homes follow each supplied user profile instead of the build machine", () =>
        {
            foreach (var profile in new[] { "different-user", "另一个 用户" })
            {
                var user = Path.Combine(Path.GetTempPath(), "PromptDC-profile-example", profile);
                SelfTests.Assert(DataDirectories.ResolveHome(null, user, ".codex", "CODEX_HOME") == Path.Combine(user, ".codex")
                    && DataDirectories.ResolveHome(" ", user, ".dsh", "DSH_HOME") == Path.Combine(user, ".dsh"), "profile not used");
            }
        });
        check("both data home overrides support tilde, absolute paths and empty values", () =>
        {
            var user = Path.Combine(Path.GetTempPath(), "another-profile");
            foreach (var (folder, variable) in new[] { (".codex", "CODEX_HOME"), (".dsh", "DSH_HOME") })
            {
                var path = Path.Combine(user, folder);
                SelfTests.Assert(DataDirectories.ResolveHome("~/" + folder, user, folder, variable) == path
                    && DataDirectories.ResolveHome("~\\" + folder, user, folder, variable) == path
                    && DataDirectories.ResolveHome(path, user, folder, variable) == path
                    && DataDirectories.ResolveHome("", user, folder, variable) == path
                    && DataDirectories.ResolveHome("~", user, folder, variable) == user, "override expansion failed");
            }
        });
        check("relative data roots fail instead of depending on a shortcut's working directory", () =>
        {
            foreach (var relative in new[] { "./data", "..\\another", "C:relative", "\\root-relative" })
            {
                try { DataDirectories.ResolveHome(relative, Path.GetTempPath(), ".codex", "CODEX_HOME"); }
                catch (InvalidOperationException) { continue; }
                throw new Exception("relative data home accepted");
            }
        });
    }
}
