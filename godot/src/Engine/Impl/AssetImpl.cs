using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Svz;
using G = Godot;

namespace UnityEngine
{
	public partial class TextAsset
	{
		internal string __text = "";
		internal byte[] __bytes;

		public partial string text => __text;
		public partial byte[] bytes => __bytes ??= System.Text.Encoding.UTF8.GetBytes(__text);
	}

	public partial class Texture
	{
		internal int __w = 2, __h = 2;
		internal FilterMode __filter = FilterMode.Bilinear;
		internal TextureWrapMode __wrap = TextureWrapMode.Repeat;
		internal int __aniso = 1;
		internal float __mipBias;
		internal int __version;

		private static int sLimit;
		private static AnisotropicFiltering sAniso;
		public static partial int masterTextureLimit { get => sLimit; set => sLimit = value; }
		public static partial AnisotropicFiltering anisotropicFiltering { get => sAniso; set => sAniso = value; }
		public partial FilterMode filterMode { get => __filter; set => __filter = value; }
		public partial int anisoLevel { get => __aniso; set => __aniso = value; }
		public partial TextureWrapMode wrapMode { get => __wrap; set => __wrap = value; }
		public partial float mipMapBias { get => __mipBias; set => __mipBias = value; }
		private static partial int Internal_GetWidth(Texture mono) => mono.__w;
		private static partial int Internal_GetHeight(Texture mono) => mono.__h;
		private static partial void Internal_GetTexelSize(Texture tex, out Vector2 output) => output = new Vector2(1f / tex.__w, 1f / tex.__h);
		public partial int GetNativeTextureID() => GetInstanceID();

		/// Godot texture for rendering
		internal virtual G.Texture2D __Godot => null;
	}

	public partial class Texture2D
	{
		internal string __resPath;        // imported texture (res://tex/..)
		internal G.Texture2D __gtex;
		internal G.Image __img;          // CPU copy, created on demand
		internal bool __imgDirty;
		internal TextureFormat __format = TextureFormat.ARGB32;
		internal bool __mip;

		public partial int mipmapCount => 1;
		public partial TextureFormat format => __format;

		private static partial void Internal_Create(Texture2D mono, int width, int height, TextureFormat format, bool mipmap)
		{
			mono.__w = Math.Max(1, width); mono.__h = Math.Max(1, height);
			mono.__format = format; mono.__mip = mipmap;
			mono.__img = G.Image.CreateEmpty(mono.__w, mono.__h, false, G.Image.Format.Rgba8);
			mono.__img.Fill(new G.Color(1, 1, 1, 1));   // Unity's default for new textures is not defined; white is safe
			mono.__imgDirty = true;
		}

		internal G.Image __Image
		{
			get
			{
				if (__img == null)
				{
					var t = __Godot;
					__img = t?.GetImage() ?? G.Image.CreateEmpty(__w, __h, false, G.Image.Format.Rgba8);
					if (__img.IsCompressed()) __img.Decompress();
					if (__img.GetFormat() != G.Image.Format.Rgba8) __img.Convert(G.Image.Format.Rgba8);
				}
				return __img;
			}
		}

		internal override G.Texture2D __Godot
		{
			get
			{
				if (__imgDirty && __img != null)
				{
					__imgDirty = false;
					if (__gtex is G.ImageTexture it && it.GetWidth() == __img.GetWidth() && it.GetHeight() == __img.GetHeight()) it.Update(__img);
					else __gtex = G.ImageTexture.CreateFromImage(__img);
					__version++;
				}
				if (__gtex == null && __resPath != null)
				{
					__gtex = G.ResourceLoader.Load<G.Texture2D>(__resPath);
					if (__gtex == null) G.GD.PrintErr("[svz] missing texture " + __resPath);
				}
				return __gtex;
			}
		}

		// Unity texture space has y up; Godot images have y down
		private static partial void INTERNAL_CALL_SetPixel(Texture2D self, int x, int y, ref Color color)
		{
			var img = self.__Image;
			if (x < 0 || y < 0 || x >= self.__w || y >= self.__h) return;
			img.SetPixel(x, self.__h - 1 - y, new G.Color(color.r, color.g, color.b, color.a));
		}

