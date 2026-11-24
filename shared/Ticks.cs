// Ticks.cs -- the tick boxes on Grip Zones' sliders (2026-10-27, Tefa: "add tick boxes to the grab zones and add the same button
// bindings as hand poser"). Every slider with a tick box registers here; while any is ticked the controller buttons (Nudge.cs,
// through Window View) step the ticked ones: X one step down, Y five down, A one up, B five up (one step = one press of its - / +).
// The same file is in Holster Zones and Gun Poser (shared\): keep them the same.
//
// 2026-11-24 (Tefa: "bring Hand Poser's controller scheme to Grip Zones, Gun Poser, Holster Zones and Animation & Timing"): every
// tick box carries a key in its Tag ("tick:<n>"), so Hand Poser's NudgeNav.cs (the left stick walks the sliders, A ticks, LT picks
// a section) and SliderUndo.cs (X undo / Y redo on the last slider) work here unchanged; and every part (a zone's box, a box of
// values) gets a SECTION tick in its head (Tag "section:<part>"): ticked, the left stick walks that part's sliders too.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

interface ITickable {
    bool Ticked { get; set; }
    void Step(int presses);   // as if its - (negative) or + (positive) button were pressed this many times
}

static class Ticks {
    public static readonly List<ITickable> All = new List<ITickable>();
    static readonly Dictionary<ITickable, CheckBox> boxes = new Dictionary<ITickable, CheckBox>();
    static readonly List<CheckBox> sectionTicks = new List<CheckBox>();
    static int next;
    public static event Action Changed;
    public static int Count { get { int n = 0; foreach (var t in All) if (t.Ticked) n++; return n; } }
    public static void Notify() { if (Changed != null) Changed(); }
    public static void ClearAll() {
        foreach (var t in All) t.Ticked = false;
        foreach (var s in sectionTicks) if (s.Checked) s.Checked = false;   // 2026-11-24: the section ticks too
        Notify();
    }
    public static void StepAll(int presses) { StepAll(presses, null); }
    // 2026-11-08: only the sliders in the part clicked last (Nudge.Allows), when the window says which
    public static void StepAll(int presses, Func<Control, bool> allow) {
        foreach (var t in All) if (t.Ticked && (allow == null || !(t is Control) || allow((Control)t))) t.Step(presses);
    }
    // 2026-11-24: the sliders whose box keys are given (Nudge.Filter: the ticked ones in the ticked sections, or the one the left
    // stick selected), ticked or not
    public static void StepKeys(int presses, HashSet<string> keys) {
        foreach (var t in All) { CheckBox b; if (boxes.TryGetValue(t, out b) && keys.Contains(b.Tag as string ?? "")) t.Step(presses); }
    }
    // every ticked slider's key (for Nudge.Filter)
    public static HashSet<string> TickedKeys() {
        var keys = new HashSet<string>();
        foreach (var kv in boxes) if (kv.Value.Checked && kv.Value.Tag is string) keys.Add((string)kv.Value.Tag);
        return keys;
    }
    // the boxes name their parts here (a zone's box, a box of values); the window passes them to its Nudge
    public static Action<Control, string> NameSection;

    // the tick box beside a slider (drawn big: SkinCheckBox with no text, Skin.cs)
    public static CheckBox Box(ITickable owner) {
        var b = new SkinCheckBox { Width = 28, Height = 28, Dock = DockStyle.Left, Margin = new Padding(0, 6, 4, 0), Tag = "tick:" + (++next) };
        b.CheckedChanged += (s, e) => { if (owner.Ticked != b.Checked) owner.Ticked = b.Checked; Notify(); };
        boxes[owner] = b;
        return b;
    }

    // 2026-11-24: a part's name for the Nudge and its SECTION tick at the left of its head line (the head's text moves over)
    public static void Section(Control part, Control head, string name) {
        if (NameSection != null) NameSection(part, name);
        var t = new SkinCheckBox { Width = 28, Height = 28, Location = new Point(0, 0), Tag = "section:" + name };
        t.CheckedChanged += (s, e) => Notify();
        sectionTicks.Add(t);
        head.Padding = new Padding(36, head.Padding.Top, head.Padding.Right, head.Padding.Bottom);
        head.Controls.Add(t);
    }
}
