using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.VisualBasic.FileIO;

namespace WorkspaceManager;

public partial class FlyoutWindow : FluentWindow
{
    readonly ObservableCollection<AppItem> savedItems = new();
    readonly ObservableCollection<AppItem> openItems = new();
    DateTime hiddenAt = DateTime.MinValue;
    bool suppress;

    public FlyoutWindow()
    {
        InitializeComponent();
        Backdrop = 3; // acrylic, like the Windows 11 quick settings flyout
        Deactivated += (_, _) => HideFlyout();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) HideFlyout(); };
        AppList.ItemsSource = savedItems;
    }

    string SelectedProfile => (ProfileBox.SelectedItem as ProfileItem)?.Name ?? "default";

    public void Toggle()
    {
        if (IsVisible) HideFlyout();
        else if ((DateTime.Now - hiddenAt).TotalMilliseconds > 300) ShowFlyout();
    }

    public void ShowFlyout(bool newProfile = false)
    {
        ShowMain();
        RefreshAll();
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 12;
        Top = wa.Bottom - Height - 12;
        Show();
        Activate();

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

        if (newProfile) NewProfile_Click(this, new RoutedEventArgs());
    }

    public void HideFlyout()
    {
        if (!IsVisible) return;
        hiddenAt = DateTime.Now;
        Hide();
    }

    // ---------- data ----------

    void RefreshAll()
    {
        openItems.Clear();
        foreach (var a in Workspace.GetOpenApps()) openItems.Add(Track(AppItem.ForOpen(a)));

        var profiles = Workspace.ListProfiles();
        if (!profiles.Contains("default")) profiles.Insert(profiles.Contains(Workspace.LastSession) ? 1 : 0, "default");
        string want = (ProfileBox.SelectedItem as ProfileItem)?.Name ?? App.Settings.LastProfile;
        SetProfiles(profiles, want);

        int saved = Workspace.ListProfiles().Count;
        SubTitle.Text = $"{openItems.Count} app{(openItems.Count == 1 ? "" : "s")} open now · {saved} saved profile{(saved == 1 ? "" : "s")}";
    }

    void SetProfiles(System.Collections.Generic.List<string> profiles, string select)
    {
        suppress = true;
        var items = profiles.Select(p => new ProfileItem(p)).ToList();
        ProfileBox.ItemsSource = items;
        ProfileBox.SelectedItem = items.FirstOrDefault(i => i.Name == select) ?? items.FirstOrDefault();
        suppress = false;
        LoadSaved();
    }

    AppItem Track(AppItem item)
    {
        item.PropertyChanged += (_, _) => UpdateButtons();
        return item;
    }

    void LoadSaved(bool keepStatus = false)
    {
        savedItems.Clear();
        var profile = Workspace.Load(SelectedProfile);
        if (profile != null)
        {
            var running = Workspace.GetRunningPaths();
            foreach (var e in profile.Apps) savedItems.Add(Track(AppItem.ForSaved(e, running.Contains(e.Path))));
        }
        if (!keepStatus)
            StatusText.Text = profile == null ? "Not saved yet" : $"Saved {Workspace.FriendlyDate(profile.SavedAt)}";
        UpdateView();
    }

    void UpdateView()
    {
        bool open = OpenTab.IsChecked == true;
        AppList.ItemsSource = open ? openItems : savedItems;
        bool empty = (open ? openItems.Count : savedItems.Count) == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = open
            ? "No apps are open right now."
            : "Nothing saved in this profile yet.\nOpen your apps and press Save.";
        UpdateButtons();
    }

    void UpdateButtons()
    {
        int toOpen = savedItems.Count(i => i.IsSelected && i.CanSelect);
        RestoreButton.IsEnabled = toOpen > 0;
        SaveButton.IsEnabled = openItems.Any(i => i.IsSelected);
    }

    // ---------- events ----------

    void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppress || ProfileBox.SelectedItem == null) return;
        App.Settings.LastProfile = SelectedProfile;
        App.Settings.Save();
        CloseInlinePanels();
        LoadSaved();
    }

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) UpdateView();
    }

    void AppList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Clicking anywhere on a row toggles its checkbox.
        for (var d = e.OriginalSource as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is CheckBox) return;
            if (d is ListBoxItem lbi && lbi.DataContext is AppItem item)
            {
                if (item.CanSelect) item.IsSelected = !item.IsSelected;
                return;
            }
            if (d is not Visual) return;
        }
    }

    void AppList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Double-click an open app to jump to its window.
        if (OpenTab.IsChecked == true && (e.OriginalSource as FrameworkElement)?.DataContext is AppItem item && item.Entry.Hwnd != IntPtr.Zero)
        {
            HideFlyout();
            Native.FocusWindow(item.Entry.Hwnd);
        }
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var apps = openItems.Where(i => i.IsSelected).Select(i => i.Entry).ToList();
        if (apps.Count == 0) { StatusText.Text = "Check at least one open app to save."; return; }
        Workspace.Save(SelectedProfile, apps);
        SavedTab.IsChecked = true;
        RefreshAll();
        StatusText.Text = $"Saved {apps.Count} app{(apps.Count == 1 ? "" : "s")} to {Workspace.DisplayOf(SelectedProfile)}.";
    }

    async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var apps = savedItems.Where(i => i.IsSelected && i.CanSelect).Select(i => i.Entry).ToList();
        if (apps.Count == 0) return;
        RestoreButton.IsEnabled = false;
        StatusText.Text = $"Opening {apps.Count} app{(apps.Count == 1 ? "" : "s")}…";
        var result = await Workspace.RestoreAsync(apps);
        StatusText.Text = result.Summary;
        await Task.Delay(1500);
        if (IsVisible) LoadSaved(keepStatus: true);
    }

    void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        CloseInlinePanels();
        NewProfilePanel.Visibility = Visibility.Visible;
        NewProfileName.Text = "";
        NewProfileHint.Text = "Saves the apps that are open right now. Letters, numbers, - and _ only.";
        NewProfileName.Focus();
    }

    void NewProfileName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CreateProfile_Click(sender, e);
        else if (e.Key == Key.Escape) { CloseInlinePanels(); e.Handled = true; }
    }

    void CreateProfile_Click(object sender, RoutedEventArgs e)
    {
        string name = NewProfileName.Text.Trim();
        if (!Workspace.IsValidName(name)) { NewProfileHint.Text = "Use letters, numbers, - and _ only."; return; }
        if (File.Exists(Workspace.FileFor(name))) { NewProfileHint.Text = $"“{name}” already exists."; return; }
        var apps = openItems.Where(i => i.IsSelected).Select(i => i.Entry).ToList();
        Workspace.Save(name, apps);
        CloseInlinePanels();
        SavedTab.IsChecked = true;
        ProfileBox.SelectedItem = null;
        App.Settings.LastProfile = name;
        RefreshAll();
        StatusText.Text = $"Created “{name}” with {apps.Count} app{(apps.Count == 1 ? "" : "s")}.";
    }

    void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        CloseInlinePanels();
        if (!File.Exists(Workspace.FileFor(SelectedProfile))) { StatusText.Text = "This profile isn't saved yet."; return; }
        DeleteText.Text = $"Delete “{Workspace.DisplayOf(SelectedProfile)}”? It goes to the Recycle Bin.";
        DeletePanel.Visibility = Visibility.Visible;
    }

    void ConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        string name = SelectedProfile;
        try
        {
            FileSystem.DeleteFile(Workspace.FileFor(name), UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            StatusText.Text = $"Moved “{Workspace.DisplayOf(name)}” to the Recycle Bin.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        CloseInlinePanels();
        ProfileBox.SelectedItem = null;
        string keep = StatusText.Text;
        RefreshAll();
        StatusText.Text = keep;
    }

    void CancelPanel_Click(object sender, RoutedEventArgs e) => CloseInlinePanels();

    void CloseInlinePanels()
    {
        NewProfilePanel.Visibility = Visibility.Collapsed;
        DeletePanel.Visibility = Visibility.Collapsed;
    }

    // ---------- settings ----------

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsPanel.Visibility == Visibility.Visible) { ShowMain(); RefreshAll(); return; }

        suppress = true;
        StartWithWindowsToggle.IsChecked = App.Settings.StartWithWindows;
        AskToggle.IsChecked = App.Settings.AskOnStartup;
        AutoSaveToggle.IsChecked = App.Settings.AutoSave;
        var profiles = Workspace.ListProfiles();
        if (!profiles.Contains(Workspace.LastSession)) profiles.Insert(0, Workspace.LastSession);
        if (!profiles.Contains("default")) profiles.Insert(1, "default");
        var items = profiles.Select(p => new ProfileItem(p)).ToList();
        StartupProfileBox.ItemsSource = items;
        StartupProfileBox.SelectedItem = items.FirstOrDefault(i => i.Name == App.Settings.StartupProfile) ?? items[0];
        suppress = false;

        MainPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        ActionButtons.Visibility = Visibility.Collapsed;
        DoneButton.Visibility = Visibility.Visible;
        HeaderTitle.Text = "Settings";
        SettingsButton.Content = ""; // back arrow
        StatusText.Text = "Changes are saved right away";
    }

    void ShowMain()
    {
        MainPanel.Visibility = Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Collapsed;
        ActionButtons.Visibility = Visibility.Visible;
        DoneButton.Visibility = Visibility.Collapsed;
        HeaderTitle.Text = "Workspaces";
        SettingsButton.Content = "";
        CloseInlinePanels();
    }

    void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (suppress) return;
        try { App.Settings.StartWithWindows = StartWithWindowsToggle.IsChecked == true; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        App.Settings.AskOnStartup = AskToggle.IsChecked == true;
        App.Settings.AutoSave = AutoSaveToggle.IsChecked == true;
        App.Settings.Save();
    }

    void StartupProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppress || StartupProfileBox.SelectedItem is not ProfileItem p) return;
        App.Settings.StartupProfile = p.Name;
        App.Settings.Save();
    }

    void PreviewPrompt_Click(object sender, RoutedEventArgs e)
    {
        HideFlyout();
        if (!App.Instance.ShowStartupPrompt(force: true))
            App.Instance.Notify("Nothing saved yet. Save some apps first.");
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Workspace.Dir);
        Process.Start(new ProcessStartInfo("explorer.exe", Workspace.Dir) { UseShellExecute = true });
    }

    void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
