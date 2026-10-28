// Ticks.cs -- the tick boxes on Grip Zones' sliders (2026-10-27, Tefa: "add tick boxes to the grab zones and add the same button
// bindings as hand poser"). Every slider with a tick box registers here; while any is ticked the controller buttons (Nudge.cs,
// through Window View) step the ticked ones: X one step down, Y five down, A one up, B five up (one step = one press of its - / +).
using System;
using System.Collections.Generic;
using System.Windows.Forms;

interface ITickable {
    bool Ticked { get; set; }
    void Step(int presses);   // as if its - (negative) or + (positive) button were pressed this many times
}

static class Ticks {
    public static readonly List<ITickable> All = new List<ITickable>();
    public static event Action Changed;
    public static int Count { get { int n = 0; foreach (var t in All) if (t.Ticked) n++; return n; } }
    public static void Notify() { if (Changed != null) Changed(); }
    public static void ClearAll() { foreach (var t in All) t.Ticked = false; Notify(); }
    public static void StepAll(int presses) { foreach (var t in All) if (t.Ticked) t.Step(presses); }

    // the tick box beside a slider (drawn big: SkinCheckBox with no text, Skin.cs)
    public static CheckBox Box(ITickable owner) {
        var b = new SkinCheckBox { Width = 28, Height = 28, Dock = DockStyle.Left, Margin = new Padding(0, 6, 4, 0) };
        b.CheckedChanged += (s, e) => { if (owner.Ticked != b.Checked) owner.Ticked = b.Checked; Notify(); };
        return b;
    }
}
