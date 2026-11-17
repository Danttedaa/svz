import sys, collections, UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
env = UnityPy.load(sys.argv[1])
g = TypeTreeGenerator("3.4.2f3"); g.load_local_dll_folder(sys.argv[1] + "/Managed")
env.typetree_generator = g
ok = collections.Counter(); bad = collections.Counter(); ex = {}
for obj in env.objects:
    if obj.type.name != "MonoBehaviour": continue
    try:
        d = obj.read_typetree(); ok[d.get("m_Script") and 1] += 1
    except Exception as e:
        bad[type(e).__name__] += 1; ex.setdefault(type(e).__name__, repr(e)[:200])
print("ok", sum(ok.values()), "bad", dict(bad)); print(ex)
