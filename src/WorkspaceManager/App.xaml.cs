using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace WorkspaceManager;

public partial class App : Application
{
    public static Settings Settings { get; private set; } = new();
    public static App Instance => (App)Current;

    Forms.NotifyIcon? tray;
    FlyoutWindow? flyout;
    Mutex? mutex;
    EventWaitHandle? showEvent;
    DispatcherTimer? autosaveTimer;
    Window? menuHost;

    static void LogError(object? ex)
    {
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkspaceManager");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "error.log"), $"[{DateTime.Now:s}] {ex}\n\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, a) => LogError(a.ExceptionObject);
        DispatcherUnhandledException += (_, a) => { LogError(a.Exception); a.Handled = true; };

        mutex = new Mutex(true, @"Local\WorkspaceManager.Singleton", out bool created);
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WorkspaceManager.Show");
        if (!created)
        {
            showEvent.Set(); // already running: ask the existing instance to open its panel
            Shutdown();
            return;
        }

        base.OnStartup(e);
        bool firstRun = !Settings.Exists;
        Settings = Settings.Load();
        if (firstRun)
        {
            Settings.StartWithWindows = true;
            Settings.Save();
        }

        Theme.Apply(Resources);
        SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            Theme.Apply(Resources);
            foreach (Window w in Windows) (w as FluentWindow)?.ApplyBackdrop();
        });

        flyout = new FlyoutWindow();
        tray = new Forms.NotifyIcon { Icon = LoadTrayIcon(), Text = "Workspace Manager", Visible = true };
        tray.MouseUp += (_, a) =>
        {
            if (a.Button == Forms.MouseButtons.Left) flyout.Toggle();
            else if (a.Button == Forms.MouseButtons.Right) ShowTrayMenu();
        };

        new Thread(() =>
        {
            while (showEvent.WaitOne()) Dispatcher.BeginInvoke(() => flyout.ShowFlyout());
        }) { IsBackground = true }.Start();

        SessionEnding += (_, _) => AutoSave();

        bool atSignIn = e.Args.Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        if (atSignIn && Settings.AskOnStartup)
        {
            // Give the desktop a moment to settle after sign-in.
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (_, _) => { t.Stop(); if (!ShowStartupPrompt()) StartAutosave(); };
            t.Start();
        }
        else
        {
            StartAutosave();
            if (!atSignIn) flyout.ShowFlyout();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (tray != null) { tray.Visible = false; tray.Dispose(); }
        base.OnExit(e);
    }

    static System.Drawing.Icon LoadTrayIcon()
    {
        var s = GetResourceStream(new Uri("pack://application:,,,/app.ico")).Stream;
        return new System.Drawing.Icon(s, Forms.SystemInformation.SmallIconSize);
    }

    /// <summary>Shows the "restore your apps?" prompt. Returns false if there was nothing to offer.</summary>
    public bool ShowStartupPrompt(bool force = false)
    {
        string name = Settings.StartupProfile;
        var profile = Workspace.Load(name);
        if (profile == null || profile.Apps.Count == 0) { name = "default"; profile = Workspace.Load(name); }
        if (profile == null || profile.Apps.Count == 0) return false;
        var running = Workspace.GetRunningPaths();
        if (!force && profile.Apps.All(a => running.Contains(a.Path))) return false;

        var w = new StartupPrompt(name);
        w.Closed += (_, _) => StartAutosave();
        w.Show();
        w.Activate();
        return true;
    }

    void StartAutosave()
    {
        if (autosaveTimer != null) return;
        autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        autosaveTimer.Tick += (_, _) => AutoSave();
        autosaveTimer.Start();
        AutoSave();
    }

    /// <summary>Keeps the "last-session" profile up to date so it can be offered at next sign-in.</summary>
    public void AutoSave()
    {
        if (!Settings.AutoSave) return;
        try
        {
            var apps = Workspace.GetOpenApps();
            if (apps.Count > 0) Workspace.Save(Workspace.LastSession, apps);
        }
        catch { }
    }

    public void Notify(string text) =>
        tray?.ShowBalloonTip(3000, "Workspace Manager", text, Forms.ToolTipIcon.None);

    public async void RestoreProfile(string name)
    {
        var profile = Workspace.Load(name);
        if (profile == null) return;
        var running = Workspace.GetRunningPaths();
        var todo = profile.Apps.Where(a => !running.Contains(a.Path)).ToList();
        var result = await Workspace.RestoreAsync(todo);
        Notify(result.Summary);
    }

    static MenuItem Item(string header, string? glyph, Action? onClick, bool bold = false)
    {
        var mi = new MenuItem { Header = header };
        if (bold) mi.FontWeight = FontWeights.SemiBold;
        if (glyph != null)
            mi.Icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14 };
        if (onClick != null) mi.Click += (_, _) => onClick();
        return mi;
    }

    void ShowTrayMenu()
    {
        menuHost ??= new Window
        {
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, AllowsTransparency = true,
            Background = Brushes.Transparent, Width = 1, Height = 1, Left = -10000, Top = -10000, Topmost = true,
        };
        menuHost.Show();
        menuHost.Activate();

        var profiles = Workspace.ListProfiles();
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        menu.Items.Add(Item("Open Workspace Manager", "", () => flyout!.ShowFlyout(), bold: true));
        menu.Items.Add(new Separator());

        var save = Item("Save open apps to", "", null);
        foreach (var p in profiles.Where(p => p != Workspace.LastSession).DefaultIfEmpty("default").Distinct())
        {
            string name = p;
            save.Items.Add(Item(Workspace.DisplayOf(name), null, () =>
            {
                var apps = Workspace.GetOpenApps();
                Workspace.Save(name, apps);
                Notify($"Saved {apps.Count} apps to {Workspace.DisplayOf(name)}.");
            }));
        }
        save.Items.Add(new Separator());
        save.Items.Add(Item("New profile…", "", () => flyout!.ShowFlyout(newProfile: true)));
        menu.Items.Add(save);

        var restore = Item("Reopen apps from", "", null);
        if (profiles.Count == 0) restore.IsEnabled = false;
        foreach (var p in profiles)
        {
            string name = p;
            restore.Items.Add(Item(Workspace.DisplayOf(name), null, () => RestoreProfile(name)));
        }
        menu.Items.Add(restore);
        menu.Items.Add(new Separator());

        var startup = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = Settings.StartWithWindows };
        startup.Click += (_, _) => Settings.StartWithWindows = startup.IsChecked;
        menu.Items.Add(startup);
        menu.Items.Add(Item("Exit", "", () => Shutdown()));

        menu.Closed += (_, _) => menuHost.Hide();
        menu.IsOpen = true;
    }
}
