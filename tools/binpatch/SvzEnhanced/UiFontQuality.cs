using System;
using System.Collections.Generic;
using System.Globalization;

namespace Svz
{
	/// <summary>
	/// For the old Unity bitmap-font renderer: prefer the existing 1536-pixel
	/// atlas on modern landscape screens rather than upscaling a 768-pixel atlas.
	/// On smaller devices and when the HD atlas is absent, preserve nearest choice.
	/// This does not modify original font data, labels, assets, or layout positions.
	/// </summary>
	public static class UiFontQuality
	{
		public static string SelectResolution(List<int> supported, int screenHeight)
		{
			if (supported == null || supported.Count == 0)
				throw new ArgumentException("No supported font resolutions", nameof(supported));

			if (screenHeight >= 700 && screenHeight <= 1536 && supported.Contains(1536))
				return "1536";

			int closest = supported[0];
			int distance = Math.Abs(screenHeight - closest);
			foreach (int candidate in supported)
			{
				int diff = Math.Abs(screenHeight - candidate);
				if (diff < distance) { distance = diff; closest = candidate; }
			}
			return closest.ToString(CultureInfo.InvariantCulture);
		}
	}
}
