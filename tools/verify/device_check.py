#!/usr/bin/env python3
"""Measure SvZ Enhanced on a connected Android phone and report whether it really runs at 120 FPS.

Two independent sources:
  1. SurfaceFlinger present timestamps of the game's surface (`dumpsys SurfaceFlinger --latency`):
     what the display actually showed, and the panel refresh period.
  2. The game's own "[svz-fps]" logcat lines: the Surface.setFrameRate result and 5-second frame stats.

Run on a PC with adb (Android platform-tools) and USB debugging enabled on the phone:
  Windows PowerShell:  py -3 tools\\verify\\device_check.py --package io.github.danttedaa.svz
  Linux / WSL / macOS: python3 tools/verify/device_check.py --package io.github.danttedaa.svz

Options: --apk PATH (install it first, keeps data: adb install -r), --warmup S (default 25),
--seconds S (default 30), --target 120, --no-launch (measure the running game; start a level first
for a gameplay measurement), --json OUT, --adb PATH.
The script starts the app but does not tap anything: for a gameplay number, use --no-launch while
a level is running. Exit code 0 = PASS, 1 = FAIL, 2 = could not measure.
"""
import argparse
import json
import re
import shutil
import statistics
import subprocess
import sys
import time

PENDING = 9223372036854775807  # INT64_MAX: frame not presented yet
STATS_RE = re.compile(r"\[svz-fps\] stats fps=(?P<fps>[\d.]+) p95=(?P<p95>[\d.]+)ms max=(?P<max>[\d.]+)ms "
                      r"missed=(?P<missed>[\d.]+)% display=(?P<display>-?\d+)Hz target=(?P<target>\d+)")


# ------------------------------------------------------------------ parsing (pure, unit tested)

def parse_latency(text):
    """dumpsys SurfaceFlinger --latency LAYER -> (refresh_period_ns, [actual_present_ns, ...])."""
    lines = [l.split() for l in (text or "").strip().splitlines() if l.strip()]
    if not lines or not lines[0] or not lines[0][0].isdigit():
        return 0, []
    period = int(lines[0][0])
    presents = []
    for parts in lines[1:]:
        if len(parts) >= 3 and parts[1].isdigit():
            t = int(parts[1])
            if 0 < t < PENDING:
                presents.append(t)
    return period, presents


def find_layer(list_text, package):
    """Pick the game's SurfaceView layer from `dumpsys SurfaceFlinger --list`."""
    cands = [l.strip() for l in (list_text or "").splitlines() if package in l and "SurfaceView" in l]
    cands = [c for c in cands if "Background" not in c] or cands
    cands.sort(key=lambda c: ("BLAST" not in c, len(c)))
    return cands[0] if cands else None


def summarize_presents(presents_ns, period_ns, target_fps):
    """Frame rate and pacing from unique present timestamps."""
    ts = sorted(set(presents_ns))
    if len(ts) < 30:
        return None
    iv = [(b - a) / 1e6 for a, b in zip(ts, ts[1:]) if b > a]
    iv = [x for x in iv if x < 250]  # gaps between sampling bursts / loading
    if len(iv) < 20:
        return None
    iv_sorted = sorted(iv)
    target_ms = 1000.0 / target_fps
    return {
        "frames": len(iv),
        "fps": round(1000.0 / statistics.mean(iv), 1),
        "median_ms": round(statistics.median(iv), 2),
        "p95_ms": round(iv_sorted[min(len(iv) - 1, int(len(iv) * 0.95))], 2),
        "p99_ms": round(iv_sorted[min(len(iv) - 1, int(len(iv) * 0.99))], 2),
        "missed_pct": round(100.0 * sum(1 for x in iv if x > 1.5 * target_ms) / len(iv), 1),
        "display_hz": round(1e9 / period_ns, 1) if period_ns else None,
    }


def parse_game_log(text):
    stats, events = [], []
    for line in (text or "").splitlines():
        if "[svz-fps]" not in line:
            continue
        m = STATS_RE.search(line)
        if m:
            stats.append({k: float(v) for k, v in m.groupdict().items()})
        else:
            events.append(line[line.index("[svz-fps]"):].strip())
    return stats, events


