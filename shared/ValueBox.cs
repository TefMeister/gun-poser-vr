// ValueBox.cs -- a box of named sliders in centimetres or degrees, for the two boxes that are not grab zones
// (2026-10-25, Tefa: "can i still tweak where the gun is pointing and move the whole left hand left right if needed? ... it
// arches over the back of the gun, then grips the bolt handle", then "all please"):
//   HAND ON THE BOLT       the left hand's place and turn while it holds the slide / pump / bolt, and how high it arcs over
//                          the gun on the way: data\visceral_re8\slide_hand.json, per gun ("ri3042", "any" = all guns), the same
//                          file and numbers as the in-game menu's sliders (lua/visceral_re8_bridge.lua reads it each second)
//   GUN IN THE RIGHT HAND  where the gun points and sits in the right hand: zones.json "aim" (per gun over "default"), read by
//                          lua/visceral_re8_zones.lua and laid over the VR mode's own right-hand offsets
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

class ValueRow {
    public string Key, Label, Unit;
    public double Min, Max, Step;
    public ValueRow(string key, string label, string unit, double min, double max, double step) { Key = key; Label = label; Unit = unit; Min = min; Max = max; Step = step; }
}

class ValueBox : Panel {
    const int TENTHS = 10;   // the sliders count tenths of a centimetre / degree
    static readonly Color Panel2 = Color.FromArgb(40, 43, 55), Ink = Color.FromArgb(235, 235, 240), Dim = Color.FromArgb(155, 160, 175);
    readonly Action changed;
    readonly ValueRow[] rows;
    readonly Dictionary<string, Row> sliders = new Dictionary<string, Row>();
    readonly Label head;
    public bool Touched;

    public ValueBox(string title, string what, ValueRow[] rows, Action changed, Func<string, EventHandler, Button> btn) {
        this.rows = rows; this.changed = changed;
        Dock = DockStyle.Top; Height = 112 + rows.Length * 40; BackColor = Color.Transparent; Padding = new Padding(0, 6, 0, 6);
        var inner = new SkinPanel { Dock = DockStyle.Fill, Tint = Color.FromArgb(150, Panel2), Padding = new Padding(8, 4, 8, 4) };
        Controls.Add(inner);
        head = new SkinLabel { Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI Semibold", 12f), ForeColor = Ink, Text = title };
        var note = new SkinLabel { Dock = DockStyle.Top, Height = 24, ForeColor = Dim, Text = what };
        var reset = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, BackColor = Color.Transparent, FlowDirection = FlowDirection.RightToLeft };
        reset.Controls.Add(btn("RESET", (s, e) => { foreach (var r in sliders.Values) r.Set(0, true); Touched = true; changed(); }));
        var stack = new List<Control> { reset };
        for (int i = rows.Length - 1; i >= 0; i--) { var r = new Row(this, rows[i], btn); sliders[rows[i].Key] = r; stack.Add(r); }
        stack.Add(note); stack.Add(head);
        foreach (var c in stack) inner.Controls.Add(c);   // docked top: added last = shown first
    }

    public string Title { set { if (head.Text != value) head.Text = value; } }

    // fill from the file; a key not set reads as 0
    public void Load(Func<string, double> get) {
        foreach (var r in rows) { double v = get(r.Key); sliders[r.Key].Set(double.IsNaN(v) ? 0 : v, false); }
        Touched = false;
    }
    public Dictionary<string, object> Values() {
        var d = new Dictionary<string, object>();
        foreach (var r in rows) d[r.Key] = Math.Round(sliders[r.Key].Value, 2);
        return d;
    }

    class Row : Panel, ITickable {
        readonly ValueBox box;
        readonly ValueRow def;
        readonly FancySlider bar;
        readonly Label value;
        readonly CheckBox tick;   // 2026-10-27: ticked = the controller buttons step it (Ticks.cs)
        bool setting;
        public bool Ticked { get { return tick.Checked; } set { tick.Checked = value; } }
        public void Step(int presses) { Nudge(presses); }
        public double Value { get { return bar.Value / (double)TENTHS; } }

