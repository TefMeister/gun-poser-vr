// ZoneFile.cs -- the two files the Grip Zones window shares with Visceral RE8, both in the game's
// reframework\data\visceral_re8\ folder:
//   zones.json        WRITTEN HERE (and by the in-game menu): per gun ("3042") over "default": each zone's offset from its joint
//                     in the gun's axes (x right, y up, z forward, metres) and its radius r (metres).
//                     (2026-10-30: the spheres show by themselves as the left hand nears one, so the window's show switch
//                     and sphere look are gone; the game keeps its own "_visual" block.)
//                     The plugin (src/zones.cpp) re-reads it within a third of a second of any change.
//   zones_state.json  READ HERE: written by lua/visceral_re8_zones.lua four times a second: the gun in hand and, per zone, its
//                     key (mag / well / slide / pump / bolt / grip), whether it is on this gun, its size and how far the hand is from it.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

class LiveZone {
    public string Key = "";
    public bool On;
    public double RadiusCm;
    public double HandCm = -1;   // -1 = not known
}

class LiveState {
    public int Gun;
    public int Spot;   // 2026-10-28: where the left hand is (bridge slot 153), for Hand Poser's docked wrist box
    public List<LiveZone> Zones = new List<LiveZone>();
    public LiveZone Find(string key) { foreach (var z in Zones) if (z.Key == key) return z; return null; }
}

class ZoneFile {
    public const double STALE_SEC = 3.0;   // zones_state.json older than this = the game script is not running
    public string Folder { get; private set; }
    public string DataDir { get { return Folder == null ? null : Path.Combine(Folder, "reframework", "data", "visceral_re8"); } }
    public string ZonesPath { get { return DataDir == null ? null : Path.Combine(DataDir, "zones.json"); } }
    public string StatePath { get { return DataDir == null ? null : Path.Combine(DataDir, "zones_state.json"); } }

    public void Use(string folder) { Folder = folder; if (folder != null) Directory.CreateDirectory(DataDir); }

    public static bool LooksLikeVillage(string folder) {
        return folder != null && File.Exists(Path.Combine(folder, "re8.exe")) && Directory.Exists(Path.Combine(folder, "reframework"));
    }

    // the running game first, then Steam's usual places
    public static string FindVillage() {
        foreach (var p in Process.GetProcessesByName("re8")) {
            try { string d = Path.GetDirectoryName(p.MainModule.FileName); if (LooksLikeVillage(d)) return d; } catch (Exception) { }
        }
        foreach (var root in new[] { @"C:\Steam", @"C:\Program Files (x86)\Steam", @"D:\Steam", @"D:\SteamLibrary", @"E:\SteamLibrary" }) {
            string d = Path.Combine(root, "steamapps", "common", "Resident Evil Village BIOHAZARD VILLAGE");
            if (LooksLikeVillage(d)) return d;
        }
        return null;
    }

    // ---- zones.json ---------------------------------------------------------------------------------------------------
    public Dictionary<string, object> Load() {
        try {
            if (File.Exists(ZonesPath)) { var o = Json.Obj(Json.Parse(File.ReadAllText(ZonesPath))); if (o != null) return o; }
        } catch (Exception) { }
        return new Dictionary<string, object>();
    }

    public void Save(Dictionary<string, object> all) {
        if (Folder == null) return;
        string tmp = ZonesPath + ".tmp";
        File.WriteAllText(tmp, Json.Write(all));
        if (File.Exists(ZonesPath)) File.Replace(tmp, ZonesPath, null); else File.Move(tmp, ZonesPath);
    }

    static Dictionary<string, object> Child(Dictionary<string, object> o, string key, bool make) {
        object v;
        if (o.TryGetValue(key, out v) && v is Dictionary<string, object>) return (Dictionary<string, object>)v;
        if (!make) return null;
        var c = new Dictionary<string, object>();
        o[key] = c;
        return c;
    }

    // a zone's number as the plugin resolves it: the gun's own over "default"; NaN = neither set (the plugin's built-in applies)
    public static double Effective(Dictionary<string, object> all, string group, string zone, string axis) {
        foreach (var g in group == "default" ? new[] { "default" } : new[] { group, "default" }) {
            var gr = Child(all, g, false);
            var z = gr == null ? null : Child(gr, zone, false);
            object v;
            if (z != null && z.TryGetValue(axis, out v) && v is double) return (double)v;
        }
        return double.NaN;
    }

    // Offsets: the plugin takes x, y and z together once any is set, so all three are written (missing ones as shown).
    public static void SetOffset(Dictionary<string, object> all, string group, string zone, double x, double y, double z) {
        var e = Child(Child(all, group, true), zone, true);
        e["x"] = x; e["y"] = y; e["z"] = z;
    }

    // a whole entry at once (the GUN IN THE RIGHT HAND box's "aim")
    public static void SetEntry(Dictionary<string, object> all, string group, string zone, Dictionary<string, object> values) {
        var e = Child(Child(all, group, true), zone, true);
        foreach (var kv in values) e[kv.Key] = kv.Value;
    }

    public static void SetRadius(Dictionary<string, object> all, string group, string zone, double r) {
        Child(Child(all, group, true), zone, true)["r"] = r;
    }

    public static void Forget(Dictionary<string, object> all, string group, string zone) {
        var g = Child(all, group, false);
        if (g != null) g.Remove(zone);
    }

    // ---- zones_state.json ---------------------------------------------------------------------------------------------
    public bool ScriptAlive() {
        try { return File.Exists(StatePath) && (DateTime.Now - File.GetLastWriteTime(StatePath)).TotalSeconds < STALE_SEC; }
        catch (IOException) { return false; }
    }

    public LiveState ReadState() {
        try {
            if (!File.Exists(StatePath)) return null;
            var o = Json.Obj(Json.Parse(File.ReadAllText(StatePath)));
            if (o == null) return null;
            object v;
            var st = new LiveState { Gun = o.TryGetValue("gun", out v) ? (int)Json.Num(v, 0) : 0 };
            if (o.TryGetValue("spot", out v)) st.Spot = (int)Json.Num(v, 0);
            if (o.TryGetValue("zones", out v) && Json.Arr(v) != null)
                foreach (var item in Json.Arr(v)) {
                    var z = Json.Obj(item);
                    if (z == null) continue;
                    object k, on, r, h;
                    st.Zones.Add(new LiveZone {
                        Key = z.TryGetValue("key", out k) ? Json.Str(k) : "",
                        On = z.TryGetValue("on", out on) && Json.Bool(on),
                        RadiusCm = z.TryGetValue("r_cm", out r) ? Json.Num(r, 0) : 0,
                        HandCm = z.TryGetValue("hand_cm", out h) ? Json.Num(h, -1) : -1,
                    });
                }
            return st;
        } catch (Exception) { return null; }   // caught mid-write: the next read gets it
    }
}