		public partial Color GetPixel(int x, int y)
		{
			var img = __Image;
			x = Mathf.Clamp(x, 0, __w - 1); y = Mathf.Clamp(y, 0, __h - 1);
			var c = img.GetPixel(x, __h - 1 - y);
			return new Color(c.R, c.G, c.B, c.A);
		}

		public partial Color GetPixelBilinear(float u, float v) => GetPixel((int)(u * __w), (int)(v * __h));

		public partial void SetPixels(int x, int y, int blockWidth, int blockHeight, Color[] colors, int miplevel)
		{
			var img = __Image;
			for (int j = 0; j < blockHeight; j++)
				for (int i = 0; i < blockWidth; i++)
				{
					var c = colors[j * blockWidth + i];
					int px = x + i, py = y + j;
					if (px < __w && py < __h) img.SetPixel(px, __h - 1 - py, new G.Color(c.r, c.g, c.b, c.a));
				}
		}

		public partial void SetPixels32(Color32[] colors, int miplevel)
		{
			var img = __Image;
			for (int j = 0; j < __h; j++)
				for (int i = 0; i < __w; i++)
				{
					var c = colors[j * __w + i];
					img.SetPixel(i, __h - 1 - j, G.Color.Color8(c.r, c.g, c.b, c.a));
				}
		}

		public partial bool LoadImage(byte[] data)
		{
			var img = new G.Image();
			if (img.LoadPngFromBuffer(data) != G.Error.Ok && img.LoadJpgFromBuffer(data) != G.Error.Ok) return false;
			img.Convert(G.Image.Format.Rgba8);
			__img = img; __w = img.GetWidth(); __h = img.GetHeight(); __imgDirty = true;
			return true;
		}

		public partial Color[] GetPixels(int x, int y, int blockWidth, int blockHeight, int miplevel)
		{
			var r = new Color[blockWidth * blockHeight];
			for (int j = 0; j < blockHeight; j++)
				for (int i = 0; i < blockWidth; i++) r[j * blockWidth + i] = GetPixel(x + i, y + j);
			return r;
		}

		public partial Color32[] GetPixels32(int miplevel)
		{
			var r = new Color32[__w * __h];
			for (int j = 0; j < __h; j++)
				for (int i = 0; i < __w; i++) r[j * __w + i] = GetPixel(i, j);
			return r;
		}

		public partial void Apply(bool updateMipmaps, bool makeNoLongerReadable) { if (__img != null) __imgDirty = true; }

		private static partial bool INTERNAL_CALL_Resize(Texture2D self, int width, int height, TextureFormat format, bool hasMipMap)
		{
			Internal_Create(self, width, height, format, hasMipMap);
			return true;
		}

		private partial bool Internal_ResizeWH(int width, int height)
		{
			Internal_Create(this, width, height, __format, __mip);
			return true;
		}

		private static partial void INTERNAL_CALL_Compress(Texture2D self, bool highQuality) { }

		public partial Rect[] PackTextures(Texture2D[] textures, int padding, int maximumAtlasSize, bool makeNoLongerReadable)
		{
			Debug.LogWarning("PackTextures not supported");
			return textures.Select(_ => new Rect(0, 0, 1, 1)).ToArray();
		}

		private static partial void INTERNAL_CALL_ReadPixels(Texture2D self, ref Rect source, int destX, int destY, bool recalculateMipMaps) { }

