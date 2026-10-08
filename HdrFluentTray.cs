using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

[assembly: System.Reflection.AssemblyTitle("FluentHDR")]
[assembly: System.Reflection.AssemblyDescription("Fluent-style automatic SDR and HDR switching for Windows")]
[assembly: System.Reflection.AssemblyProduct("FluentHDR")]
[assembly: System.Reflection.AssemblyVersion("0.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.1.0.0")]

public static class HdrFluentTray
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
        AppContext.SetSwitch("Switch.System.Windows.DoNotUsePresentationDpiCapabilityTier2OrGreater", false);
        string folder = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        try {
            if (args.Contains("--quit")) {
                try { using (EventWaitHandle signal = EventWaitHandle.OpenExisting("Local\\CodexHdrSwitch.FluentQuit")) signal.Set(); } catch { }
                return 0;
            }
            if (args.Length > 0 && args[0] == "--render-preview") {
                Application previewApp = new Application();
                TrayController controller = new TrayController(folder, false);
                controller.RefreshSynchronously();
                controller.ExportPreview(args[1]);
                controller.Dispose();
                return 0;
            }
            bool created;
            using (Mutex instance = new Mutex(true, "Local\\CodexHdrSwitch.FluentTray", out created)) {
                if (!created) {
                    if (!args.Contains("--startup") || args.Contains("--settings")) {
                        try { using (EventWaitHandle signal = EventWaitHandle.OpenExisting(args.Contains("--settings") ? "Local\\CodexHdrSwitch.FluentSettings" : "Local\\CodexHdrSwitch.FluentShow")) signal.Set(); } catch { }
                    }
                    return 0;
                }
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                using (TrayController controller = new TrayController(folder, true)) {
                    controller.Start(!args.Contains("--startup") && !args.Contains("--settings"));
                    if (args.Contains("--settings")) controller.ShowSettings();
                    app.Run();
                }
                instance.ReleaseMutex();
            }
            return 0;
        } catch (Exception ex) {
            try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexHdrSwitch", "tray-error.log"), DateTime.Now.ToString("s") + " " + ex + Environment.NewLine); } catch { }
            Forms.MessageBox.Show("显示模式工具未能启动。请从开始菜单重新打开。\n\n" + ex.Message, "显示模式", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Error);
            return 1;
        }
    }
}

