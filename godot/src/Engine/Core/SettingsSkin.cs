using System;
using Godot;

namespace Svz
{
    /// Visual theme for the native audio settings modal and its entry buttons.
    ///
    /// The original game draws dialogs on a parchment scroll with wooden rollers, uses red-lacquer
    /// framed buttons with white outlined lettering, and confirms with a green check. When the
    /// installed game data contains those textures they are referenced at runtime (never copied
    /// into this repository); otherwise a procedural approximation with the same palette is used.
    /// Each texture is accepted only when its exact size matches the pinned 3.4.0 data.
    public static class SettingsSkin
    {
        // Texture ids in the exported game data (res://tex/<id>/1.png) and their expected sizes.
        public const string ScrollPath = "res://tex/3058/1.png", ButtonPath = "res://tex/2613/1.png", CheckPath = "res://tex/574/1.png";
        private static readonly Vector2I ScrollSize = new(1024, 1024), ButtonSize = new(256, 128), CheckSize = new(144, 144);
        // Opaque region of the scroll and its rollers/top/bottom edges (texture pixels).
        private static readonly Rect2 ScrollRegion = new(47, 206, 930, 611);
        // Painted frame of the blank button, excluding its transparent padding.
        private static readonly Rect2 ButtonRegion = new(20, 16, 214, 100);

        public static readonly Color Ink = new("2b0d05"), Cream = new("fff4dc"), Gold = new("e2b245"),
            DarkGold = new("8a5a14"), Lacquer = new("a3201a"), DarkLacquer = new("4a0906"), Parchment = new("e8c48a"),
            TitleRed = new("d3261a");

        public static bool UsesOriginalArt { get; private set; }
        public static Texture2D CheckIcon { get; private set; }
        public static Theme Theme { get; private set; }

        public const int ButtonHeight = 84, RowHeight = 76, EntryWidth = 280;

        public static Texture2D TryLoad(string path, Vector2I size)
        {
            try
            {
                if (!ResourceLoader.Exists(path)) return null;
                var texture = ResourceLoader.Load<Texture2D>(path);
                return Accept(texture, size) ? texture : null;
            }
            catch (Exception) { return null; }
        }

        /// Only the exact pinned texture dimensions are accepted; other data falls back to the procedural skin.
        public static bool Accept(Texture2D texture, Vector2I size) =>
            texture != null && texture.GetWidth() == size.X && texture.GetHeight() == size.Y;

        public static Theme Build(bool allowOriginal = true)
        {
            var scroll = allowOriginal ? TryLoad(ScrollPath, ScrollSize) : null;
            var button = allowOriginal ? TryLoad(ButtonPath, ButtonSize) : null;
            var check = allowOriginal ? TryLoad(CheckPath, CheckSize) : null;
            UsesOriginalArt = scroll != null && button != null && check != null;
            if (!UsesOriginalArt) scroll = button = check = null;
            GD.Print("[svz-settings] menu art: " + (UsesOriginalArt ? "original game textures" : "procedural fallback"));

            var theme = new Theme();
            theme.SetStylebox("panel", "PanelContainer", Panel(scroll));
            ButtonStyles(theme, "Button", button);
            ButtonStyles(theme, "CheckButton", button);
            foreach (var type in new[] { "Button", "CheckButton" })
            {
                theme.SetColor("font_color", type, Cream); theme.SetColor("font_hover_color", type, Colors.White);
                theme.SetColor("font_pressed_color", type, Cream); theme.SetColor("font_hover_pressed_color", type, Colors.White);
                theme.SetColor("font_focus_color", type, Colors.White); theme.SetColor("font_outline_color", type, Ink);
                theme.SetConstant("outline_size", type, 8); theme.SetFontSize("font_size", type, 28);
                theme.SetConstant("h_separation", type, 14);
            }
            theme.SetColor("font_color", "Label", Cream); theme.SetColor("font_outline_color", "Label", Ink);
            theme.SetConstant("outline_size", "Label", 8); theme.SetFontSize("font_size", "Label", 26);

            CheckIcon = Icon(check, 56);
            var emptyBox = EmptyBox(56);
            foreach (var name in new[] { "checked", "checked_mirrored", "checked_disabled", "checked_disabled_mirrored" })
                theme.SetIcon(name, "CheckButton", CheckIcon);
            foreach (var name in new[] { "unchecked", "unchecked_mirrored", "unchecked_disabled", "unchecked_disabled_mirrored" })
                theme.SetIcon(name, "CheckButton", emptyBox);

            theme.SetStylebox("slider", "HSlider", Track(new Color("3a1a0c"), 9));
            theme.SetStylebox("grabber_area", "HSlider", Track(Lacquer, 9));
            theme.SetStylebox("grabber_area_highlight", "HSlider", Track(Lacquer.Lightened(0.15f), 9));
            var coin = Coin(54, false); var coinLit = Coin(54, true);
            theme.SetIcon("grabber", "HSlider", coin); theme.SetIcon("grabber_highlight", "HSlider", coinLit);
            theme.SetIcon("grabber_disabled", "HSlider", coin);
            theme.SetStylebox("focus", "HSlider", Focus());
            Theme = theme;
            return theme;
        }

