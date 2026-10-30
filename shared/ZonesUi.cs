// ZonesUi.cs -- the Grip Zones window. Top: the game and the gun in hand, and whether the sliders set THIS GUN
// or ALL GUNS. Then one box per zone (the magazine grab, the well, the slide / pump / bolt, the front grip): its live line ("hand 14 cm away,
// needs 12", green when the hand is inside), four sliders (forward / back, up / down, right / left, size) each with - and +
// buttons for half-centimetre steps (easier than dragging with a VR pointer), and RESET. Every change goes into zones.json at
// once; the plugin moves the zone and its sphere within a third of a second. Large type, so Window View can hang it in the headset.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

class ZonesUi : Form {
    const int POLL_MS = 250, SAVE_DEBOUNCE_MS = 60, FIND_GAME_EVERY_MS = 5000;
    const int OFFSET_MM = 300, SIZE_MIN_MM = 20, SIZE_MAX_MM = 400, NUDGE_MM = 5;
    static readonly Color Back = Color.FromArgb(28, 30, 38), Panel2 = Color.FromArgb(40, 43, 55), Ink = Color.FromArgb(235, 235, 240);
    static readonly Color Dim = Color.FromArgb(155, 160, 175), Good = Color.FromArgb(90, 200, 120), Bad = Color.FromArgb(225, 120, 95);
    static readonly Color Warn = Color.FromArgb(230, 190, 90), ButtonBack = Color.FromArgb(62, 67, 86);

    readonly Settings settings = new Settings();
    readonly ZoneFile file = new ZoneFile();
    readonly Timer poll = new Timer(), saveTimer = new Timer(), nudgeTimer = new Timer();
    readonly Nudge nudge = new Nudge();   // 2026-10-27: the controller buttons step the ticked sliders (Ticks.cs)
    Label nudgeNote;
    DateTime lastFind = DateTime.MinValue, lastZonesWrite = DateTime.MinValue;
    LiveState live;
    int shownGun = -1;
    bool syncing;

    Label status;
    SkinPanel scroll;
    RadioButton thisGun, allGuns;
    readonly List<ZoneBox> boxes = new List<ZoneBox>();
    // 2026-10-25 (Tefa: "all please"): the hand on the slide / pump / bolt (and its arc over the gun), and the gun in the right hand
    ValueBox handBox, aimBox;
    PairsBox pairsBox;   // 2026-10-25: the numbered hand pairs (PairsBox.cs)
    // 2026-10-28 (Tefa: "list all the possible grip pairs for that weapon ... so that things fit into a window and i don't have to
    // scroll down and up, i can just choose the parts i want from a drop down menu and all the needed sliders will appear"): ONE
    // part at a time. PART opens a list INSIDE the window (a Windows drop-down's list is a window of its own, never shown by
    // Window View in the headset) of what the gun in hand has; picking one shows only its boxes: where the left grip takes it
    // (START) and where the hand sits (LAND).
    class Entry { public string Key, Label; public Control[] Shows; }
    string entryKey = "part";
    Button partBtn;
    FlowLayoutPanel partList;
    const int PART_ROW_H = 40;
    string partWord = "SLIDE / PUMP / BOLT";   // slide / pump / bolt: what the gun in hand has
    DateTime lastHandWrite = DateTime.MinValue;

    public ZonesUi(string gameArg) {
        Text = "Grip Zones";
        BackColor = Back; ForeColor = Ink;
        Font = new Font("Segoe UI", 11f);
        ClientSize = new Size(620, 1010);
        MinimumSize = new Size(520, 600);
        StartPosition = FormStartPosition.CenterScreen;
        string ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "grip_zones.ico");
        try { Icon = new Icon(ico); } catch (Exception) { }
        DoubleBuffered = true;
        Backdrop.Init(this, ico, Back);   // the icon, pixelated and faint, behind the whole window (Tefa 2026-10-25)

