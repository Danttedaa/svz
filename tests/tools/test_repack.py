"""tools/binpatch/repack.py on synthetic data: Android string pools, a hand-built binary manifest, Godot's
sparse pack directory, and a whole (tiny) APK rewrite. No game files."""
import hashlib
import os
import struct
import sys
import tempfile
import unittest
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "tools", "binpatch"))
import repack as rp  # noqa: E402


def pool(strings, utf8):
    return rp.build_string_pool(strings, utf8, rp.UTF8_FLAG if utf8 else 0, b"", [])


def manifest(strings, version_code=1, package_index=3):
    """Minimal binary AndroidManifest: <manifest android:versionCode=.. package=..>"""
    sp = pool(strings, utf8=False)
    resmap = struct.pack("<HHI", 0x0180, 8, 8 + 4 * 2) + struct.pack("<II", 0x0101021B, 0)  # versionCode, (unused)
    attrs = struct.pack("<IIIHBBI", 0xFFFFFFFF, 0, 0xFFFFFFFF, 8, 0, 0x10, version_code)          # versionCode (int)
    attrs += struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, package_index, 8, 0, 0x03, package_index)       # package (string)
    ext = struct.pack("<IIHHHHHH", 0xFFFFFFFF, 2, 20, 20, 2, 0, 0, 0)                               # ns, name=manifest
    start = struct.pack("<HHIII", 0x0102, 16, 16 + len(ext) + len(attrs), 1, 0xFFFFFFFF) + ext + attrs
    body = sp + resmap + start
    return struct.pack("<HHI", 0x0003, 8, 8 + len(body)) + body


class StringPools(unittest.TestCase):
    STRINGS = ["versionCode", "package", "manifest", "com.mesflit.svz", "com.mesflit.svz.fileprovider",
               "ünïcødé ✓", "", "x" * 300]

    def test_round_trip_both_encodings(self):
        for utf8 in (True, False):
            parsed = rp.parse_string_pool(pool(self.STRINGS, utf8))
            self.assertEqual(parsed[0], self.STRINGS, f"utf8={utf8}")
            self.assertEqual(parsed[1], utf8)

    def test_rename_package_in_manifest(self):
        m = manifest(self.STRINGS)
        out, n = rp.rename_package(m, "com.mesflit.svz", "io.github.danttedaa.svz")
        self.assertEqual(n, 2)
        self.assertEqual(struct.unpack_from("<I", out, 4)[0], len(out))        # container size updated
        strings = rp.parse_string_pool(out[8:8 + struct.unpack_from("<I", out, 12)[0]])[0]
        self.assertIn("io.github.danttedaa.svz", strings)
        self.assertIn("io.github.danttedaa.svz.fileprovider", strings)
        self.assertNotIn("com.mesflit.svz", strings)
        self.assertEqual(strings.index("io.github.danttedaa.svz"), 3)        # indices unchanged
        with self.assertRaises(ValueError):
            rp.rename_package(m, "4", "x.y")

    def test_set_version_code(self):
        m = manifest(self.STRINGS, version_code=1)
        out = rp.set_manifest_attr_int(m, 0x0101021B, 1042)
        self.assertEqual(len(out), len(m))
        self.assertIn(struct.pack("<I", 1042), out)
        with self.assertRaises(ValueError):
            rp.set_manifest_attr_int(m, 0x01010003, 5)  # attribute not present

    def test_relabel_arsc(self):
        sp = pool(["SvZ Defense", "other", "SvZ Defense"], utf8=True)
        arsc = struct.pack("<HHII", 0x0002, 12, 12 + len(sp) + 8, 1) + sp + b"PKGDATA!"
        out, n = rp.relabel(arsc, "SvZ Defense", "SvZ Enhanced")
        self.assertEqual(n, 2)
        self.assertTrue(out.endswith(b"PKGDATA!"))                              # following chunks untouched
        self.assertEqual(struct.unpack_from("<I", out, 4)[0], len(out))


def sparsepck(files):
    header = struct.pack("<IIIIII", 0x43504447, 4, 4, 7, 2, 0b110) + struct.pack("<QQ", 0, 104) + b"\0" * 64
    entries = [{"path": p, "ofs": 0, "size": len(d), "md5": hashlib.md5(d).digest(), "flags": 0} for p, d in files.items()]
    return rp.write_sparsepck(header, entries)


