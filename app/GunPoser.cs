// GunPoser.cs -- the Gun Poser window (2026-10-28, Tefa: "and then we need a new thing called the Gun Poser as well"). It
// follows the gun in the right hand and shows six sliders for THAT gun: aim it up / down, left / right, roll it, and move it
// forward / up / right in the hand. The numbers are zones.json "aim" (per gun over "default"), the same ones Grip Zones'
// "the gun in the right hand" entry sets, read by lua/visceral_re8_zones.lua and laid over the VR mode's own right-hand
// offsets; so the two windows always show the same numbers and nothing in the game had to change. ALL GUNS sets "default".
// Built with a copy of Grip Zones' files (shared\, build.bat: csc /main:GunPoserProgram), so it shares its look, sliders,
// tick boxes, the controller nudge through Window View, the game folder setting and the gun names. Its own repo since
// 2026-10-28 (Tefa: "it's own page, idea is to make it available across many games").
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

static class GunPoserProgram {
    [STAThread]
    static int Main(string[] args) {
        string gameArg = null;
        for (int i = 0; i < args.Length; i++) if (args[i] == "--game" && i + 1 < args.Length) gameArg = args[++i];
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new GunPoserUi(gameArg));
        return 0;
    }
}

class GunPoserUi : Form {
    const int POLL_MS = 250, SAVE_DEBOUNCE_MS = 120, FIND_GAME_EVERY_MS = 5000;
    static readonly Color Back = Color.FromArgb(28, 30, 38), Ink = Color.FromArgb(235, 235, 240), Dim = Color.FromArgb(155, 160, 175);
    static readonly Color Good = Color.FromArgb(90, 200, 120), Warn = Color.FromArgb(230, 190, 90), Bad = Color.FromArgb(225, 120, 95);

    readonly Settings settings = new Settings();   // the same game folder as Grip Zones
    readonly ZoneFile file = new ZoneFile();
    readonly Timer poll = new Timer(), saveTimer = new Timer(), nudgeTimer = new Timer();
    readonly Nudge nudge = new Nudge();
    readonly Label status, which, nudgeNote;
    readonly CheckBox allGuns;
    readonly ValueBox aim;
    readonly bool testFolder;   // --game: a test folder, never remembered
    DateTime lastFind = DateTime.MinValue, lastZonesWrite = DateTime.MinValue;
    int gun;
    string loadedGroup = "";
    bool syncing;

    public GunPoserUi(string gameArg) {
        testFolder = gameArg != null;
        Text = "Gun Poser";
        BackColor = Back; ForeColor = Ink;
        Font = new Font("Segoe UI", 11f);
        ClientSize = new Size(620, 570);
        MinimumSize = new Size(520, 500);
        StartPosition = FormStartPosition.CenterScreen;
        string ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "grip_zones.ico");
        try { Icon = new Icon(ico); } catch (Exception) { }
        DoubleBuffered = true;
        Backdrop.Init(this, ico, Back);

        var body = new SkinPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        Controls.Add(body);
        aim = new ValueBox("THE GUN IN THE RIGHT HAND", "where it points and sits in the hand; the same numbers as Grip Zones' entry", new[] {
            new ValueRow("pitch", "aim up / down", "°", -45, 45, 1), new ValueRow("yaw", "aim left / right", "°", -45, 45, 1),
            new ValueRow("roll", "roll", "°", -45, 45, 1), new ValueRow("z", "forward / back", "cm", -10, 10, 0.5),
            new ValueRow("y", "up / down", "cm", -10, 10, 0.5), new ValueRow("x", "right / left", "cm", -10, 10, 0.5) },
            () => { if (!syncing) { saveTimer.Stop(); saveTimer.Start(); } }, (t, h) => ZonesUi.Btn(t, h));
        body.Controls.Add(aim);

