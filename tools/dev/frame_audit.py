#!/usr/bin/env python3
"""Static audit of decompiled Unity C# for frame-rate-dependent logic.

Flags code inside per-frame callbacks (Update, LateUpdate, OnGUI) and coroutine
loops that would behave differently at 120 FPS than at 30/60 FPS:

  FRAMECOUNT   uses Time.frameCount (logic keyed to frame number)
  LERP_CONST   Lerp/Slerp/MoveTowards/SmoothDamp-like call with a constant factor and no deltaTime
  CONST_STEP   += / -= / *= by a numeric literal with no deltaTime on the line
  FRAME_WAIT   `yield return null` / WaitForEndOfFrame inside a loop (waits N frames, not N seconds)
  COUNTER      ++/-- on a field whose name suggests a frame/tick counter

Heuristic only: every hit needs a human review. Output: TSV on stdout (category, file, line, method, code).
Usage: python3 -I frame_audit.py <scripts-dir> [--summary]
"""
import re
import sys
from pathlib import Path

PER_FRAME_RE = re.compile(r"^(Update\w*|LateUpdate|OnGUI|Tick\w*|Step\w*|Process\w*)$")
METHOD_RE = re.compile(r"^\s*(?:(?:public|private|protected|internal|static|virtual|override|sealed|new|unsafe|abstract)\s+)*"
                       r"[\w<>\[\],\. ]+?\s+(\w+)\s*\([^;]*\)\s*(?:where [^{]*)?$")
TIME_WORDS = re.compile(r"deltaTime|fixedDeltaTime|smoothDeltaTime|Time\.time|realtimeSinceStartup|timeSinceLevelLoad|\bdt\b|elapsed", re.I)
LERP_RE = re.compile(r"\b(Lerp|LerpAngle|Slerp|MoveTowards|RotateTowards|SmoothStep)\s*\(")
CONST_STEP_RE = re.compile(r"(\+=|-=|\*=)\s*\(?\s*-?\d+(\.\d+)?f?\s*\)?\s*;")
COUNTER_RE = re.compile(r"\b(m?_?\w*(frame|tick|Frame|Tick)\w*)\s*(\+\+|--)|(\+\+|--)\s*(m?_?\w*(frame|tick|Frame|Tick)\w*)")
FRAME_WAIT_RE = re.compile(r"yield\s+return\s+(null|0|new\s+WaitForEndOfFrame\s*\(\s*\))\s*;")
LOOP_RE = re.compile(r"^\s*(while|for|do)\b")


def strip_comments(line: str) -> str:
    return re.sub(r"//.*", "", line)


def audit_file(path: Path):
    lines = path.read_text(encoding="utf-8-sig", errors="replace").splitlines()
    depth = 0
    method = None          # (name, depth at which its body opened)
    pending_method = None
    loop_depths = []       # brace depths of enclosing loops inside current method
    pending_loop = False
    is_coroutine = False
    for i, raw in enumerate(lines, 1):
        line = strip_comments(raw)
        m = METHOD_RE.match(line)
        if m and method is None and "=" not in line.split("(")[0]:
            pending_method = m.group(1)
            is_coroutine = "IEnumerator" in line
        if LOOP_RE.match(line) and method is not None:
            pending_loop = True
        name = method[0] if method else None
        per_frame = bool(name and PER_FRAME_RE.match(name))
        in_loop = bool(loop_depths)
        if method is not None:
            code = raw.strip()
            if "Time.frameCount" in line or "renderedFrameCount" in line:
                yield ("FRAMECOUNT", i, name, code)
            if per_frame or (is_coroutine and in_loop):
                if LERP_RE.search(line) and not TIME_WORDS.search(line):
                    yield ("LERP_CONST", i, name, code)
                if CONST_STEP_RE.search(line) and not TIME_WORDS.search(line):
                    yield ("CONST_STEP", i, name, code)
            if COUNTER_RE.search(line):
                yield ("COUNTER", i, name, code)
            if is_coroutine and in_loop and FRAME_WAIT_RE.search(line):
                yield ("FRAME_WAIT", i, name, code)
        for ch in line:
            if ch == "{":
                depth += 1
                if pending_method and method is None:
                    method = (pending_method, depth)
                    pending_method = None
                elif pending_loop:
                    loop_depths.append(depth)
                    pending_loop = False
            elif ch == "}":
                if loop_depths and loop_depths[-1] == depth:
                    loop_depths.pop()
                if method and method[1] == depth:
                    method = None
                    loop_depths.clear()
                    is_coroutine = False
                depth -= 1
        if line.rstrip().endswith(";"):
            pending_loop = False  # single-statement loop body or a plain statement


def main(argv):
    if len(argv) < 2:
        print(__doc__, file=sys.stderr)
        return 2
    root = Path(argv[1])
    summary = "--summary" in argv
    counts = {}
    files_hit = set()
    for path in sorted(root.rglob("*.cs")):
        for cat, ln, meth, code in audit_file(path):
            counts[cat] = counts.get(cat, 0) + 1
            files_hit.add(path)
            if not summary:
                print(f"{cat}\t{path.relative_to(root)}\t{ln}\t{meth}\t{code}")
    if summary:
        for k in sorted(counts):
            print(f"{k}\t{counts[k]}")
        print(f"FILES\t{len(files_hit)}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
