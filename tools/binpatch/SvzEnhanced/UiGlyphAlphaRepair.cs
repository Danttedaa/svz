using System;

namespace Svz
{
    /// <summary>
    /// Repairs isolated low-opacity pinholes in downsampled original bitmap glyphs.
    /// Changes ONLY pixels surrounded by ink on at least three cardinal sides.
    /// Does not grow external glyph edges or fill ordinary multi-pixel counters.
    /// Called once while preparing the cached original-HD atlas, never per frame.
    /// </summary>
    public static class UiGlyphAlphaRepair
    {
        public static int Repair(byte[] rgba, int width, int height)
        {
            if (rgba == null) throw new ArgumentNullException(nameof(rgba));
            if (width < 3 || height < 3 || (long)width * height * 4 != rgba.Length)
                throw new ArgumentException("Expected packed RGBA8 pixels of the supplied dimensions");
            var original = (byte[])rgba.Clone();
            int repaired = 0;
            for (int y = 1; y < height - 1; y++)
            for (int x = 1; x < width - 1; x++)
            {
                int p = (y * width + x) * 4;
                if (original[p + 3] > 80) continue;
                int left = p - 4, right = p + 4;
                int above = p - width * 4, below = p + width * 4;
                bool l = original[left + 3] >= 180, r = original[right + 3] >= 180;
                bool t = original[above + 3] >= 180, b = original[below + 3] >= 180;
                if (!((l && r && (t || b)) || (t && b && (l || r)))) continue;
                int count = 0, rs = 0, gs = 0, bs = 0, minimum = 255;
                foreach (int n in new[] { left, right, above, below })
                {
                    if (original[n + 3] < 180) continue;
                    rs += original[n]; gs += original[n + 1]; bs += original[n + 2];
                    minimum = Math.Min(minimum, original[n + 3]);
                    count++;
                }
                if (count < 3) continue;
                rgba[p] = (byte)(rs / count);
                rgba[p + 1] = (byte)(gs / count);
                rgba[p + 2] = (byte)(bs / count);
                rgba[p + 3] = (byte)Math.Min(220, minimum);
                repaired++;
            }
            return repaired;
        }
    }
}
