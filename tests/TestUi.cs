using System;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

public static class VerifyFluentHover
{
    static Button Find(DependencyObject root, string name) {
        Button button = root as Button;
        if (button != null && AutomationProperties.GetName(button) == name) return button;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { Button match = Find(VisualTreeHelper.GetChild(root, i), name); if (match != null) return match; }
        return null;
    }
    static bool Transparent(Brush brush) { return brush is SolidColorBrush && ((SolidColorBrush)brush).Color.A == 0; }
    static void SimulateHover(Button button, bool enter) { button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = enter ? Mouse.MouseEnterEvent : Mouse.MouseLeaveEvent }); }
    [STAThread] public static int Main(string[] args) {
        new Application();
        using (TrayController controller = new TrayController(args[0], false)) {
            Window popup = (Window)typeof(TrayController).GetField("popup", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller);
            FrameworkElement content = (FrameworkElement)popup.Content;
            content.Measure(new Size(360, double.PositiveInfinity)); content.Arrange(new Rect(0, 0, 360, content.DesiredSize.Height)); content.UpdateLayout();
            foreach (string name in new[] { "应用规则", "设置", "关闭面板" }) {
                Button button = Find(content, name);
                if (button == null || !Transparent(button.Background)) throw new Exception("Initial background is not transparent: " + name);
                Brush baseForeground = button.Foreground;
                SimulateHover(button, true);
                if (name == "应用规则" && (!Transparent(button.Background) || object.ReferenceEquals(baseForeground, button.Foreground))) throw new Exception("Text-only hover is incorrect.");
                SimulateHover(button, false);
                if (!Transparent(button.Background) || !object.ReferenceEquals(baseForeground, button.Foreground)) throw new Exception("Mouse leave failed to restore: " + name);
                SimulateHover(button, true); SimulateHover(button, false);
                if (!Transparent(button.Background)) throw new Exception("Repeated hover failed: " + name);
                Console.WriteLine("PASS: " + name + " hover and mouse-leave restore");
            }
        }
        return 0;
    }
}