		public partial byte[] EncodeToPNG() => __Image.SavePngToBuffer();

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			var s = (Texture2D)src;
			__w = s.__w; __h = s.__h; __resPath = s.__resPath; __gtex = s.__gtex; __format = s.__format;
			__img = s.__img != null ? (G.Image)s.__img.Duplicate() : null; __imgDirty = __img != null;
		}
	}

	public partial class Shader
	{
		internal string __shaderName = "";
		internal int __queue = 2000;
		internal static readonly Dictionary<string, Shader> __byName = new Dictionary<string, Shader>(StringComparer.OrdinalIgnoreCase);
		internal static readonly Dictionary<string, object> __globals = new Dictionary<string, object>();

		public partial bool isSupported => true;
		private int __p_maximumLOD;
		public partial int maximumLOD { get => __p_maximumLOD; set => __p_maximumLOD = value; }private static int sGlobalLod = 1000;
		public static partial int globalMaximumLOD { get => sGlobalLod; set => sGlobalLod = value; }
		public partial int renderQueue => __queue;

		public static partial Shader Find(string name)
		{
			if (string.IsNullOrEmpty(name)) return null;
			if (__byName.TryGetValue(name, out var s) && !s.__destroyed) return s;
			AssetDb.EnsureShadersIndexed();
			if (__byName.TryGetValue(name, out s)) return s;
			// unknown: create a virtual shader the renderer maps by name
			s = new Shader { __shaderName = name, __name = name, __isAsset = true };
			__byName[name] = s;
			return s;
		}

		internal static partial Shader FindBuiltin(string name) => Find(name);
		public static partial void EnableKeyword(string keyword) { }
		public static partial void DisableKeyword(string keyword) { }
		private static partial void INTERNAL_CALL_SetGlobalColor(string propertyName, ref Color color) => __globals[propertyName] = color;
		public static partial void SetGlobalFloat(string propertyName, float value) => __globals[propertyName] = value;
		public static partial void SetGlobalTexture(string propertyName, Texture tex) => __globals[propertyName] = tex;
		private static partial void INTERNAL_CALL_SetGlobalMatrix(string propertyName, ref Matrix4x4 mat) => __globals[propertyName] = mat;
		public static partial void SetGlobalTexGenMode(string propertyName, TexGenMode mode) { }
		public static partial void SetGlobalTextureMatrixName(string propertyName, string matrixName) { }
		private static int sNextProp = 1;
		private static readonly Dictionary<string, int> sProps = new Dictionary<string, int>();
		public static partial int PropertyToID(string name) => sProps.TryGetValue(name, out int i) ? i : sProps[name] = sProps.Count + sNextProp;
		public static partial void WarmupAllShaders() { }

		internal static string ParseName(string script, string fallback)
		{
			if (script != null && script.StartsWith("Shader"))
			{
				int a = script.IndexOf('"'), b = a >= 0 ? script.IndexOf('"', a + 1) : -1;
				if (b > a) return script.Substring(a + 1, b - a - 1);
			}
			return fallback;
		}
	}

	public partial class Material
	{
		internal Shader __shader;
		internal readonly Dictionary<string, TexEnv> __tex = new Dictionary<string, TexEnv>();
		internal readonly Dictionary<string, float> __floats = new Dictionary<string, float>();
		internal readonly Dictionary<string, Color> __colors = new Dictionary<string, Color>();
		internal int __queue = -1;
		internal int __version;
		internal G.Material __gmat;
		internal int __gmatVersion = -1;

		internal class TexEnv { public Texture tex; public Vector2 scale = Vector2.one, offset = Vector2.zero; }

		private void Touch() => __version++;

		public partial Shader shader { get => __shader; set { __shader = value; Touch(); } }
		public partial int passCount => 1;
		public partial int renderQueue { get => __queue >= 0 ? __queue : (__shader?.__queue ?? 2000); set { __queue = value; Touch(); } }

		private static partial void INTERNAL_CALL_SetColor(Material self, string propertyName, ref Color color) { self.__colors[propertyName] = color; self.Touch(); }
		public partial Color GetColor(string propertyName) => __colors.TryGetValue(propertyName, out var c) ? c : (propertyName == "_Color" || propertyName == "_TintColor" ? Color.white : new Color(0, 0, 0, 0));

		private TexEnv Env(string name)
		{
			if (!__tex.TryGetValue(name, out var e)) __tex[name] = e = new TexEnv();
			return e;
		}

		public partial void SetTexture(string propertyName, Texture texture) { Env(propertyName).tex = texture; Touch(); }
		public partial Texture GetTexture(string propertyName) => __tex.TryGetValue(propertyName, out var e) ? e.tex : null;
		private static partial void Internal_GetTextureOffset(Material mat, string name, out Vector2 output) => output = mat.__tex.TryGetValue(name, out var e) ? e.offset : Vector2.zero;
		private static partial void Internal_GetTextureScale(Material mat, string name, out Vector2 output) => output = mat.__tex.TryGetValue(name, out var e) ? e.scale : Vector2.one;
		private static partial void Internal_GetTexturePivot(Material mat, string name, out Vector2 output) => output = Vector2.zero;
		private static partial void INTERNAL_CALL_SetTextureOffset(Material self, string propertyName, ref Vector2 offset) { self.Env(propertyName).offset = offset; self.Touch(); }
		private static partial void INTERNAL_CALL_SetTextureScale(Material self, string propertyName, ref Vector2 scale) { self.Env(propertyName).scale = scale; self.Touch(); }
		private static partial void INTERNAL_CALL_SetMatrix(Material self, string propertyName, ref Matrix4x4 matrix) { }
		public partial Matrix4x4 GetMatrix(string propertyName) => Matrix4x4.identity;
		public partial void SetFloat(string propertyName, float value) { __floats[propertyName] = value; Touch(); }
		public partial float GetFloat(string propertyName) => __floats.TryGetValue(propertyName, out var f) ? f : 0f;
		public partial bool HasProperty(string propertyName) => __floats.ContainsKey(propertyName) || __colors.ContainsKey(propertyName) || __tex.ContainsKey(propertyName) || propertyName == "_Color" || propertyName == "_MainTex";
		public partial string GetTag(string tag, bool searchFallbacks, string defaultValue) => defaultValue;

		public partial void Lerp(Material start, Material end, float t)
		{
			foreach (var k in start.__colors.Keys.Union(end.__colors.Keys)) __colors[k] = Color.Lerp(start.GetColor(k), end.GetColor(k), t);
			foreach (var k in start.__floats.Keys.Union(end.__floats.Keys)) __floats[k] = Mathf.Lerp(start.GetFloat(k), end.GetFloat(k), t);
			Touch();
		}

		public partial bool SetPass(int pass) => true;
		private static partial void Internal_CreateWithString(Material mono, string contents) => mono.__shader = Shader.Find(Shader.ParseName(contents, "Diffuse"));
		private static partial void Internal_CreateWithShader(Material mono, Shader shader) => mono.__shader = shader;
		private static partial void Internal_CreateWithMaterial(Material mono, Material source) => mono.CopyPropertiesFromMaterial(source);

		public partial void CopyPropertiesFromMaterial(Material mat)
		{
			if (mat == null) return;
			__shader = mat.__shader;
			__tex.Clear();
			foreach (var kv in mat.__tex) __tex[kv.Key] = new TexEnv { tex = kv.Value.tex, scale = kv.Value.scale, offset = kv.Value.offset };
			__floats.Clear(); foreach (var kv in mat.__floats) __floats[kv.Key] = kv.Value;
			__colors.Clear(); foreach (var kv in mat.__colors) __colors[kv.Key] = kv.Value;
			__queue = mat.__queue;
			Touch();
		}

		private static Material sDefault;
		internal static Material __Default()
		{
			if (sDefault == null) sDefault = new Material(Shader.Find("Diffuse")) { name = "Default-Diffuse" };
			return sDefault;
		}

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			CopyPropertiesFromMaterial((Material)src);
		}
	}

	public partial class Mesh
	{
		internal string __binPath;
		internal bool __loaded = true;
		internal Vector3[] __v = new Vector3[0], __n = new Vector3[0];
		internal Vector4[] __t = new Vector4[0];
		internal Vector2[] __uv = new Vector2[0], __uv2 = new Vector2[0];
		internal Color[] __c = new Color[0];
		internal List<int[]> __subs = new List<int[]> { new int[0] };
		internal BoneWeight[] __bw = new BoneWeight[0];
		internal Matrix4x4[] __bind = new Matrix4x4[0];
		internal Bounds __bounds;
		internal int __version;
		internal G.ArrayMesh __gmesh;
		internal int __gmeshVersion = -1;

		private void L() { if (!__loaded) MeshBin.Load(this); }
		private void Touch() => __version++;

		private static partial void Internal_Create(Mesh mono) { }
		public partial Vector3[] vertices { get { L(); return (Vector3[])__v.Clone(); } set { L(); __v = value ?? new Vector3[0]; Touch(); } }
		public partial Vector3[] normals { get { L(); return (Vector3[])__n.Clone(); } set { L(); __n = value ?? new Vector3[0]; Touch(); } }
		public partial Vector4[] tangents { get { L(); return (Vector4[])__t.Clone(); } set { L(); __t = value ?? new Vector4[0]; Touch(); } }
		public partial Vector2[] uv { get { L(); return (Vector2[])__uv.Clone(); } set { L(); __uv = value ?? new Vector2[0]; Touch(); } }
		public partial Vector2[] uv2 { get { L(); return (Vector2[])__uv2.Clone(); } set { L(); __uv2 = value ?? new Vector2[0]; Touch(); } }
		public partial Color[] colors { get { L(); return (Color[])__c.Clone(); } set { L(); __c = value ?? new Color[0]; Touch(); } }
		public partial int[] triangles
		{
			get { L(); return __subs.SelectMany(s => s).ToArray(); }
			set { L(); __subs = new List<int[]> { value ?? new int[0] }; Touch(); }
		}
		public partial int vertexCount { get { L(); return __v.Length; } }
		public partial int subMeshCount
		{
			get { L(); return __subs.Count; }
			set { L(); while (__subs.Count < value) __subs.Add(new int[0]); if (__subs.Count > value) __subs.RemoveRange(value, __subs.Count - value); Touch(); }
		}
		public partial BoneWeight[] boneWeights { get { L(); return (BoneWeight[])__bw.Clone(); } set { L(); __bw = value ?? new BoneWeight[0]; Touch(); } }
		public partial Matrix4x4[] bindposes { get { L(); return (Matrix4x4[])__bind.Clone(); } set { L(); __bind = value ?? new Matrix4x4[0]; Touch(); } }

		private static partial void INTERNAL_CALL_Clear(Mesh self)
		{
			self.L();
			self.__v = new Vector3[0]; self.__n = new Vector3[0]; self.__t = new Vector4[0];
			self.__uv = new Vector2[0]; self.__uv2 = new Vector2[0]; self.__c = new Color[0];
			self.__subs = new List<int[]> { new int[0] }; self.__bw = new BoneWeight[0];
			self.Touch();
		}

		private partial void INTERNAL_get_bounds(out Bounds value) { L(); value = __bounds; }
		private partial void INTERNAL_set_bounds(ref Bounds value) => __bounds = value;

		private static partial void INTERNAL_CALL_RecalculateBounds(Mesh self)
		{
			self.L();
			if (self.__v.Length == 0) { self.__bounds = default; return; }
			Vector3 mn = self.__v[0], mx = self.__v[0];
			foreach (var v in self.__v) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
			self.__bounds = new Bounds((mn + mx) * 0.5f, mx - mn);
		}

		private static partial void INTERNAL_CALL_RecalculateNormals(Mesh self)
		{
			self.L();
			var n = new Vector3[self.__v.Length];
			foreach (var s in self.__subs)
				for (int i = 0; i + 2 < s.Length; i += 3)
				{
					Vector3 a = self.__v[s[i]], b = self.__v[s[i + 1]], c = self.__v[s[i + 2]];
					Vector3 f = Vector3.Cross(b - a, c - a);
					n[s[i]] += f; n[s[i + 1]] += f; n[s[i + 2]] += f;
				}
			for (int i = 0; i < n.Length; i++) n[i] = n[i].normalized;
			self.__n = n; self.Touch();
		}

		private static partial void INTERNAL_CALL_Optimize(Mesh self) { }
		public partial int[] GetTriangles(int submesh) { L(); return submesh < __subs.Count ? (int[])__subs[submesh].Clone() : new int[0]; }

		public partial void SetTriangles(int[] triangles, int submesh)
		{
			L();
			while (__subs.Count <= submesh) __subs.Add(new int[0]);
			__subs[submesh] = triangles ?? new int[0];
			Touch();
		}

		internal partial void SetTrianglesDontRebuildCollisionTriangles(int[] triangles, bool isStrip, int submesh) =>
			SetTriangles(isStrip ? MeshBin.StripToList(triangles) : triangles, submesh);

		public partial void SetTriangleStrip(int[] triangles, int submesh) => SetTriangles(MeshBin.StripToList(triangles), submesh);
		public partial int[] GetTriangleStrip(int submesh) => GetTriangles(submesh);

		public partial void CombineMeshes(CombineInstance[] combine, bool mergeSubMeshes, bool useMatrices)
		{
			var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var c = new List<Color>();
			var subs = new List<List<int>>();
			foreach (var ci in combine)
			{
				var m = ci.mesh; if (m == null) continue;
				m.L();
				int baseV = v.Count;
				Matrix4x4 mat = useMatrices ? ci.transform : Matrix4x4.identity;
				foreach (var p in m.__v) v.Add(mat.MultiplyPoint3x4(p));
				foreach (var p in m.__n) n.Add(mat.MultiplyVector(p).normalized);
				uv.AddRange(m.__uv.Length == m.__v.Length ? m.__uv : new Vector2[m.__v.Length]);
				c.AddRange(m.__c.Length == m.__v.Length ? m.__c : Enumerable.Repeat(Color.white, m.__v.Length));
				var tri = m.GetTriangles(ci.subMeshIndex).Select(i => i + baseV);
				if (mergeSubMeshes) { if (subs.Count == 0) subs.Add(new List<int>()); subs[0].AddRange(tri); }
				else subs.Add(tri.ToList());
			}
			__v = v.ToArray(); __n = n.Count == v.Count ? n.ToArray() : new Vector3[0]; __uv = uv.ToArray(); __c = c.ToArray();
			__subs = subs.Select(s => s.ToArray()).ToList();
			if (__subs.Count == 0) __subs.Add(new int[0]);
			INTERNAL_CALL_RecalculateBounds(this);
			Touch();
		}

		internal override void __CopyFrom(Object src, Cloner cl)
		{
			base.__CopyFrom(src, cl);
			var s = (Mesh)src; s.L();
			__v = (Vector3[])s.__v.Clone(); __n = (Vector3[])s.__n.Clone(); __t = (Vector4[])s.__t.Clone();
			__uv = (Vector2[])s.__uv.Clone(); __uv2 = (Vector2[])s.__uv2.Clone(); __c = (Color[])s.__c.Clone();
			__subs = s.__subs.Select(x => (int[])x.Clone()).ToList(); __bw = (BoneWeight[])s.__bw.Clone(); __bind = (Matrix4x4[])s.__bind.Clone();
			__bounds = s.__bounds; Touch();
		}
	}

	public partial class Font
	{
		internal Material __material;
		internal Texture __texture;
		internal float __lineSpacing, __ascent, __kerning = 1f;
		internal int __asciiStart;
		internal readonly Dictionary<int, Glyph> __glyphs = new Dictionary<int, Glyph>();

		internal struct Glyph { public Rect uv, vert; public float width; }

		public partial Material material { get => __material; set => __material = value; }
		private string[] __p_fontNames = new string[0];
		public partial string[] fontNames { get => __p_fontNames; set => __p_fontNames = value; }
		public partial bool HasCharacter(char c) => __glyphs.ContainsKey(c);
	}
}

