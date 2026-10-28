// PairsBox.cs -- HAND PAIRS (2026-10-25, Tefa: "number 3 green sphere must send the hand to nr 3 yellow spot", then "doesn't
// have to be colours. as long as somehow i can tell that a spot is activation spot and where it takes the hand afterwards, and
// what it is for"). Each pair has a number, a name (what it is for), the gun it is for ("any" = all guns), an ACTIVATION
// sphere (the left grip pressed inside it starts the pair) and a LANDING spot (where the left hand then goes and stays while the
// grip is held). Only the pair picked here is drawn on the gun: a big sphere = activation, a small dot = landing, a dotted
// line between them. File: data\visceral_re8\pairs.json { "show": n, "pairs": [ { n, name, gun, ax, ay, az, ar, lx, ly, lz } ] },
// offsets from the gun hand in the gun's axes (x right, y up, z forward, metres), read by the plugin (src/pairs.cpp) each second.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

static class PairFile {
    public static string PathIn(ZoneFile f) { return f.DataDir == null ? null : Path.Combine(f.DataDir, "pairs.json"); }
    public static Dictionary<string, object> Load(ZoneFile f) {
        try {
            string p = PathIn(f);
            if (p != null && File.Exists(p)) { var o = Json.Obj(Json.Parse(File.ReadAllText(p))); if (o != null) return o; }
        } catch (Exception) { }
        return new Dictionary<string, object>();
    }
    public static void Save(ZoneFile f, Dictionary<string, object> all) {
        string p = PathIn(f);
        if (p == null) return;
        string tmp = p + ".tmp";
        File.WriteAllText(tmp, Json.Write(all));
        if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
    }
    public static List<Dictionary<string, object>> Pairs(Dictionary<string, object> all) {
        var list = new List<Dictionary<string, object>>();
        object v;
        if (all.TryGetValue("pairs", out v) && Json.Arr(v) != null) foreach (var e in Json.Arr(v)) { var d = Json.Obj(e); if (d != null) list.Add(d); }
        list.Sort((a, b) => Num(a, "n").CompareTo(Num(b, "n")));
        return list;
    }
    public static void SetPairs(Dictionary<string, object> all, List<Dictionary<string, object>> list) {
        var arr = new List<object>();
        foreach (var d in list) arr.Add(d);
        all["pairs"] = arr;
    }
    public static double Num(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? Json.Num(v, 0) : 0; }
    public static string Str(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? Json.Str(v) : ""; }
}

class PairsBox : Panel {
    const double M_PER_CM = 0.01;
    // a new pair's first numbers (cm): the activation sphere a little ahead of the gun hand, the landing spot just in front of it
    const double NEW_AZ_CM = 15, NEW_AR_CM = 6, NEW_LY_CM = 3, NEW_LZ_CM = 12;
    // the sliders' reach from the gun hand (Tefa 2026-10-25: "could not be moved across the weapons whole length"): the stock
    // behind the hand, the barrel well ahead of it on the long guns
    const double BACK_CM = -60, AHEAD_CM = 100, SIDE_CM = 40;
    static readonly Color Panel2 = Color.FromArgb(40, 43, 55), Ink = Color.FromArgb(235, 235, 240), Dim = Color.FromArgb(155, 160, 175);
    static readonly Color Back = Color.FromArgb(28, 30, 38);

    readonly ZoneFile file;
    readonly Func<string> gunKey;            // "any" or this gun's number, from the window's THIS GUN / ALL GUNS choice
    readonly Func<string, EventHandler, Button> btn;
    readonly Label which;
    readonly TextBox name;
    readonly ValueBox values;
    readonly Button showBtn, removeBtn;
    readonly Timer saveTimer = new Timer { Interval = 120 };
    List<Dictionary<string, object>> pairs = new List<Dictionary<string, object>>();
    int sel = -1;                            // index into pairs
    bool shown = true, syncing;
    DateTime lastWrite = DateTime.MinValue, removeArmed = DateTime.MinValue;

