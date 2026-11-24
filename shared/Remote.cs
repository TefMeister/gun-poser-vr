// Remote.cs -- a zone set from outside the window, as if its sliders had been moved (2026-11-23).
// Tefa 2026-11-23: "please use all the poser family tools for all this, and tweak the tools themselves as you please": a session
// with robot hands (nobody at the mouse) had been writing zones.json itself, around the window. Now it writes ONE small file
// and the window does the rest as for a slider: the zone moves in the game at once, the sliders show it, the file is saved.
//
//   <game>\reframework\data\visceral_re8\zones_remote.json
//       {"seq": 7, "zone": "slide", "group": "this", "forward": 4.0, "up": 2.5, "right": -1.0, "size": 9.0}
//       seq      a NEW number each time            zone   mag | well | slide | slide-left | pump | bolt | grip | ...
//       group    "this" (the gun in hand, the default) or "all" (the default every gun uses)
//       forward, up, right, size   centimetres, the sliders' own meaning; the ones left out stay as they are
//   <game>\reframework\data\visceral_re8\zones_remote_done.json   {"seq": 7, "ok": true, "group": "3048", "note": ""}
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

partial class ZonesUi {
    double remoteSeq = -1;
    DateTime remoteWrote = DateTime.MinValue;

    void RemotePoll() {
        if (file.DataDir == null || saveTimer.Enabled) return;   // never across a save of our own that is still pending
        string path = Path.Combine(file.DataDir, "zones_remote.json");
        DateTime wrote;
        try { if (!File.Exists(path)) return; wrote = File.GetLastWriteTime(path); } catch (IOException) { return; }
        if (wrote == remoteWrote) return;
        Dictionary<string, object> o;
        try { o = Json.Obj(Json.Parse(File.ReadAllText(path))); } catch (Exception) { return; }   // half-written: next time
        remoteWrote = wrote;
        if (o == null) return;
        object v; double seq = o.TryGetValue("seq", out v) ? Json.Num(v, -1) : -1;
        if (seq == remoteSeq) return;
        remoteSeq = seq;
        string zone = o.TryGetValue("zone", out v) ? Json.Str(v) : "", note = "", group = "";
        bool all = o.TryGetValue("group", out v) && Json.Str(v) == "all", ok = false;
        if (zone == "") note = "no zone named";
        else if (!all && (live == null || live.Gun <= 0)) note = "no gun in hand (group \"all\" sets the default)";
        else {
            group = all ? "default" : live.Gun.ToString();
            var data = file.Load();
            Func<string, double> cur = a => { double n = ZoneFile.Effective(data, group, zone, a); return double.IsNaN(n) ? 0 : n; };
            double x = cur("x"), y = cur("y"), z = cur("z");
            bool moved = false;
            if (o.TryGetValue("right", out v) && v is double) { x = -(double)v / 100.0; moved = true; }
            if (o.TryGetValue("up", out v) && v is double) { y = (double)v / 100.0; moved = true; }
            if (o.TryGetValue("forward", out v) && v is double) { z = (double)v / 100.0; moved = true; }
            if (moved) ZoneFile.SetOffset(data, group, zone, x, y, z);
            bool sized = o.TryGetValue("size", out v) && v is double;
            if (sized) ZoneFile.SetRadius(data, group, zone, (double)v / 100.0);
            if (!moved && !sized) note = "wants forward / up / right / size (cm)";
            else {
                try { file.Save(data); ok = true; RefreshFromFile(); }
                catch (Exception ex) { note = "could not save: " + ex.Message; }
            }
        }
        string done = "{\"seq\": " + seq.ToString(CultureInfo.InvariantCulture) + ", \"ok\": " + (ok ? "true" : "false") +
                      ", \"group\": " + Json.Write(group) + ", \"note\": " + Json.Write(note) + "}";
        try { File.WriteAllText(Path.Combine(file.DataDir, "zones_remote_done.json"), done); } catch (IOException) { }
    }
}