        public Row(ValueBox box, ValueRow def, Func<string, EventHandler, Button> btn) {
            this.box = box; this.def = def;
            Dock = DockStyle.Top; Height = 40; BackColor = Color.Transparent;
            var name = new SkinLabel { Text = def.Label, Dock = DockStyle.Left, Width = 150, ForeColor = Dim, Padding = new Padding(0, 8, 0, 0) };
            value = new SkinLabel { Dock = DockStyle.Right, Width = 82, ForeColor = Ink, TextAlign = ContentAlignment.MiddleRight };
            var minus = btn("−", (s, e) => Nudge(-1)); minus.Dock = DockStyle.Right; minus.Width = 40;
            var plus = btn("+", (s, e) => Nudge(1)); plus.Dock = DockStyle.Right; plus.Width = 40;
            minus.Font = plus.Font = new Font("Segoe UI Semibold", 13f);
            var gap = new Panel { Dock = DockStyle.Right, Width = 4, BackColor = Color.Transparent };
            bar = new FancySlider { Dock = DockStyle.Fill, Minimum = (int)(def.Min * TENTHS), Maximum = (int)(def.Max * TENTHS), SmallChange = 1, LargeChange = 10, Signed = def.Min < 0 };
            if (def.Unit != "°") bar.EndsFrom(def.Label);
            bar.ValueChanged += (s, e) => { ShowValue(); if (!setting) { box.Touched = true; box.changed(); } };
            Controls.Add(bar); Controls.Add(minus); Controls.Add(gap); Controls.Add(plus); Controls.Add(value); Controls.Add(name);
            tick = Ticks.Box(this); Controls.Add(tick); Ticks.All.Add(this);
            ShowValue();
        }
        void Nudge(int dir) { bar.Value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, bar.Value + dir * (int)Math.Round(def.Step * TENTHS))); }
        public void Set(double v, bool tell) {
            setting = !tell;
            bar.Value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, (int)Math.Round(v * TENTHS)));
            setting = false;
            ShowValue();
        }
        void ShowValue() {
            double v = Value;
            value.Text = (def.Min < 0 && v > 0 ? "+" : "") + v.ToString("0.0") + " " + def.Unit;
        }
    }
}

// data\visceral_re8\slide_hand.json: { "any": { x, y, z, rx, ry, rz, arc }, "ri3042": { ... }, ... }
static class HandFile {
    public static string PathIn(ZoneFile f) { return f.DataDir == null ? null : Path.Combine(f.DataDir, "slide_hand.json"); }
    public static Dictionary<string, object> Load(ZoneFile f) {
        try {
            string p = PathIn(f);
            if (p != null && File.Exists(p)) {
                var o = Json.Obj(Json.Parse(File.ReadAllText(p)));
                if (o != null) {
                    // the file from before 2026-10-24 is one hand for every gun at the top ({ x, y, z }): it becomes "any",
                    // so per-gun hands can sit beside it (the game reads both shapes)
                    if (o.ContainsKey("x") && o["x"] is double) {
                        var any = new Dictionary<string, object>();
                        foreach (var k in new List<string>(o.Keys)) if (!(o[k] is Dictionary<string, object>)) { any[k] = o[k]; o.Remove(k); }
                        if (!o.ContainsKey("any")) o["any"] = any;
                    }
                    return o;
                }
            }
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
    // as the game reads them (visceral_re8_bridge.lua hand_for): the gun's own entry if it has one (a missing key = 0), else
    // "any"; NaN = not set at all
    public static double Get(Dictionary<string, object> all, string gunKey, string key) {
        object o, v;
        string g = all.TryGetValue(gunKey, out o) && o is Dictionary<string, object> ? gunKey : "any";
        if (!all.TryGetValue(g, out o) || !(o is Dictionary<string, object>)) return double.NaN;
        return ((Dictionary<string, object>)o).TryGetValue(key, out v) && v is double ? (double)v : 0.0;
    }
}
