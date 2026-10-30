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
