"""Independent suppression check.

Run InputRecorder BEFORE MacroHub: low-level hooks are called newest-first, so the recorder's (older) hook only
sees keystrokes that MacroHub let through. For every physical press in the vendor bitmap, a hardware key event
reaching the recorder within WINDOW ms means the pad's original key leaked.
Only meaningful while the active layer blocks every control (e.g. "diag").
"""
import os, sys, datetime

path = sys.argv[1] if len(sys.argv) > 1 and sys.argv[1] else os.path.join(os.path.dirname(os.path.abspath(__file__)), "InputRecorder", "record.log")
since = sys.argv[2] if len(sys.argv) > 2 else None   # optional HH:MM:SS start time
WINDOW = 40
ts = lambda s: datetime.datetime.strptime(s, "%H:%M:%S.%f")

presses, hooks = [], []
prev = bytes(24)
for line in open(path, encoding="utf-8"):
    p = line.split()
    if len(p) < 2 or (since and p[0] < since):
        continue
    t = ts(p[0])
    if "PAD VENDOR" in line:
        cur = bytes.fromhex(p[3])[1:25]
        for i in range(24):
            new = cur[i] & ~prev[i]
            for b in range(8):
                if new >> b & 1:
                    presses.append((t, i * 8 + b))
        prev = cur
    elif "HOOK-FIRST" in line or ("PAD HOOK" in line and "DOWN" in line) or ("PAD KBD" in line and "DOWN" in line):
        # PAD KBD = Raw Input from the pad, which Windows only generates when no hook blocked the key
        hooks.append(t)

leaks = []
for t, code in presses:
    if any(0 <= (h - t).total_seconds() * 1000 <= WINDOW for h in hooks):
        leaks.append((t.strftime("%H:%M:%S.%f")[:-3], code))
print(f"physical presses: {len(presses)}  leaked to older hook: {len(leaks)}")
for l in leaks[:20]:
    print("  leak", l)
