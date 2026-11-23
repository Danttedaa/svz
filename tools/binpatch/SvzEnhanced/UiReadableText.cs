using System;
using System.Reflection;
using Godot;

namespace Svz
{
    /// <summary>
    /// Optional native-Godot renderer for SMALL GUIText labels. It is deliberately
    /// independent of the old bitmap glyph atlas: the alpha silhouette itself is
    /// distressed in the original game, and a solid-color copy does not fix that.
    /// All original layout measurements and strings are read from the existing
    /// object, and the original DrawText is used on any unsupported input/error.
    /// </summary>
    public static class UiReadableText
    {
        private const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static bool sWarned;
        private static Font sFont;

        private static object Member(object obj, string name)
        {
            if (obj == null) return null;
            var type = obj.GetType();
            return type.GetField(name, F)?.GetValue(obj) ??
                type.GetProperty(name, F)?.GetValue(obj);
        }

        private static float Scalar(object obj, string name)
        {
            object value = Member(obj, name);
            return value == null ? float.NaN : Convert.ToSingle(value);
        }

        /// <returns>True only if the native font actually drew the entire label.</returns>
        public static bool TryDraw(CanvasItem self, object guiText, float screenHeight)
        {
            if (UiText.FontMode != UiInkPolicy.Native || self == null || guiText == null)
                return false;
            try
            {
                string text = Member(guiText, "__text") as string;
                object legacyFont = Member(guiText, "__font");
                float spacing = Scalar(legacyFont, "__lineSpacing");
                float lineFactor = Scalar(guiText, "__lineSpacing");
                float lineHeight = spacing * lineFactor;
                object legacyColor = Member(guiText, "__Color");
                float r = Scalar(legacyColor, "r"), g = Scalar(legacyColor, "g");
                float b = Scalar(legacyColor, "b"), a = Scalar(legacyColor, "a");
                if (!UiReadablePolicy.Eligible(text, lineHeight, screenHeight, r, g, b, a))
                    return false;

                // GUIText.__ScreenRect() is the port's existing TextLayout.Measure
                // result, so original anchor/position/wrapping are left unchanged.
                MethodInfo rectMethod = guiText.GetType().GetMethod("__ScreenRect", F);
                object rect = rectMethod?.Invoke(guiText, null);
                float x = Scalar(rect, "x"), y = Scalar(rect, "y");
                float w = Scalar(rect, "width"), h = Scalar(rect, "height");
                if (!float.IsFinite(x) || !float.IsFinite(y) ||
                    !float.IsFinite(w) || !float.IsFinite(h) ||
                    w < 6 || h < 6 || w > 3000 || h > 1600) return false;

                var font = sFont ??= ThemeDB.FallbackFont;
                if (font == null) return false;
                int size = UiReadablePolicy.FontSize(lineHeight);
                var lines = text.Replace("\r", "").Split('\n');
                // Do not paint native text if it would overflow the existing
                // UI's measured box. Shrink only as far as a legible 12 px.
                foreach (string line in lines)
                {
                    while (size > 12 &&
                        font.GetStringSize(line, HorizontalAlignment.Left, -1, size).X > w * 1.05f)
                        size--;
                    if (font.GetStringSize(line, HorizontalAlignment.Left, -1, size).X > w * 1.12f)
                        return false; // original renderer handles unusual tight layouts
                }

                int originalAlignment = Convert.ToInt32(Member(guiText, "__align"));
                HorizontalAlignment align = originalAlignment == 1 ? HorizontalAlignment.Center :
                    originalAlignment == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                float top = screenHeight - (y + h);
                float ascent = font.GetAscent(size);
                // The original game predominantly uses dark item/ability labels.
                // A restrained light edge increases contrast on blue Shop panels.
                var ink = new Color(0.06f, 0.065f, 0.075f, a);
                var edge = new Color(0.97f, 0.92f, 0.79f, a * 0.84f);
                for (int i = 0; i < lines.Length; i++)
                {
                    float baseline = top + ascent + i * lineHeight;
                    var at = new Vector2(x, baseline);
                    self.DrawStringOutline(font, at, lines[i], align, w, size, 1, edge);
                    self.DrawString(font, at, lines[i], align, w, size, ink);
                }
                return true;
            }
            catch (Exception e)
            {
                if (!sWarned)
                {
                    sWarned = true;
                    GD.Print("[svz-ui] native text fallback to original glyphs: " + e.Message);
                }
                return false;
            }
        }
    }
}
