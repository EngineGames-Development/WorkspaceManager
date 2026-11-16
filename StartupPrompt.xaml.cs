using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WorkspaceManager;

public partial class StartupPrompt : FluentWindow
{
    readonly ObservableCollection<AppItem> items = new();

    public StartupPrompt(string profile)
    {
        InitializeComponent();
        Backdrop = 2; // Mica
        AppList.ItemsSource = items;

        var profiles = Workspace.ListProfiles().Select(p => new ProfileItem(p)).ToList();
        ProfileBox.ItemsSource = profiles;
        ProfileBox.SelectedItem = profiles.FirstOrDefault(p => p.Name == profile) ?? profiles.FirstOrDefault();

        Loaded += (_, _) =>
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        };
    }

    void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        items.Clear();
        if (ProfileBox.SelectedItem is not ProfileItem p) return;
        var profile = Workspace.Load(p.Name);
        if (profile != null)
        {
            var running = Workspace.GetRunningPaths();
            foreach (var a in profile.Apps)
            {
                var item = AppItem.ForSaved(a, running.Contains(a.Path));
                item.PropertyChanged += (_, _) => UpdateYes();
                items.Add(item);
            }
            Lead.Text = $"You had {profile.Apps.Count} app{(profile.Apps.Count == 1 ? "" : "s")} open {Workspace.FriendlyDate(profile.SavedAt)}. Want to pick up where you left off?";
        }
        UpdateYes();
    }

    void UpdateYes()
    {
        int n = items.Count(i => i.IsSelected && i.CanSelect);
        YesButton.Content = n == 0 ? "Nothing to reopen" : $"Reopen {n} app{(n == 1 ? "" : "s")}";
        YesButton.IsEnabled = n > 0;
    }

    void AppList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
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

    void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == Root || e.OriginalSource is TextBlock || e.OriginalSource is Image) DragMove();
    }

    void SaveDontAsk()
    {
        if (DontAsk.IsChecked == true)
        {
            App.Settings.AskOnStartup = false;
            App.Settings.Save();
        }
    }

    async void Yes_Click(object sender, RoutedEventArgs e)
    {
        var apps = items.Where(i => i.IsSelected && i.CanSelect).Select(i => i.Entry).ToList();
        SaveDontAsk();
        Close();
        var result = await Workspace.RestoreAsync(apps);
        App.Instance.Notify(result.Summary);
    }

    void No_Click(object sender, RoutedEventArgs e)
    {
        SaveDontAsk();
        Close();
    }
}
