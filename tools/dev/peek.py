import sys, UnityPy, json
env = UnityPy.load(sys.argv[1]); want = sys.argv[2]; k = int(sys.argv[3]) if len(sys.argv) > 3 else 1
def short(v, depth=0):
    if isinstance(v, dict): return {a: short(b, depth+1) for a, b in v.items()}
    if isinstance(v, list): return [short(x, depth+1) for x in v[:4]] + ([f"...{len(v)}"] if len(v) > 4 else [])
    if isinstance(v, (bytes, bytearray)): return f"<{len(v)} bytes>"
    if isinstance(v, str) and len(v) > 120: return v[:120] + "..."
    return v
for obj in env.objects:
    if obj.type.name == want:
        print(json.dumps(short(obj.read_typetree()), indent=1, default=str)[:4000]); k -= 1
        if k == 0: break
