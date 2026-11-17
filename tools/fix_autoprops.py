"""Rewrite `partial T Name { get; set; } [= init];` auto properties in impl files into field-backed ones
(partial properties cannot be auto-implemented in C# 13)."""
import re, sys, pathlib
PAT = re.compile(r"^(\s*)((?:public|private|internal|protected)\s+)((?:static\s+)?)partial\s+([\w.<>\[\]]+)\s+(\w+)\s*\{\s*get;\s*set;\s*\}\s*(?:[ \t]*=\s*([^;]+);)?", re.M)
for f in sys.argv[1:]:
    p = pathlib.Path(f); s = p.read_text()
    def rep(m):
        ind, acc, st, typ, name, init = m.groups()
        fld = f"__p_{name}"
        init_s = f" = {init.strip()}" if init else ""
        return f"{ind}private {st}{typ} {fld}{init_s};\n{ind}{acc}{st}partial {typ} {name} {{ get => {fld}; set => {fld} = value; }}"
    s2 = PAT.sub(rep, s)
    if s2 != s: p.write_text(s2); print("fixed", f)
