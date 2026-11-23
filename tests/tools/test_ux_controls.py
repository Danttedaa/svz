"""Guard the agreed single-gesture UX against future hidden controls."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
FPS = ROOT / "godot/src/Engine/Core/FramePacing.cs"
UI = ROOT / "tools/binpatch/SvzEnhanced/UiText.cs"

class SingleGestureTests(unittest.TestCase):
    def test_only_one_hold_across_the_runtime(self):
        sources = FPS.read_text() + "\n" + UI.read_text()
        gestures = re.findall(r"new\s+HoldGesture\s*\(\s*(\d+)\s*,\s*([\d.]+)\s*\)", sources)
        self.assertEqual(gestures, [("2", "2.0")],
                         "At most one gesture: 2-finger hold for text A/B")

    def test_fps_is_not_accidentally_changed_by_touches(self):
        source = FPS.read_text()
        self.assertNotIn("sToggleFps", source)
        self.assertNotIn("sToggleOverlay", source)
        self.assertIn('DisplayServer.WindowSetVsyncMode', source)
        self.assertIn('SetOverlay(Settings.ShowFps)', source)

    def test_user_font_control_is_two_state_only(self):
        source = UI.read_text()
        self.assertNotIn("sCycle.Update(", source)
        self.assertNotIn("NextScaleStep(", source)
        self.assertIn("FontMode == UiInkPolicy.Native ? UiInkPolicy.Original : UiInkPolicy.Native", source)
        self.assertIn('cf.SetValue("text", "font_mode_v3", FontMode)', source)
        self.assertIn('UiFontQuality.TextScale = 1f;', source)

if __name__ == "__main__":
    unittest.main()
