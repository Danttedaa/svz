#!/usr/bin/env python3
"""Certificate SHA-256 fingerprints in one canonical form (64 upper-case hex digits, no colons).

  keytool -list -v ... | cert_fingerprint.py        first "SHA256:" fingerprint in keytool output
  printf %s "ab:cd:..." | cert_fingerprint.py --normalize
Prints nothing and exits 1 when no valid fingerprint is found.
"""
import re
import sys


def normalize(value: str) -> str:
    hexdigits = re.sub(r"[\s:]", "", value or "").upper()
    return hexdigits if re.fullmatch(r"[0-9A-F]{64}", hexdigits) else ""


def from_keytool(text: str) -> str:
    for line in (text or "").splitlines():
        m = re.match(r"^\s*SHA-?256:\s*([0-9A-Fa-f:]+)\s*$", line)
        if m:
            return normalize(m.group(1))
    return ""


def main(argv):
    data = sys.stdin.read()
    fp = normalize(data) if "--normalize" in argv else from_keytool(data)
    if not fp:
        return 1
    print(fp)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