namespace Svz
{
	public static partial class Builtins
	{
		static partial void RegisterAssets();

		internal static void RegisterAssetLoaders()
		{
			AssetDb.Register("TextAsset", (o, d, ld) =>
			{
				var t = (UnityEngine.TextAsset)o;
				var s = d.Get("m_Script");
				if (s.ValueKind == JsonValueKind.String) t.__text = s.GetString();
				else { t.__bytes = s.Bytes(); t.__text = System.Text.Encoding.UTF8.GetString(t.__bytes); }
			});
			AssetDb.Register("Texture2D", (o, d, ld) =>
			{
				var t = (UnityEngine.Texture2D)o;
				t.__w = d.Int("m_Width"); t.__h = d.Int("m_Height");
				t.__format = (UnityEngine.TextureFormat)d.Int("m_TextureFormat");
				var ts = d.Get("m_TextureSettings");
				t.__filter = (UnityEngine.FilterMode)ts.Int("m_FilterMode", 1);
				t.__wrap = (UnityEngine.TextureWrapMode)ts.Int("m_WrapMode");
				t.__resPath = $"res://tex/{ld.file}/{AssetDb.CurrentPid}.png";
			});
			AssetDb.Register("Shader", (o, d, ld) =>
			{
				var s = (UnityEngine.Shader)o;
				string script = d.Str("m_Script");
				s.__shaderName = UnityEngine.Shader.ParseName(script, d.Str("m_Name"));
				s.__queue = ShaderInfo.QueueFromSource(script);
				if (!UnityEngine.Shader.__byName.ContainsKey(s.__shaderName)) UnityEngine.Shader.__byName[s.__shaderName] = s;
			});
			AssetDb.Register("Material", (o, d, ld) =>
			{
				var m = (UnityEngine.Material)o;
				m.__shader = ld.Ref<UnityEngine.Shader>(d.Get("m_Shader"));
				var sp = d.Get("m_SavedProperties");
				foreach (var e in sp.Get("m_TexEnvs").EnumerateArray())
				{
					string n = e[0].Str("name");
					var v = e[1];
					m.__tex[n] = new UnityEngine.Material.TexEnv { tex = ld.Ref<UnityEngine.Texture>(v.Get("m_Texture")), scale = v.Get("m_Scale").V2(), offset = v.Get("m_Offset").V2() };
				}
				foreach (var e in sp.Get("m_Floats").EnumerateArray()) m.__floats[e[0].Str("name")] = (float)e[1].GetDouble();
				foreach (var e in sp.Get("m_Colors").EnumerateArray()) m.__colors[e[0].Str("name")] = e[1].Col();
			});
			AssetDb.Register("Mesh", (o, d, ld) =>
			{
				var m = (UnityEngine.Mesh)o;
				m.__binPath = $"res://data/mesh/{ld.file}_{AssetDb.CurrentPid}.bin";
				m.__loaded = false;
				var subs = d.Get("m_SubMeshes");
				if (subs.ValueKind == JsonValueKind.Array && subs.GetArrayLength() > 0)
				{
					var aabb = subs[0].Get("localAABB");
					m.__bounds = new UnityEngine.Bounds(aabb.Get("m_Center").V3(), aabb.Get("m_Extent").V3() * 2f);
				}
			});
			AssetDb.Register("Font", (o, d, ld) =>
			{
				var f = (UnityEngine.Font)o;
				f.__material = ld.Ref<UnityEngine.Material>(d.Get("m_DefaultMaterial"));
				f.__texture = ld.Ref<UnityEngine.Texture>(d.Get("m_Texture"));
				f.__lineSpacing = d.Float("m_LineSpacing");
				f.__ascent = d.Float("m_Ascent");
				f.__kerning = d.Float("m_Kerning", 1f);
				f.__asciiStart = d.Int("m_AsciiStartOffset");
				foreach (var c in d.Get("m_CharacterRects").EnumerateArray())
					f.__glyphs[c.Int("index")] = new UnityEngine.Font.Glyph { uv = c.Get("uv").R(), vert = c.Get("vert").R(), width = c.Float("width") };
			});
		}
	}