    public PairsBox(ZoneFile file, Func<string> gunKey, Func<string, EventHandler, Button> btn) {
        this.file = file; this.gunKey = gunKey; this.btn = btn;
        Dock = DockStyle.Top; Height = 112 + 7 * 40 + 170; BackColor = Color.Transparent; Padding = new Padding(0, 6, 0, 6);
        var inner = new SkinPanel { Dock = DockStyle.Fill, Tint = Color.FromArgb(150, Panel2), Padding = new Padding(8, 4, 8, 4) };
        Controls.Add(inner);
        var head = new SkinLabel { Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI Semibold", 12f), ForeColor = Ink, Text = "HAND PAIRS" };
        var note = new SkinLabel { Dock = DockStyle.Top, Height = 44, ForeColor = Dim,
            Text = "on the gun: BIG sphere = left grip there starts it, small DOT = where the hand lands, dotted line between. Only the pair shown here is drawn." };
        which = new SkinLabel { Dock = DockStyle.Top, Height = 30, ForeColor = Ink, Font = new Font("Segoe UI Semibold", 11f), Padding = new Padding(0, 6, 0, 0) };
        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, BackColor = Color.Transparent, WrapContents = false };
        nav.Controls.Add(btn("◀", (s, e) => Pick(sel - 1)));
        nav.Controls.Add(btn("▶", (s, e) => Pick(sel + 1)));
        nav.Controls.Add(btn("ADD A PAIR", (s, e) => Add()));
        showBtn = btn("HIDE", (s, e) => { shown = !shown; Save(); });
        nav.Controls.Add(showBtn);
        removeBtn = btn("REMOVE", (s, e) => Remove());
        nav.Controls.Add(removeBtn);
        var nameRow = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.Transparent };
        nameRow.Controls.Add(name = new TextBox { Dock = DockStyle.Fill, BackColor = Back, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 12f) });
        nameRow.Controls.Add(new SkinLabel { Text = "what it is for:", Dock = DockStyle.Left, Width = 150, ForeColor = Dim, Padding = new Padding(0, 8, 0, 0) });
        name.TextChanged += (s, e) => { if (!syncing) Changed(); };
        values = new ValueBox("WHERE", "the activation sphere and the landing spot, from the gun hand, in the gun's axes", new[] {
            new ValueRow("az", "START: forward / back", "cm", BACK_CM, AHEAD_CM, 0.5), new ValueRow("ay", "START: up / down", "cm", -SIDE_CM, SIDE_CM, 0.5),
            new ValueRow("ax", "START: right / left", "cm", -SIDE_CM, SIDE_CM, 0.5), new ValueRow("ar", "START: sphere size", "cm", 1, 30, 0.5),
            new ValueRow("lz", "LAND: forward / back", "cm", BACK_CM, AHEAD_CM, 0.5), new ValueRow("ly", "LAND: up / down", "cm", -SIDE_CM, SIDE_CM, 0.5),
            new ValueRow("lx", "LAND: right / left", "cm", -SIDE_CM, SIDE_CM, 0.5) }, () => Changed(), btn);
        foreach (var c in new Control[] { values, nameRow, nav, which, note, head }) inner.Controls.Add(c);   // docked top: added last = shown first
        saveTimer.Tick += (s, e) => { saveTimer.Stop(); Save(); };
        ShowPair();
    }

    void Changed() { if (!syncing) { saveTimer.Stop(); saveTimer.Start(); } }

    // 2026-10-28 (Tefa: one list of every pair the gun in hand has): the window's PART list shows these and picks one
    public List<KeyValuePair<int, string>> ForGun(string gun) {
        var r = new List<KeyValuePair<int, string>>();
        foreach (var d in pairs) {
            string g = PairFile.Str(d, "gun");
            if (g == "" || g == "any" || gun == "any" || g == gun) r.Add(new KeyValuePair<int, string>((int)PairFile.Num(d, "n"), PairFile.Str(d, "name")));
        }
        return r;
    }
    public void SelectNumber(int n) { int i = pairs.FindIndex(d => (int)PairFile.Num(d, "n") == n); if (i >= 0) Pick(i); }
    public int SelectedNumber { get { return sel >= 0 && sel < pairs.Count ? (int)PairFile.Num(pairs[sel], "n") : 0; } }
    public void AddNew() { Add(); }
    static string GunLabel(string g) { int n; return int.TryParse(g, out n) ? Program.GunName(n) : g; }

    // the file changed outside (the game never writes it; another copy of the window might): read it again
    public void Poll() {
        if (file.Folder == null || saveTimer.Enabled) return;
        DateTime w = DateTime.MinValue;
        try { string p = PairFile.PathIn(file); if (p != null && File.Exists(p)) w = File.GetLastWriteTime(p); } catch (IOException) { }
        if (w != lastWrite) Reload();
    }

    public void Reload() {
        if (file.Folder == null) return;
        var all = PairFile.Load(file);
        pairs = PairFile.Pairs(all);
        object v;
        int showN = all.TryGetValue("show", out v) ? (int)Json.Num(v, 0) : 0;
        shown = showN > 0;
        int keep = sel >= 0 && sel < pairs.Count ? (int)PairFile.Num(pairs[sel], "n") : showN;
        sel = pairs.FindIndex(d => (int)PairFile.Num(d, "n") == keep);
        if (sel < 0 && pairs.Count > 0) sel = 0;
        try { string p = PairFile.PathIn(file); lastWrite = p != null && File.Exists(p) ? File.GetLastWriteTime(p) : DateTime.MinValue; } catch (IOException) { }
        ShowPair();
    }

    void ShowPair() {
        syncing = true;
        bool any = sel >= 0 && sel < pairs.Count;
        values.Enabled = name.Enabled = removeBtn.Enabled = showBtn.Enabled = any;
        if (any) {
            var d = pairs[sel];
            string g = PairFile.Str(d, "gun");
            which.Text = "PAIR " + (int)PairFile.Num(d, "n") + "  (" + (sel + 1) + " of " + pairs.Count + ")  ·  " +
                         (g == "" || g == "any" ? "all guns" : GunLabel(g)) + (shown ? "" : "  ·  HIDDEN");
            name.Text = PairFile.Str(d, "name");
            values.Load(k => PairFile.Num(d, k) / M_PER_CM);
        } else {
            which.Text = "no pairs yet: ADD A PAIR";
            name.Text = "";
        }
        showBtn.Text = shown ? "HIDE" : "SHOW";
        syncing = false;
    }

    void Pick(int i) {
        if (pairs.Count == 0) return;
        Store();
        sel = ((i % pairs.Count) + pairs.Count) % pairs.Count;
        shown = true;
        ShowPair();
        Save();
    }

    void Add() {
        if (file.Folder == null) return;
        Store();
        int n = 1;
        foreach (var d in pairs) n = Math.Max(n, (int)PairFile.Num(d, "n") + 1);
        var p = new Dictionary<string, object> {
            { "n", (double)n }, { "name", "pair " + n }, { "gun", gunKey() },
            { "ax", 0.0 }, { "ay", 0.0 }, { "az", NEW_AZ_CM * M_PER_CM }, { "ar", NEW_AR_CM * M_PER_CM },
            { "lx", 0.0 }, { "ly", NEW_LY_CM * M_PER_CM }, { "lz", NEW_LZ_CM * M_PER_CM } };
        pairs.Add(p);
        sel = pairs.Count - 1;
        shown = true;
        ShowPair();
        Save();
    }

    void Remove() {   // two clicks within three seconds, no pop-up (awkward inside the headset)
        if (sel < 0) return;
        if (removeArmed == DateTime.MinValue || (DateTime.Now - removeArmed).TotalSeconds > 3) { removeArmed = DateTime.Now; removeBtn.Text = "SURE? CLICK AGAIN"; return; }
        removeArmed = DateTime.MinValue;
        removeBtn.Text = "REMOVE";
        pairs.RemoveAt(sel);
        if (sel >= pairs.Count) sel = pairs.Count - 1;
        ShowPair();
        Save();
    }

    // the sliders and the name into the selected pair
    void Store() {
        if (sel < 0 || sel >= pairs.Count) return;
        var d = pairs[sel];
        foreach (var kv in values.Values()) d[kv.Key] = Math.Round((double)kv.Value * M_PER_CM, 4);
        d["name"] = name.Text;
    }

    void Save() {
        if (file.Folder == null) return;
        Store();
        var all = PairFile.Load(file);
        PairFile.SetPairs(all, pairs);
        all["show"] = (double)(shown && sel >= 0 && sel < pairs.Count ? (int)PairFile.Num(pairs[sel], "n") : 0);
        try { PairFile.Save(file, all); string p = PairFile.PathIn(file); lastWrite = File.GetLastWriteTime(p); } catch (Exception) { }
        ShowPair();
    }
}
