"""Summarize InputRecorder/record.log: map each vendor bitmap bit to the key/consumer/mouse signal that follows it,
and measure vendor-report -> LL-hook latency."""
import os, re, sys, datetime, collections

path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "InputRecorder", "record.log")
ts = lambda s: datetime.datetime.strptime(s, "%H:%M:%S.%f")

prev = bytes(24)
pending = []            # vendor bits that went down, not yet attributed: (time, code)
table = collections.defaultdict(collections.Counter)   # vendor code -> Counter(signal)
unattributed = collections.Counter()
lat = []
last_vendor_down_t = None
hook_first = 0

for line in open(path, encoding="utf-8"):
    parts = line.split()
    if len(parts) < 3:
        continue
    t = ts(parts[0])
    if "PAD VENDOR" in line:
        cur = bytes.fromhex(parts[3])[1:]
        for i in range(max(len(cur), len(prev))):
            a = cur[i] if i < len(cur) else 0
            b = prev[i] if i < len(prev) else 0
            for bit in range(8):
                if (a >> bit) & 1 and not (b >> bit) & 1:
                    pending.append((t, i * 8 + bit))
                    last_vendor_down_t = t
        prev = cur
        continue
    sig = None
    if "PAD KBD" in line and "DOWN" in line:
        sig = parts[3] + " " + parts[4]
    elif "PAD HID" in line:
        sig = "hid " + parts[3]
    elif "PAD MOUSE" in line:
        sig = "mouse " + " ".join(parts[3:7])
    elif "PAD HOOK" in line and "DOWN" in line and last_vendor_down_t:
        lat.append((t - last_vendor_down_t).total_seconds() * 1000)
    elif "HOOK-FIRST" in line:
        hook_first += 1
    if not sig:
        continue
    pending = [(pt, c) for pt, c in pending if (t - pt).total_seconds() < 0.05]
    if pending:
        pt, code = pending.pop(0)
        table[code][sig] += 1
    else:
        unattributed[sig] += 1

print("vendor bit -> signals")
for code in sorted(table):
    print(f"  vendor:{code:3d} (byte{code // 8 + 1} bit{code % 8})  {dict(table[code])}")
print("unattributed:", dict(unattributed))
if lat:
    lat.sort()
    print(f"vendor->hook latency ms: n={len(lat)} min={lat[0]:.1f} median={lat[len(lat)//2]:.1f} max={lat[-1]:.1f}")
print("HOOK-FIRST lines (hw keys where raw input was not yet available):", hook_first)