        private static StyleBox Panel(Texture2D scroll)
        {
            if (scroll != null)
                return new StyleBoxTexture { Texture = scroll, RegionRect = ScrollRegion,
                    TextureMarginLeft = 150, TextureMarginRight = 150, TextureMarginTop = 70, TextureMarginBottom = 80,
                    ContentMarginLeft = 158, ContentMarginRight = 158, ContentMarginTop = 52, ContentMarginBottom = 56 };
            return new StyleBoxFlat { BgColor = Parchment, BorderColor = new Color("6b3510"),
                BorderWidthLeft = 34, BorderWidthRight = 34, BorderWidthTop = 6, BorderWidthBottom = 6,
                CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18, CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
                ShadowColor = new Color(0, 0, 0, 0.45f), ShadowSize = 10,
                ContentMarginLeft = 158, ContentMarginRight = 158, ContentMarginTop = 52, ContentMarginBottom = 56 };
        }

        private static StyleBox Frame(Texture2D button, Color modulate, float pressOffset)
        {
            if (button != null)
                return new StyleBoxTexture { Texture = button, RegionRect = ButtonRegion, ModulateColor = modulate,
                    TextureMarginLeft = 18, TextureMarginRight = 18, TextureMarginTop = 16, TextureMarginBottom = 18,
                    ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 10 + pressOffset, ContentMarginBottom = 10 - pressOffset };
            return new StyleBoxFlat { BgColor = Lacquer * modulate, BorderColor = DarkLacquer,
                BorderWidthLeft = 6, BorderWidthRight = 6, BorderWidthTop = 6, BorderWidthBottom = 6,
                CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
                ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 4, ShadowOffset = new Vector2(0, 3),
                ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 10 + pressOffset, ContentMarginBottom = 10 - pressOffset };
        }

        private static void ButtonStyles(Theme theme, string type, Texture2D button)
        {
            theme.SetStylebox("normal", type, Frame(button, Colors.White, 0));
            theme.SetStylebox("hover", type, Frame(button, new Color(1.12f, 1.06f, 1.0f), 0));
            theme.SetStylebox("pressed", type, Frame(button, new Color(0.78f, 0.72f, 0.72f), 2));
            theme.SetStylebox("hover_pressed", type, Frame(button, new Color(0.85f, 0.78f, 0.78f), 2));
            theme.SetStylebox("disabled", type, Frame(button, new Color(0.6f, 0.6f, 0.6f), 0));
            theme.SetStylebox("focus", type, Focus());
        }

