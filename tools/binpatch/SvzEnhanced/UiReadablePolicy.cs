using System;

namespace Svz
{
    /// <summary>
    /// Independent guard for the optional clean Godot font path.
    /// Never replace the large, stylized headings or colored special labels.
    /// </summary>
    public static class UiReadablePolicy
    {
        public static bool Eligible(string text, float lineHeight, float viewportHeight,
            float red, float green, float blue, float alpha)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 240 ||
                !float.IsFinite(lineHeight) || !float.IsFinite(viewportHeight) ||
                !float.IsFinite(red) || !float.IsFinite(green) ||
                !float.IsFinite(blue) || !float.IsFinite(alpha)) return false;
            if (viewportHeight <= 0 || lineHeight < 10f ||
                lineHeight > viewportHeight * 0.067f || alpha < 0.05f) return false;
            if (Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) > 0.20f)
                return false;
            return true;
        }

        public static int FontSize(float lineHeight)
        {
            if (!float.IsFinite(lineHeight)) return 16;
            return Math.Clamp((int)Math.Round(lineHeight * 0.85f), 12, 42);
        }
    }
}
