"""Kills the real Orgodesk.exe --orgo-command mid-apply at random moments, against a scratch data folder, and checks
layout.json after every kill. Never touches the real %APPDATA%; Debug build (no licence check)."""
import json, os, random, shutil, subprocess, sys, time

EXE = r"E:\AI Programs\Toolchain\orgo-suite\apps\orgodesk\src\Orgodesk.App\bin\Debug\net10.0-windows\Orgodesk.exe"
DATA = r"E:\AI Programs\_caches\tmp\claude\f307f83841e70014\18313082-1144-40f9-a327-7111ee35ad1b\scratchpad\kill-data"
shutil.rmtree(DATA, ignore_errors=True)

def ask(req, kill_after=None):
    p = subprocess.Popen([EXE, "--orgo-command", "--data", DATA], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    p.stdin.write(json.dumps(req).encode()); p.stdin.close()
    if kill_after is None:
        out = p.stdout.read(); p.wait()
        return json.loads(out), False
    time.sleep(kill_after)
    killed = p.poll() is None
    if killed:
        p.kill()
    out = p.stdout.read(); p.wait()
    try: return json.loads(out), killed
    except Exception: return None, killed

random.seed(7)
bad, killed_n, done_n, n = [], 0, 0, 80
for i in range(n):
    pv, _ = ask({"protocol": 1, "verb": "preview", "action": "new_group", "args": {"name": f"G{i}"}})
    if not pv or not pv.get("ok"):
        bad.append(("preview failed", i, pv)); continue
    delay = random.uniform(0.25, 0.55)
    ap, killed = ask({"protocol": 1, "verb": "apply", "previewId": pv["result"]["previewId"]}, kill_after=delay)
    killed_n += killed
    done_n += (not killed)
    path = os.path.join(DATA, "layout.json")
    if os.path.exists(path):
        try: lay = json.load(open(path, encoding="utf-8"))
        except Exception as e: bad.append(("layout.json damaged after kill", i, str(e)))
        else:
            if not lay["pages"] or any("title" not in f for pg in lay["pages"] for f in pg["fences"]):
                bad.append(("layout.json incomplete", i))
    # every snapshot must parse too
    snaps = os.path.join(DATA, "snapshots")
    if os.path.isdir(snaps):
        for f in os.listdir(snaps):
            try: json.load(open(os.path.join(snaps, f), encoding="utf-8"))
            except Exception as e: bad.append(("snapshot damaged", i, f, str(e)))
    st, _ = ask({"protocol": 1, "verb": "status"})
    if not st or not st.get("ok") or st["result"]["tool"]["layout"] != "ok":
        bad.append(("status not ok after kill", i, st))
# one more full apply must still work after all the kills (an abandoned lock is taken over)
pv, _ = ask({"protocol": 1, "verb": "preview", "action": "new_group", "args": {"name": "Last"}})
ap, _ = ask({"protocol": 1, "verb": "apply", "previewId": pv["result"]["previewId"]})
print("rounds", n, "killed mid-run", killed_n, "finished before kill", done_n)
print("final apply ok:", ap and ap.get("ok"))
print("groups on disk:", len(json.load(open(os.path.join(DATA, "layout.json"), encoding="utf-8"))["pages"][0]["fences"]))
print("tmp files left:", [f for f in os.listdir(DATA) if f.endswith(".tmp")])
print("problems:", bad)
