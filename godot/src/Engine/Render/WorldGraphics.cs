using System;
using System.Runtime.CompilerServices;
using Godot;

namespace Svz
{
    /// Restore world-only texture sampling; original artwork and GUI atlases stay unchanged.
    public static class WorldGraphics
    {
        private sealed class TextureEntry { public Texture2D Texture; }
        private sealed class ShaderEntry { public bool World; }
        private static readonly ConditionalWeakTable<Texture2D, TextureEntry> Textures = new();
        private static readonly ConditionalWeakTable<Shader, ShaderEntry> Shaders = new();
        private static readonly ConditionalWeakTable<ShaderMaterial, ShaderEntry> Materials = new();
        private static readonly StringName MainTexture = new("main_tex"), SecondTexture = new("second_tex");
        private static readonly bool Legacy = Array.IndexOf(OS.GetCmdlineUserArgs(), "--test-legacy-textures") >= 0;
        public const long MemoryBudget = 24 * 1024 * 1024;
        public static long PreparedBytes { get; private set; }
        public static int PreparedCount { get; private set; }
        private static readonly System.Collections.Generic.Dictionary<string, StringName> Names = new(StringComparer.Ordinal);
        public static int NamesCreated => Names.Count;
        /// Shader parameter names are constant: reuse one StringName per name instead of allocating a
        /// native StringName (and a tracked disposable) on every material refresh.
        public static StringName Name(string name)
        {
            if (!Names.TryGetValue(name, out var cached)) Names[name] = cached = new StringName(name);
            return cached;
        }
        public static string TuneShader(string code)
        {
            if (Legacy || code == null || code.Contains("ALPHA =", StringComparison.Ordinal) ||
                code.Contains("discard;", StringComparison.Ordinal) || code.Contains("blend_add", StringComparison.Ordinal) ||
                code.Contains("blend_premul_alpha", StringComparison.Ordinal)) return code;
            return code.Replace("filter_linear_mipmap, repeat_enable", "filter_linear_mipmap_anisotropic, repeat_enable", StringComparison.Ordinal);
        }
        public static long MipBytes(int width, int height)
        {
            if (width < 1 || height < 1 || width > 1024 || height > 1024) return 0;
            long size = 0;
            while (true) { size += (long)width * height * 4; if (width == 1 && height == 1) return size;
                width = Math.Max(1, width / 2); height = Math.Max(1, height / 2); }
        }
        public static Texture2D Prepare(Texture2D source) => source == null || Legacy ? source : Textures.GetValue(source, BuildTexture).Texture;
        private static TextureEntry BuildTexture(Texture2D source)
        {
            long bytes = MipBytes(source.GetWidth(), source.GetHeight());
            if (bytes == 0 || PreparedBytes + bytes > MemoryBudget) return new TextureEntry { Texture = source };
            try
            {
                var original = source.GetImage();
                using var image = original?.Duplicate() as Image;
                if (image == null || image.IsEmpty() || image.HasMipmaps()) return new TextureEntry { Texture = source };
                if (image.IsCompressed() && image.Decompress() != Error.Ok) return new TextureEntry { Texture = source };
                image.Convert(Image.Format.Rgba8);
                if (image.GenerateMipmaps() != Error.Ok) return new TextureEntry { Texture = source };
                var texture = ImageTexture.CreateFromImage(image);
                PreparedBytes += bytes; PreparedCount++;
                GD.Print($"[svz-world] mipmaps restored {image.GetWidth()}x{image.GetHeight()} budget={PreparedBytes}/{MemoryBudget}");
                return new TextureEntry { Texture = texture };
            }
            catch (Exception e) { GD.Print("[svz-world] original texture retained: " + e.Message); return new TextureEntry { Texture = source }; }
        }
        public static void SetParameter(ShaderMaterial material, StringName parameter, Variant value)
        {
            try
            {
                if (value.VariantType == Variant.Type.Object && value.AsGodotObject() is Texture2D texture &&
                    (parameter == MainTexture || parameter == SecondTexture) &&
                    Materials.GetValue(material, mat => mat.Shader == null ? new ShaderEntry() : Shaders.GetValue(mat.Shader,
                        shader => new ShaderEntry { World = shader.Code.Contains("filter_linear_mipmap_anisotropic", StringComparison.Ordinal) })).World)
                    material.SetShaderParameter(parameter, Prepare(texture));
                else material.SetShaderParameter(parameter, value);
            }
            finally { value.Dispose(); }
        }
    }
}
