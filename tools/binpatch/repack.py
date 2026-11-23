#!/usr/bin/env python3
"""Repackage a prebuilt SvZ port APK with the patched SvZPort.dll and SvzEnhanced.dll (no game data changes).

  repack.py IN.apk OUT-unsigned.apk --dll ARCH=PATCHED_SvZPort.dll [--dll ...] --enhanced SvzEnhanced.dll
            [--package NEW.ID] [--label "App name"] [--version-code N] [--version-name S]

- replaces assets/.godot/mono/publish/<arch>/SvZPort.dll and adds SvzEnhanced.dll next to it
- updates Godot's sparse pack directory (assets/assets.sparsepck: size + MD5 of every changed/added file;
  Godot takes file lengths from this directory)
- optionally renames the package (manifest string pool: package, provider authorities, ...) and the app
  label (resources.arsc string pool), so the result installs NEXT TO the original instead of conflicting
- keeps every other entry byte for byte with its original compression; drops the old signature files
The output must still be zip-aligned and signed (zipalign -p 4, apksigner sign).
"""
import argparse
import hashlib
import struct
import sys
import zipfile

SPARSE = "assets/assets.sparsepck"
PUBLISH = "assets/.godot/mono/publish/{arch}/{name}"


# ---------------------------------------------------------------- Godot sparse pack directory (PCK v4)

def read_sparsepck(data):
    magic, version, major, minor, patch, flags = struct.unpack_from("<IIIIII", data, 0)
    if magic != 0x43504447 or version != 4:
        raise ValueError(f"not a Godot v4 pack (magic {magic:#x}, version {version})")
    if flags & 1:
        raise ValueError("encrypted pack directories are not supported")
    file_base, dir_offset = struct.unpack_from("<QQ", data, 24)
    header = data[:dir_offset]
    p = dir_offset
    (count,) = struct.unpack_from("<I", data, p)
    p += 4
    entries = []
    for _ in range(count):
        (slen,) = struct.unpack_from("<I", data, p)
        p += 4
        path = data[p:p + slen].rstrip(b"\0").decode("utf-8")
        p += slen
        ofs, size = struct.unpack_from("<QQ", data, p)
        p += 16
        md5 = data[p:p + 16]
        p += 16
        (fl,) = struct.unpack_from("<I", data, p)
        p += 4
        entries.append({"path": path, "ofs": ofs, "size": size, "md5": md5, "flags": fl})
    if p != len(data):
        raise ValueError(f"trailing bytes in pack directory ({len(data) - p})")
    return header, entries


def write_sparsepck(header, entries):
    out = bytearray(header)
    out += struct.pack("<I", len(entries))
    for e in entries:
        raw = e["path"].encode("utf-8")
        padded = raw + b"\0" * ((4 - len(raw) % 4) % 4)
        out += struct.pack("<I", len(padded)) + padded
        out += struct.pack("<QQ", e["ofs"], e["size"]) + e["md5"] + struct.pack("<I", e["flags"])
    return bytes(out)


def set_pack_file(entries, path, data):
    """Add or update a sparse entry for res://path."""
    md5 = hashlib.md5(data).digest()
    for e in entries:
        if e["path"] == path:
            e["size"], e["md5"] = len(data), md5
            return "updated"
    entries.append({"path": path, "ofs": 0, "size": len(data), "md5": md5, "flags": 0})
    return "added"


# ---------------------------------------------------------------- Android binary string pools (AXML, ARSC)

RES_STRING_POOL_TYPE = 0x0001
UTF8_FLAG = 0x100


def _decode_len8(b, p):
    n = b[p]
    if n & 0x80:
        return ((n & 0x7F) << 8) | b[p + 1], p + 2
    return n, p + 1


def _decode_len16(b, p):
    (n,) = struct.unpack_from("<H", b, p)
    if n & 0x8000:
        (n2,) = struct.unpack_from("<H", b, p + 2)
        return ((n & 0x7FFF) << 16) | n2, p + 4
    return n, p + 2


def _encode_len8(n):
    if n > 0x7FFF:
        raise ValueError("string too long")
    return bytes([n]) if n < 0x80 else bytes([0x80 | (n >> 8), n & 0xFF])


def _encode_len16(n):
    if n > 0x7FFFFFFF:
        raise ValueError("string too long")
    return struct.pack("<H", n) if n < 0x8000 else struct.pack("<HH", 0x8000 | (n >> 16), n & 0xFFFF)


