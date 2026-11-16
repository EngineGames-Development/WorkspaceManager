using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WorkspaceManager;

public class AppEntry
{
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string Title { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Aumid { get; set; }
    [JsonIgnore] public IntPtr Hwnd { get; set; }

    [JsonIgnore] public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName!;
}

public class WorkspaceProfile
{
    public string Profile { get; set; } = "";
    public string SavedAt { get; set; } = "";
    public List<AppEntry> Apps { get; set; } = new();
}

public record RestoreResult(int Launched, List<string> Failed)
{
    public string Summary =>
        Failed.Count == 0
            ? (Launched == 0 ? "Everything was already open." : $"Opened {Launched} app{(Launched == 1 ? "" : "s")}.")
            : $"Opened {Launched}. Couldn't open {string.Join(", ", Failed)}.";
}

/// <summary>Reads and writes the same JSON profiles as ~/.claude/scripts/workspace.ps1.</summary>
public static class Workspace
{
    public const string LastSession = "last-session";

    public static string Dir { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "workspaces");

    static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "TextInputHost", "ApplicationFrameHost", "SystemSettings", "ShellExperienceHost",
        "StartMenuExperienceHost", "SearchHost", "SearchApp", "LockApp", "explorer",
        "NVIDIA Overlay", "GameBar", "GameBarFTServer", "Widgets", "WidgetBoard",
        "SecurityHealthSystray", "CompPkgSrv", "dwm", "ctfmon", "powershell", "pwsh",
        "WindowsTerminal", "OpenConsole", "conhost", "RuntimeBroker", "PhoneExperienceHost",
        "WorkspaceManager"
    };

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static bool IsValidName(string name) => Regex.IsMatch(name, @"^[\w\-\.]+$");
    public static string FileFor(string name) => System.IO.Path.Combine(Dir, name + ".json");

    public static string DisplayOf(string profile) => profile switch
    {
        LastSession => "Last session (automatic)",
        "default" => "Default",
        _ => profile,
    };

    public static List<string> ListProfiles()
    {
        if (!Directory.Exists(Dir)) return new();
        return Directory.GetFiles(Dir, "*.json")
            .Select(f => System.IO.Path.GetFileNameWithoutExtension(f))
            .OrderBy(n => n == LastSession ? 0 : n == "default" ? 1 : 2)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<AppEntry> GetOpenApps()
    {
        var result = new List<AppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        uint self = (uint)Environment.ProcessId;
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\";

        var windows = new List<IntPtr>();
        Native.EnumWindows((h, _) => { windows.Add(h); return true; }, IntPtr.Zero);

        foreach (var h in windows)
        {
            if (!Native.IsWindowVisible(h)) continue;
            if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) continue;
            long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0) continue;
            string title = Native.GetTitle(h);
            if (string.IsNullOrWhiteSpace(title)) continue;

            Native.GetWindowThreadProcessId(h, out uint pid);
            string? path = Native.GetProcessPath(pid);
            bool cloaked = Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int c, 4) == 0 && c != 0;

            // Store apps live inside an ApplicationFrameHost frame; find the real app process.
            if (path != null && System.IO.Path.GetFileNameWithoutExtension(path)
                    .Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            {
                if (cloaked) continue;
                uint framePid = pid, childPid = 0;
                Native.EnumChildWindows(h, (ch, _) =>
                {
                    Native.GetWindowThreadProcessId(ch, out uint p);
                    if (p != framePid) { childPid = p; return false; }
                    return true;
                }, IntPtr.Zero);
                if (childPid == 0) continue;
                pid = childPid;
                path = Native.GetProcessPath(pid);
            }

            if (path == null || pid == self) continue;
            bool isPackaged = path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
                           || path.Contains(@"\SystemApps\", StringComparison.OrdinalIgnoreCase);
            if (cloaked && isPackaged) continue; // suspended background store apps
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (Noise.Contains(name)) continue;
            if (path.StartsWith(winDir, StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(path)) continue;

            result.Add(new AppEntry
            {
                Name = name,
                DisplayName = Describe(path, name),
                Title = title,
                Path = path,
                Aumid = isPackaged ? Native.GetAumid(pid) : null,
                Hwnd = h,
            });
        }
        return result;
    }

    static string Describe(string path, string fallback)
    {
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(path);
            var d = vi.FileDescription?.Trim();
            if (!string.IsNullOrEmpty(d) && d.Length < 40 && !d.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return d;
            var p = vi.ProductName?.Trim();
            if (!string.IsNullOrEmpty(p) && p.Length < 40) return p;
        }
        catch { }
        return fallback.Length > 0 ? char.ToUpper(fallback[0]) + fallback[1..] : fallback;
    }

    public static HashSet<string> GetRunningPaths()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                var path = Native.GetProcessPath((uint)p.Id);
                if (path != null) set.Add(path);
            }
        }
        return set;
    }

    public static WorkspaceProfile? Load(string name)
    {
        var file = FileFor(name);
        if (!File.Exists(file)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            var profile = new WorkspaceProfile
            {
                Profile = name,
                SavedAt = root.TryGetProperty("SavedAt", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : "",
            };
            if (root.TryGetProperty("Apps", out var apps))
            {
                IEnumerable<JsonElement> items = apps.ValueKind switch
                {
                    JsonValueKind.Array => apps.EnumerateArray().ToList(),
                    JsonValueKind.Object => new[] { apps },
                    _ => Array.Empty<JsonElement>(),
                };
                foreach (var a in items)
                {
                    string Get(string k) => a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                    var e = new AppEntry { Name = Get("Name"), DisplayName = Get("DisplayName"), Title = Get("Title"), Path = Get("Path"), Aumid = Get("Aumid") };
                    if (e.Aumid == "") e.Aumid = null;
                    if (string.IsNullOrWhiteSpace(e.DisplayName)) e.DisplayName = Describe(e.Path, e.Name);
                    if (e.Path != "") profile.Apps.Add(e);
                }
            }
            return profile;
        }
        catch { return null; }
    }

    public static void Save(string name, IEnumerable<AppEntry> apps)
    {
        Directory.CreateDirectory(Dir);
        var profile = new WorkspaceProfile { Profile = name, SavedAt = DateTime.Now.ToString("s"), Apps = apps.ToList() };
        // UTF-8 with BOM so Windows PowerShell 5.1 (the .ps1 script) reads umlauts correctly.
        File.WriteAllText(FileFor(name), JsonSerializer.Serialize(profile, JsonOpts), new UTF8Encoding(true));
    }

    public static string? Launch(AppEntry a)
    {
        try
        {
            if (!string.IsNullOrEmpty(a.Aumid))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{a.Aumid}") { UseShellExecute = true });
                return null;
            }
            if (!File.Exists(a.Path)) return "not installed";
            Process.Start(new ProcessStartInfo(a.Path)
            {
                UseShellExecute = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(a.Path) ?? "",
            });
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public static async Task<RestoreResult> RestoreAsync(IReadOnlyList<AppEntry> apps)
    {
        var failed = new List<string>();
        int ok = 0;
        foreach (var a in apps)
        {
            var err = await Task.Run(() => Launch(a));
            if (err == null) ok++; else failed.Add(a.Label);
            await Task.Delay(350);
        }
        return new RestoreResult(ok, failed);
    }

    public static string FriendlyDate(string iso)
    {
        if (!DateTime.TryParse(iso, out var d)) return iso;
        if (d.Date == DateTime.Today) return $"today at {d:HH:mm}";
        if (d.Date == DateTime.Today.AddDays(-1)) return $"yesterday at {d:HH:mm}";
        return d.ToString("d MMM, HH:mm");
    }
}
