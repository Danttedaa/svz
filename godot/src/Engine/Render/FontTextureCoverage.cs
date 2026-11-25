using System;
using System.Runtime.CompilerServices;
using Godot;

namespace Svz
{
	/// Unity's GUI/Text shader takes coverage from the atlas alpha channel;
	/// atlas RGB must not multiply the GUIText material color. Legacy Alpha8
	/// exports can have black RGB, which otherwise turns foreground and shadow
	/// into two overlapping black copies of every label.
	public static class FontTextureCoverage
	{
		private sealed class Atlas { public Texture2D Texture; }
		private static readonly ConditionalWeakTable<Texture2D, Atlas> sAtlases = new();

		public static Texture2D ForFont(Texture2D source) =>
			source == null ? null : sAtlases.GetValue(source, Create).Texture;

		/// White luminance plus untouched coverage; source data is never changed.
		public static byte[] AlphaOnly(byte[] rgba, int pixelCount)
		{
			if (rgba == null) throw new ArgumentNullException(nameof(rgba));
			if (pixelCount < 0 || (long)pixelCount * 4 > rgba.Length)
				throw new ArgumentException("Invalid RGBA coverage buffer");
			var la = new byte[checked(pixelCount * 2)];
			for (int i = 0; i < pixelCount; i++)
			{
				la[i * 2] = 255;
				la[i * 2 + 1] = rgba[i * 4 + 3];
			}
			return la;
		}

		private static Atlas Create(Texture2D source)
		{
			try
			{
				using var image = source.GetImage();
				if (image == null || image.IsEmpty()) return new Atlas { Texture = source };
				if (image.IsCompressed() && image.Decompress() != Error.Ok)
					return new Atlas { Texture = source };
				image.Convert(Image.Format.Rgba8);
				int width = image.GetWidth(), height = image.GetHeight();
				var coverage = AlphaOnly(image.GetData(), checked(width * height));
				using var white = Image.CreateFromData(width, height, false, Image.Format.La8, coverage);
				var result = ImageTexture.CreateFromImage(white);
				GD.Print($"[svz-ui] font alpha-only coverage prepared: {width}x{height}");
				return new Atlas { Texture = result };
			}
			catch (Exception e)
			{
				GD.Print("[svz-ui] font coverage fallback: " + e.Message);
				return new Atlas { Texture = source };
			}
		}
	}
}