        /// Gold outline for keyboard/controller focus; touch presses do not show it.
        private static StyleBox Focus() => new StyleBoxFlat { DrawCenter = false, BorderColor = Gold,
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ExpandMarginLeft = 4, ExpandMarginRight = 4, ExpandMarginTop = 4, ExpandMarginBottom = 4 };

        private static StyleBoxFlat Track(Color fill, int half) => new StyleBoxFlat { BgColor = fill, BorderColor = DarkGold,
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = half, CornerRadiusTopRight = half, CornerRadiusBottomLeft = half, CornerRadiusBottomRight = half,
            ContentMarginTop = half, ContentMarginBottom = half };

        /// Scales a texture's image once (icons are drawn at their native pixel size).
        private static Texture2D Icon(Texture2D source, int size)
        {
            if (source == null) return CheckFallback(size);
            using var image = source.GetImage();
            if (image == null) return CheckFallback(size);
            var copy = (Image)image.Duplicate();
            if (copy.IsCompressed()) copy.Decompress();
            copy.Resize(size, size, Image.Interpolation.Lanczos);
            return ImageTexture.CreateFromImage(copy);
        }

        private static Image Canvas(int size) => Image.CreateEmpty(size, size, false, Image.Format.Rgba8);

        /// Gold medallion grabber, drawn procedurally (antialiased by coverage).
        public static Texture2D Coin(int size, bool lit)
        {
            var img = Canvas(size); float c = (size - 1) / 2f, r = size / 2f - 1;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - c, dy = y - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float cover = Mathf.Clamp(r - d + 0.5f, 0, 1);
                    if (cover <= 0) continue;
                    Color col;
                    if (d > r - 3.2f) col = DarkLacquer;                                   // dark rim
                    else if (d > r - 7f) col = DarkGold.Lerp(Gold, Mathf.Clamp((-dy / r) * 0.8f + 0.5f, 0, 1)); // bevel
                    else
                    {
                        float shade = Mathf.Clamp(0.65f - (dx + dy) / (2.4f * r), 0, 1);   // light from top-left
                        col = new Color("c27a12").Lerp(new Color("ffd75e"), shade);
                        if (Mathf.Abs(d - (r - 12)) < 1.1f) col = col.Darkened(0.35f);      // engraved ring
                    }
                    if (lit) col = col.Lightened(0.12f);
                    col.A = cover; img.SetPixel(x, y, col);
                }
            return ImageTexture.CreateFromImage(img);
        }

        /// Unchecked box: parchment square in a dark lacquer frame.
        public static Texture2D EmptyBox(int size)
        {
            var img = Canvas(size); int inset = 6, frame = 5;
            for (int y = inset; y < size - inset; y++)
                for (int x = inset; x < size - inset; x++)
                {
                    bool edge = x < inset + frame || y < inset + frame || x >= size - inset - frame || y >= size - inset - frame;
                    img.SetPixel(x, y, edge ? DarkLacquer : Parchment.Darkened(0.08f * (y - inset) / size));
                }
            return ImageTexture.CreateFromImage(img);
        }

        private static Texture2D CheckFallback(int size)
        {
            var img = Canvas(size); var box = EmptyBox(size).GetImage();
            img.BlitRect(box, new Rect2I(0, 0, size, size), Vector2I.Zero);
            var green = new Color("1f9a2a");
            // two thick strokes of a check mark
            void Stroke(Vector2 a, Vector2 b)
            {
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        var p = new Vector2(x, y); var ab = b - a;
                        float t = Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0, 1);
                        float d = (a + ab * t - p).Length();
                        if (d < 5.5f) img.SetPixel(x, y, d < 3.8f ? green : new Color(0.05f, 0.2f, 0.05f));
                    }
            }
            Stroke(new Vector2(size * 0.22f, size * 0.52f), new Vector2(size * 0.42f, size * 0.74f));
            Stroke(new Vector2(size * 0.42f, size * 0.74f), new Vector2(size * 0.82f, size * 0.22f));
            return ImageTexture.CreateFromImage(img);
        }
    }
}
