using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;

namespace WorkspaceManager;

public class Settings
{
    public bool AskOnStartup { get; set; } = true;
    public bool AutoSave { get; set; } = true;
    public string StartupProfile { get; set; } = Workspace.LastSession;
    public string LastProfile { get; set; } = "default";

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkspaceManager", "settings.json");
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "WorkspaceManager";

    public static bool Exists => File.Exists(FilePath);

    public static Settings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    [JsonIgnore]
    public bool StartWithWindows
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(RunName) != null;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue(RunName, $"\"{Environment.ProcessPath}\" --startup");
            else k.DeleteValue(RunName, false);
        }
    }
}

public static class Theme
{
    public static bool IsDark =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int v && v == 0;

    static Color Accent(bool dark)
    {
        // AccentPalette: Light3, Light2, Light1, Base, Dark1, Dark2, Dark3 (RGBA). Windows 11 uses Light2 in dark mode, Dark1 in light mode.
        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentPalette", null) is byte[] p && p.Length >= 32)
        {
            int i = dark ? 1 : 4;
            return Color.FromRgb(p[i * 4], p[i * 4 + 1], p[i * 4 + 2]);
        }
        return dark ? Color.FromRgb(0x4C, 0xC2, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8);
    }

    static SolidColorBrush B(uint argb)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }

    public static void Apply(ResourceDictionary r)
    {
        bool d = IsDark;
        var accent = new SolidColorBrush(Accent(d)); accent.Freeze();
        r["WmAccent"] = accent;
        r["WmAccentText"] = d ? B(0xFF000000) : B(0xFFFFFFFF);
        r["WmText"] = d ? B(0xFFFFFFFF) : B(0xE4000000);
        r["WmTextSecondary"] = d ? B(0xC5FFFFFF) : B(0x9E000000);
        r["WmCard"] = d ? B(0x0DFFFFFF) : B(0xB3FFFFFF);
        r["WmCardStroke"] = d ? B(0x19000000) : B(0x0F000000);
        r["WmControl"] = d ? B(0x0FFFFFFF) : B(0xB3FFFFFF);
        r["WmControlHover"] = d ? B(0x15FFFFFF) : B(0x80F9F9F9);
        r["WmControlStroke"] = d ? B(0x18FFFFFF) : B(0x29000000);
        r["WmSubtleHover"] = d ? B(0x0FFFFFFF) : B(0x09000000);
        r["WmFooter"] = d ? B(0x40000000) : B(0x0A000000);
        r["WmDivider"] = d ? B(0x15FFFFFF) : B(0x14000000);
        r["WmFallbackBg"] = d ? B(0xFF202020) : B(0xFFF3F3F3);
        r["WmSuccess"] = d ? B(0xFF6CCB5F) : B(0xFF0F7B0F);
    }
}

/// <summary>Borderless window with Windows 11 Mica/Acrylic backdrop and rounded corners.</summary>
public class FluentWindow : Window
{
    /// <summary>2 = Mica, 3 = Acrylic (transient/flyout), 4 = Mica Alt.</summary>
    public int Backdrop { get; set; } = 2;

    public FluentWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyBackdrop();
    }

    public void ApplyBackdrop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = Theme.IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, 4);           // DWMWA_USE_IMMERSIVE_DARK_MODE
        int corner = 2;
        Native.DwmSetWindowAttribute(hwnd, 33, ref corner, 4);         // DWMWA_WINDOW_CORNER_PREFERENCE = round
        int backdrop = Backdrop;
        int hr = Native.DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4); // DWMWA_SYSTEMBACKDROP_TYPE
        var src = HwndSource.FromHwnd(hwnd);
        if (hr == 0 && src?.CompositionTarget != null)
        {
            src.CompositionTarget.BackgroundColor = Colors.Transparent;
            Background = Brushes.Transparent;
        }
        else
        {
            SetResourceReference(BackgroundProperty, "WmFallbackBg");
        }
    }
}

public static class IconCache
{
    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string path)
    {
        if (Cache.TryGetValue(path, out var img)) return img;
        try
        {
            if (File.Exists(path))
            {
                using var ic = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (ic != null)
                {
                    var bs = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bs.Freeze();
                    img = bs;
                }
            }
        }
        catch { }
        Cache[path] = img;
        return img;
    }
}

public class AppItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public AppItem(AppEntry entry)
    {
        Entry = entry;
        Icon = IconCache.Get(entry.Path);
    }

    public AppEntry Entry { get; }
    public ImageSource? Icon { get; }
    public string DisplayName => Entry.Label;
    public string Title => Entry.Title;

    bool selected;
    public bool IsSelected
    {
        get => selected;
        set { if (selected == value) return; selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }

    public bool CanSelect { get; init; } = true;
    public string StatusText { get; init; } = "";
    public Visibility StatusVisibility => StatusText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static AppItem ForOpen(AppEntry e) => new(e) { IsSelected = true };

    public static AppItem ForSaved(AppEntry e, bool running) =>
        running ? new(e) { CanSelect = false, StatusText = "Running" }
                : new(e) { IsSelected = true };
}

public record ProfileItem(string Name)
{
    public override string ToString() => Workspace.DisplayOf(Name);
}
