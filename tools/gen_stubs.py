"""Generate NotImplemented bodies for every partial icall member of the UnityEngine shim
that has no hand-written implementation in godot/src/Engine/Impl.

usage: python -I gen_stubs.py <shim dir> <impl dir> <out file> [--report]
Relies on ILSpy formatting: one declaration per line, tab indentation.
"""
import re, sys, pathlib, collections

TYPE = re.compile(r"^(\t*)(?:[\w ]*?\s)?(class|struct)\s+(\w+(?:<[^>]+>)?)")
NS = re.compile(r"^namespace\s+([\w.]+)")
PROP = re.compile(r"^\t*((?:\w+\s+)*?)partial\s+([\w.<>\[\], ]+?)\s+(\w+)\s*$")
METH = re.compile(r"^\t*((?:\w+\s+)*?)partial\s+([\w.<>\[\], ]+?)\s+(\w+)\s*\((.*)\)\s*;\s*$")
IMPL = re.compile(r"\bpartial\s+[\w.<>\[\], ]+?\s+(\w+)\s*(\(([^)]*)\)|\{|$|=>)")


# classes whose icalls silently do nothing (platform services the offline port does not have)
INERT = set("""AndroidJNI AndroidJNIHelper AndroidInput Network NetworkView NetworkViewID NetworkPlayer MasterServer
BitStream Ping Gizmos Profiler Caching AssetBundle AssetBundleCreateRequest iPhoneKeyboard iPhoneUtils iPhoneInput
Gyroscope TerrainData TerrainRenderer TreeRenderer DetailRenderer WindZone Tree Terrain TerrainCollider
OcclusionArea LensFlare Projector ProceduralMaterial Reproduction UnityLogWriter HostData""".split())


def split_params(p):
    out, depth, cur = [], 0, ""
    for ch in p:
        if ch in "<([": depth += 1
        elif ch in ">)]": depth -= 1
        if ch == "," and depth == 0: out.append(cur); cur = ""
        else: cur += ch
    if cur.strip(): out.append(cur)
    return out


def nparams(p):
    p = p.strip()
    if not p:
        return 0
    depth, n = 0, 1
    for ch in p:
        if ch in "<([":
            depth += 1
        elif ch in ">)]":
            depth -= 1
        elif ch == "," and depth == 0:
            n += 1
    return n


def scan(path, decls):
    """yield (ns, type_path tuple of (kw,name), line_index, line) for lines inside types"""
    ns, stack = "", []
    lines = path.read_text(encoding="utf-8").split("\n")
    for i, line in enumerate(lines):
        m = NS.match(line)
        if m:
            ns = m[1]
            continue
        m = TYPE.match(line)
        if m and not line.strip().startswith("//") and "}" not in line:
            ind = len(m[1])
            while stack and stack[-1][0] >= ind:
                stack.pop()
            stack.append((ind, m[2], m[3]))
            continue
        m = re.match(r"^(\t*)\}\s*;?\s*$", line)
        if m and stack and len(m[1]) == stack[-1][0]:
            stack.pop()
            continue
        yield ns, tuple((k, n) for _, k, n in stack), i, lines


def key(tpath, name, n):
    return ".".join(t[1] for t in tpath) + "." + name + "/" + str(n)


def main():
    shim, impl, outf = map(pathlib.Path, sys.argv[1:4])
    implemented = set()
    for f in impl.rglob("*.cs"):
        if f.name.endswith(".g.cs"):
            continue
        for ns, tpath, i, lines in scan(f, None):
            m = IMPL.search(lines[i])
            if m and not lines[i].lstrip().startswith("//"):
                n = nparams(m[3]) if m[2] and m[2].startswith("(") else -1
                implemented.add(key(tpath, m[1], n))

    stubs = collections.defaultdict(list)  # (ns, tpath) -> lines
    total = done = 0
    for f in sorted(shim.rglob("*.cs")):
        for ns, tpath, i, lines in scan(f, None):
            line = lines[i]
            if "partial" not in line or not tpath or "partial void __Init" in line:
                continue
            if re.search(r"\b(class|struct)\b", line):
                continue
            m = METH.match(line)
            if m:
                mods, ret, name, params = m[1], m[2], m[3], m[4]
                total += 1
                if key(tpath, name, nparams(params)) in implemented:
                    done += 1
                    continue
                if tpath[-1][1] in INERT:
                    outs = [x.strip().split()[-1] for x in split_params(params) if x.strip().startswith("out ")]
                    ret_s = "" if ret.strip() == "void" else "return default;"
                    body = "{ " + " ".join(f"{o} = default;" for o in outs) + " " + ret_s + " }"
                else:
                    body = f"=> throw new global::System.NotImplementedException(\"icall {tpath[-1][1]}.{name}\");"
                stubs[(ns, tpath)].append(f"{mods}partial {ret} {name}({params}) {body}")
                continue
            m = PROP.match(line)
            if m and lines[i + 1].strip() == "{":
                mods, typ, name = m[1], m[2], m[3]
                acc, j = [], i + 2
                while lines[j].strip() != "}":
                    acc.append(lines[j].strip().rstrip(";"))
                    j += 1
                total += 1
                if key(tpath, name, -1) in implemented:
                    done += 1
                    continue
                t = "throw new global::System.NotImplementedException(\"icall %s.%s\")" % (tpath[-1][1], name)
                if tpath[-1][1] in INERT:
                    parts = " ".join(("get => default;" if a == "get" else "set { }") for a in acc)
                else:
                    parts = " ".join(f"{a} => {t};" for a in acc)
                stubs[(ns, tpath)].append(f"{mods}partial {typ} {name} {{ {parts} }}")

    out = ["// <auto-generated> by tools/gen_stubs.py - do not edit", "#pragma warning disable CS0162, CS8618",
           "using System;", "using System.Collections;", "using System.Runtime.InteropServices;", "using UnityEngine;", "using UnityEngineInternal;"]
    for (ns, tpath), members in sorted(stubs.items()):
        out.append(f"namespace {ns}\n{{")
        for kw, name in tpath:
            out.append(f"partial {kw} {name}\n{{")
        out += ["\t" + m for m in members]
        out.append("}" * len(tpath) + "\n}")
    outf.write_text("\n".join(out) + "\n", encoding="utf-8")
    print(f"icalls: {total}, implemented: {done}, stubbed: {total - done}")
    if "--report" in sys.argv:
        for (ns, tpath), members in sorted(stubs.items()):
            print(tpath[-1][1], len(members))


main()
