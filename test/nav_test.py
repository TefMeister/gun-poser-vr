r"""nav_test.py -- Hand Poser's controller scheme in Gun Poser (2026-11-24): LT picks a section, the left stick walks its sliders,
the right stick tunes, A ticks, B unticks the section, X undo / Y redo on the last slider only, a held grip stops everything.
Against a pretend Village (a test folder with a fresh zones_state.json) and a pretend Window View block (the controllers), in
build\navtest only: no real game, no real mouse (clicks are posted to the window), no real settings (--data).

    python test\nav_test.py            -> PASS / FAIL lines, build\navtest\*.png

Needs Window View and the real Gun Poser closed (the controller block and the window title are the same)."""
import ctypes, json, mmap, os, shutil, struct, subprocess, sys, threading, time
from ctypes import wintypes

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
T = os.path.join(ROOT, "build", "navtest")
TITLE = "Gun Poser"
user32 = ctypes.windll.user32
CTRL_MODE, STICK_Y, SQUEEZE_R, BUTTONS, LSTICK_Y, STICK_X, LSTICK_X = 536, 580, 576, 584, 624, 636, 640
B_A, B_Y, B_B, B_X = 1, 2, 4, 8
results = []


def check(name, ok, info=""):
    results.append(ok)
    print(("PASS " if ok else "FAIL ") + name + (" (" + info + ")" if info else ""), flush=True)


def put_f(view, off, v): view[off:off + 4] = struct.pack("<f", v)
def put_i(view, off, v): view[off:off + 4] = struct.pack("<i", v)


def pulse(view, off, value, hold=0.12, as_int=False):
    (put_i if as_int else put_f)(view, off, value)
    time.sleep(hold)
    (put_i if as_int else put_f)(view, off, 0)
    time.sleep(0.3)


def children(hwnd):
    out = []
    proc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def cb(h, _):
        n = user32.GetWindowTextLengthW(h)
        buf = ctypes.create_unicode_buffer(n + 1)
        user32.GetWindowTextW(h, buf, n + 1)
        out.append((h, buf.value))
        return True
    user32.EnumChildWindows(hwnd, proc(cb), 0)
    return out


def post_click(h, middle=False):
    lp = (8 << 16) | 120   # 120 px in: past the section tick at the head's left
    down, up, mk = (0x207, 0x208, 0x10) if middle else (0x201, 0x202, 1)
    user32.PostMessageW(h, down, mk, lp)
    time.sleep(0.05)
    user32.PostMessageW(h, up, 0, lp)
    time.sleep(0.4)


def grab(name):
    g = os.path.join(os.path.dirname(ROOT), "hand-poser-vr", "test", "grab_window.py")
    if os.path.exists(g):
        subprocess.run([sys.executable, g, TITLE, os.path.join(T, name)], check=False)


DATA = os.path.join(T, "FakeVillage", "reframework", "data", "visceral_re8")
STATE = {"t": 1, "gun": 3042, "zones": []}
alive = True


def keep_state():   # the pretend game script: zones_state.json kept fresh (older than 3 s = the game is gone)
    while alive:
        try:
            with open(os.path.join(DATA, "zones_state.json"), "w") as f:
                json.dump(STATE, f)
        except OSError:
            pass
        time.sleep(0.5)


def zone(axis):
    time.sleep(0.4)   # the window saves 60 ms after a change
    try:
        with open(os.path.join(DATA, "zones.json"), encoding="utf-8") as f:
            v = json.load(f).get("3042", {}).get("aim", {}).get(axis)
    except (OSError, ValueError):
        return 0.0
    return round(v or 0.0, 4)


