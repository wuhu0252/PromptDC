using System.Diagnostics;
using System.Windows.Automation;

namespace PromptDC;

internal sealed record HarnessSnapshot(List<ChatMessage> Fragments, bool ConversationScoped);

internal static class HarnessWindow
{
    // Verified against the signed official Windows 0.2.1-rc.3 build. These raw UIA
    // ancestors survive Chromium's flattened Control View; they are NOT localized titles.
    private const string UserBubbleClass = "LOmM4q_bubble";

    internal static bool IsWindow(nint window)
    {
        try
        {
            var path = WindowCapture.ProcessPath(window);
            return Path.GetFileName(path).Equals("DeepSeek Harness.exe", StringComparison.OrdinalIgnoreCase)
                && FileVersionInfo.GetVersionInfo(path).ProductName == "DeepSeek Harness";
        }
        catch { return false; }
    }

    internal static HarnessSnapshot Capture(nint window, ComposerTarget? target = null)
    {
        if (!IsWindow(window)) throw new InvalidOperationException("当前窗口不是 DeepSeek Harness 官方桌面版。");
        if (FileVersionInfo.GetVersionInfo(WindowCapture.ProcessPath(window)).FileVersion != "0.2.1-rc.3")
            throw new InvalidOperationException("此 Harness 版本尚未验证，已停止读取上下文，原文未替换。");
        var root = AutomationElement.FromHandle(window);
        var documents = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
        var pages = documents.Cast<AutomationElement>().Where(element => !element.Current.IsOffscreen
            && element.TryGetCurrentPattern(ValuePattern.Pattern, out var value)
            && ((ValuePattern)value).Current.Value == "dsh-app://app/").ToList();
        if (pages.Count != 1) throw new InvalidOperationException("无法唯一确定 Harness 主页面，原文未替换。");
        var page = pages[0];
        target ??= ComposerTarget.Locate(window, "harness", false);
        // Harness's body is a Raw View-only container. Query the exposed document and
        // filter every fragment back to the exact selected body rather than depending
        // on FindAll traversing an ignored Chromium accessibility node.
        var elements = page.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        if (elements.Count > 12000) throw new InvalidOperationException("Harness 界面过大，已停止上下文读取。");
        var bodies = new HashSet<string>(StringComparer.Ordinal);
        var fragments = new List<ChatMessage>();
        foreach (AutomationElement element in elements)
        {
            var data = element.Current;
            if (data.IsPassword || string.IsNullOrWhiteSpace(data.Name)) continue;
            if (data.ControlType != ControlType.Text && data.ControlType != ControlType.ListItem) continue;
            // Some Chromium list items expose their text only as Name. Avoid double counting
            // when they also expose a text child.
            if (data.ControlType == ControlType.ListItem && element.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)) != null) continue;
            var current = element;
            var user = false;
            AutomationElement? body = null;
            var excluded = false;
            for (var depth = 0; depth < 40; depth++)
            {
                current = TreeWalker.RawViewWalker.GetParent(current);
                if (current == null || current.Equals(page)) break;
                var ancestor = current.Current;
                if (ComposerTarget.HasClass(ancestor.ClassName, ComposerTarget.HarnessScope)) { body = current; break; }
                if (ComposerTarget.HasClass(ancestor.ClassName, UserBubbleClass)) user = true;
                if (ancestor.IsPassword || ancestor.ControlType == ControlType.Edit
                    || ancestor.ControlType == ControlType.Button || ancestor.ControlType == ControlType.TreeItem
                    || ancestor.ControlType == ControlType.Menu || ancestor.ControlType == ControlType.MenuBar
                    || ancestor.ControlType == ControlType.TabItem || ancestor.ControlType == ControlType.StatusBar)
                { excluded = true; break; }
            }
            if (body == null || excluded || !body.Equals(target.Scope)) continue;
            bodies.Add(string.Join(",", body.GetRuntimeId()));
            if (data.Name.Length > 6000) continue;
            fragments.Add(new(user ? "user" : "assistant", data.Name));
        }
        if (bodies.Count != 1 || fragments.Count == 0)
            throw new InvalidOperationException("无法唯一识别 Harness 对话区域，原文未替换。");
        return new(fragments, true);
    }
}