public sealed class TrayController : IDisposable
{
    private readonly string folder, stateFolder, manualFile, stopFile, statusFile, settingsFile, configFile, startupFile;
    private readonly JavaScriptSerializer json = new JavaScriptSerializer();
    private readonly bool live;
    private readonly SolidColorBrush surface = new SolidColorBrush(), inner = new SolidColorBrush(), text = new SolidColorBrush(), subtle = new SolidColorBrush(), line = new SolidColorBrush(), accent = new SolidColorBrush(), selected = new SolidColorBrush();
    private readonly Dictionary<string, ToggleButton> modeButtons = new Dictionary<string, ToggleButton>();
    private Window popup, settingsWindow;
    private TextBlock monitorLabel, currentMode, contextLabel, modeHint, notice;
    private Border statusDot;
    private Forms.NotifyIcon tray;
    private Forms.ContextMenuStrip trayMenu;
    private DispatcherTimer timer;
    private EventWaitHandle showSignal, quitSignal, settingsSignal;
    private RegisteredWaitHandle showRegistration, quitRegistration, settingsRegistration;
    private Dictionary<string, object> config;
    private HdrDisplay.DisplayInfo[] displays = new HdrDisplay.DisplayInfo[0];
    private Dictionary<string, object> worker;
    private string theme = "system", mode = "auto", actual = "SDR", lastIconKey = "";
    private bool dark, busy, refreshing, closing, workerStarting;
    private DateTime lastWorkerStart = DateTime.MinValue;
    private string errorMessage = "", readError = "";

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attribute, ref int value, int size);

    public TrayController(string folder, bool live)
    {
        this.folder = folder; this.live = live;
        stateFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexHdrSwitch");
        Directory.CreateDirectory(stateFolder);
        manualFile = Path.Combine(stateFolder, "manual.json"); stopFile = Path.Combine(stateFolder, "stop.request"); statusFile = Path.Combine(stateFolder, "status.json");
        settingsFile = Path.Combine(stateFolder, "ui-settings.json"); configFile = Path.Combine(folder, "config.json");
        startupFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "HDR 自动切换.lnk");
        config = ReadJson(configFile) ?? new Dictionary<string, object>();
        Dictionary<string, object> settings = ReadJson(settingsFile);
        if (settings != null) theme = Value(settings, "Theme", "system");
        ApplyTheme();
        BuildPopup();
    }

    public void Start(bool show)
    {
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CodexHdrSwitch.FluentShow");
        showRegistration = ThreadPool.RegisterWaitForSingleObject(showSignal, delegate { Application.Current.Dispatcher.BeginInvoke(new Action(ShowPopup)); }, null, -1, false);
        quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CodexHdrSwitch.FluentQuit");
        quitRegistration = ThreadPool.RegisterWaitForSingleObject(quitSignal, delegate { Application.Current.Dispatcher.BeginInvoke(new Action(Quit)); }, null, -1, false);
        settingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CodexHdrSwitch.FluentSettings");
        settingsRegistration = ThreadPool.RegisterWaitForSingleObject(settingsSignal, delegate { Application.Current.Dispatcher.BeginInvoke(new Action(delegate { popup.Hide(); ShowSettings(); })); }, null, -1, false);
        trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Opening += delegate { BuildTrayMenu(); };
        tray = new Forms.NotifyIcon();
        tray.Text = "显示模式";
        tray.Icon = MakeTrayIcon(false);
        tray.ContextMenuStrip = trayMenu;
        tray.Visible = true;
        tray.MouseClick += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) { if (popup.IsVisible) popup.Hide(); else ShowPopup(); } };
        timer = new DispatcherTimer(); timer.Interval = TimeSpan.FromSeconds(2); timer.Tick += delegate { RefreshAsync(); }; timer.Start();
        EnsureWorker();
        RefreshAsync();
        if (show) ShowPopup();
    }

    private Dictionary<string, object> ReadJson(string path)
    {
        try { return json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8)); } catch { return null; }
    }
    private static string Value(Dictionary<string, object> data, string key, string fallback)
    {
        object value; return data != null && data.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : fallback;
    }
    private static bool Flag(Dictionary<string, object> data, string key)
    {
        object value; return data != null && data.TryGetValue(key, out value) && value != null && Convert.ToBoolean(value);
    }
    private void WriteJson(string path, object data)
    {
        string temp = path + "." + Process.GetCurrentProcess().Id + ".tmp";
        File.WriteAllText(temp, json.Serialize(data), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
    }
    private static bool RegistryLight(string name)
    {
        try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) return key == null || Convert.ToInt32(key.GetValue(name, 1)) != 0; } catch { return true; }
    }
    private void ApplyTheme()
    {
        bool nextDark = theme == "dark" || (theme == "system" && !RegistryLight("AppsUseLightTheme"));
        bool paletteChanged = dark != nextDark || surface.Color != ColorOf(nextDark ? "#262A33" : "#F9FAFC");
        dark = nextDark;
        if (!paletteChanged) return;
        surface.Color = ColorOf(dark ? "#262A33" : "#F9FAFC");
        inner.Color = ColorOf(dark ? "#1D2129" : "#EDF0F5");
        text.Color = ColorOf(dark ? "#F3F4F7" : "#20242D");
        subtle.Color = ColorOf(dark ? "#AEB7C7" : "#636C7B");
        line.Color = ColorOf(dark ? "#424855" : "#DEE3EC");
        accent.Color = ColorOf(dark ? "#8AAAFF" : "#3868D7");
        selected.Color = ColorOf(dark ? "#303E60" : "#E7EDFC");
        if (Application.Current != null) foreach (Window window in Application.Current.Windows) {
            if (!window.Resources.Contains("HdrSurface")) continue;
            SetDialogPalette(window);
            IntPtr h = new WindowInteropHelper(window).Handle;
            if (h != IntPtr.Zero) { int value = dark ? 1 : 0; DwmSetWindowAttribute(h, 20, ref value, 4); }
        }
    }
    private void SetDialogPalette(Window window)
    {
        window.Resources["HdrSurface"] = surface.CloneCurrentValue(); window.Resources["HdrInner"] = inner.CloneCurrentValue();
        window.Resources["HdrText"] = text.CloneCurrentValue(); window.Resources["HdrLine"] = line.CloneCurrentValue();
        window.Resources["HdrAccent"] = accent.CloneCurrentValue(); window.Resources["HdrSelected"] = selected.CloneCurrentValue();
    }
    private static Color ColorOf(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
    private TextBlock Label(string value, double size, Brush brush)
    {
        return new TextBlock { Text = value, FontSize = size, Foreground = brush, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), VerticalAlignment = VerticalAlignment.Center };
    }
    private TextBlock Symbol(string glyph, double size, Brush brush)
    {
        TextBlock symbol = Label(glyph, size, brush); symbol.FontFamily = new FontFamily("Segoe Fluent Icons"); return symbol;
    }
    private ControlTemplate ButtonTemplate(Type controlType)
    {
        FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        if (controlType == typeof(TabItem)) presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.AppendChild(presenter);
        return new ControlTemplate(controlType) { VisualTree = border };
    }
    private Button Button(string caption, RoutedEventHandler click, bool quiet = false)
    {
        Button button = new Button { Content = caption, FontSize = 12, Foreground = text, Background = inner, BorderThickness = new Thickness(0), Padding = new Thickness(12, 8, 12, 8), MinHeight = 34, Template = ButtonTemplate(typeof(Button)), Cursor = Cursors.Hand };
        Brush restingBackground = null, restingForeground = null;
        button.Click += click;
        button.MouseEnter += delegate { restingBackground = button.Background; restingForeground = button.Foreground; if (quiet) button.Foreground = accent; else button.Background = selected; };
        button.MouseLeave += delegate { if (restingBackground != null) button.Background = restingBackground; if (restingForeground != null) button.Foreground = restingForeground; };
        AutomationProperties.SetName(button, caption);
        return button;
    }

    private void BuildPopup()
    {
        popup = new Window { Title = "显示模式", Width = 360, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true, UseLayoutRounding = true, SnapsToDevicePixels = true };
        popup.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) popup.Hide(); };
        popup.Deactivated += delegate { if (!busy) popup.Hide(); };
        Border shell = new Border { Background = surface, BorderBrush = line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Margin = new Thickness(8), Padding = new Thickness(20), Effect = new DropShadowEffect { BlurRadius = 15, ShadowDepth = 2, Opacity = .22 } };
        StackPanel root = new StackPanel(); shell.Child = root; popup.Content = shell;
        Grid header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Border iconTile = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = selected, HorizontalAlignment = HorizontalAlignment.Left, Child = Symbol("\uE7F4", 20, accent) };
        ((TextBlock)iconTile.Child).HorizontalAlignment = HorizontalAlignment.Center;
        header.Children.Add(iconTile);
        StackPanel headerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        headerText.Children.Add(Label("显示模式", 15, text)); monitorLabel = Label("外接显示器", 11, subtle); monitorLabel.Margin = new Thickness(0, 3, 0, 0); monitorLabel.TextTrimming = TextTrimming.CharacterEllipsis; headerText.Children.Add(monitorLabel); Grid.SetColumn(headerText, 1); header.Children.Add(headerText);
        Button close = Button("\uE8BB", delegate { popup.Hide(); }); close.FontFamily = new FontFamily("Segoe Fluent Icons"); close.FontSize = 10; close.Padding = new Thickness(7); close.MinHeight = 28; close.Width = 28; close.Background = Brushes.Transparent; close.VerticalAlignment = VerticalAlignment.Top; AutomationProperties.SetName(close, "关闭面板"); Grid.SetColumn(close, 2); header.Children.Add(close);
        root.Children.Add(header);
        Grid state = new Grid { Margin = new Thickness(0, 24, 0, 20) }; state.ColumnDefinitions.Add(new ColumnDefinition()); state.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel modeRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        statusDot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = accent, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        currentMode = Label("SDR", 26, text); modeRow.Children.Add(statusDot); modeRow.Children.Add(currentMode); state.Children.Add(modeRow);
        StackPanel context = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        contextLabel = Label("写代码", 12, subtle); contextLabel.TextAlignment = TextAlignment.Right;
        modeHint = Label("自动识别已开启", 11, subtle); modeHint.Margin = new Thickness(0, 4, 0, 0); modeHint.TextAlignment = TextAlignment.Right;
        context.Children.Add(contextLabel); context.Children.Add(modeHint); Grid.SetColumn(context, 1); state.Children.Add(context); root.Children.Add(state);
        Border group = new Border { CornerRadius = new CornerRadius(10), Background = inner, Padding = new Thickness(4) };
        UniformGrid modes = new UniformGrid { Columns = 3 };
        string[] keys = { "auto", "sdr", "hdr" }, labels = { "自动", "SDR", "HDR" }, symbols = { "\uE8EE", "\uE943", "\uE714" };
        for (int i = 0; i < keys.Length; i++) {
            string key = keys[i]; ToggleButton button = new ToggleButton { MinHeight = 38, Margin = new Thickness(1, 0, 1, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(7, 6, 7, 6), Template = ButtonTemplate(typeof(ToggleButton)) };
            StackPanel content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            TextBlock symbol = Symbol(symbols[i], 14, text); symbol.Margin = new Thickness(0, 0, 6, 0); content.Children.Add(symbol); content.Children.Add(Label(labels[i], 12, text)); button.Content = content;
            button.Click += delegate { ChangeMode(key); };
            AutomationProperties.SetName(button, key == "auto" ? "自动模式" : "固定 " + key.ToUpperInvariant());
            modes.Children.Add(button); modeButtons.Add(key, button);
        }
        group.Child = modes; root.Children.Add(group);
        notice = Label("", 11, subtle); notice.TextWrapping = TextWrapping.Wrap; notice.Margin = new Thickness(0, 10, 0, 0); notice.Visibility = Visibility.Collapsed; root.Children.Add(notice);
        Grid footer = new Grid { Margin = new Thickness(0, 14, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Button rules = Button("应用规则", delegate { popup.Hide(); ShowRules(); }, true); rules.Background = Brushes.Transparent; rules.Padding = new Thickness(0, 5, 0, 5); rules.HorizontalAlignment = HorizontalAlignment.Left; rules.Foreground = subtle; rules.MinHeight = 28; footer.Children.Add(rules);
        Button settings = Button("\uE713", delegate { popup.Hide(); ShowSettings(); }); settings.FontFamily = new FontFamily("Segoe Fluent Icons"); settings.FontSize = 15; settings.Padding = new Thickness(5); settings.Width = 30; settings.MinHeight = 28; settings.Background = Brushes.Transparent; AutomationProperties.SetName(settings, "设置"); Grid.SetColumn(settings, 1); footer.Children.Add(settings);
        root.Children.Add(footer);
    }

    private HdrDisplay.DisplayInfo[] SelectedDisplays(HdrDisplay.DisplayInfo[] all)
    {
        string[] selectors = Strings(config, "TargetDisplays");
        return all.Where(d => d.HdrSupported && (selectors.Length == 0 || selectors.Any(s => String.Equals(s, d.Name, StringComparison.OrdinalIgnoreCase) || String.Equals(s, d.DeviceName, StringComparison.OrdinalIgnoreCase) || String.Equals(s, d.Key, StringComparison.OrdinalIgnoreCase) || String.Equals(s, d.MonitorDevicePath, StringComparison.OrdinalIgnoreCase)))).ToArray();
    }
    public void RefreshSynchronously()
    {
        displays = SelectedDisplays(HdrDisplay.Enumerate());
        worker = ReadJson(statusFile);
        Dictionary<string, object> manual = ReadJson(manualFile);
        mode = manual == null ? "auto" : (Flag(manual, "Enabled") ? "hdr" : "sdr");
        actual = displays.Length == 0 ? "—" : (displays.All(d => d.ActiveHdr) ? "HDR" : (displays.All(d => !d.ActiveHdr) ? "SDR" : "混合"));
        UpdateView();
    }
    private async void RefreshAsync()
    {
        if (refreshing || closing) return;
        refreshing = true;
        try {
            HdrDisplay.DisplayInfo[] read = await Task.Run(() => HdrDisplay.Enumerate());
            displays = SelectedDisplays(read);
            worker = ReadJson(statusFile);
            Dictionary<string, object> manual = ReadJson(manualFile);
            mode = manual == null ? "auto" : (Flag(manual, "Enabled") ? "hdr" : "sdr");
            actual = displays.Length == 0 ? "—" : (displays.All(d => d.ActiveHdr) ? "HDR" : (displays.All(d => !d.ActiveHdr) ? "SDR" : "混合"));
            readError = "";
            ApplyTheme(); UpdateView(); EnsureWorker();
        } catch (Exception ex) { readError = "暂时无法读取显示器状态"; Log(ex); UpdateView(); }
        finally { refreshing = false; }
    }
    private void UpdateView()
    {
        currentMode.Text = actual;
        monitorLabel.Text = displays.Length == 1 ? displays[0].Name.Trim() : (displays.Length == 0 ? "未连接 HDR 显示器" : displays.Length + " 台 HDR 显示器");
        monitorLabel.ToolTip = monitorLabel.Text;
        statusDot.Background = actual == "HDR" ? new SolidColorBrush(ColorOf(dark ? "#B8A5FF" : "#7960D6")) : accent;
        string reason = Value(worker, "Reason", "");
        contextLabel.Text = mode == "auto" ? (reason.StartsWith("editor:") ? "正在写代码" : (reason.StartsWith("game") ? "正在玩游戏" : (reason.StartsWith("media") || reason.StartsWith("browser-video") || reason.StartsWith("browser-fullscreen") ? "视频与影音" : "日常使用"))) : (mode == "hdr" ? "影音与游戏" : "写代码与阅读");
        modeHint.Text = mode == "auto" ? (Flag(worker, "Running") ? "自动识别已开启" : "正在启动自动识别") : "已固定显示模式";
        foreach (KeyValuePair<string, ToggleButton> item in modeButtons) { item.Value.IsChecked = item.Key == mode; item.Value.Background = item.Key == mode ? selected : Brushes.Transparent; item.Value.IsEnabled = !busy; }
        string backgroundError = Flag(worker, "Running") && !String.IsNullOrEmpty(Value(worker, "LastError", "")) ? "自动切换暂未完成，正在重试。" : "";
        string message = busy ? "正在切换显示模式…" : (displays.Length == 0 ? "连接支持 HDR 的显示器后会自动恢复。" : (!String.IsNullOrEmpty(errorMessage) ? errorMessage : (!String.IsNullOrEmpty(readError) ? readError : backgroundError)));
        notice.Text = message; notice.Visibility = String.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        if (tray != null) {
            tray.Text = "显示模式 · " + actual + " · " + (mode == "auto" ? "自动" : "固定");
            string key = actual + ":" + RegistryLight("SystemUsesLightTheme");
            if (key != lastIconKey) { Drawing.Icon old = tray.Icon; tray.Icon = MakeTrayIcon(actual == "HDR"); if (old != null) old.Dispose(); lastIconKey = key; }
        }
    }
    private Drawing.Icon MakeTrayIcon(bool hdr)
    {
        using (Drawing.Bitmap bitmap = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppArgb))
        using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap))
        using (Drawing.Font font = new Drawing.Font("Segoe Fluent Icons", 23, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Pixel))
        using (Drawing.SolidBrush foreground = new Drawing.SolidBrush(RegistryLight("SystemUsesLightTheme") ? Drawing.Color.FromArgb(40, 47, 59) : Drawing.Color.FromArgb(236, 240, 247)))
        using (Drawing.SolidBrush dot = new Drawing.SolidBrush(hdr ? Drawing.Color.FromArgb(137, 109, 232) : Drawing.Color.FromArgb(88, 155, 255))) {
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.DrawString("\uE7F4", font, foreground, new Drawing.RectangleF(1, 1, 30, 29), new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center });
            using (Drawing.SolidBrush halo = new Drawing.SolidBrush(RegistryLight("SystemUsesLightTheme") ? Drawing.Color.White : Drawing.Color.FromArgb(32, 32, 32))) graphics.FillEllipse(halo, 22, 22, 9, 9);
            graphics.FillEllipse(dot, 23, 23, 7, 7);
            IntPtr handle = bitmap.GetHicon();
            Drawing.Icon icon = (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone(); DestroyIcon(handle); return icon;
        }
    }
    public void ShowPopup()
    {
        if (closing) return;
        RefreshAsync();
        popup.Show(); popup.UpdateLayout();
        Forms.Screen screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        PresentationSource source = PresentationSource.FromVisual(popup);
        Matrix transform = source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformFromDevice : Matrix.Identity;
        Point bottomRight = transform.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        Point topLeft = transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        popup.Left = Math.Max(topLeft.X + 8, bottomRight.X - popup.ActualWidth - 8);
        popup.Top = Math.Max(topLeft.Y + 8, bottomRight.Y - popup.ActualHeight - 8);
        popup.Activate();
    }
    private async void ChangeMode(string requested)
    {
        if (busy) return;
        busy = true; errorMessage = ""; UpdateView();
        try {
            if (requested == "auto") {
                if (File.Exists(manualFile)) File.Delete(manualFile);
                mode = "auto"; EnsureWorker();
            } else {
                bool enable = requested == "hdr";
                WriteJson(manualFile, new Dictionary<string, object> { {"Enabled", enable}, {"Updated", DateTimeOffset.Now.ToString("o")} });
                mode = requested;
                string[] targets = Strings(config, "TargetDisplays");
                await Task.Run(() => {
                    using (Mutex gate = new Mutex(false, "Local\\CodexHdrSwitch.DisplaySet")) {
                        bool owned = false;
                        try {
                            try { owned = gate.WaitOne(10000); } catch (AbandonedMutexException) { owned = true; }
                            if (!owned) throw new InvalidOperationException("Display switch is busy.");
                            HdrDisplay.SetResult[] results = HdrDisplay.Set(enable, targets);
                            if (results.Length == 0) throw new InvalidOperationException("No HDR display connected.");
                            if (results.Any(r => !r.Verified)) throw new InvalidOperationException(String.Join("; ", results.Where(r => !r.Verified).Select(r => r.Error)));
                        } finally { if (owned) gate.ReleaseMutex(); }
                    }
                });
            }
        } catch (Exception ex) { errorMessage = displays.Length == 0 ? "显示器未连接，所选模式已保存。" : "切换未完成，请稍后重试。"; Log(ex); }
        finally { busy = false; UpdateView(); RefreshAsync(); }
        if (requested == "auto" && String.IsNullOrEmpty(errorMessage)) popup.Hide();
    }

    private bool WorkerAlive()
    {
        Dictionary<string, object> state = ReadJson(statusFile);
        if (!Flag(state, "Running")) return false;
        DateTime updated;
        if (!DateTime.TryParse(Value(state, "Updated", ""), out updated) || (DateTime.Now - updated).TotalSeconds > 20) return false;
        int id;
        if (!Int32.TryParse(Value(state, "ProcessId", ""), out id)) return false;
        try { using (Process p = Process.GetProcessById(id)) return !p.HasExited && p.ProcessName.Equals("powershell", StringComparison.OrdinalIgnoreCase); } catch { return false; }
    }
    private void EnsureWorker()
    {
        if (!live || closing || workerStarting || WorkerAlive() || (DateTime.Now - lastWorkerStart).TotalSeconds < 15) return;
        lastWorkerStart = DateTime.Now; workerStarting = true;
        try {
            ProcessStartInfo start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + Path.Combine(folder, "HdrSwitch.ps1") + "\" -Mode Auto -Startup");
            start.UseShellExecute = false; start.CreateNoWindow = true; start.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process child = Process.Start(start)) { }
        } catch (Exception ex) { errorMessage = "自动识别未能启动"; Log(ex); }
        finally { workerStarting = false; }
    }
    private void BuildTrayMenu()
    {
        trayMenu.Items.Clear();
        trayMenu.BackColor = Drawing.Color.FromArgb(dark ? 38 : 249, dark ? 42 : 250, dark ? 51 : 252);
        trayMenu.ForeColor = dark ? Drawing.Color.WhiteSmoke : Drawing.Color.FromArgb(32, 36, 45);
        AddMenu("显示模式 · " + actual, delegate { ShowPopup(); }, false);
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        AddMenu("自动识别", delegate { ChangeMode("auto"); }, mode == "auto");
        AddMenu("固定 SDR", delegate { ChangeMode("sdr"); }, mode == "sdr");
        AddMenu("固定 HDR", delegate { ChangeMode("hdr"); }, mode == "hdr");
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        AddMenu("应用规则", delegate { ShowRules(); }, false);
        AddMenu("登录时启动", delegate { SetStartup(!File.Exists(startupFile)); }, File.Exists(startupFile));
        AddMenu("退出", delegate { Quit(); }, false);
    }
    private void AddMenu(string caption, EventHandler action, bool check)
    {
        Forms.ToolStripMenuItem item = new Forms.ToolStripMenuItem(caption); item.Checked = check; item.Click += action; trayMenu.Items.Add(item);
    }
    private void SetStartup(bool enabled)
    {
        if (!enabled) { if (File.Exists(startupFile)) File.Delete(startupFile); return; }
        Type type = Type.GetTypeFromProgID("WScript.Shell");
        dynamic shell = Activator.CreateInstance(type);
        dynamic shortcut = shell.CreateShortcut(startupFile);
        shortcut.TargetPath = Path.Combine(folder, "HdrFluentTray.exe"); shortcut.Arguments = "--startup"; shortcut.WorkingDirectory = folder; shortcut.Description = "显示模式：代码 SDR，视频和游戏 HDR"; shortcut.IconLocation = Path.Combine(folder, "HdrFluentTray.exe") + ",0"; shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell);
    }
    private Window Dialog(string title, double width, double height)
    {
        Window window = new Window { Title = title, Width = width, Height = height, Background = surface, Foreground = text, WindowStartupLocation = WindowStartupLocation.CenterScreen, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), FontSize = 12, UseLayoutRounding = true, MinWidth = 390, MinHeight = 280 };
        window.SourceInitialized += delegate {
            IntPtr h = new WindowInteropHelper(window).Handle; int corner = 2, themeValue = dark ? 1 : 0;
            DwmSetWindowAttribute(h, 33, ref corner, 4); DwmSetWindowAttribute(h, 20, ref themeValue, 4);
        };
        window.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) window.Close(); };
        SetDialogPalette(window);
        return window;
    }
    private static string[] Strings(Dictionary<string, object> dictionary, string key)
    {
        object array;
        if (!dictionary.TryGetValue(key, out array) || !(array is IEnumerable) || array is string) return new string[0];
        return ((IEnumerable)array).Cast<object>().Where(v => v != null).Select(v => Convert.ToString(v)).ToArray();
    }
    private void ShowRules()
    {
        Window window = Dialog("自动切换规则", 560, 520);
        DockPanel root = new DockPanel { Margin = new Thickness(20) };
        TextBlock intro = Label("代码和终端使用 SDR；播放器与游戏使用 HDR。", 12, subtle); intro.Margin = new Thickness(0, 0, 0, 16); DockPanel.SetDock(intro, Dock.Top); root.Children.Add(intro);
        TabControl tabs = new TabControl { Background = surface, Foreground = text, BorderBrush = line };
        Style tabStyle = new Style(typeof(TabItem));
        tabStyle.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate(typeof(TabItem))));
        tabStyle.Setters.Add(new Setter(Control.BackgroundProperty, inner.Clone()));
        tabStyle.Setters.Add(new Setter(Control.ForegroundProperty, text.Clone()));
        tabStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 9, 12, 9)));
        tabStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 5, 5)));
        Trigger activeTab = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        activeTab.Setters.Add(new Setter(Control.BackgroundProperty, selected.Clone()));
        activeTab.Setters.Add(new Setter(Control.ForegroundProperty, accent.Clone())); tabStyle.Triggers.Add(activeTab);
        string[] keys = { "EditorProcesses", "MediaProcesses", "GameProcesses", "GameRoots" }, names = { "代码 / 终端", "播放器", "游戏", "游戏目录" };
        for (int i = 0; i < keys.Length; i++) {
            string key = keys[i]; bool directories = key == "GameRoots";
            DockPanel panel = new DockPanel { Margin = new Thickness(12) };
            ListBox list = new ListBox { Background = inner, Foreground = text, BorderBrush = line, BorderThickness = new Thickness(1), Padding = new Thickness(5), FontSize = 12 };
            Action reload = delegate { Dictionary<string, object> latest = ReadJson(configFile); if (latest != null) config = latest; list.ItemsSource = Strings(config, key).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray(); };
            StackPanel actions = new StackPanel(); DockPanel.SetDock(actions, Dock.Bottom);
            TextBox entry = new TextBox { Background = inner, Foreground = text, BorderBrush = line, Margin = new Thickness(0, 12, 0, 8), Padding = new Thickness(8), MinHeight = 34 };
            AutomationProperties.SetName(entry, directories ? "游戏安装目录" : "应用名称或 exe 文件名"); entry.ToolTip = directories ? "添加游戏安装目录" : "输入应用名，例如 Code 或 game.exe";
            actions.Children.Add(entry);
            Grid buttons = new Grid(); buttons.ColumnDefinitions.Add(new ColumnDefinition()); buttons.ColumnDefinitions.Add(new ColumnDefinition()); buttons.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock result = Label("", 11, subtle); result.Margin = new Thickness(0, 9, 0, 0); result.TextWrapping = TextWrapping.Wrap;
            Action<string, bool> save = delegate(string value, bool remove) {
                try {
                    Dictionary<string, object> latest = ReadJson(configFile); if (latest == null) throw new InvalidOperationException("Rules file unavailable.");
                    List<string> values = Strings(latest, key).ToList();
                    if (remove) values.RemoveAll(v => String.Equals(v, value, StringComparison.OrdinalIgnoreCase));
                    else if (!values.Any(v => String.Equals(v, value, StringComparison.OrdinalIgnoreCase))) values.Add(value);
                    latest[key] = values.ToArray(); WriteJson(configFile, latest); config = latest; reload(); entry.Clear(); result.Text = "已保存，自动规则将在几秒内更新。";
                } catch (Exception ex) { result.Text = "保存失败，请稍后重试。"; Log(ex); }
            };
            Button add = Button("添加", delegate {
                string value = entry.Text.Trim().Trim('"');
                if (String.IsNullOrWhiteSpace(value)) return;
                if (directories) { try { value = Path.GetFullPath(value); if (!String.Equals(value, Path.GetPathRoot(value), StringComparison.OrdinalIgnoreCase)) value = value.TrimEnd('\\'); } catch { result.Text = "请输入有效的安装目录。"; return; } }
                else { value = Path.GetFileName(value); if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) value = value.Substring(0, value.Length - 4); }
                save(value, false);
            }); add.Margin = new Thickness(0, 0, 6, 0); buttons.Children.Add(add);
            Button browse = Button("浏览…", delegate {
                if (directories) { using (Forms.FolderBrowserDialog picker = new Forms.FolderBrowserDialog()) { picker.Description = "选择游戏安装目录"; if (picker.ShowDialog() == Forms.DialogResult.OK) entry.Text = picker.SelectedPath; } }
                else { Microsoft.Win32.OpenFileDialog picker = new Microsoft.Win32.OpenFileDialog { Filter = "应用程序 (*.exe)|*.exe", Title = "选择应用程序" }; if (picker.ShowDialog(window) == true) entry.Text = picker.FileName; }
            }); browse.Margin = new Thickness(0, 0, 6, 0); Grid.SetColumn(browse, 1); buttons.Children.Add(browse);
            Button removeButton = Button("移除所选", delegate { if (list.SelectedItem != null) save(Convert.ToString(list.SelectedItem), true); }); Grid.SetColumn(removeButton, 2); buttons.Children.Add(removeButton);
            actions.Children.Add(buttons); actions.Children.Add(result); panel.Children.Add(actions); panel.Children.Add(list); reload();
            TabItem tab = new TabItem { Header = names[i], Content = panel, Style = tabStyle }; tabs.Items.Add(tab);
        }
        root.Children.Add(tabs); window.Content = root; window.Show(); window.Activate();
    }
    private ComboBox ChoiceBox(object items, int selectedIndex, string name)
    {
        const string markup = @"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ComboBox}'>
          <Grid>
            <ToggleButton Focusable='False' ClickMode='Press' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
              <ToggleButton.Template><ControlTemplate TargetType='{x:Type ToggleButton}'>
                <Border x:Name='ChoiceBorder' CornerRadius='8' Background='{DynamicResource HdrInner}' BorderBrush='{DynamicResource HdrLine}' BorderThickness='1'/>
                <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='ChoiceBorder' Property='BorderBrush' Value='{DynamicResource HdrAccent}'/></Trigger></ControlTemplate.Triggers>
              </ControlTemplate></ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter Margin='12,0,38,0' VerticalAlignment='Center' IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'/>
            <TextBlock Text='&#xE70D;' FontFamily='Segoe Fluent Icons' FontSize='10' Foreground='{DynamicResource HdrText}' VerticalAlignment='Center' HorizontalAlignment='Right' Margin='0,0,13,0' IsHitTestVisible='False'/>
            <Popup x:Name='PART_Popup' Placement='Bottom' VerticalOffset='5' AllowsTransparency='True' Focusable='False' IsOpen='{TemplateBinding IsDropDownOpen}' PopupAnimation='Fade'>
              <Border CornerRadius='8' Background='{DynamicResource HdrSurface}' BorderBrush='{DynamicResource HdrLine}' BorderThickness='1' Padding='4' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'>
                <ScrollViewer MaxHeight='250' CanContentScroll='True' HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Auto'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer>
              </Border>
            </Popup>
          </Grid>
        </ControlTemplate>";
        ComboBox box = new ComboBox { Margin = new Thickness(0, 8, 0, 20), MinHeight = 36, Foreground = text, FontSize = 12, ItemsSource = (IEnumerable)items, SelectedIndex = selectedIndex, Template = (ControlTemplate)XamlReader.Parse(markup) };
        Style itemStyle = new Style(typeof(ComboBoxItem));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate(typeof(ComboBoxItem))));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("HdrText")));
        Trigger hovered = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
        hovered.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("HdrSelected"))); itemStyle.Triggers.Add(hovered);
        Trigger chosen = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
        chosen.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("HdrAccent"))); itemStyle.Triggers.Add(chosen);
        box.ItemContainerStyle = itemStyle; AutomationProperties.SetName(box, name);
        return box;
    }
    private sealed class DelayChoice
    {
        public int Milliseconds;
        public override string ToString() { return (Milliseconds / 1000.0).ToString("0.###") + " 秒"; }
    }
    private static int DelayValue(Dictionary<string, object> data, string key, int fallback)
    {
        int value; return Int32.TryParse(Value(data, key, ""), out value) && value >= 0 && value <= 3600000 ? value : fallback;
    }
    private ComboBox DelayBox(string key, int fallback, string name, TextBlock feedback)
    {
        int current = DelayValue(config, key, fallback);
        int[] values = new int[] { 1000, 2000, 3000, 5000, 8000, 10000, 15000, 30000, 60000 };
        DelayChoice[] choices = values.Concat(new int[] { current }).Distinct().OrderBy(v => v).Select(v => new DelayChoice { Milliseconds = v }).ToArray();
        ComboBox box = ChoiceBox(choices, Array.FindIndex(choices, v => v.Milliseconds == current), name);
        box.SelectionChanged += delegate {
            DelayChoice choice = box.SelectedItem as DelayChoice; if (choice == null) return;
            try {
                Dictionary<string, object> latest = ReadJson(configFile); if (latest == null) throw new InvalidOperationException("Rules file unavailable.");
                latest[key] = choice.Milliseconds; WriteJson(configFile, latest); config = latest;
                feedback.Text = "已保存，自动模式下生效。";
            } catch (Exception ex) { feedback.Text = "保存失败，请重新选择。"; Log(ex); }
        };
        return box;
    }
    public void ShowSettings()
    {
        if (settingsWindow != null) { settingsWindow.Activate(); return; }
        Dictionary<string, object> latest = ReadJson(configFile); if (latest != null) config = latest;
        Window window = Dialog("显示模式设置", 420, 480); settingsWindow = window;
        window.Closed += delegate { settingsWindow = null; };
        StackPanel root = new StackPanel { Margin = new Thickness(24) };
        CheckBox startup = new CheckBox { Content = "登录 Windows 后运行", IsChecked = File.Exists(startupFile), Foreground = text, Margin = new Thickness(0, 4, 0, 22), FontSize = 13 };
        startup.Click += delegate { try { SetStartup(startup.IsChecked == true); } catch (Exception ex) { Log(ex); } }; root.Children.Add(startup);
        root.Children.Add(Label("外观", 12, subtle));
        ComboBox appearance = ChoiceBox(new string[] { "跟随系统", "浅色", "深色" }, theme == "light" ? 1 : (theme == "dark" ? 2 : 0), "外观");
        appearance.SelectionChanged += delegate { theme = new string[] { "system", "light", "dark" }[appearance.SelectedIndex]; WriteJson(settingsFile, new Dictionary<string, object> { {"Theme", theme} }); ApplyTheme(); UpdateView(); }; root.Children.Add(appearance);
        TextBlock feedback = Label("设置会自动保存。", 11, subtle); feedback.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(Label("自动切换延迟 · 代码／影音／游戏", 12, subtle));
        root.Children.Add(DelayBox("SwitchDelayMs", 2000, "自动切换延迟", feedback));
        root.Children.Add(Label("其它应用返回 SDR 的延迟", 12, subtle));
        root.Children.Add(DelayBox("DefaultDelayMs", 8000, "其它应用返回 SDR 的延迟", feedback));
        TextBlock caption = Label("前台状态持续达到所选时间后再切换。\n仅自动模式生效，实际时间可能有少量偏差。", 11, subtle); caption.TextWrapping = TextWrapping.Wrap; root.Children.Add(caption); root.Children.Add(feedback);
        window.Content = root; window.Show(); window.Activate();
    }
    public void ExportPreview(string path)
    {
        FrameworkElement content = (FrameworkElement)popup.Content;
        content.Measure(new Size(360, Double.PositiveInfinity)); content.Arrange(new Rect(0, 0, 360, content.DesiredSize.Height)); content.UpdateLayout();
        int height = Math.Max(1, (int)Math.Ceiling(content.ActualHeight));
        RenderTargetBitmap bitmap = new RenderTargetBitmap(720, height * 2, 192, 192, PixelFormats.Pbgra32); bitmap.Render(content);
        PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (FileStream stream = File.Create(path)) encoder.Save(stream);
    }
    private void Log(Exception ex)
    {
        try { File.AppendAllText(Path.Combine(stateFolder, "tray-error.log"), DateTime.Now.ToString("s") + " " + ex.Message + Environment.NewLine); } catch { }
    }
    private void Quit()
    {
        closing = true;
        try { File.WriteAllText(stopFile, "stop"); } catch { }
        popup.Hide(); Application.Current.Shutdown();
    }
    public void Dispose()
    {
        closing = true;
        if (timer != null) timer.Stop();
        if (showRegistration != null) showRegistration.Unregister(null);
        if (quitRegistration != null) quitRegistration.Unregister(null);
        if (settingsRegistration != null) settingsRegistration.Unregister(null);
        if (showSignal != null) showSignal.Dispose();
        if (quitSignal != null) quitSignal.Dispose();
        if (settingsSignal != null) settingsSignal.Dispose();
        if (tray != null) { tray.Visible = false; Drawing.Icon icon = tray.Icon; tray.Dispose(); if (icon != null) icon.Dispose(); }
        if (trayMenu != null) trayMenu.Dispose();
        if (popup != null) popup.Close();
    }
}