class SparsePack(unittest.TestCase):
    def test_round_trip_and_update(self):
        files = {".godot/mono/publish/arm64/SvZPort.dll": b"old", "project.binary": b"pb"}
        data = sparsepck(files)
        header, entries = rp.read_sparsepck(data)
        self.assertEqual(rp.write_sparsepck(header, entries), data)
        self.assertEqual(rp.set_pack_file(entries, ".godot/mono/publish/arm64/SvZPort.dll", b"newer"), "updated")
        self.assertEqual(rp.set_pack_file(entries, ".godot/mono/publish/arm64/SvzEnhanced.dll", b"enh"), "added")
        _, again = rp.read_sparsepck(rp.write_sparsepck(header, entries))
        by_path = {e["path"]: e for e in again}
        self.assertEqual(by_path[".godot/mono/publish/arm64/SvZPort.dll"]["size"], 5)
        self.assertEqual(by_path[".godot/mono/publish/arm64/SvzEnhanced.dll"]["md5"], hashlib.md5(b"enh").digest())

    def test_rejects_encrypted_or_wrong_version(self):
        bad = bytearray(sparsepck({"a": b"1"}))
        struct.pack_into("<I", bad, 20, 0b111)
        with self.assertRaises(ValueError):
            rp.read_sparsepck(bytes(bad))
        struct.pack_into("<I", bad, 4, 3)
        with self.assertRaises(ValueError):
            rp.read_sparsepck(bytes(bad))


class WholeApk(unittest.TestCase):
    def test_repack_tiny_apk(self):
        tmp = tempfile.mkdtemp()
        src, dst = os.path.join(tmp, "in.apk"), os.path.join(tmp, "out.apk")
        dll, enh = os.path.join(tmp, "patched.dll"), os.path.join(tmp, "enh.dll")
        for path, data in ((dll, b"PATCHED"), (enh, b"ENHANCED")):
            with open(path, "wb") as f:
                f.write(data)
        files = {".godot/mono/publish/arm64/SvZPort.dll": b"ORIGINAL", "project.binary": b"pb"}
        with zipfile.ZipFile(src, "w") as z:
            z.writestr("AndroidManifest.xml", manifest(StringPools.STRINGS))
            z.writestr(zipfile.ZipInfo("resources.arsc"), b"arsc-untouched")      # stored
            z.writestr("assets/assets.sparsepck", sparsepck(files))
            for p, d in files.items():
                z.writestr("assets/" + p, d, compress_type=zipfile.ZIP_DEFLATED)
            z.writestr("META-INF/CERT.RSA", b"sig")
            z.writestr("META-INF/MANIFEST.MF", b"mf")
            z.writestr("META-INF/services/x", b"keep me")
        rp._apk_info = lambda path: {"package": "com.mesflit.svz", "label": "SvZ Defense"}
        report = rp.repack(src, dst, {"arm64": dll}, enh, package="io.github.danttedaa.svz", version_code=1001)
        self.assertTrue(any("added" in r for r in report))
        self.assertEqual(rp.verify_pack(dst), [])
        z = zipfile.ZipFile(dst)
        self.addCleanup(z.close)
        names = z.namelist()
        self.assertEqual(z.read("assets/.godot/mono/publish/arm64/SvZPort.dll"), b"PATCHED")
        self.assertEqual(z.read("assets/.godot/mono/publish/arm64/SvzEnhanced.dll"), b"ENHANCED")
        self.assertNotIn("META-INF/CERT.RSA", names)
        self.assertNotIn("META-INF/MANIFEST.MF", names)
        self.assertIn("META-INF/services/x", names)
        self.assertEqual(z.getinfo("resources.arsc").compress_type, zipfile.ZIP_STORED)
        self.assertEqual(z.getinfo("assets/.godot/mono/publish/arm64/SvzEnhanced.dll").compress_type, zipfile.ZIP_DEFLATED)
        man = z.read("AndroidManifest.xml")
        self.assertIn("io.github.danttedaa.svz".encode("utf-16-le"), man)
        self.assertIn(struct.pack("<I", 1001), man)


if __name__ == "__main__":
    unittest.main()
