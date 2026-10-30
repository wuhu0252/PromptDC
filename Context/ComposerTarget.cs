using System.Windows.Automation;

namespace PromptDC;

// The active editor owns its conversation. Chromium may omit layout ancestors from
// Control View while still exposing them through Raw View in compact window layouts.
internal sealed record ComposerTarget(AutomationElement Scope, string Identity, bool IsNewChat = false)
{
    internal const string CodexScope = "group/thread-scroll-layout";
    internal const string CodexNewChatScope = "group/home-composer-layout";
    internal const string HarnessScope = "an3WzG_body";

    internal static ComposerTarget Locate(nint window, string provider, bool requireFocus)
    {
        var root = AutomationElement.FromHandle(window);
        if (requireFocus && Native.GetForegroundWindow() != window) throw Fail();
        var focused = AutomationElement.FocusedElement;
        var active = FromElement(focused, root, provider);
        if (active != null && Native.GetForegroundWindow() == window) return active;
        if (requireFocus) throw Fail();
        // Read-only diagnostics can target a background window. Never use this fallback
        // for enhancement, and refuse ambiguous editors instead of choosing the first.
        var candidates = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        var targets = candidates.Cast<AutomationElement>().Select(element => FromElement(element, root, provider))
            .Where(target => target != null).DistinctBy(target => target!.Identity).ToList();
        if (targets.Count != 1) throw Fail();
        return targets[0]!;
    }

    private static ComposerTarget? FromElement(AutomationElement? element, AutomationElement root, string provider)
    {
        if (element == null) return null;
        AutomationElement? editor = null, scope = null;
        var chain = new List<string>();
        var ownerFound = false;
        for (var depth = 0; element != null && depth < 80; depth++, element = TreeWalker.RawViewWalker.GetParent(element))
        {
            if (element.Equals(root)) { ownerFound = true; break; }
            var current = element.Current;
            if (editor == null)
            {
                if (current.ControlType != ControlType.Edit) continue;
                if (current.IsPassword || current.IsOffscreen || !current.IsEnabled) return null;
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && ((ValuePattern)value).Current.IsReadOnly) return null;
                editor = element;
            }
            chain.Add(current.ClassName);
            if (scope == null && (HasClass(current.ClassName, provider == "codex" ? CodexScope : HarnessScope)
                || provider == "codex" && HasClass(current.ClassName, CodexNewChatScope))) scope = element;
        }
        if (!ownerFound || editor == null || scope == null || !IsComposerChain(provider, chain)) return null;
        var isNewChat = provider == "codex" && HasClass(scope.Current.ClassName, CodexNewChatScope);
        return new(scope, (isNewChat ? "draft:" : "") + string.Join(",", editor.GetRuntimeId()), isNewChat);
    }

    // Shared policy seam: tests exercise extra inputs, compact/expanded layout wrappers,
    // nested panes and lookalike search editors without touching personal chat contents.
    internal static bool IsComposerChain(string provider, IReadOnlyList<string> chain)
    {
        if (chain.Count == 0) return false;
        if (provider == "codex")
        {
            if (!HasClass(chain[0], "ProseMirror")) return false;
            var body = -1; var scope = -1;
            for (var i = 1; i < chain.Count; i++)
            {
                if (body < 0 && chain[i].Split(' ').Any(token => token.StartsWith("_ComposerLayoutBody_", StringComparison.Ordinal))) body = i;
                if (HasClass(chain[i], CodexScope) || HasClass(chain[i], CodexNewChatScope)) { scope = i; break; }
            }
            return body >= 0 && scope > body && chain.Count(c => HasClass(c, CodexScope) || HasClass(c, CodexNewChatScope)) == 1;
        }
        if (provider == "harness")
            return HasClass(chain[0], "GRgSLq_input") && chain.TakeWhile(c => !HasClass(c, HarnessScope))
                .Any(c => HasClass(c, "an3WzG_composerSeat")) && chain.Any(c => HasClass(c, HarnessScope));
        return false;
    }

    internal static bool HasClass(string classes, string token) => classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Contains(token, StringComparer.Ordinal);
    private static InvalidOperationException Fail() => new("请先在 Codex 或 Harness 的聊天输入框内选中文字并保持焦点；无法确定当前聊天输入框，原文未替换。");
}
