using System;

namespace Svz
{
    /// <summary>
    /// Conservative small-text fill policy for the original colored bitmap font atlases.
    /// The original glyph coordinates, kerning, dimensions, wrapping and decorations remain unchanged.
    /// Only small, nearly monochrome glyphs can use a solid RGB fill with the original alpha mask.
    /// </summary>
    public static class UiInkPolicy
    {
        public const int Solid = 0, Original = 1, Outlined = 2, Native = 3, SmoothedOriginal = 4;

        /// <returns>+1 dark solid glyph, -1 light solid glyph, 0 preserve the original RGB texture.</returns>
        public static int InkKind(float glyphHeight, float viewportHeight,
            float atlasLuminance, float atlasChroma, float red, float green, float blue, float alpha)
        {
            if (!float.IsFinite(glyphHeight) || !float.IsFinite(viewportHeight) ||
                !float.IsFinite(atlasLuminance) || !float.IsFinite(atlasChroma) ||
                !float.IsFinite(red) || !float.IsFinite(green) || !float.IsFinite(blue) ||
                !float.IsFinite(alpha)) return 0;
            if (viewportHeight <= 0f || glyphHeight < 4f ||
                glyphHeight > 0.052f * viewportHeight) return 0;

            // Leave colored fonts and colored material overrides (e.g. red reward-day labels) alone.
            float modChroma = Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue));
            if (atlasChroma > 0.16f || modChroma > 0.20f || alpha < 0.05f) return 0;

            float visible = atlasLuminance * (0.299f * red + 0.587f * green + 0.114f * blue);
            if (visible < 0.50f) return 1;
            if (visible > 0.68f) return -1;
            return 0;
        }

        // The app intentionally exposes only a single two-state A/B comparison.
        public static int NextMode(int current) => current == Original ? SmoothedOriginal : Original;
        public static string ModeName(int mode) => mode == SmoothedOriginal ? "original HD" : mode == Native ? "readable" : mode == Solid ? "solid" : mode == Outlined ? "outlined" : "original";
    }
}
