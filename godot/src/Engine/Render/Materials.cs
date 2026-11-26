using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using G = Godot;

namespace Svz
{
	/// Re-implements the game's Unity 3 shaders as Godot spatial shaders.
	public static class Materials
	{
		[Flags]
		public enum F
		{
			None = 0, Lit = 1, VColor = 2, Alpha = 4, Add = 8, Premul = 16, Cutout = 32, CullOff = 64, NoZWrite = 128,
			TwoTex = 256, Lightmap = 512, Text = 1024, Rim = 2048, ColorIntensity = 4096, MainInt = 8192, Tint2x = 16384, SignedAdd = 32768,
		}

		public struct Kind
		{
			public F flags;
			public string tint;      // uniform name of the tint color in the Unity material
		}

		public static Kind Classify(string shader)
		{
			switch (shader)
			{
				case "Griptonite/TextureVertColors": return new Kind { flags = F.VColor | F.MainInt, tint = "_MainColor" };
				case "Griptonite/TextureVertColorsAlpha": return new Kind { flags = F.VColor | F.MainInt | F.Alpha, tint = "_MainColor" };
				case "Griptonite/TextureVertexColorsCutoff": return new Kind { flags = F.VColor | F.MainInt | F.Cutout, tint = "_MainColor" };
				case "Griptonite/DiffuseWithColorIntensity": return new Kind { flags = F.Lit | F.ColorIntensity, tint = "_MainColor" };
				case "Griptonite/DiffuseVertColors": return new Kind { flags = F.Lit | F.VColor | F.MainInt, tint = "_MainColor" };
				case "Griptonite/DiffuseRimLight": return new Kind { flags = F.Lit | F.Rim, tint = "" };
				case "Mobile/OpaqueEthereal": return new Kind { flags = F.Lit | F.Rim, tint = "" };
				case "Mobile/Ethereal": return new Kind { flags = F.Lit | F.Rim | F.Alpha, tint = "" };
				case "AlphaBlend": return new Kind { flags = F.Alpha | F.NoZWrite, tint = "_TintColor" };
				case "Mobile/Particles/Additive": return new Kind { flags = F.VColor | F.Add | F.CullOff | F.NoZWrite, tint = "" };
				case "Mobile/Particles/Alpha Blended": return new Kind { flags = F.VColor | F.Alpha | F.CullOff | F.NoZWrite, tint = "" };
				case "Particles/Alpha Blended": return new Kind { flags = F.VColor | F.Alpha | F.CullOff | F.NoZWrite | F.Tint2x, tint = "_TintColor" };
				case "Particles/Additive": return new Kind { flags = F.VColor | F.Add | F.CullOff | F.NoZWrite | F.Tint2x, tint = "_TintColor" };
				case "Particles/Alpha Blended Premultiply": return new Kind { flags = F.VColor | F.Premul | F.CullOff | F.NoZWrite, tint = "" };
				case "GUI/Text Shader": return new Kind { flags = F.Text | F.VColor | F.Alpha | F.CullOff | F.NoZWrite, tint = "_Color" };
				case "GripUnlit/TwoTextureBlend (Supports Lightmap)": return new Kind { flags = F.TwoTex | F.VColor, tint = "" };
				case "GripUnlit/Transparent Cutout With Vert Colors": return new Kind { flags = F.VColor | F.Cutout, tint = "" };
				case "Transparent/Cutout/Soft Edge Unlit": return new Kind { flags = F.Cutout, tint = "_Color" };
				case "Transparent/Cutout/Diffuse":
				case "Transparent/Cutout/VertexLit": return new Kind { flags = F.Lit | F.Cutout, tint = "_Color" };
				case "Transparent/Diffuse":
				case "Transparent/Specular":
				case "Transparent/VertexLit": return new Kind { flags = F.Lit | F.Alpha, tint = "_Color" };
				case "Unlit/Texture":
				case "Mobile/Unlit (Supports Lightmap)": return new Kind { flags = F.None, tint = "" };
				case "Billboard/Displacement Map Shader": return new Kind { flags = F.Alpha | F.NoZWrite | F.CullOff, tint = "" };
				default:
					if (shader != null && shader.StartsWith("Particles/")) return new Kind { flags = F.VColor | F.Alpha | F.CullOff | F.NoZWrite | F.Tint2x, tint = "_TintColor" };
					return new Kind { flags = F.Lit, tint = "_Color" };   // Diffuse, Mobile/Diffuse, Specular, Bumped ...
			}
		}

