using System.Linq;
using System.Text.Json;
using UnityEngine;

namespace Svz
{
	public static partial class Builtins
	{
		static partial void RegisterRendering()
		{
			RegisterAssetLoaders();
			AssetDb.Register("MeshFilter", (o, d, ld) => ((MeshFilter)o).__mesh = ld.Ref<Mesh>(d.Get("m_Mesh")));
			AssetDb.PopulateFn renderer = (o, d, ld) =>
			{
				var r = (Renderer)o;
				r.__enabled = d.Bool("m_Enabled", true);
				r.__mats = ld.Refs<Material>(d.Get("m_Materials")).ToArray();
				r.__castShadows = d.Bool("m_CastShadows", true);
				r.__receiveShadows = d.Bool("m_ReceiveShadows", true);
				r.__lightmapIndex = d.Int("m_LightmapIndex", 255);
				r.__lightmapST = d.Get("m_LightmapTilingOffset").V4();
				var subs = d.Get("m_SubsetIndices");
				if (subs.ValueKind == JsonValueKind.Array && subs.GetArrayLength() > 0)
					r.__subsets = subs.EnumerateArray().Select(x => x.AsInt()).ToArray();
				r.__batchRoot = ld.Ref<Transform>(d.Get("m_StaticBatchRoot"));
			};
			AssetDb.Register("MeshRenderer", renderer);
			AssetDb.Register("SkinnedMeshRenderer", (o, d, ld) =>
			{
				renderer(o, d, ld);
				var s = (SkinnedMeshRenderer)o;
				s.__smesh = ld.Ref<Mesh>(d.Get("m_Mesh"));
				s.__bones = ld.Refs<Transform>(d.Get("m_Bones")).ToArray();
				s.__quality = (SkinQuality)d.Int("m_Quality");
				s.__updateOffscreen = d.Bool("m_UpdateWhenOffscreen");
				var aabb = d.Get("m_AABB");
				s.__localBounds = new Bounds(aabb.Get("m_Center").V3(), aabb.Get("m_Extent").V3() * 2f);
			});
			AssetDb.Register("Camera", (o, d, ld) =>
			{
				var c = (Camera)o;
				c.__clear = (CameraClearFlags)d.Int("m_ClearFlags", 1);
				c.__bg = d.Get("m_BackGroundColor").Col();
				c.__rect = d.Get("m_NormalizedViewPortRect").R();
				c.__near = d.Float("near clip plane", 0.3f);
				c.__far = d.Float("far clip plane", 1000f);
				c.__fov = d.Float("field of view", 60f);
				c.__orthographic = d.Bool("orthographic");
				c.__ortho = d.Float("orthographic size", 5f);
				c.__depth = d.Float("m_Depth");
				c.__cull = d.Get("m_CullingMask").Int("m_Bits", -1);
			});
			AssetDb.Register("Light", (o, d, ld) =>
			{
				var l = (Light)o;
				l.__type = (LightType)d.Int("m_Type", 2);
				l.__color = d.Get("m_Color").Col();
				l.__intensity = d.Float("m_Intensity", 1f);
				l.__range = d.Float("m_Range", 10f);
				l.__spot = d.Float("m_SpotAngle", 30f);
				l.__cull = d.Get("m_CullingMask").Int("m_Bits", -1);
				l.__mode = (LightRenderMode)d.Int("m_RenderMode");
				// baked-only lights (Lightmapping == 2 "BakedOnly") must not light again at runtime
				l.__lightmapped = d.Int("m_Lightmapping") == 2 && d.Bool("m_ActuallyLightmapped");
			});
			AssetDb.Register("GUIText", (o, d, ld) =>
			{
				var t = (GUIText)o;
				t.__text = d.Str("m_Text");
				t.__anchor = (TextAnchor)d.Int("m_Anchor");
				t.__align = (TextAlignment)d.Int("m_Alignment");
				t.__pixelOffset = d.Get("m_PixelOffset").V2();
				t.__lineSpacing = d.Float("m_LineSpacing", 1f);
				t.__tabSize = d.Float("m_TabSize", 4f);
				t.__font = ld.Ref<Font>(d.Get("m_Font"));
				t.__material = ld.Ref<Material>(d.Get("m_Material"));
				t.__fontSize = d.Int("m_FontSize");
				t.__style = (FontStyle)d.Int("m_FontStyle");
			});
			AssetDb.Register("GUITexture", (o, d, ld) =>
			{
				var t = (GUITexture)o;
				t.__texture = ld.Ref<Texture>(d.Get("m_Texture"));
				t.__color = d.Get("m_Color").Col();
				t.__inset = d.Get("m_PixelInset").R();
			});
		}
	}

	public static partial class AssetDb
	{
		public static void LoadLightmaps(JsonElement d, Loader ld)
		{
			SceneLightmaps.Maps.Clear();
			foreach (var lm in d.Get("m_Lightmaps").EnumerateArray())
				SceneLightmaps.Maps.Add(ld.Ref<Texture2D>(lm.Get("m_Lightmap")));
		}
	}
}