	public static class ShaderInfo
	{
		public static int QueueFromSource(string src)
		{
			if (string.IsNullOrEmpty(src)) return 2000;
			int i = src.IndexOf("\"Queue\"=\"", StringComparison.Ordinal);
			if (i < 0) return 2000;
			string q = src.Substring(i + 9, Math.Min(20, src.Length - i - 9));
			q = q.Substring(0, q.IndexOf('"') < 0 ? q.Length : q.IndexOf('"'));
			int off = 0, p = q.IndexOfAny(new[] { '+', '-' });
			string b = p > 0 ? q.Substring(0, p) : q;
			if (p > 0) int.TryParse(q.Substring(p), out off);
			int baseQ = b switch { "Background" => 1000, "Geometry" => 2000, "AlphaTest" => 2450, "Transparent" => 3000, "Overlay" => 4000, _ => 2000 };
			return baseQ + off;
		}
	}

	public static class MeshBin
	{
		public static void Load(UnityEngine.Mesh m)
		{
			m.__loaded = true;
			byte[] b;
			try { b = AssetDb.ReadBytes(m.__binPath); }
			catch (Exception e) { Godot.GD.PrintErr($"[svz] mesh {m.__binPath}: {e.Message}"); return; }
			using var r = new BinaryReader(new MemoryStream(b));
			if (new string(r.ReadChars(4)) != "SVZM") return;
			int n = r.ReadInt32(), flags = r.ReadInt32(), nsub = r.ReadInt32(), nbind = r.ReadInt32();
			UnityEngine.Vector3 V3() => new UnityEngine.Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
			UnityEngine.Vector2 V2() => new UnityEngine.Vector2(r.ReadSingle(), r.ReadSingle());
			m.__v = new UnityEngine.Vector3[n]; for (int i = 0; i < n; i++) m.__v[i] = V3();
			if ((flags & 1) != 0) { m.__n = new UnityEngine.Vector3[n]; for (int i = 0; i < n; i++) m.__n[i] = V3(); }
			if ((flags & 2) != 0) { m.__uv = new UnityEngine.Vector2[n]; for (int i = 0; i < n; i++) m.__uv[i] = V2(); }
			if ((flags & 4) != 0) { m.__uv2 = new UnityEngine.Vector2[n]; for (int i = 0; i < n; i++) m.__uv2[i] = V2(); }
			if ((flags & 8) != 0) { m.__c = new UnityEngine.Color[n]; for (int i = 0; i < n; i++) { var c = r.ReadBytes(4); m.__c[i] = new UnityEngine.Color(c[0] / 255f, c[1] / 255f, c[2] / 255f, c[3] / 255f); } }
			if ((flags & 16) != 0) { m.__t = new UnityEngine.Vector4[n]; for (int i = 0; i < n; i++) m.__t[i] = new UnityEngine.Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
			if ((flags & 32) != 0)
			{
				m.__bw = new UnityEngine.BoneWeight[n];
				for (int i = 0; i < n; i++)
				{
					var w = new UnityEngine.BoneWeight();
					w.boneIndex0 = r.ReadInt32(); w.weight0 = r.ReadSingle();
					w.boneIndex1 = r.ReadInt32(); w.weight1 = r.ReadSingle();
					w.boneIndex2 = r.ReadInt32(); w.weight2 = r.ReadSingle();
					w.boneIndex3 = r.ReadInt32(); w.weight3 = r.ReadSingle();
					m.__bw[i] = w;
				}
			}
			m.__bind = new UnityEngine.Matrix4x4[nbind];
			for (int k = 0; k < nbind; k++)
			{
				var mt = new UnityEngine.Matrix4x4();
				for (int row = 0; row < 4; row++)
					for (int col = 0; col < 4; col++) mt[row, col] = r.ReadSingle();
				m.__bind[k] = mt;
			}
			m.__subs = new List<int[]>();
			for (int s = 0; s < nsub; s++)
			{
				int cnt = r.ReadInt32();
				var idx = new int[cnt];
				for (int i = 0; i < cnt; i++) idx[i] = r.ReadInt32();
				m.__subs.Add(idx);
			}
			if (m.__subs.Count == 0) m.__subs.Add(new int[0]);
			m.__version++;
		}

		public static int[] StripToList(int[] strip)
		{
			var l = new List<int>();
			for (int i = 0; i + 2 < strip.Length; i++)
			{
				int a = strip[i], b = strip[i + 1], c = strip[i + 2];
				if (a == b || b == c || a == c) continue;
				if (i % 2 == 0) { l.Add(a); l.Add(b); l.Add(c); } else { l.Add(b); l.Add(a); l.Add(c); }
			}
			return l.ToArray();
		}
	}
}
