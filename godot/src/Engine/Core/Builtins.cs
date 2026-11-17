using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Svz
{
	/// Registers loaders for the built-in Unity classes.
	public static partial class Builtins
	{
		public static void Init()
		{
			AssetDb.Register("Transform", (o, d, ld) =>
			{
				var t = (Transform)o;
				t.__lp = d.Get("m_LocalPosition").V3();
				t.__lr = d.Get("m_LocalRotation").Q();
				t.__ls = d.Get("m_LocalScale").V3();
				var father = ld.Ref<Transform>(d.Get("m_Father"));
				t.__parent = father;
				t.__children.Clear();
				foreach (var c in ld.Refs<Transform>(d.Get("m_Children")))
				{
					if (c == null) continue;
					c.__parent = t;
					if (!t.__children.Contains(c)) t.__children.Add(c);
				}
				// make sure the owner GameObject exists (prefab children are reached through transforms)
				ld.Ref<GameObject>(d.Get("m_GameObject"));
				Transform.__version++;
			});
			RegisterRendering();
			RegisterAnimation();
			RegisterAudio();
			RegisterPhysics();
			RegisterParticles();
			RegisterGui();
		}

		static partial void RegisterRendering();
		static partial void RegisterAnimation();
		static partial void RegisterAudio();
		static partial void RegisterPhysics();
		static partial void RegisterParticles();
		static partial void RegisterGui();
	}

	public static class Builtin
	{
		public static Object Get(Type type, string path) => null;
	}

	public static class SceneSettings
	{
		public static Color Ambient = new Color(0.2f, 0.2f, 0.2f, 1f);
		public static bool Fog;
		public static Color FogColor;
		public static float FogDensity, FogStart, FogEnd;
		public static int FogMode;

		public static void Apply(JsonElement d, Loader ld)
		{
			Ambient = d.Get("m_AmbientLight").Col();
			Fog = d.Bool("m_Fog");
			FogColor = d.Get("m_FogColor").Col();
			FogMode = d.Int("m_FogMode");
			FogDensity = d.Float("m_FogDensity");
			FogStart = d.Float("m_LinearFogStart");
			FogEnd = d.Float("m_LinearFogEnd");
			Render.OnSceneSettings();
		}
	}
}
