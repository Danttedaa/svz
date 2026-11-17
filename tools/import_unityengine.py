"""One-time import of the decompiled Unity 3.4 UnityEngine.dll into the Godot shim.

- every class/struct becomes `partial`
- every `extern` icall member becomes a `partial` member (C# 13 partial properties/methods)
- extern constructors get an empty body that calls the optional `__Init()` partial hook

The bodies are written by hand in godot/src/Engine/Impl or generated as
NotImplemented stubs by tools/gen_stubs.py.

usage: python -I import_unityengine.py <ilspy UnityEngine dir> <out dir>
"""
import re, sys, pathlib

src, out = map(pathlib.Path, sys.argv[1:3])
out.mkdir(parents=True, exist_ok=True)

ATTR = re.compile(r"^\s*\[(MethodImpl\(MethodImplOptions\.InternalCall\)|WrapperlessIcall)\]\s*\n", re.M)
TYPEDECL = re.compile(r"^((?:public|internal|private|protected|sealed|abstract|static|unsafe|\s)*)\b(class|struct)\b", re.M)
CTOR = re.compile(r"^(\s*)((?:public|internal|private|protected)\s+)extern\s+(\w+)\(([^)]*)\);", re.M)

n_files = 0
for f in sorted(src.rglob("*.cs")):
    rel = f.relative_to(src)
    if rel.parts[0] in ("Properties", "obj"):
        continue
    text = f.read_text(encoding="utf-8-sig")
    text = ATTR.sub("", text)
    text = CTOR.sub(lambda m: f"{m[1]}{m[2]}{m[3]}({m[4]}) {{ __Init(); }}\n{m[1]}partial void __Init();", text)
    # `public static extern T X(...)` / `public extern T X { get; set; }` -> partial
    text = re.sub(r"\bextern\b(?=[^;{\n]*(\(|\n\s*\{))", "partial", text)
    text = re.sub(r"\bstatic partial\b", "static partial", text)

    def mk_partial(m):
        mods = m[1]
        return m[0] if "partial" in mods else f"{mods}partial {m[2]}"
    text = TYPEDECL.sub(mk_partial, text)
    # C# requires `partial` right before class/struct, after other modifiers (already true)
    dst = out / rel
    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_text(text, encoding="utf-8")
    n_files += 1
print("imported", n_files, "files")
