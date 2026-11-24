using System;

namespace Svz
{
    /// <summary>
    /// Determines when to prefilter a genuine SvZ bitmap-font atlas at 2:1.
    /// The original text geometry, UV layout, coloring, outline and metrics are
    /// unchanged. Avoid replacing headings or fonts rendered near native size.
    /// </summary>
    public static class UiGlyphSampling
    {
        public static bool UseHalfAtlas(int nearestResolution, int selectedResolution,
            float glyphHeight, float viewportHeight, int width, int height)
        {
            if (nearestResolution != 768 || selectedResolution != 1536 ||
                !float.IsFinite(glyphHeight) || !float.IsFinite(viewportHeight) ||
                glyphHeight < 8f || viewportHeight < 200f ||
                glyphHeight > viewportHeight * 0.067f ||
                width < 32 || height < 32 || width > 4096 || height > 4096)
                return false;
            return true;
        }

        public static float MapCoordinate(float originalCoordinate, int originalSize, int filteredSize)
        {
            if (originalSize <= 0 || filteredSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(originalSize));
            return originalCoordinate * ((float)filteredSize / originalSize);
        }

        public static int HalfSize(int originalSize)
        {
            if (originalSize < 32) throw new ArgumentOutOfRangeException(nameof(originalSize));
            return Math.Max(1, originalSize / 2);
        }
    }
}