def main():
    global alive
    if user32.FindWindowW(None, TITLE):
        sys.exit("close the real Gun Poser first")
    shutil.rmtree(T, ignore_errors=True)
    os.makedirs(DATA)
    open(os.path.join(T, "FakeVillage", "re8.exe"), "w").close()
    os.makedirs(os.path.join(T, "appdata"))
    with open(os.path.join(T, "appdata", "settings.txt"), "w") as f:
        f.write("")
    threading.Thread(target=keep_state, daemon=True).start()
    wv = mmap.mmap(-1, 644, tagname=r"Local\FlatToVR_WindowView_v2")
    put_i(wv, CTRL_MODE, 1)
    app = subprocess.Popen([os.path.join(ROOT, "dist", "GunPoser.exe"), "--game", os.path.join(T, "FakeVillage"), "--data", os.path.join(T, "appdata")])
    try:
        time.sleep(4)
        hwnd = user32.FindWindowW(None, TITLE)
        check("the window is up", bool(hwnd))
        kids = children(hwnd)
        head = [h for h, t in kids if t.startswith("THE GUN IN THE RIGHT HAND")]
        check("the gun's box is there", bool(head), ", ".join(sorted(set(t for _, t in kids if t))[:12]))
        check("one undo pair on the window", sum(1 for _, t in kids if t == "↶") == 1 and sum(1 for _, t in kids if t == "↷") == 1)
        if not head:
            return
        grab("1-open.png")
        post_click(head[0], middle=True)   # LT: the gun's section ticked, its first slider selected
        grab("2-section.png")
        pulse(wv, STICK_X, 1.0)            # RS right: the selected slider (aim down / up) one step (1 degree)
        p1 = zone("pitch")
        check("LT picks the section and its first slider; RS steps it", p1 == 1.0, "pitch=%s" % p1)
        pulse(wv, BUTTONS, B_X, as_int=True)   # X: undo
        check("X undoes it", zone("pitch") == 0.0, "pitch=%s" % zone("pitch"))
        pulse(wv, BUTTONS, B_Y, as_int=True)   # Y: redo
        check("Y redoes it", zone("pitch") == 1.0, "pitch=%s" % zone("pitch"))
        pulse(wv, BUTTONS, B_X, as_int=True)
        pulse(wv, LSTICK_Y, -1.0)          # LS down: the next slider (aim left / right)
        pulse(wv, STICK_X, 1.0)
        y1, p2 = zone("yaw"), zone("pitch")
        check("LS down walks to the next slider, RS steps only that one", y1 == -1.0 and p2 == 0.0, "yaw=%s pitch=%s" % (y1, p2))
        pulse(wv, BUTTONS, B_A, as_int=True)   # A: tick aim left / right
        pulse(wv, LSTICK_Y, 1.0)               # LS up: aim down / up again
        pulse(wv, BUTTONS, B_A, as_int=True)   # A: tick it too
        grab("3-two-ticked.png")
        pulse(wv, STICK_X, 1.0)                # both ticked sliders step
        y2, p3 = zone("yaw"), zone("pitch")
        check("A ticks two sliders; RS steps both", y2 == -2.0 and p3 == 1.0, "yaw=%s pitch=%s" % (y2, p3))
        pulse(wv, BUTTONS, B_X, as_int=True)   # X: undo the two together
        check("X undoes both (one press moved them together)", zone("yaw") == -1.0 and zone("pitch") == 0.0, "yaw=%s pitch=%s" % (zone("yaw"), zone("pitch")))
        pulse(wv, BUTTONS, B_B, as_int=True)   # B: the section's slider ticks off
        pulse(wv, STICK_X, 1.0)                # now only the selected one (aim down / up) steps
        y3, p4 = zone("yaw"), zone("pitch")
        check("B unticks the section's sliders: RS steps the selected one only", y3 == -1.0 and p4 == 1.0, "yaw=%s pitch=%s" % (y3, p4))
        put_f(wv, SQUEEZE_R, 1.0)              # the right grip held (a window carried in Window View) ...
        time.sleep(0.2)
        pulse(wv, STICK_X, 1.0)                # ... the right stick does nothing
        pulse(wv, BUTTONS, B_X, as_int=True)   # ... nor X
        put_f(wv, SQUEEZE_R, 0.0)
        check("a grip held: no button or stick moves a slider", zone("pitch") == 1.0 and zone("yaw") == -1.0, "yaw=%s pitch=%s" % (zone("yaw"), zone("pitch")))
        grab("4-end.png")
    finally:
        h = user32.FindWindowW(None, TITLE)
        if h:
            user32.PostMessageW(h, 0x10, 0, 0)   # WM_CLOSE: the window's own close
        time.sleep(2)
        if app.poll() is None:
            app.terminate()
        alive = False
        wv.close()
    print("ALL PASS" if results and all(results) else "SOME FAILED", flush=True)


if __name__ == "__main__":
    main()
