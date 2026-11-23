#!/usr/bin/env python3
"""Emulator-only test variant of a built APK: append Godot command-line arguments to assets/_cl_.

Godot's Android activity ignores command-line extras from other apps (exported activities strip them), so
the emulator smoke test cannot open the Store or a battle directly with `am start`. Godot does read the
packaged command line in assets/_cl_ (int32-LE count, then int32-LE length + UTF-8 bytes per argument).

  smoke_variant.py IN.apk OUT-unsigned.apk -- --level=3 [more args]

Writes an UNSIGNED copy; the caller zipaligns and signs it with a throwaway key. Never publish the output:
it is a test build that starts in a debug level.
"""
import struct
import sys
import zipfile


def read_args(data):
    if len(data) < 4:
        return []
    (n,) = struct.unpack_from("<i", data, 0)
    out, pos = [], 4
    for _ in range(n):
        (ln,) = struct.unpack_from("<i", data, pos)
        pos += 4
        out.append(data[pos:pos + ln].decode("utf-8"))
        pos += ln
    return out


def write_args(args):
    b = struct.pack("<i", len(args))
    for a in args:
        e = a.encode("utf-8")
        b += struct.pack("<i", len(e)) + e
    return b


def main(argv):
    if len(argv) < 4 or "--" not in argv:
        print(__doc__, file=sys.stderr)
        return 2
    src, dst = argv[1], argv[2]
    extra = argv[argv.index("--") + 1:]
    with zipfile.ZipFile(src) as zin, zipfile.ZipFile(dst, "w") as zout:
        names = zin.namelist()
        if "assets/_cl_" not in names:
            print("no assets/_cl_ in the APK", file=sys.stderr)
            return 1
        for info in zin.infolist():
            if info.filename.startswith("META-INF/"):
                continue   # old signature; the variant is re-signed
            data = zin.read(info)
            if info.filename == "assets/_cl_":
                args = read_args(data)
                if "--" not in args:
                    args.append("--")
                args += extra
                data = write_args(args)
                print("assets/_cl_:", " ".join(args))
            zi = zipfile.ZipInfo(info.filename, date_time=info.date_time)
            zi.compress_type = info.compress_type
            zi.external_attr = info.external_attr
            zout.writestr(zi, data)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
