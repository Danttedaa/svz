"""Export the Unity 3.4 game data into the formats the Godot runtime loads.

  godot/data/files.json             global table of serialized files (index = file key)
  godot/data/sf/<key>.json.gz       every object of that file: {pathID: {"c": class, "d": typetree}}
  godot/data/mesh/<key>_<pid>.bin   meshes (see MeshBin in the runtime)
  godot/tex/<key>/<pid>.png         textures (Godot imports them, VRAM compressed)
  godot/audio/<key>/<pid>.<ext>     audio clips in their original container (wav/ogg/mp3)

PPtrs become [fileKey, pathID] (fileKey -1 = null, -2 = unknown external).
Byte arrays become {"$b64": ...}; big payloads (texture/audio/mesh data) are written to files instead.

usage: python -I export_assets.py <Data dir> <godot dir> [-j N]
"""
import sys, os, json, gzip, base64, struct, pathlib, multiprocessing as mp

DATA, GODOT = map(pathlib.Path, sys.argv[1:3])
JOBS = int(sys.argv[sys.argv.index("-j") + 1]) if "-j" in sys.argv else max(1, (os.cpu_count() or 2) - 1)
SKIP = {"Managed", "settings.xml", "splash.png"}


def file_table():
    names = sorted(p.name for p in DATA.iterdir() if p.is_file() and p.name not in SKIP)
    return names


def norm_external(path):
    p = path.replace("\\", "/").split("/")[-1].lower()
    return p


def pptr_fix(value, externals, keys):
    """recursively replace PPtr dicts and bytes"""
    if isinstance(value, dict):
        if set(value.keys()) == {"m_FileID", "m_PathID"}:
            fid, pid = value["m_FileID"], value["m_PathID"]
            if pid == 0:
                return [-1, 0]
            if fid == 0:
                return [keys["__self__"], pid]
            ext = externals[fid - 1] if 0 < fid <= len(externals) else None
            if ext is None:
                return [-2, pid]
            k = keys.get(ext)
            if k is None:
                return ["builtin:" + ext, pid]
            return [k, pid]
        return {k: pptr_fix(v, externals, keys) for k, v in value.items()}
    if isinstance(value, tuple):
        value = list(value)
    if isinstance(value, list):
        if value and isinstance(value[0], int) and len(value) > 64 and all(isinstance(x, int) and 0 <= x < 256 for x in value[:64]):
            try:
                return {"$b64": base64.b64encode(bytes(value)).decode()}
            except ValueError:
                pass
        return [pptr_fix(v, externals, keys) for v in value]
    if isinstance(value, (bytes, bytearray)):
        return {"$b64": base64.b64encode(value).decode()}
    if isinstance(value, float):
        if value != value:
            return 0.0
        if value in (float("inf"), float("-inf")):   # stepped animation tangents: keep as +-3.4e38
            return 3.4e38 if value > 0 else -3.4e38
    return value


def tri_list(sub, idx16):
    first = sub["firstByte"] // 2
    seq = idx16[first:first + sub["indexCount"]]
    if not sub.get("isTriStrip"):
        return list(seq)
    out = []
    for i in range(len(seq) - 2):
        a, b, c = seq[i], seq[i + 1], seq[i + 2]
        if a == b or b == c or a == c:
            continue
        out += (a, b, c) if i % 2 == 0 else (b, a, c)
    return out