def parse_string_pool(chunk):
    """chunk: bytes of a ResStringPool chunk. Returns (strings, utf8, flags, style_data, style_offsets, header_size)."""
    typ, hsize, size, count, style_count, flags, strings_start, styles_start = struct.unpack_from("<HHIIIIII", chunk, 0)
    if typ != RES_STRING_POOL_TYPE:
        raise ValueError("not a string pool")
    utf8 = bool(flags & UTF8_FLAG)
    offsets = struct.unpack_from(f"<{count}I", chunk, hsize)
    style_offsets = struct.unpack_from(f"<{style_count}I", chunk, hsize + 4 * count)
    strings = []
    for off in offsets:
        p = strings_start + off
        if utf8:
            _, p = _decode_len8(chunk, p)          # length in UTF-16 units
            blen, p = _decode_len8(chunk, p)       # length in bytes
            strings.append(chunk[p:p + blen].decode("utf-8"))
        else:
            n, p = _decode_len16(chunk, p)
            strings.append(chunk[p:p + 2 * n].decode("utf-16-le"))
    style_data = chunk[styles_start:size] if style_count else b""
    return strings, utf8, flags, style_data, list(style_offsets), hsize


def build_string_pool(strings, utf8, flags, style_data, style_offsets, hsize=28):
    data = bytearray()
    offsets = []
    for s in strings:
        offsets.append(len(data))
        if utf8:
            enc = s.encode("utf-8")
            data += _encode_len8(len(s.encode("utf-16-le")) // 2) + _encode_len8(len(enc)) + enc + b"\0"
        else:
            enc = s.encode("utf-16-le")
            data += _encode_len16(len(enc) // 2) + enc + b"\0\0"
    data += b"\0" * ((4 - len(data) % 4) % 4)
    strings_start = hsize + 4 * len(strings) + 4 * len(style_offsets)
    styles_start = strings_start + len(data) if style_offsets else 0
    size = strings_start + len(data) + len(style_data)
    head = struct.pack("<HHIIIIII", RES_STRING_POOL_TYPE, hsize, size, len(strings), len(style_offsets), flags,
                       strings_start, styles_start)
    head += b"\0" * (hsize - len(head))
    return head + struct.pack(f"<{len(strings)}I", *offsets) + struct.pack(f"<{len(style_offsets)}I", *style_offsets) + bytes(data) + style_data


def rewrite_first_pool(container, header_size, replace):
    """container: an AXML (type 3) or ARSC (type 2) file; its first child chunk at header_size is the string pool.
    replace(str) -> str. Returns (new_bytes, number_of_changed_strings)."""
    typ, hsize, total = struct.unpack_from("<HHI", container, 0)
    if hsize != header_size or total != len(container):
        raise ValueError(f"unexpected container header (type {typ}, header {hsize}, size {total}/{len(container)})")
    (_, _, pool_size) = struct.unpack_from("<HHI", container, hsize)
    pool = container[hsize:hsize + pool_size]
    strings, utf8, flags, style_data, style_offsets, phsize = parse_string_pool(pool)
    new_strings = [replace(s) for s in strings]
    changed = sum(1 for a, b in zip(strings, new_strings) if a != b)
    new_pool = build_string_pool(new_strings, utf8, flags, style_data, style_offsets, phsize)
    body = container[:hsize] + new_pool + container[hsize + pool_size:]
    body = body[:4] + struct.pack("<I", len(body)) + body[8:]
    return body, changed


def rename_package(manifest, old, new):
    if not old or "." not in old:
        raise ValueError(f"refusing to rename suspicious package name {old!r}")

    def repl(s):
        if s == old or s.startswith(old + "."):
            return new + s[len(old):]
        return s
    return rewrite_first_pool(manifest, 8, repl)


def relabel(arsc, old, new):
    return rewrite_first_pool(arsc, 12, lambda s: new if s == old else s)


def set_manifest_attr_int(manifest, attr_res_id, value):
    """Set an integer attribute (e.g. versionCode 0x0101021b) on the <manifest> start tag in place."""
    # resource map chunk follows the string pool; find the attribute name index for the resource id
    hsize = 8
    (_, _, pool_size) = struct.unpack_from("<HHI", manifest, hsize)
    p = hsize + pool_size
    typ, chsize, size = struct.unpack_from("<HHI", manifest, p)
    if typ != 0x0180:
        raise ValueError("no resource map in manifest")
    ids = struct.unpack_from(f"<{(size - chsize) // 4}I", manifest, p + chsize)
    if attr_res_id not in ids:
        raise ValueError(f"attribute {attr_res_id:#x} not in resource map")
    name_index = ids.index(attr_res_id)
    p += size
    buf = bytearray(manifest)
    while p < len(buf):
        typ, chsize, size = struct.unpack_from("<HHI", buf, p)
        if typ == 0x0102:  # start element
            attr_start, attr_size, attr_count = struct.unpack_from("<HHH", buf, p + chsize + 8)
            a = p + chsize + attr_start
            for i in range(attr_count):
                q = a + i * attr_size
                ns, name, raw, vsize, _res0, dtype, data = struct.unpack_from("<IIIHBBI", buf, q)
                if name == name_index:
                    if dtype not in (0x10, 0x11):
                        raise ValueError("attribute is not an integer")
                    struct.pack_into("<I", buf, q + 16, value)
                    return bytes(buf)
            raise ValueError("attribute not on the first element")
        p += size
    raise ValueError("no start element")


# ---------------------------------------------------------------- APK rewrite

def _read(path):
    with open(path, "rb") as f:
        return f.read()


def repack(src, dst, dlls, enhanced, package=None, label=None, version_code=None):
    with zipfile.ZipFile(src) as zin:
        return _repack(zin, src, dst, dlls, enhanced, package, label, version_code)


def _repack(zin, src, dst, dlls, enhanced, package, label, version_code):
    names = zin.namelist()
    header, entries = read_sparsepck(zin.read(SPARSE))
    replaced, added = {}, {}
    enh = _read(enhanced)
    report = []
    for arch, path in dlls.items():
        target = PUBLISH.format(arch=arch, name="SvZPort.dll")
        if target not in names:
            raise ValueError(f"{target} not in {src}")
        replaced[target] = _read(path)
        added[PUBLISH.format(arch=arch, name="SvzEnhanced.dll")] = enh
    for name, data in list(replaced.items()) + list(added.items()):
        report.append(f"{set_pack_file(entries, name[len('assets/'):], data)} pack entry {name}")
    replaced[SPARSE] = write_sparsepck(header, entries)

    old_pkg = None
    if package or version_code:
        from_manifest = zin.read("AndroidManifest.xml")
        man = from_manifest
        if package:
            old_pkg = _apk_info(src)["package"]
            man, n = rename_package(man, old_pkg, package)
            report.append(f"package {old_pkg} -> {package} ({n} manifest strings)")
        if version_code:
            man = set_manifest_attr_int(man, 0x0101021B, int(version_code))
            report.append(f"versionCode -> {version_code}")
        replaced["AndroidManifest.xml"] = man
    if label:
        arsc, n = relabel(zin.read("resources.arsc"), _apk_info(src)["label"], label)
        if n == 0:
            raise ValueError("app label string not found in resources.arsc")
        replaced["resources.arsc"] = arsc
        report.append(f"label -> {label} ({n} strings)")

    with zipfile.ZipFile(dst, "w") as zout:
        for info in zin.infolist():
            if info.filename.startswith("META-INF/") and info.filename.split("/")[-1].upper().endswith((".SF", ".RSA", ".DSA", ".EC", "MANIFEST.MF")):
                continue  # old signature
            data = replaced.get(info.filename)
            out = zipfile.ZipInfo(info.filename, date_time=info.date_time)
            out.compress_type = info.compress_type
            out.external_attr = info.external_attr
            zout.writestr(out, zin.read(info.filename) if data is None else data)
        for name, data in added.items():
            ref = zin.getinfo(PUBLISH.format(arch=name.split("/")[-2], name="SvZPort.dll"))
            out = zipfile.ZipInfo(name, date_time=ref.date_time)
            out.compress_type = ref.compress_type
            zout.writestr(out, data)
    return report


def _apk_info(apk_path):
    """Package and label from the parsed manifest/resources (androguard)."""
    try:
        from loguru import logger
        logger.remove()
    except ImportError:
        pass
    from androguard.core.apk import APK
    a = APK(apk_path)
    return {"package": a.get_package(), "label": a.get_app_name()}


def verify_pack(apk):
    """Every sparse pack entry must match the file in the APK (size + MD5). Returns list of problems."""
    with zipfile.ZipFile(apk) as z:
        names = set(z.namelist())
        _, entries = read_sparsepck(z.read(SPARSE))
        problems = []
        for e in entries:
            name = "assets/" + e["path"]
            if name not in names:
                problems.append(f"missing {name}")
                continue
            data = z.read(name)
            if len(data) != e["size"] or hashlib.md5(data).digest() != e["md5"]:
                problems.append(f"mismatch {name}")
        return problems


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--dll", action="append", default=[], help="ARCH=path of the patched SvZPort.dll (arm64, x86_64)")
    ap.add_argument("--enhanced", required=True)
    ap.add_argument("--package")
    ap.add_argument("--label")
    ap.add_argument("--version-code")
    a = ap.parse_args(argv)
    dlls = dict(x.split("=", 1) for x in a.dll)
    if not dlls:
        ap.error("at least one --dll ARCH=path")
    try:
        for line in repack(a.src, a.dst, dlls, a.enhanced, a.package, a.label, a.version_code):
            print(line)
        problems = verify_pack(a.dst)
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as e:
        print(f"repack: {e}", file=sys.stderr)
        return 2
    if problems:
        print("repack: pack directory does not match the APK: " + "; ".join(problems[:5]), file=sys.stderr)
        return 1
    print(f"pack directory verified ({a.dst})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