        var top = new SkinPanel { Dock = DockStyle.Top, Height = 152, Tint = Color.FromArgb(120, Back) };
        Controls.Add(top);
        nudgeNote = new SkinLabel { Dock = DockStyle.Top, Height = 30, ForeColor = Dim, Padding = new Padding(10, 4, 0, 0) };
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, BackColor = Color.Transparent, Padding = new Padding(6, 2, 6, 0) };
        row.Controls.Add(ZonesUi.Btn("CLEAR ALL TICKS", (s, e) => Ticks.ClearAll()));
        allGuns = new SkinCheckBox { Text = "ALL GUNS (the numbers every gun starts from)", AutoSize = true, ForeColor = Ink, Padding = new Padding(8, 8, 0, 0) };
        allGuns.CheckedChanged += (s, e) => { loadedGroup = ""; Poll(); };
        row.Controls.Add(allGuns);
        which = new SkinLabel { Dock = DockStyle.Top, Height = 40, Font = new Font("Segoe UI Semibold", 13f), ForeColor = Ink, Padding = new Padding(10, 8, 0, 0) };
        status = new SkinLabel { Dock = DockStyle.Top, Height = 34, ForeColor = Dim, Padding = new Padding(10, 8, 0, 0) };
        top.Controls.Add(nudgeNote);
        top.Controls.Add(row);
        top.Controls.Add(which);
        top.Controls.Add(status);

        string folder = gameArg ?? settings.Get("game", null);
        if (!testFolder && !ZoneFile.LooksLikeVillage(folder)) folder = ZoneFile.FindVillage();
        if (folder != null) { try { file.Use(folder); } catch (Exception) { } }

        poll.Interval = POLL_MS; poll.Tick += (s, e) => Poll(); poll.Start();
        saveTimer.Interval = SAVE_DEBOUNCE_MS; saveTimer.Tick += (s, e) => { saveTimer.Stop(); Save(); };
        nudgeTimer.Interval = 15; nudgeTimer.Tick += (s, e) => { bool started; double step = nudge.Poll(Ticks.Count > 0, out started); if (step != 0) Ticks.StepAll((int)step); };
        nudgeTimer.Start();
        Ticks.Changed += ShowNudge;
        ShowNudge();
        Poll();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.PaintUnder(this, e.Graphics, e.ClipRectangle); }
    protected override CreateParams CreateParams { get { return Backdrop.Composited(base.CreateParams); } }

    void ShowNudge() {
        int n = Ticks.Count;
        nudgeNote.Text = n == 0 ? "tick sliders: X -1, Y -5, A +1, B +5" : !nudge.Linked ? n + " ticked: start Window View, click the left stick in"
            : n + " ticked: X -1, Y -5, A +1, B +5 (a held grip repeats)";
        nudgeNote.ForeColor = n > 0 && nudge.Linked ? Good : Dim;
    }

    // "default" = every gun; otherwise the gun in hand's own numbers (laid over "default" by the game)
    string Group() { return allGuns.Checked || gun <= 0 ? "default" : gun.ToString(); }

    void Poll() {
        if (file.Folder == null) {
            if ((DateTime.Now - lastFind).TotalMilliseconds > FIND_GAME_EVERY_MS) {
                lastFind = DateTime.Now;
                string f = ZoneFile.FindVillage();
                if (f != null) { try { file.Use(f); } catch (Exception) { } }
            }
            status.Text = "Village not found: set it once in Grip Zones (GAME FOLDER...)";
            status.ForeColor = Bad;
            return;
        }
        var live = file.ScriptAlive() ? file.ReadState() : null;
        gun = live == null ? 0 : live.Gun;
        if (live == null) { status.Text = "Village found, but the game is not running: the sliders set ALL GUNS"; status.ForeColor = Warn; }
        else { status.Text = "connected · in hand: " + (gun > 0 ? Program.GunName(gun) : "no gun"); status.ForeColor = Good; }
        string g = Group();
        which.Text = g == "default" ? "SETTING: ALL GUNS" : "SETTING: " + Program.GunName(gun).ToUpperInvariant() + " ONLY";
        DateTime w;
        try { w = File.Exists(file.ZonesPath) ? File.GetLastWriteTime(file.ZonesPath) : DateTime.MinValue; } catch (IOException) { return; }
        // another gun drawn, or Grip Zones / the game menu changed the numbers: show what the file holds now
        if (!saveTimer.Enabled && (g != loadedGroup || w != lastZonesWrite)) {
            loadedGroup = g; lastZonesWrite = w;
            var all = file.Load();
            syncing = true;
            aim.Load(k => ZoneFile.Effective(all, g, "aim", k));
            syncing = false;
        }
    }

    void Save() {
        if (file.Folder == null || !aim.Touched) return;
        var all = file.Load();   // fresh: keeps what Grip Zones, the in-game menu or another gun's numbers hold
        ZoneFile.SetEntry(all, loadedGroup == "" ? Group() : loadedGroup, "aim", aim.Values());
        aim.Touched = false;
        try { file.Save(all); lastZonesWrite = File.GetLastWriteTime(file.ZonesPath); }
        catch (Exception ex) { status.Text = "could not save: " + ex.Message; status.ForeColor = Bad; }
    }
}