def verdict(sf, game_stats, target):
    """PASS only when the display and the presented frames both show the target rate."""
    reasons, ok = [], True
    tol = 0.95 * target
    if sf is None:
        reasons.append("no SurfaceFlinger frame data")
        ok = False
    else:
        if sf["display_hz"] is not None and abs(sf["display_hz"] - target) > 2:
            ok = False
            reasons.append(f"panel at {sf['display_hz']} Hz, not {target}")
        if sf["fps"] < tol:
            ok = False
            reasons.append(f"presented {sf['fps']} fps < {tol:.0f}")
        if sf["median_ms"] > 1000.0 / target * 1.25:
            ok = False
            reasons.append(f"median frame {sf['median_ms']} ms")
        if sf["missed_pct"] > 5:
            ok = False
            reasons.append(f"{sf['missed_pct']}% frames missed a refresh")
    if game_stats:
        last = game_stats[-1]
        if last["fps"] < tol:
            ok = False
            reasons.append(f"game reports {last['fps']} fps")
    else:
        reasons.append("no [svz-fps] stats in logcat (older build or log_stats=false)")
    return ("PASS" if ok else "FAIL"), reasons


# ------------------------------------------------------------------ device I/O

class Adb:
    def __init__(self, path):
        self.path = path

    def run(self, *args, check=True, timeout=60):
        r = subprocess.run([self.path, *args], capture_output=True, text=True, timeout=timeout)
        if check and r.returncode != 0:
            raise RuntimeError(f"adb {' '.join(args)} failed: {(r.stderr or r.stdout).strip()}")
        return r.stdout

    def shell(self, cmd, **kw):
        return self.run("shell", cmd, **kw)


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--package", required=True)
    p.add_argument("--apk")
    p.add_argument("--warmup", type=float, default=25)
    p.add_argument("--seconds", type=float, default=30)
    p.add_argument("--target", type=int, default=120)
    p.add_argument("--no-launch", action="store_true")
    p.add_argument("--json")
    p.add_argument("--adb", default=shutil.which("adb") or "adb")
    a = p.parse_args(argv)
    adb = Adb(a.adb)
    report = {"package": a.package, "target": a.target}
    try:
        devices = [l.split()[0] for l in adb.run("devices").splitlines()[1:] if l.strip().endswith("device")]
        if len(devices) != 1:
            print(f"need exactly one device with USB debugging authorised, found {len(devices)}", file=sys.stderr)
            return 2
        report["model"] = adb.shell("getprop ro.product.model").strip()
        report["android"] = adb.shell("getprop ro.build.version.release").strip()
        report["abis"] = adb.shell("getprop ro.product.cpu.abilist").strip()
        if a.apk:
            print(f"installing {a.apk} (adb install -r keeps the save game)")
            adb.run("install", "-r", a.apk, timeout=600)
        if a.package not in adb.shell(f"pm path {a.package}", check=False):
            print(f"{a.package} is not installed", file=sys.stderr)
            return 2
        if not a.no_launch:
            adb.run("logcat", "-c")
            adb.shell(f"monkey -p {a.package} -c android.intent.category.LAUNCHER 1")
            print(f"launched; waiting {a.warmup:.0f} s")
            time.sleep(a.warmup)
        layer = find_layer(adb.shell("dumpsys SurfaceFlinger --list"), a.package)
        report["layer"] = layer
        presents, period = [], 0
        if layer:
            adb.shell(f"dumpsys SurfaceFlinger --latency-clear '{layer}'", check=False)
            end = time.time() + a.seconds
            while time.time() < end:  # the latency buffer holds ~127 frames: sample every 0.5 s
                per, pres = parse_latency(adb.shell(f"dumpsys SurfaceFlinger --latency '{layer}'", check=False))
                period = per or period
                presents.extend(pres)
                time.sleep(0.5)
        else:
            print("game surface not found in SurfaceFlinger (is the game in the foreground?)", file=sys.stderr)
        sf = summarize_presents(presents, period, a.target)
        stats, events = parse_game_log(adb.run("logcat", "-d", "-s", "godot", check=False))
        report.update({"surfaceflinger": sf, "game_stats": stats[-6:], "game_events": events[-20:]})
    except (RuntimeError, subprocess.TimeoutExpired, FileNotFoundError) as e:
        print(f"device_check: {e}", file=sys.stderr)
        return 2
    result, reasons = verdict(sf, stats, a.target)
    report.update({"result": result, "reasons": reasons})
    print(f"\n{report.get('model')} / Android {report.get('android')} / ABIs {report.get('abis')}")
    print(f"surface: {layer}")
    print(f"SurfaceFlinger: {json.dumps(sf)}")
    for e in events[-8:]:
        print("game: " + e)
    if stats:
        print("game stats (last): " + json.dumps(stats[-1]))
    print(f"\nRESULT: {result}" + ("" if not reasons else " - " + "; ".join(reasons)))
    if a.json:
        with open(a.json, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)
    return 0 if result == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
