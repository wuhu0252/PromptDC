using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace PromptDC;

internal sealed record WindowText(string Type, string Text, int Width, int Height, bool Offscreen, string Excluded = "");
internal sealed record WindowSnapshot(List<WindowText> Texts, bool ConversationScoped = false);

internal static class WindowCapture
{
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    public static bool IsCodexWindow(nint window)
    {
        try
        {
            var path = ProcessPath(window);
            return path.Contains("OpenAI.Codex", StringComparison.OrdinalIgnoreCase)
                || (Path.GetFileName(path).Equals("Codex.exe", StringComparison.OrdinalIgnoreCase)
                    && FileVersionInfo.GetVersionInfo(path).ProductName?.Contains("Codex", StringComparison.OrdinalIgnoreCase) == true);
        }
        catch { return false; }
    }
    internal static string ProcessPath(nint window)
    {
        GetWindowThreadProcessId(window, out var pid);
        return Process.GetProcessById((int)pid).MainModule?.FileName ?? "";
    }
    // Called only by the disposable worker process. UIA calls are never made on the UI thread.
    public static WindowSnapshot Capture(nint window, ComposerTarget? target = null)
    {
        // Do not count every chat-shaped group in the whole window. In Chromium's compact
        // layout this node can exist only in Raw View, and other panes may have other editors.
        target ??= ComposerTarget.Locate(window, "codex", false);
        var root = target.Scope;
        var cache = new CacheRequest { TreeScope = TreeScope.Element };
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(AutomationElement.IsOffscreenProperty);
        cache.Add(AutomationElement.IsPasswordProperty);
        cache.Add(AutomationElement.BoundingRectangleProperty);
        var texts = new List<WindowText>();
        using (cache.Activate())
        {
            var elements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            if (elements.Count > 12000) throw new InvalidOperationException("当前对话界面过大，无法完整核对，原文未替换。");
            for (var i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                try
                {
                    var current = element.Cached;
                    if (current.IsPassword || current.ControlType == ControlType.Edit) continue;
                    var name = current.Name;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var excluded = current.ControlType == ControlType.Text ? Exclusion(element, root) : "";
                    if (excluded == "input") continue;
                    var rect = current.BoundingRectangle;
                    texts.Add(new(current.ControlType.ProgrammaticName, name.Length > 24000 ? name[..24000] : name,
                        (int)rect.Width, (int)rect.Height, current.IsOffscreen, excluded));
                }
                catch (ElementNotAvailableException) { }
            }
        }
        return new(texts, true);
    }
    private static string Exclusion(AutomationElement element, AutomationElement root)
    {
        var current = element;
        for (var depth = 0; depth < 30; depth++)
        {
            current = TreeWalker.RawViewWalker.GetParent(current);
            if (current == null || current.Equals(root)) break;
            var data = current.Current;
            var type = data.ControlType;
            if (data.IsPassword || type == ControlType.Edit || data.AutomationId.Contains("composer", StringComparison.OrdinalIgnoreCase)) return "input";
            if (type == ControlType.Button) return "button";
            if (type == ControlType.ListItem) return "listitem";
            if (type == ControlType.Menu || type == ControlType.MenuItem || type == ControlType.ToolBar
                || type == ControlType.TabItem || type == ControlType.TreeItem
                || data.AutomationId.Contains("sidebar", StringComparison.OrdinalIgnoreCase)) return "navigation";
        }
        return "";
    }
}
