import sys, collections, UnityPy
env = UnityPy.load(sys.argv[1])
c = collections.Counter(); ok = collections.Counter(); err = {}
for obj in env.objects:
    t = obj.type.name; c[t] += 1
    try:
        d = obj.read(); ok[t] += 1
    except Exception as e:
        err.setdefault(t, repr(e)[:150])
for t, n in c.most_common(40): print(f"{t:25s} {n:6d} ok={ok[t]}")
for t, e in list(err.items())[:15]: print("ERR", t, e)
