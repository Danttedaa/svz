import sys, collections, importlib.util
spec = importlib.util.spec_from_file_location("ex", sys.argv[2]); 
sys.argv = [sys.argv[0], sys.argv[1], "/tmp/none"]
ex = importlib.util.module_from_spec(spec); spec.loader.exec_module(ex)
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
env = UnityPy.load(sys.argv[1]); g = TypeTreeGenerator("3.4.2f3"); g.load_local_dll_folder(sys.argv[1] + "/Managed"); env.typetree_generator = g
ex.patch_enabled_align()
ok = bad = nulls = 0; errs = collections.Counter()
for o in env.objects:
    if o.type.name != "MonoBehaviour": continue
    try:
        tt = o.read_typetree(); ok += 1
        s = tt["m_Script"]
        if not (0 <= s["m_FileID"] < 500): bad += 1
        if s["m_PathID"] == 0: nulls += 1
    except Exception as e:
        errs[repr(e)[:120]] += 1
print("ok", ok, "badscript", bad, "nullscript", nulls, errs.most_common(5))