def write_mesh(d, path):
    """MeshBin v1: little endian
    magic 'SVZM', u32 nverts, u32 flags(bit0 normals,1 uv0,2 uv1,3 colors,4 tangents,5 skin), u32 nsub, u32 nbind
    positions f32*3, normals f32*3, uv0 f32*2, uv1 f32*2, colors u8*4 (rgba), tangents f32*4,
    skin: per vertex 4x(i32 bone, f32 weight); bindposes f32*16 (row major m00 m01 ..)
    per submesh: u32 nidx, u32*nidx (triangle list)"""
    V = d["m_Vertices"]; n = len(V)
    def arr(key, comps, size):
        a = d.get(key) or []
        return a if len(a) == n else None
    N, U0, U1, T = arr("m_Normals", 3, 4), arr("m_UV", 2, 4), arr("m_UV1", 2, 4), arr("m_Tangents", 4, 4)
    C = d.get("m_Colors") or []
    C = C if len(C) == n else None
    S = d.get("m_Skin") or []
    S = S if len(S) == n else None
    B = d.get("m_BindPose") or []
    raw = d.get("m_IndexBuffer") or []
    raw = bytes(raw) if not isinstance(raw, (bytes, bytearray)) else raw
    idx16 = struct.unpack(f"<{len(raw)//2}H", raw[: len(raw) // 2 * 2])
    subs = [tri_list(s, idx16) for s in d.get("m_SubMeshes", [])]
    flags = (1 if N else 0) | (2 if U0 else 0) | (4 if U1 else 0) | (8 if C else 0) | (16 if T else 0) | (32 if S else 0)
    out = bytearray(b"SVZM") + struct.pack("<4I", n, flags, len(subs), len(B))
    out += b"".join(struct.pack("<3f", v["x"], v["y"], v["z"]) for v in V)
    if N: out += b"".join(struct.pack("<3f", v["x"], v["y"], v["z"]) for v in N)
    if U0: out += b"".join(struct.pack("<2f", v["x"], v["y"]) for v in U0)
    if U1: out += b"".join(struct.pack("<2f", v["x"], v["y"]) for v in U1)
    if C:
        for c in C:
            if "rgba" in c:  # ColorRGBA packed uint32
                out += struct.pack("<I", c["rgba"])
            else:
                out += bytes(int(max(0, min(1, c[k])) * 255 + 0.5) for k in ("r", "g", "b", "a"))
    if T: out += b"".join(struct.pack("<4f", v["x"], v["y"], v["z"], v["w"]) for v in T)
    if S:
        for w in S:
            for i in range(4):
                out += struct.pack("<if", w.get(f"boneIndex[{i}]", w.get(f"boneIndex{i}", 0)), w.get(f"weight[{i}]", w.get(f"weight{i}", 0.0)))
    for m in B:
        out += struct.pack("<16f", *[m[f"e{r}{c}"] for r in range(4) for c in range(4)])
    for s in subs:
        out += struct.pack("<I", len(s)) + struct.pack(f"<{len(s)}I", *s)
    path.write_bytes(out)


def patch_enabled_align():
    """TypeTreeGenerator("3.4.x") omits the 4-byte align after MonoBehaviour.m_Enabled; fix it."""
    from UnityPy.files.ObjectReader import ObjectReader
    if getattr(ObjectReader, "_svz_patched", False):
        return
    orig = ObjectReader._get_typetree_node
    def fixed(self, *a, **k):
        node = orig(self, *a, **k)
        if self.type.name != "MonoBehaviour":
            return node
        for c in node.m_Children or []:
            if c.m_Name == "m_Enabled" and not (c.m_MetaFlag or 0) & 0x4000:
                c.m_MetaFlag = (c.m_MetaFlag or 0) | 0x4000
        return node
    ObjectReader._get_typetree_node = fixed
    ObjectReader._svz_patched = True


AUDIO_EXT = {13: "mp3", 14: "ogg", 20: "wav", 22: "xm", 23: "mod", 24: "it", 10: "aiff"}


def export_file(args):
    key, name = args
    patch_enabled_align()
    import UnityPy
    from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
    gen = TypeTreeGenerator("3.4.2f3"); gen.load_local_dll_folder(str(DATA / "Managed"))
    env = UnityPy.load(str(DATA / name)); env.typetree_generator = gen
    sf = next((f for f in env.files.values() if hasattr(f, "externals")), None)
    if sf is None:
        stream = DATA / (name + ".streamingResourceImage")
        if stream.exists():
            return export_streamed_audio(key, name, stream)
        return key, name, 0, [f"{name}: not a serialized file"]
    names = TABLE
    keys = {n.lower(): i for i, n in enumerate(names)}
    keys["__self__"] = key
    externals = [norm_external(e.path) for e in sf.externals]
    objs, errors = {}, []
    texdir, auddir, meshdir = GODOT / "tex" / str(key), GODOT / "audio" / str(key), GODOT / "data" / "mesh"
    for obj in sf.objects.values():
        cls = obj.type.name
        try:
            tt = obj.read_typetree()
        except Exception as e:  # keep going, report
            errors.append(f"{name}:{obj.path_id}:{cls}:{e!r}"[:300])
            continue
        if cls == "MonoBehaviour":
            # 3.4 layout: PPtr m_GameObject (8), UInt8 m_Enabled (+3 align), PPtr m_Script @12
            raw = obj.get_raw_data()
            if len(raw) >= 20:
                fid, pid = struct.unpack_from("<ii", raw, 12)
                tt["m_Script"] = {"m_FileID": fid, "m_PathID": pid}
        if cls == "Texture2D":
            try:
                img = obj.read().image
                texdir.mkdir(parents=True, exist_ok=True)
                img.save(texdir / f"{obj.path_id}.png")
            except Exception as e:
                errors.append(f"{name}:{obj.path_id}:Texture2D image:{e!r}"[:300])
            tt.pop("image data", None)
        elif cls == "AudioClip":
            data = tt.pop("m_AudioData", None)
            if data:
                ext = AUDIO_EXT.get(tt.get("m_Type"), "bin")
                auddir.mkdir(parents=True, exist_ok=True)
                (auddir / f"{obj.path_id}.{ext}").write_bytes(bytes(data))
                tt["$file"] = f"audio/{key}/{obj.path_id}.{ext}"
        elif cls == "Mesh":
            meshdir.mkdir(parents=True, exist_ok=True)
            try:
                write_mesh(tt, meshdir / f"{key}_{obj.path_id}.bin")
            except Exception as e:
                errors.append(f"{name}:{obj.path_id}:Mesh:{e!r}"[:300])
            for k in ("m_Vertices", "m_Normals", "m_UV", "m_UV1", "m_Tangents", "m_Colors", "m_Skin", "m_IndexBuffer", "m_BindPose", "m_CompressedMesh"):
                tt.pop(k, None)
        objs[str(obj.path_id)] = {"c": cls, "t": obj.type_id if hasattr(obj, "type_id") else obj.class_id, "d": pptr_fix(tt, externals, keys)}
    (GODOT / "data" / "sf").mkdir(parents=True, exist_ok=True)
    payload = {"name": name, "externals": externals, "objects": objs}
    with gzip.open(GODOT / "data" / "sf" / f"{key}.json.gz", "wt", compresslevel=6) as f:
        json.dump(payload, f, separators=(",", ":"), default=str)
    return key, name, len(objs), errors


def export_streamed_audio(key, name, stream):
    """Unity 3.4 streamed music: tiny serialized file with one AudioClip (path id 1) + raw stream file."""
    raw = (DATA / name).read_bytes()
    nlen = struct.unpack_from("<i", raw, 16)[0]
    clip_name = raw[20:20 + nlen].decode("utf-8", "replace")
    off = 20 + nlen + (-(20 + nlen) % 4)
    fmt, typ = struct.unpack_from("<ii", raw, off)
    data = stream.read_bytes()
    ext = "wav" if data[:4] == b"RIFF" else "ogg" if data[:4] == b"OggS" else "mp3"
    auddir = GODOT / "audio" / str(key)
    auddir.mkdir(parents=True, exist_ok=True)
    (auddir / f"1.{ext}").write_bytes(data)
    obj = {"c": "AudioClip", "t": 83, "d": {"m_Name": clip_name, "m_Format": fmt, "m_Type": typ, "m_Stream": 2, "$file": f"audio/{key}/1.{ext}"}}
    (GODOT / "data" / "sf").mkdir(parents=True, exist_ok=True)
    with gzip.open(GODOT / "data" / "sf" / f"{key}.json.gz", "wt") as f:
        json.dump({"name": name, "externals": [], "objects": {"1": obj}}, f)
    return key, name, 1, []


TABLE = file_table()

if __name__ == "__main__":
    (GODOT / "data").mkdir(parents=True, exist_ok=True)
    (GODOT / "data" / "files.json").write_text(json.dumps(TABLE))
    only = [a for a in sys.argv[3:] if not a.startswith("-") and not a.isdigit()]
    work = [(i, n) for i, n in enumerate(TABLE) if not only or n in only]
    # largest first for better load balance
    work.sort(key=lambda w: -(DATA / w[1]).stat().st_size)
    allerr, total = [], 0
    with mp.get_context("fork").Pool(JOBS) as pool:
        for i, (key, name, n, errs) in enumerate(pool.imap_unordered(export_file, work)):
            total += n; allerr += errs
            if i % 200 == 0:
                print(f"[{i}/{len(work)}] {name}: {n} objects", flush=True)
    (GODOT / "data" / "export_errors.txt").write_text("\n".join(allerr))
    print("files", len(work), "objects", total, "errors", len(allerr))


def write_shader_index(godot):
    """name -> [fileKey, pathID] for Shader.Find"""
    import glob, re
    idx = {}
    for f in sorted(glob.glob(str(godot / "data" / "sf" / "*.json.gz"))):
        k = int(pathlib.Path(f).name.split(".")[0])
        with gzip.open(f, "rt") as fh:
            d = json.load(fh)
        for pid, o in d["objects"].items():
            if o["c"] == "Shader":
                m = re.match(r'Shader "([^"]+)"', o["d"].get("m_Script", "") or "")
                name = m[1] if m else o["d"].get("m_Name", "")
                idx.setdefault(name, [k, int(pid)])
    (godot / "data" / "shaders.json").write_text(json.dumps(idx))
    return len(idx)


if __name__ == "__main__" and "--no-index" not in sys.argv:
    print("shader index:", write_shader_index(GODOT))
