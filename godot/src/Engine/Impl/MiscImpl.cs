using System;
using System.Linq;
using Svz;
using SceneSettings = Svz.SceneSettings;

namespace UnityEngine
{
	public partial struct LayerMask
	{
		public static partial string LayerToName(int layer) => Layers.Name(layer);
		public static partial int NameToLayer(string layerName) => Layers.Index(layerName);
	}
}

namespace Svz
{
	public static class Layers
	{
		private static string[] sNames;

		private static string[] Names
		{
			get
			{
				if (sNames != null) return sNames;
				sNames = new string[32];
				int main = AssetDb.FileKey("mainData");
				foreach (var o in AssetDb.File(main).objects.Values)
				{
					if (o.GetProperty("c").GetString() != "TagManager") continue;
					var d = o.GetProperty("d");
					for (int i = 0; i < 32; i++)
					{
						string key = (i < 8 ? "Builtin Layer " : "User Layer ") + i;
						sNames[i] = d.Str(key);
					}
				}
				return sNames;
			}
		}

		public static string Name(int layer) => layer >= 0 && layer < 32 ? Names[layer] ?? "" : "";
		public static int Index(string name) => string.IsNullOrEmpty(name) ? -1 : Array.IndexOf(Names, name);
	}
}

namespace UnityEngine
{
	// parameterless constructors used by the asset loader
	public partial class Texture2D { internal Texture2D() { } }
	public partial class Material { internal Material() { } }
	public partial class Cubemap { internal Cubemap() { } }
	public partial class RenderTexture { internal RenderTexture() { } }
}

namespace UnityEngine
{
	public sealed partial class RenderSettings
	{
		public static partial bool fog { get => SceneSettings.Fog; set { SceneSettings.Fog = value; Svz.Render.OnSceneSettings(); } }
		public static partial FogMode fogMode { get => (FogMode)SceneSettings.FogMode; set => SceneSettings.FogMode = (int)value; }
		public static partial float fogDensity { get => SceneSettings.FogDensity; set { SceneSettings.FogDensity = value; Svz.Render.OnSceneSettings(); } }
		public static partial float fogStartDistance { get => SceneSettings.FogStart; set => SceneSettings.FogStart = value; }
		public static partial float fogEndDistance { get => SceneSettings.FogEnd; set => SceneSettings.FogEnd = value; }
		public static partial float haloStrength { get => 0.5f; set { } }
		public static partial float flareStrength { get => 1f; set { } }
		private static Material sSkybox;
		public static partial Material skybox { get => sSkybox; set => sSkybox = value; }
		private static partial void INTERNAL_get_fogColor(out Color value) => value = SceneSettings.FogColor;
		private static partial void INTERNAL_set_fogColor(ref Color value) { SceneSettings.FogColor = value; Svz.Render.OnSceneSettings(); }
		private static partial void INTERNAL_get_ambientLight(out Color value) => value = SceneSettings.Ambient;
		private static partial void INTERNAL_set_ambientLight(ref Color value) { SceneSettings.Ambient = value; Svz.Render.OnSceneSettings(); }
	}
}
