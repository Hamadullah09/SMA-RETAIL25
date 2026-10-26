using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace RetailPlus.App.Infrastructure;

/// <summary>
/// Keyboard shortcuts that can always be seen: a button with a <see cref="Look.ShortcutProperty"/> shows its key as a
/// badge, and pressing that key presses the button, in whatever window is active, as long as the button is on screen
/// and enabled. Save and Cancel buttons show Enter and Esc, which WPF already handles for them.
/// </summary>
public static class Shortcuts
{
    /// <summary>Keys a text box with text uses for editing; they only become shortcuts when the box is empty.</summary>
    private static readonly HashSet<string> EditingKeys = ["Del", "Esc", "Ctrl+A", "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+Y", "Ctrl+Del"];

    /// <summary>Called once at start-up: every window answers the shortcuts of its buttons.</summary>
    public static void Register() =>
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnKey));

    private static void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Handled || sender is not Window window) return;
        var gesture = Describe(e);
        if (gesture is null) return;
        if (Keyboard.FocusedElement is TextBox { Text.Length: > 0 } && EditingKeys.Contains(gesture)) return;
        if (Find(window, gesture) is not { } button) return;
        e.Handled = true;
        Press(button);
    }

    /// <summary>
    /// The key as it is written on the badges ("F5", "Ctrl+P", "Ctrl+Shift+S", "Del", "Esc"), or null for keys that
    /// type (letters and digits without Ctrl or Alt), move (arrows, Tab) or confirm (Enter).
    /// </summary>
    public static string? Describe(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        string? name = key switch
        {
            >= Key.F1 and <= Key.F24 => key.ToString(),
            Key.Delete => "Del",
            Key.Insert => "Ins",
            Key.Escape => "Esc",
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            >= Key.NumPad0 and <= Key.NumPad9 when mods != ModifierKeys.None => ((int)(key - Key.NumPad0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            >= Key.A and <= Key.Z => key.ToString(),
            _ => null,
        };
        if (name is null) return null;
        bool typing = name.Length == 1 && (mods & (ModifierKeys.Control | ModifierKeys.Alt)) == 0;
        if (typing) return null;
        return (mods.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") + (mods.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") +
               (mods.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") + name;
    }

    /// <summary>The visible, enabled button of <paramref name="root"/> whose badge shows <paramref name="gesture"/>.</summary>
    public static ButtonBase? Find(DependencyObject root, string gesture)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is ButtonBase b && string.Equals(Look.GetShortcut(b), gesture, StringComparison.OrdinalIgnoreCase)
                && b.IsVisible && b.IsEnabled && !(gesture is "Esc" && b is Button { IsCancel: true }))
                return b;
            if (node is not (Visual or System.Windows.Media.Media3D.Visual3D)) continue;
            for (int i = 0, n = VisualTreeHelper.GetChildrenCount(node); i < n; i++) queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }

    /// <summary>Presses a button the way a click does (toggles a toggle button).</summary>
    public static void Press(ButtonBase button)
    {
        switch (button)
        {
            case ToggleButton t when new ToggleButtonAutomationPeer(t).GetPattern(PatternInterface.Toggle) is IToggleProvider toggle:
                toggle.Toggle();
                break;
            case Button b when new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke:
                invoke.Invoke();
                break;
        }
    }
}