		private static readonly Dictionary<F, G.Shader> sShaders = new Dictionary<F, G.Shader>();

		private static G.Shader ShaderFor(F f)
		{
			if (sShaders.TryGetValue(f, out var s)) return s;
			s = new G.Shader { Code = Build(f) };
			sShaders[f] = s;
			return s;
		}

		private static string Build(F f)
		{
			var sb = new StringBuilder("shader_type spatial;\nrender_mode ");
			var modes = new List<string>();
			if ((f & F.Add) != 0) modes.Add("blend_add");
			else if ((f & F.Premul) != 0) modes.Add("blend_premul_alpha");
			else modes.Add("blend_mix");
			bool lightmap = (f & F.Lightmap) != 0;
			if ((f & F.Lit) == 0 || lightmap) modes.Add("unshaded");
			modes.Add((f & F.CullOff) != 0 || System.Environment.GetEnvironmentVariable("SVZ_NOCULL") == "1" ? "cull_disabled" : "cull_back");
			if ((f & F.NoZWrite) != 0) modes.Add("depth_draw_never");
			// Unity's particle/additive shaders turn fog off (Fog { Color (0,0,0,0) }); Godot would paint fog over the whole quad
			if ((f & (F.Add | F.Premul)) != 0 || (f & F.VColor) != 0 && (f & F.CullOff) != 0) modes.Add("fog_disabled");
			modes.Add("specular_disabled");
			sb.Append(string.Join(", ", modes)).Append(";\n");
			sb.Append(@"
uniform sampler2D main_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D second_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D lm_tex : source_color, filter_linear, repeat_disable;
uniform vec4 main_st = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 second_st = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 lm_st = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 tint : source_color = vec4(1.0);
uniform vec4 tint2 : source_color = vec4(1.0);
uniform float main_int = 1.0;
uniform float color_intensity = 0.0;
uniform float cutoff = 0.5;
uniform vec4 rim_color : source_color = vec4(0.0);
uniform float rim_power = 3.0;
uniform float rim_alpha = 1.0;
varying vec4 vcol;
varying vec2 uv_second;
varying vec2 uv_lm;

vec2 unity_st(vec2 uv, vec4 st) { return vec2(uv.x * st.x + st.z, uv.y * st.y + 1.0 - st.y - st.w); }

void vertex() {
	vcol = COLOR;
	vcol.rgb = pow(max(vcol.rgb, vec3(0.0)), vec3(2.2));
	uv_second = unity_st(UV, second_st);
	uv_lm = unity_st(UV2, lm_st);
	UV = unity_st(UV, main_st);
}

void fragment() {
	vec4 t = texture(main_tex, UV);
	vec4 c;
");
			if ((f & F.Text) != 0) sb.Append("\tc = vec4(tint.rgb, tint.a * t.a);\n\tc *= vcol;\n");
			else if ((f & F.TwoTex) != 0)
			{
				sb.Append("\tvec4 t2 = texture(second_tex, uv_second);\n\tc = mix(t, t2, vcol.a);\n");
				if (lightmap) sb.Append("\tc.rgb *= texture(lm_tex, uv_lm).rgb * 2.0;\n\tc.rgb += vcol.rgb - 0.5;\n");
				else sb.Append("\tc.rgb *= vcol.rgb;\n");
				sb.Append("\tc.a = 1.0;\n");
			}
			else if ((f & F.ColorIntensity) != 0) sb.Append("\tc = t * (1.0 - color_intensity) + tint * color_intensity;\n");
			else
			{
				sb.Append("\tc = t * tint;\n");
				if ((f & F.Tint2x) != 0) sb.Append("\tc *= 2.0;\n");
				if ((f & F.VColor) != 0)
				{
					if ((f & F.MainInt) != 0) sb.Append("\tc.rgb *= vcol.rgb * main_int;\n\tc.a *= vcol.a;\n");
					else sb.Append("\tc *= vcol;\n");
				}
			}
			if ((f & F.Premul) != 0) sb.Append("\tc.rgb *= c.a;\n");
			if (lightmap && (f & F.TwoTex) == 0) sb.Append("\tc.rgb *= texture(lm_tex, uv_lm).rgb * 2.0;\n");
			if ((f & F.Rim) != 0)
			{
				sb.Append("\tfloat rim = pow(1.0 - clamp(dot(normalize(VIEW), NORMAL), 0.0, 1.0), rim_power);\n");
				sb.Append("\tEMISSION = rim_color.rgb * rim;\n");
				if ((f & F.Alpha) != 0) sb.Append("\tc.a = clamp(rim_alpha + rim, 0.0, 1.0);\n");
			}
			sb.Append("\tALBEDO = c.rgb;\n");
			if ((f & (F.Alpha | F.Add | F.Premul)) != 0) sb.Append("\tALPHA = clamp(c.a, 0.0, 1.0);\n");
			if ((f & F.Cutout) != 0) sb.Append("\tif (c.a <= cutoff) discard;\n");
			sb.Append("}\n");
			if ((f & F.Lit) != 0 && !lightmap)
				sb.Append(@"
void light() {
	DIFFUSE_LIGHT += max(dot(NORMAL, LIGHT), 0.0) * ATTENUATION * LIGHT_COLOR / PI * 2.0;
}
");
			return WorldGraphics.TuneShader(sb.ToString());
		}

		// one Godot material per (Unity material, lightmap variant)
		private sealed class Entry { public G.ShaderMaterial mat; public int version = -1, lmTexVersion = -1; public F flags; }
		private static readonly Dictionary<(Material, int, Vector4), Entry> sCache = new Dictionary<(Material, int, Vector4), Entry>();
		private static G.StandardMaterial3D sMissing;

		public static G.Material Get(Material m, Renderer r)
		{
			if (m == null || m.__destroyed)
				return sMissing ??= new G.StandardMaterial3D { AlbedoColor = new G.Color(1, 0, 1) };
			int lm = r != null ? r.__lightmapIndex : 255;
			Texture lmTex = lm < 254 ? SceneLightmaps.Get(lm) : null;
			if (lmTex == null) lm = 255;
			var key = (m, lm, lm < 254 ? r.__lightmapST : default);
			if (!sCache.TryGetValue(key, out var e)) sCache[key] = e = new Entry();
			if (e.version == m.__version && e.mat != null && (lmTex == null || e.lmTexVersion == lmTex.__version)) return e.mat;
			string sname = m.__shader?.__shaderName ?? "Diffuse";
			var kind = Classify(sname);
			F flags = kind.flags | (lmTex != null ? F.Lightmap : F.None);
			if (e.mat == null || e.flags != flags) e.mat = new G.ShaderMaterial { Shader = ShaderFor(flags) };
			e.flags = flags;
			var mat = e.mat;
			WorldGraphics.SetParameter(mat, WorldGraphics.Name("main_tex"), TexOrWhite(m, "_MainTex", "_Background"));
			WorldGraphics.SetParameter(mat, WorldGraphics.Name("main_st"), ST(m, "_MainTex", "_Background"));
			if ((flags & F.TwoTex) != 0)
			{
				WorldGraphics.SetParameter(mat, WorldGraphics.Name("second_tex"), TexOrWhite(m, "_SecondTex"));
				WorldGraphics.SetParameter(mat, WorldGraphics.Name("second_st"), ST(m, "_SecondTex"));
			}
			Color tint = Color.white;
			if (!string.IsNullOrEmpty(kind.tint) && m.__colors.TryGetValue(kind.tint, out var tc)) tint = tc;
			else if (kind.tint == "_TintColor") tint = new Color(0.5f, 0.5f, 0.5f, 0.5f);
			WorldGraphics.SetParameter(mat, WorldGraphics.Name("tint"), Render.C(tint));
			WorldGraphics.SetParameter(mat, WorldGraphics.Name("main_int"), m.__floats.TryGetValue("_MainTexInt", out var mi) ? mi : 1f);
			WorldGraphics.SetParameter(mat, WorldGraphics.Name("color_intensity"), m.__floats.TryGetValue("_ColorIntensity", out var ci) ? ci : 0f);
			WorldGraphics.SetParameter(mat, WorldGraphics.Name("cutoff"), m.__floats.TryGetValue("_Cutoff", out var co) ? co : 0.5f);
			WorldGraphics.SetParameter(mat, WorldGraphics.Name("rim_color"), Render.C(m.__colors.TryGetValue("_RimColor", out var rc) ? rc : new Color(0, 0, 0, 0)));
			WorldGraphics.SetParameter(mat, WorldGraphics.Name("rim_power"), m.__floats.TryGetValue("_RimPower", out var rp) ? rp : 3f);
			WorldGraphics.SetParameter(mat, WorldGraphics.Name("rim_alpha"), m.__floats.TryGetValue("_Alpha", out var ra) ? ra : (m.__floats.TryGetValue("_BlendAmt", out var ba) ? ba : 1f));
			if (lmTex != null)
			{
				WorldGraphics.SetParameter(mat, WorldGraphics.Name("lm_tex"), lmTex.__Godot);
				var st = r.__lightmapST;
				// Unity lightmap ST: uv2 * st.xy + st.zw (applied in Unity uv space, handled by unity_st())
				WorldGraphics.SetParameter(mat, WorldGraphics.Name("lm_st"), new G.Vector4(st.x, st.y, st.z, st.w));
				e.lmTexVersion = lmTex.__version;
			}
			int q = m.renderQueue;
			mat.RenderPriority = (flags & (F.Alpha | F.Add | F.Premul)) != 0 ? Math.Clamp((q - 3000) / 4, -127, 127) : Math.Clamp((q - 2000) / 8, -127, 127);
			e.version = m.__version;
			return mat;
		}

		private static G.Texture2D sWhite;
		public static G.Texture2D White
		{
			get
			{
				if (sWhite == null)
				{
					var img = G.Image.CreateEmpty(2, 2, false, G.Image.Format.Rgba8);
					img.Fill(new G.Color(1, 1, 1, 1));
					sWhite = G.ImageTexture.CreateFromImage(img);
				}
				return sWhite;
			}
		}

		private static G.Texture2D TexOrWhite(Material m, params string[] names)
		{
			foreach (var n in names)
				if (m.__tex.TryGetValue(n, out var te) && te.tex != null && !te.tex.__destroyed)
				{
					var g = te.tex.__Godot;
					if (g != null) return g;
				}
			return White;
		}

		private static G.Vector4 ST(Material m, params string[] names)
		{
			foreach (var n in names)
				if (m.__tex.TryGetValue(n, out var te))
					return new G.Vector4(te.scale.x, te.scale.y, te.offset.x, te.offset.y);
			return new G.Vector4(1, 1, 0, 0);
		}
	}

	public static class SceneLightmaps
	{
		public static readonly List<Texture2D> Maps = new List<Texture2D>();
		public static Texture2D Get(int i) => i >= 0 && i < Maps.Count ? Maps[i] : null;
	}
}