        scroll = new SkinPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
        Controls.Add(scroll);
        boxes.Add(new ZoneBox(this, "mag", "START: GRAB THE MAGAZINE", "left grip on the seated magazine"));
        boxes.Add(new ZoneBox(this, "well", "LAND: THE WELL", "where a magazine goes back in (long guns)"));
        boxes.Add(new ZoneBox(this, "part", "START: SLIDE / PUMP / BOLT", "left grip in it takes it"));
        // 2026-10-28 (Tefa: "another pair is called slide left"): a pistol slide's left-side sphere, its own hold and pose
        boxes.Add(new ZoneBox(this, "slide-left", "START: SLIDE, LEFT SIDE", "left grip in it takes the slide from the left (its own hold and Hand Poser pose)"));
        boxes.Add(new ZoneBox(this, "grip", "FRONT GRIP", "left grip in it holds the gun two-handed: this gun's kept grip spot, moved by these sliders"));
        // 2026-10-27: the parts some guns have (Program.HasPart): only the gun in hand's are shown
        boxes.Add(new ZoneBox(this, "handle", "GRIP HANDLE", "handguns: left grip in it cups the handle"));
        boxes.Add(new ZoneBox(this, "drum", "THE DRUM", "revolvers: left grip opens it and holds the hand there; LT loads a round, LT when full closes it"));
        boxes.Add(new ZoneBox(this, "insert", "INSERT POINT", "GM 79: a grenade held with the left grip goes in here; left grip + LT takes one out (live or spent)"));
        handBox = new ValueBox("HAND ON THE BOLT", "the left hand while it holds the slide / pump / bolt: move it, turn it, and arc it over the gun on the way", new[] {
            // 2026-10-29 (Tefa: "move them smoothly for the weapon's length worth"): a rifle's length forward / back, not 15 cm
            new ValueRow("z", "forward / back", "cm", -60, 100, 0.5), new ValueRow("y", "up / down", "cm", -30, 30, 0.5),
            new ValueRow("x", "right / left", "cm", -30, 30, 0.5), new ValueRow("rx", "tilt fingers up / down", "°", -180, 180, 1),
            new ValueRow("ry", "turn left / right", "°", -180, 180, 1), new ValueRow("rz", "roll", "°", -180, 180, 1),
            new ValueRow("arc", "arc over the gun", "cm", 0, 25, 0.5) }, () => Changed(), (t, h) => Btn(t, h));
        aimBox = new ValueBox("GUN IN THE RIGHT HAND", "where the gun points and sits in the right hand", new[] {
            new ValueRow("pitch", "aim up / down", "°", -45, 45, 1), new ValueRow("yaw", "aim left / right", "°", -45, 45, 1),
            new ValueRow("roll", "roll", "°", -45, 45, 1), new ValueRow("z", "forward / back", "cm", -10, 10, 0.5),
            new ValueRow("y", "up / down", "cm", -10, 10, 0.5), new ValueRow("x", "right / left", "cm", -10, 10, 0.5) }, () => Changed(), (t, h) => Btn(t, h));
        pairsBox = new PairsBox(file, () => Group() == "default" ? "any" : Group(), (t, h) => Btn(t, h));
        scroll.Controls.Add(pairsBox);  // docked top: added first = shown last
        scroll.Controls.Add(aimBox);
        scroll.Controls.Add(handBox);
        for (int i = boxes.Count - 1; i >= 0; i--) scroll.Controls.Add(boxes[i]);   // docked top: added last = shown first
        partList = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 0, BackColor = Color.Transparent, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };
        scroll.Controls.Add(partList);   // above every box
        entryKey = settings.Get("part", "part");
        BuildTop();
        BuildBottom();

        string folder = gameArg ?? settings.Get("game", null);
        if (!ZoneFile.LooksLikeVillage(folder) && gameArg == null) folder = ZoneFile.FindVillage();
        if (folder != null && (gameArg != null || ZoneFile.LooksLikeVillage(folder))) UseFolder(folder, gameArg == null);

        // start at the top with nothing focused (a focused slider made the list scroll under the top bar)
        Shown += (s, e) => { ActiveControl = partBtn; scroll.AutoScrollPosition = new Point(0, 0); };
        poll.Interval = POLL_MS; poll.Tick += (s, e) => Poll(); poll.Start();
        saveTimer.Interval = SAVE_DEBOUNCE_MS; saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveAll(); };
        nudgeTimer.Interval = 15; nudgeTimer.Tick += (s, e) => NudgeTick(); nudgeTimer.Start();
        Ticks.Changed += ShowNudge;
        ShowNudge();
        Poll();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.PaintUnder(this, e.Graphics, e.ClipRectangle); }
    protected override CreateParams CreateParams { get { return Backdrop.Composited(base.CreateParams); } }

    // ---- building -------------------------------------------------------------------------------------------------------
    internal static Button Btn(string text, EventHandler onClick, int width = 0) {
        var b = new SkinButton { Text = text, FlatStyle = FlatStyle.Flat, BackColor = ButtonBack, ForeColor = Ink, Height = 34, AutoSize = width == 0, Margin = new Padding(3) };
        b.FlatAppearance.BorderSize = 0;
        if (width > 0) b.Width = width;
        b.Click += onClick;
        return b;
    }

    static FlowLayoutPanel Row(int height) {
        return new FlowLayoutPanel { Dock = DockStyle.Top, Height = height, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(6, 2, 6, 0) };
    }

    void BuildTop() {
        var top = new SkinPanel { Dock = DockStyle.Top, Height = 214, Tint = Color.FromArgb(120, Back) };
        Controls.Add(top);
        status = new SkinLabel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(10, 8, 0, 0), ForeColor = Dim };
        var one = Row(42);
        one.Controls.Add(Btn("GAME FOLDER...", (s, e) => PickFolder()));
        var two = Row(42);
        two.Controls.Add(new SkinLabel { Text = "the sliders set:", AutoSize = true, ForeColor = Dim, Padding = new Padding(0, 8, 6, 0) });
        thisGun = new SkinRadioButton { Text = "THIS GUN", AutoSize = true, Checked = settings.Get("scope", "gun") != "all", ForeColor = Ink, BackColor = Color.Transparent, Padding = new Padding(0, 6, 8, 0) };
        allGuns = new SkinRadioButton { Text = "ALL GUNS", AutoSize = true, Checked = settings.Get("scope", "gun") == "all", ForeColor = Ink, BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 0) };
        EventHandler scope = (s, e) => { if (((RadioButton)s).Checked) { settings.Set("scope", allGuns.Checked ? "all" : "gun"); RefreshFromFile(); } };
        thisGun.CheckedChanged += scope; allGuns.CheckedChanged += scope;
        two.Controls.Add(thisGun); two.Controls.Add(allGuns);
        var three = Row(42);   // 2026-10-27: the ticked sliders and the controller buttons
        three.Controls.Add(Btn("CLEAR ALL TICKS", (s, e) => Ticks.ClearAll()));
        nudgeNote = new SkinLabel { AutoSize = true, ForeColor = Dim, Padding = new Padding(4, 8, 0, 0) };
        three.Controls.Add(nudgeNote);
        var four = Row(48);
        partBtn = Btn("PART", (s, e) => { if (partList.Visible) CloseParts(); else OpenParts(); });
        partBtn.Font = new Font("Segoe UI Semibold", 12f);
        partBtn.AutoSize = false; partBtn.Width = 560; partBtn.Height = 40; partBtn.TextAlign = ContentAlignment.MiddleLeft;
        four.Controls.Add(partBtn);
        top.Controls.Add(four);
        top.Controls.Add(three);
        top.Controls.Add(two);
        top.Controls.Add(one);
        top.Controls.Add(status);
    }

    // 2026-10-25 (Tefa: "please remove that, i'm afraid to accidentally press it, i'll just ask you if i ever need to clear all of
    // it"): the FORGET THIS GUN'S / ALL-GUNS ZONES button is gone (a session clears zones.json by hand when asked)
    void BuildBottom() { }

    // ---- one part at a time (2026-10-28) ----------------------------------------------------------------------------------
    ZoneBox BoxOf(string slot) { foreach (var b in boxes) if (b.Slot == slot) return b; return null; }
    bool Has(string slot) { int gun = live == null ? 0 : live.Gun; return allGuns.Checked || gun <= 0 || Program.HasPart(gun, slot); }

    List<Entry> Entries() {
        var r = new List<Entry>();
        string partName = partWord.ToLowerInvariant();
        if (Has("mag")) r.Add(new Entry { Key = "mag", Label = Has("well") ? "magazine  ->  the well" : "magazine",
                                          Shows = Has("well") ? new Control[] { BoxOf("mag"), BoxOf("well") } : new Control[] { BoxOf("mag") } });
        if (Has("part") && Has("slide-left") && !allGuns.Checked && partName == "slide") {   // a pistol: the slide from either side
            r.Add(new Entry { Key = "part", Label = "slide, right side  (where LG takes it, where the hand sits)", Shows = new Control[] { BoxOf("part"), handBox } });
            r.Add(new Entry { Key = "slide-left", Label = "slide, left side  (where LG takes it, where the hand sits)", Shows = new Control[] { BoxOf("slide-left"), handBox } });
        } else if (Has("part")) r.Add(new Entry { Key = "part", Label = partName + "  (where LG takes it, where the hand sits)", Shows = new Control[] { BoxOf("part"), handBox } });
        r.Add(new Entry { Key = "grip", Label = "front grip", Shows = new Control[] { BoxOf("grip") } });
        if (Has("handle")) r.Add(new Entry { Key = "handle", Label = "grip handle", Shows = new Control[] { BoxOf("handle") } });
        if (Has("drum")) r.Add(new Entry { Key = "drum", Label = "the drum", Shows = new Control[] { BoxOf("drum") } });
        if (Has("insert")) r.Add(new Entry { Key = "insert", Label = "insert point", Shows = new Control[] { BoxOf("insert") } });
        r.Add(new Entry { Key = "aim", Label = "the gun in the right hand", Shows = new Control[] { aimBox } });
        foreach (var p in pairsBox.ForGun(Group() == "default" ? "any" : Group()))
            r.Add(new Entry { Key = "pair:" + p.Key, Label = "pair " + p.Key + "  -  " + p.Value, Shows = new Control[] { pairsBox } });
        r.Add(new Entry { Key = "pair:new", Label = "+  a new pair", Shows = new Control[] { pairsBox } });
        return r;
    }

    Entry Current(List<Entry> all) {
        foreach (var e in all) if (e.Key == entryKey) return e;
        if (entryKey.StartsWith("pair:"))   // a pair picked with the pairs box's own arrows is still "a pair"
            foreach (var e in all) if (e.Key == "pair:" + pairsBox.SelectedNumber) return e;
        return all[0];
    }

    // only the picked part's boxes are shown; called every poll, so a gun change follows at once
    void ShowPart() {
        var all = Entries();
        var cur = Current(all);
        var shown = new HashSet<Control>(cur.Shows);
        foreach (var b in boxes) if (b.Visible != shown.Contains(b)) b.Visible = shown.Contains(b);
        foreach (var c in new Control[] { handBox, aimBox, pairsBox }) if (c.Visible != shown.Contains(c)) c.Visible = shown.Contains(c);
        string t = "PART:  " + cur.Label + "     \u25BC";
        if (partBtn.Text != t) partBtn.Text = t;
    }

    void OpenParts() {
        var all = Entries();
        var cur = Current(all);
        partList.SuspendLayout();
        var old = new List<Control>();
        foreach (Control c in partList.Controls) old.Add(c);
        partList.Controls.Clear();
        foreach (var c in old) c.Dispose();
        foreach (var e in all) {
            var pick = e;
            var b = Btn((e == cur ? "\u25B6  " : "     ") + e.Label, (s, ev) => { PickPart(pick.Key); BeginInvoke(new Action(CloseParts)); });
            b.AutoSize = false; b.Width = Math.Max(300, scroll.ClientSize.Width - 30); b.TextAlign = ContentAlignment.MiddleLeft;
            partList.Controls.Add(b);
        }
        partList.Height = all.Count * PART_ROW_H + 4;
        partList.Visible = true;
        partList.ResumeLayout();
        scroll.AutoScrollPosition = new Point(0, 0);
    }
    void CloseParts() { partList.Visible = false; partList.Height = 0; }

    void PickPart(string key) {
        if (key == "pair:new") { pairsBox.AddNew(); key = "pair:" + pairsBox.SelectedNumber; }
        else if (key.StartsWith("pair:")) { int n; if (int.TryParse(key.Substring(5), out n)) pairsBox.SelectNumber(n); }
        entryKey = key;
        settings.Set("part", key);
        ShowPart();
        scroll.AutoScrollPosition = new Point(0, 0);
    }

    // ---- the controller nudge (Nudge.cs, the same buttons as Hand Poser) -------------------------------------------------
    void ShowNudge() {
        if (nudgeNote == null) return;
        int n = Ticks.Count;
        nudgeNote.Text = n == 0 ? "tick sliders: X -1, Y -5, A +1, B +5" : !nudge.Linked ? n + " ticked: start Window View, click the left stick in"
            : n + " ticked: X -1, Y -5, A +1, B +5 (a held grip repeats)";
        nudgeNote.ForeColor = n > 0 && nudge.Linked ? Good : Dim;
    }
    void NudgeTick() {
        bool started;
        double step = nudge.Poll(Ticks.Count > 0, out started);
        if (step == 0) return;
        Ticks.StepAll((int)step);
    }

    // ---- the game ---------------------------------------------------------------------------------------------------------
    void UseFolder(string folder, bool remember) {
        try { file.Use(folder); } catch (Exception ex) { status.Text = "cannot use that folder: " + ex.Message; return; }
        if (remember) settings.Set("game", folder);
        lastZonesWrite = DateTime.MinValue;
        RefreshFromFile();
        pairsBox.Reload();
    }

    void PickFolder() {
        using (var d = new FolderBrowserDialog { Description = "Pick Resident Evil Village's install folder (the one with re8.exe)" }) {
            if (d.ShowDialog(this) == DialogResult.OK) UseFolder(d.SelectedPath, true);
        }
    }

    // slide_hand.json names a gun "ri3042"; "any" = all guns (as the in-game menu)
    string HandKey() { string g = Group(); return g == "default" ? "any" : "ri" + g; }

    internal string Group() {
        if (allGuns.Checked || live == null || live.Gun <= 0) return "default";
        return live.Gun.ToString();
    }

    void Poll() {
        if (file.Folder == null) {
            if ((DateTime.Now - lastFind).TotalMilliseconds > FIND_GAME_EVERY_MS) {
                lastFind = DateTime.Now;
                string f = ZoneFile.FindVillage();
                if (f != null) UseFolder(f, true);
            }
            status.Text = "Village not found: press GAME FOLDER...";
            status.ForeColor = Bad;
            return;
        }
        bool alive = file.ScriptAlive();
        live = alive ? (file.ReadState() ?? live) : null;
        if (!alive) {
            status.Text = "Village found, but the game is not running (or the zones script is not in it)";
            status.ForeColor = Warn;
        } else if (live == null || live.Gun <= 0) {
            status.Text = "connected · no gun in hand: the sliders set ALL GUNS";
            status.ForeColor = Good;
        } else {
            status.Text = "connected · in hand: " + Program.GunName(live.Gun) + (allGuns.Checked ? " · setting ALL GUNS" : " · setting this gun");
            status.ForeColor = Good;
        }
        // the gun changed, or the in-game menu saved: show the numbers now in force (never while a save of ours is pending)
        int gun = live == null ? 0 : live.Gun;
        DateTime w = DateTime.MinValue;
        try { if (File.Exists(file.ZonesPath)) w = File.GetLastWriteTime(file.ZonesPath); } catch (IOException) { }
        DateTime hw = DateTime.MinValue;
        try { string hp = HandFile.PathIn(file); if (hp != null && File.Exists(hp)) hw = File.GetLastWriteTime(hp); } catch (IOException) { }
        if (!saveTimer.Enabled && (gun != shownGun || w != lastZonesWrite || hw != lastHandWrite)) RefreshFromFile();
        if (handBox != null) {   // named for what the gun in hand has
            string part = "SLIDE / PUMP / BOLT";   // no gun in hand: any of them
            if (live != null) foreach (var k in new[] { "bolt", "pump", "slide" }) { var z = live.Find(k); if (z != null && z.On) { part = k.ToUpperInvariant(); break; } }
            handBox.Title = "LAND: HAND ON THE " + part;
            partWord = part;
        }
        foreach (var b in boxes) b.ShowLive(live);
        pairsBox.Poll();
        ShowPart();
    }

    void RefreshFromFile() {
        if (file.Folder == null) return;
        try { lastZonesWrite = File.Exists(file.ZonesPath) ? File.GetLastWriteTime(file.ZonesPath) : DateTime.MinValue; } catch (IOException) { }
        shownGun = live == null ? 0 : live.Gun;
        var all = file.Load();
        syncing = true;
        foreach (var b in boxes) b.Load(all, Group(), live);
        try { string hp = HandFile.PathIn(file); lastHandWrite = hp != null && File.Exists(hp) ? File.GetLastWriteTime(hp) : DateTime.MinValue; } catch (IOException) { }
        var hands = HandFile.Load(file);
        handBox.Load(k => HandFile.Get(hands, HandKey(), k));
        aimBox.Load(k => ZoneFile.Effective(all, Group(), "aim", k));
        syncing = false;
    }

    internal void Changed() { if (!syncing) { saveTimer.Stop(); saveTimer.Start(); } }

    void SaveAll() {
        if (file.Folder == null) return;
        var all = file.Load();   // fresh: keeps what the in-game menu or another gun's numbers hold
        foreach (var b in boxes) b.Store(all, Group());
        if (aimBox.Touched) { ZoneFile.SetEntry(all, Group(), "aim", aimBox.Values()); aimBox.Touched = false; }
        try { file.Save(all); lastZonesWrite = File.GetLastWriteTime(file.ZonesPath); }
        catch (Exception ex) { status.Text = "could not save: " + ex.Message; status.ForeColor = Bad; }
        if (handBox.Touched) {   // the same file as the in-game menu's slide-hand sliders
            try {
                var hands = HandFile.Load(file);
                var mine = handBox.Values();
                object old;
                if (hands.TryGetValue(HandKey(), out old) && old is Dictionary<string, object>)
                    foreach (var kv in (Dictionary<string, object>)old) if (!mine.ContainsKey(kv.Key)) mine[kv.Key] = kv.Value;
                hands[HandKey()] = mine;
                HandFile.Save(file, hands);
                lastHandWrite = File.GetLastWriteTime(HandFile.PathIn(file));
                handBox.Touched = false;
            } catch (Exception ex) { status.Text = "could not save the hand: " + ex.Message; status.ForeColor = Bad; }
        }
    }

    internal void Forget(string zone) {
        if (file.Folder == null) return;
        var all = file.Load();
        ZoneFile.Forget(all, Group(), zone);
        try { file.Save(all); } catch (Exception) { }
        RefreshFromFile();
    }

    // ---- one zone ---------------------------------------------------------------------------------------------------------
    class ZoneBox : Panel {
        readonly ZonesUi ui;
        readonly string slot, title;   // slot "part" = whichever the gun has (slide / pump / bolt)
        public string Slot { get { return slot; } }
        string key;
        readonly Label head, note;
        readonly Slider fwd, up, right, size;
        bool userSize;   // the size was set (this gun or the default); otherwise it shows the plugin's own

        public ZoneBox(ZonesUi ui, string slot, string title, string what) {
            this.ui = ui; this.slot = slot; this.title = title; key = slot == "part" ? "slide" : slot;
            Dock = DockStyle.Top; Height = 276; BackColor = Color.Transparent; Padding = new Padding(0, 6, 0, 6);
            var inner = new SkinPanel { Dock = DockStyle.Fill, Tint = Color.FromArgb(150, Panel2), Padding = new Padding(8, 4, 8, 4) };
            Controls.Add(inner);
            head = new SkinLabel { Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI Semibold", 12f), ForeColor = Ink };
            note = new SkinLabel { Dock = DockStyle.Top, Height = 24, ForeColor = Dim, Text = what };
            fwd = new Slider(this, "forward / back", -OFFSET_MM, OFFSET_MM, true);
            up = new Slider(this, "up / down", -OFFSET_MM, OFFSET_MM, true);
            right = new Slider(this, "right / left", -OFFSET_MM, OFFSET_MM, true);
            size = new Slider(this, "size", SIZE_MIN_MM, SIZE_MAX_MM, false);
            var reset = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, BackColor = Color.Transparent, FlowDirection = FlowDirection.RightToLeft };
            reset.Controls.Add(Btn("RESET", (s, e) => ui.Forget(key)));
            foreach (Control c in new Control[] { reset, size, right, up, fwd, note, head }) inner.Controls.Add(c);
        }

        static double BuiltInCm(string k) { return k == "mag" ? 12 : k == "well" ? 16 : k == "bolt" ? 9 : k == "grip" ? 10 : k == "handle" ? 8 : k == "drum" ? 6 : k == "insert" ? 7 : 14; }

        public void Load(Dictionary<string, object> all, string group, LiveState live) {
            if (slot == "part") { var lz = PartLive(live); key = lz != null ? lz.Key : key; }
            Func<string, double> mm = a => { double v = ZoneFile.Effective(all, group, key, a); return double.IsNaN(v) ? 0 : v * 1000; };
            fwd.Set(mm("z")); up.Set(mm("y")); right.Set(mm("x"));
            double r = ZoneFile.Effective(all, group, key, "r");
            userSize = !double.IsNaN(r);
            if (userSize) size.Set(r * 1000);
            else { var lz = Live(live); size.Set((lz != null && lz.RadiusCm > 0 ? lz.RadiusCm : BuiltInCm(key)) * 10); }
        }

        public void Store(Dictionary<string, object> all, string group) {
            if (fwd.Touched || up.Touched || right.Touched) ZoneFile.SetOffset(all, group, key, right.Value / 1000.0, up.Value / 1000.0, fwd.Value / 1000.0);
            if (size.Touched || userSize) { ZoneFile.SetRadius(all, group, key, size.Value / 1000.0); userSize = true; }
            fwd.Touched = up.Touched = right.Touched = size.Touched = false;
        }

        LiveZone PartLive(LiveState live) {
            if (live == null) return null;
            foreach (var k in new[] { "bolt", "pump", "slide" }) { var z = live.Find(k); if (z != null && z.On) return z; }
            return live.Find("bolt") ?? live.Find("pump") ?? live.Find("slide");
        }
        LiveZone Live(LiveState live) { return slot == "part" ? PartLive(live) : (live == null ? null : live.Find(key)); }

        public void ShowLive(LiveState live) {
            var z = Live(live);
            string name = slot == "part" ? title.Replace("SLIDE / PUMP / BOLT", key.ToUpperInvariant()) : title;
            if (z == null || !z.On) {
                head.Text = name + (live == null ? "" : "  ·  not on this gun right now");
                head.ForeColor = Dim;
                return;
            }
            if (z.HandCm >= 0) {
                bool inside = z.HandCm <= z.RadiusCm;
                head.Text = string.Format("{0}  ·  hand {1:0} cm away, needs {2:0}", name, z.HandCm, z.RadiusCm);
                head.ForeColor = inside ? Good : Ink;
            } else {
                head.Text = string.Format("{0}  ·  size {1:0} cm", name, z.RadiusCm);
                head.ForeColor = Ink;
            }
        }

        public void Changed() { ui.Changed(); }
    }

    // a labelled slider in millimetres, shown in centimetres, with - / + buttons
    class Slider : Panel, ITickable {
        readonly ZoneBox box;
        readonly CheckBox tick;   // 2026-10-27: ticked = the controller buttons step it (Ticks.cs)
        public bool Ticked { get { return tick.Checked; } set { tick.Checked = value; } }
        public void Step(int presses) { Nudge(presses * NUDGE_MM); }
        readonly FancySlider bar;
        readonly Label value;
        readonly bool signed;
        bool setting;
        public bool Touched;
        public int Value { get { return bar.Value; } }

        public Slider(ZoneBox box, string label, int min, int max, bool signed) {
            this.box = box; this.signed = signed;
            Dock = DockStyle.Top; Height = 40; BackColor = Color.Transparent;
            var name = new SkinLabel { Text = label, Dock = DockStyle.Left, Width = 128, ForeColor = Dim, Padding = new Padding(0, 8, 0, 0) };
            value = new SkinLabel { Dock = DockStyle.Right, Width = 82, ForeColor = Ink, TextAlign = ContentAlignment.MiddleRight };
            var minus = Btn("−", (s, e) => Nudge(-NUDGE_MM), 40); minus.Dock = DockStyle.Right;
            var plus = Btn("+", (s, e) => Nudge(NUDGE_MM), 40); plus.Dock = DockStyle.Right;
            minus.Font = plus.Font = new Font("Segoe UI Semibold", 13f);
            var gap = new Panel { Dock = DockStyle.Right, Width = 4, BackColor = Color.Transparent };
            bar = new FancySlider { Dock = DockStyle.Fill, Minimum = min, Maximum = max, SmallChange = 1, LargeChange = 10, Signed = signed };
            bar.EndsFrom(label);
            bar.ValueChanged += (s, e) => { ShowValue(); if (!setting) { Touched = true; box.Changed(); } };
            Controls.Add(bar); Controls.Add(minus); Controls.Add(gap); Controls.Add(plus); Controls.Add(value); Controls.Add(name);
            tick = Ticks.Box(this); Controls.Add(tick); Ticks.All.Add(this);
            ShowValue();
        }

        void Nudge(int mm) { bar.Value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, bar.Value + mm)); }

        public void Set(double mm) {
            setting = true;
            bar.Value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, (int)Math.Round(mm)));
            setting = false;
            ShowValue();
        }

        void ShowValue() {
            double cm = bar.Value / 10.0;
            value.Text = (signed && cm > 0 ? "+" : "") + cm.ToString("0.0") + " cm";
        }
    }
}
