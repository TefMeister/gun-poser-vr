// GripZones.cs -- start-up, settings, gun names and the self-test of the Grip Zones window (Visceral RE8).
//
//   GripZones.exe                      the window
//   GripZones.exe --game <folder>      use this game folder instead of looking for Village
//   GripZones.exe --selftest <folder>  no window: exercise the zone file in <folder> (a stand-in game folder), print PASS/FAIL
//
// Tefa 2026-10-25: "can you make the gun grip activation sliders a separate thing just like you did with the hand poser, so i
// can use it with Window View". The same sliders as the in-game menu "Visceral RE8 grab zones", in a desktop window.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

static class Program {
    public static readonly string AppData = SettingsFolder();

    // called Grip Zones until 2026-10-27 (Tefa: "can you change the name to grip zones"): the old settings come along once
    static string SettingsFolder() {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string now = Path.Combine(root, "GripZonesVR"), old = Path.Combine(root, "GrabZonesVR");
        try {
            if (!Directory.Exists(now) && Directory.Exists(old)) {
                Directory.CreateDirectory(now);
                foreach (string f in Directory.GetFiles(old)) File.Copy(f, Path.Combine(now, Path.GetFileName(f)));
            }
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return now;
    }

    // the guns Visceral RE8 knows (src/weapons.cpp), by number
    public static readonly Dictionary<int, string> Guns = new Dictionary<int, string> {
        {3040, "M1911"}, {3046, "LEMI"}, {3047, "V61 Custom"}, {3050, "S.T.A.K.E"}, {3115, "Rocket Pistol"},
        {3122, "Samurai Edge - AW Model-01"}, {3124, "USM-AI"}, {3135, "LEMI - Shadows of Rose"}, {3081, "LEMI - Mercenaries"},
        {3082, "M1911 - Mercenaries"}, {3083, "V61 Custom - Mercenaries"}, {3130, "USM-AI - Mercenaries"}, {3042, "F2 Rifle"},
        {3087, "F2 Rifle - Mercenaries"}, {3049, "WCX"}, {3125, "Dragoon"}, {3129, "Dragoon - Mercenaries"}, {3048, "SYG-12"},
        {3086, "SYG-12 - Mercenaries"}, {3021, "W870 TAC"}, {3085, "W870 TAC - Mercenaries"}, {3036, "M1897"},
        {3084, "M1897 - Mercenaries"}, {3147, "M1897 - Shadows of Rose"}, {3023, "GM 79"}, {3008, "M1851 Wolfsbane"},
        {3116, "Handcannon PZ"}, {3022, "Knife"}, {3044, "Knife"},
    };
    // 2026-10-27 (Tefa: "on any weapon there have to be parts in the selection to tweak - front grip - mag if applicable - grip
    // handle if applicable, not on long guns - slide/bolt - the revolver/magnum drum"): which parts each gun has, from
    // src/weapons.cpp (reload / shape / rack / pump / breech). The front grip is on every gun.
    static readonly HashSet<int> MagPistols = new HashSet<int> { 3040, 3046, 3047, 3050, 3122, 3124, 3135, 3081, 3082, 3083, 3130 };
    static readonly HashSet<int> MagLongs = new HashSet<int> { 3042, 3087, 3049, 3125, 3129, 3048, 3086 };
    static readonly HashSet<int> Pumps = new HashSet<int> { 3021, 3085, 3036, 3084, 3147 };
    static readonly HashSet<int> Drums = new HashSet<int> { 3008, 3116 };
    static readonly HashSet<int> Breaks = new HashSet<int> { 3023 };
    public static bool HasPart(int gun, string key) {
        switch (key) {
        case "grip": return true;
        case "mag": return MagPistols.Contains(gun) || MagLongs.Contains(gun);
        case "well": return MagLongs.Contains(gun);
        case "part": return MagPistols.Contains(gun) || MagLongs.Contains(gun) || Pumps.Contains(gun);
        case "handle": return MagPistols.Contains(gun) || Drums.Contains(gun);
        case "slide-left": return MagPistols.Contains(gun);   // 2026-10-28: a pistol slide's left-side sphere
        case "drum": return Drums.Contains(gun);
        case "insert": return Breaks.Contains(gun);
        default: return true;
        }
    }
    public static string GunName(int n) { string s; return Guns.TryGetValue(n, out s) ? s : ("gun " + n); }

    [STAThread]
    static int Main(string[] args) {
        string gameArg = null;
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "--game" && i + 1 < args.Length) gameArg = args[++i];
            else if (args[i] == "--selftest" && i + 1 < args.Length) return SelfTest(args[++i]) ? 0 : 1;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ZonesUi(gameArg));
        return 0;
    }

    // ---- self-test: the file rules the plugin depends on, in a stand-in game folder ------------------------------------
    static bool SelfTest(string folder) {
        bool ok = true;
        Action<bool, string> check = (c, what) => { Console.WriteLine((c ? "PASS " : "FAIL ") + what); if (!c) ok = false; };
        Directory.CreateDirectory(Path.Combine(folder, "reframework"));
        var f = new ZoneFile();
        f.Use(folder);
        if (File.Exists(f.ZonesPath)) File.Delete(f.ZonesPath);

        var all = f.Load();
        check(all.Count == 0, "no zones.json = an empty set");
        check(double.IsNaN(ZoneFile.Effective(all, "3042", "mag", "r")), "nothing set = the plugin's own number (NaN here)");
        ZoneFile.SetRadius(all, "default", "mag", 0.15);
        ZoneFile.SetOffset(all, "3042", "mag", 0.0, -0.02, 0.01);
        ZoneFile.SetShow(all, true);
        f.Save(all);

        var back = f.Load();
        check(Math.Abs(ZoneFile.Effective(back, "3042", "mag", "r") - 0.15) < 1e-9, "a gun without its own size takes the default's");
        check(Math.Abs(ZoneFile.Effective(back, "3042", "mag", "y") + 0.02) < 1e-9, "the gun's own offset is kept");
        check(Math.Abs(ZoneFile.Effective(back, "3042", "mag", "x")) < 1e-9, "all three offset numbers are written together");
        check(ZoneFile.Show(back), "the show switch is kept");
        ZoneFile.SetLook(back, 7);
        check(ZoneFile.Look(back) == 0, "the sphere look wraps round after the last one");
        ZoneFile.Forget(back, "3042", "mag");
        check(double.IsNaN(ZoneFile.Effective(back, "3042", "mag", "y")), "forget drops the gun's own numbers");
        check(Math.Abs(ZoneFile.Effective(back, "3042", "mag", "r") - 0.15) < 1e-9, "...and the default still applies");

        File.WriteAllText(f.StatePath, "{\"t\":1,\"gun\":3042,\"zones\":[{\"key\":\"mag\",\"on\":true,\"r_cm\":12,\"hand_cm\":14.5},{\"key\":\"bolt\",\"on\":true,\"r_cm\":9}]}");
        var st = f.ReadState();
        check(st != null && st.Gun == 3042, "the state file names the gun");
        check(st != null && st.Find("mag") != null && Math.Abs(st.Find("mag").HandCm - 14.5) < 1e-9, "the hand distance is read");
        check(st != null && st.Find("bolt") != null && st.Find("bolt").HandCm < 0, "a missing hand distance reads as unknown");
        check(f.ScriptAlive(), "a fresh state file counts as the game script running");
        File.WriteAllText(f.StatePath, "{\"t\":1,\"gun\":30");
        check(f.ReadState() == null, "a half-written state file is skipped, not a crash");
        Console.WriteLine(ok ? "SELFTEST OK" : "SELFTEST FAILED");
        return ok;
    }
}

// settings.txt: one `key=value` per line, in %APPDATA%\GripZonesVR (GrabZonesVR before the rename).
class Settings {
    readonly string file = Path.Combine(Program.AppData, "settings.txt");
    readonly Dictionary<string, string> values = new Dictionary<string, string>();

    public Settings() {
        try {
            if (File.Exists(file))
                foreach (var line in File.ReadAllLines(file)) {
                    int eq = line.IndexOf('=');
                    if (eq > 0) values[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
        } catch (IOException) { }
    }

    public string Get(string key, string dflt) { string v; return values.TryGetValue(key, out v) ? v : dflt; }

    public void Set(string key, string value) {
        values[key] = value;
        try {
            Directory.CreateDirectory(Program.AppData);
            File.WriteAllLines(file, values.Select(kv => kv.Key + "=" + kv.Value).ToArray());
        } catch (IOException) { }
    }
}
