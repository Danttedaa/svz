using System;
using System.Collections.Generic;
using System.Linq;
using Svz;
using G = Godot;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEngine
{
	public partial class Renderer
	{
		internal Material[] __mats = new Material[0];
		internal bool __enabled = true;
		internal int[] __subsets;            // static batching: submeshes of the combined mesh this renderer draws
		internal Transform __batchRoot;
		internal bool __castShadows = true, __receiveShadows = true;
		internal int __lightmapIndex = 255;
		internal Vector4 __lightmapST = new Vector4(1, 1, 0, 0);
		internal G.MeshInstance3D __node;
		internal bool __visible;

		internal partial Transform staticBatchRootTransform { get => null; set { } }
		internal partial int staticBatchIndex => 0;
		public partial bool isPartOfStaticBatch => false;
		public partial bool enabled { get => __enabled; set => __enabled = value; }
		public partial bool castShadows { get => __castShadows; set => __castShadows = value; }
		public partial bool receiveShadows { get => __receiveShadows; set => __receiveShadows = value; }

		public partial Material material
		{
			get
			{
				if (__mats.Length == 0) return null;
				__Instance(0);
				return __mats[0];
			}
			set { if (__mats.Length == 0) __mats = new Material[1]; __mats[0] = value; }
		}

		// Unity: accessing .material makes a per-renderer copy
		private readonly HashSet<int> __instanced = new HashSet<int>();
		internal void __Instance(int i)
		{
			if (__mats[i] == null || __instanced.Contains(i)) return;
			var m = new Material(__mats[i]) { name = __mats[i].name + " (Instance)" };
			__mats[i] = m;
			__instanced.Add(i);
		}

		public partial Material sharedMaterial
		{
			get => __mats.Length > 0 ? __mats[0] : null;
			set { if (__mats.Length == 0) __mats = new Material[1]; __mats[0] = value; __instanced.Remove(0); }
		}

		public partial Material[] sharedMaterials { get => (Material[])__mats.Clone(); set { __mats = value ?? new Material[0]; __instanced.Clear(); } }

		public partial Material[] materials
		{
			get { for (int i = 0; i < __mats.Length; i++) __Instance(i); return (Material[])__mats.Clone(); }
			set { __mats = value ?? new Material[0]; }
		}

		public partial Bounds bounds
		{
			get
			{
				var m = __Mesh;
				var b = m != null ? m.bounds : new Bounds(Vector3.zero, Vector3.zero);
				var l2w = transform.localToWorldMatrix;
				Vector3 c = l2w.MultiplyPoint3x4(b.center);
				Vector3 e = b.extents, s = transform.lossyScale;
				return new Bounds(c, Vector3.Scale(e * 2f, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z))));
			}
		}

		public partial int lightmapIndex { get => __lightmapIndex; set => __lightmapIndex = value; }
		public partial bool isVisible => __visible;
		internal partial void SetSubsetIndex(int index, int subSetIndexForMaterial) { }
		private partial void INTERNAL_get_worldToLocalMatrix(out Matrix4x4 value) => value = transform.worldToLocalMatrix;
		private partial void INTERNAL_get_localToWorldMatrix(out Matrix4x4 value) => value = transform.localToWorldMatrix;
		private partial void INTERNAL_get_lightmapTilingOffset(out Vector4 value) => value = __lightmapST;
		private partial void INTERNAL_set_lightmapTilingOffset(ref Vector4 value) => __lightmapST = value;
		public partial void Render(int material) { }

		internal virtual Mesh __Mesh => GetComponent<MeshFilter>()?.__mesh;

		internal override void __OnAttach() => Svz.Render.Register(this);
		internal override void __OnDestroyNative() => Svz.Render.Unregister(this);

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			var s = (Renderer)src;
			__mats = (Material[])s.__mats.Clone();
			__castShadows = s.__castShadows; __receiveShadows = s.__receiveShadows;
			__lightmapIndex = s.__lightmapIndex; __lightmapST = s.__lightmapST;
			__subsets = s.__subsets; __batchRoot = s.__batchRoot != null && c != null ? c.Remap(s.__batchRoot) : s.__batchRoot;
		}
	}

	public partial class MeshFilter
	{
		internal Mesh __mesh;
		private bool __instanced;

		public partial Mesh mesh
		{
			get
			{
				if (__mesh != null && !__instanced)
				{
					var m = new Mesh();
					m.__CopyFrom(__mesh, null);
					m.name = __mesh.name + " Instance";
					__mesh = m; __instanced = true;
				}
				else if (__mesh == null) { __mesh = new Mesh(); __instanced = true; }
				return __mesh;
			}
			set { __mesh = value; __instanced = true; }
		}

		public partial Mesh sharedMesh { get => __mesh; set { __mesh = value; __instanced = false; } }

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			__mesh = ((MeshFilter)src).__mesh;
		}
	}

	public partial class SkinnedMeshRenderer
	{
		internal Transform[] __bones = new Transform[0];
		internal Mesh __smesh;
		internal SkinQuality __quality;
		internal bool __updateOffscreen;
		internal Bounds __localBounds;
		internal G.Skeleton3D __skel;
		internal G.Skin __skin;
		internal int __skinVersion = -1;

		public partial Transform[] bones { get => (Transform[])__bones.Clone(); set { __bones = value ?? new Transform[0]; __skinVersion = -1; } }
		public partial SkinQuality quality { get => __quality; set => __quality = value; }
		public partial Mesh sharedMesh { get => __smesh; set { __smesh = value; __skinVersion = -1; } }
		public partial bool updateWhenOffscreen { get => __updateOffscreen; set => __updateOffscreen = value; }
		private partial void INTERNAL_get_localBounds(out Bounds value) => value = __localBounds;
		private partial void INTERNAL_set_localBounds(ref Bounds value) => __localBounds = value;
		internal override Mesh __Mesh => __smesh;

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			var s = (SkinnedMeshRenderer)src;
			__bones = s.__bones.Select(b => c != null ? c.Remap(b) : b).ToArray();
			__smesh = s.__smesh; __quality = s.__quality; __localBounds = s.__localBounds;
		}
	}

	public partial class Light
	{
		internal LightType __type = LightType.Point;
		internal float __intensity = 1f, __range = 10f, __spot = 30f;
		internal Color __color = Color.white;
		internal int __cull = -1;
		internal LightRenderMode __mode;
		internal bool __lightmapped;
		internal G.Light3D __node;

		public partial LightType type { get => __type; set => __type = value; }
		public partial float intensity { get => __intensity; set => __intensity = value; }
		public partial LightShadows shadows { get => LightShadows.None; set { } }
		public partial float shadowStrength { get => 1f; set { } }
		public partial float shadowBias { get => 0.05f; set { } }
		public partial float shadowSoftness { get => 4f; set { } }
		public partial float shadowSoftnessFade { get => 1f; set { } }
		public partial float range { get => __range; set => __range = value; }
		public partial float spotAngle { get => __spot; set => __spot = value; }
		public partial Texture cookie { get => null; set { } }
		public partial Flare flare { get => null; set { } }
		public partial LightRenderMode renderMode { get => __mode; set => __mode = value; }
		public partial int cullingMask { get => __cull; set => __cull = value; }
		private static int sPixelLights = 4;
		public static partial int pixelLightCount { get => sPixelLights; set => sPixelLights = value; }
		private partial void INTERNAL_get_color(out Color value) => value = __color;
		private partial void INTERNAL_set_color(ref Color value) => __color = value;
		public static partial Light[] GetLights(LightType type, int layer) => Svz.Render.Lights.Where(l => l.__type == type).ToArray();

		internal override void __OnAttach() => Svz.Render.Lights.Add(this);
		internal override void __OnDestroyNative() { Svz.Render.Lights.Remove(this); __node?.QueueFree(); __node = null; }

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			var s = (Light)src;
			__type = s.__type; __intensity = s.__intensity; __range = s.__range; __spot = s.__spot; __color = s.__color;
			__cull = s.__cull; __mode = s.__mode; __lightmapped = s.__lightmapped;
		}
	}

	public partial class Camera
	{
		internal float __fov = 60f, __near = 0.3f, __far = 1000f, __ortho = 5f, __depth;
		internal bool __orthographic;
		internal int __cull = -1;
		internal CameraClearFlags __clear = CameraClearFlags.Skybox;
		internal Color __bg = new Color(0.19f, 0.3f, 0.47f, 0f);
		internal Rect __rect = new Rect(0, 0, 1, 1);
		internal float __aspect = -1f;
		internal Matrix4x4? __customProj, __customW2C;
		internal G.SubViewport __vp;
		internal G.Camera3D __cam;
		internal G.TextureRect __view;
		internal G.Environment __env;
		internal float[] __layerCull = new float[32];
		private static Camera sCurrent;

		public partial float fieldOfView { get => __fov; set => __fov = value; }
		public partial float nearClipPlane { get => __near; set => __near = value; }
		public partial float farClipPlane { get => __far; set => __far = value; }
		public partial RenderingPath renderingPath { get => RenderingPath.Forward; set { } }
		public partial RenderingPath actualRenderingPath => RenderingPath.Forward;
		public partial float orthographicSize { get => __ortho; set => __ortho = value; }
		public partial bool orthographic { get => __orthographic; set => __orthographic = value; }
		public partial float depth { get => __depth; set => __depth = value; }
		public partial float aspect { get => __aspect > 0 ? __aspect : pixelWidth / Mathf.Max(1f, pixelHeight); set => __aspect = value; }
		public partial int cullingMask { get => __cull; set => __cull = value; }
		public partial RenderTexture targetTexture { get => null; set { } }
		public partial float pixelWidth => __rect.width * Screen.width;
		public partial float pixelHeight => __rect.height * Screen.height;
		public partial CameraClearFlags clearFlags { get => __clear; set => __clear = value; }

		public static partial Camera main
		{
			get
			{
				Camera best = null;
				foreach (var c in Svz.Render.Cameras)
					if (c.__enabled && c.gameObject.active && c.gameObject.CompareTag("MainCamera") && (best == null || c.__depth < best.__depth)) best = c;
				return best;
			}
		}

		public static partial Camera current => sCurrent ?? main;
		public static partial Camera[] allCameras => Svz.Render.Cameras.Where(c => c.__enabled && c.gameObject.active).ToArray();
		public partial float[] layerCullDistances { get => (float[])__layerCull.Clone(); set => __layerCull = value ?? new float[32]; }
		public partial DepthTextureMode depthTextureMode { get => DepthTextureMode.None; set { } }
		private partial void INTERNAL_get_backgroundColor(out Color value) => value = __bg;
		private partial void INTERNAL_set_backgroundColor(ref Color value) => __bg = value;
		private partial void INTERNAL_get_rect(out Rect value) => value = __rect;
		private partial void INTERNAL_set_rect(ref Rect value) => __rect = value;
		private partial void INTERNAL_get_pixelRect(out Rect value) => value = new Rect(__rect.x * Screen.width, __rect.y * Screen.height, pixelWidth, pixelHeight);
		private partial void INTERNAL_set_pixelRect(ref Rect value) => __rect = new Rect(value.x / Screen.width, value.y / Screen.height, value.width / Screen.width, value.height / Screen.height);

		// Unity camera space is right handed with -Z forward (OpenGL convention)
		internal Matrix4x4 __W2C
		{
			get
			{
				if (__customW2C.HasValue) return __customW2C.Value;
				var flip = Matrix4x4.Scale(new Vector3(1, 1, -1));
				return flip * transform.worldToLocalMatrix;
			}
		}

		internal Matrix4x4 __Proj
		{
			get
			{
				if (__customProj.HasValue) return __customProj.Value;
				return __orthographic
					? Matrix4x4.Ortho(-__ortho * aspect, __ortho * aspect, -__ortho, __ortho, __near, __far)
					: Matrix4x4.Perspective(__fov, aspect, __near, __far);
			}
		}

		private partial void INTERNAL_get_cameraToWorldMatrix(out Matrix4x4 value) => value = __W2C.inverse;
		private partial void INTERNAL_get_worldToCameraMatrix(out Matrix4x4 value) => value = __W2C;
		private partial void INTERNAL_set_worldToCameraMatrix(ref Matrix4x4 value) => __customW2C = value;
		private static partial void INTERNAL_CALL_ResetWorldToCameraMatrix(Camera self) => self.__customW2C = null;
		private partial void INTERNAL_get_projectionMatrix(out Matrix4x4 value) => value = __Proj;
		private partial void INTERNAL_set_projectionMatrix(ref Matrix4x4 value) => __customProj = value;
		private static partial void INTERNAL_CALL_ResetProjectionMatrix(Camera self) => self.__customProj = null;
		private static partial void INTERNAL_CALL_ResetAspect(Camera self) => self.__aspect = -1f;
		private partial void INTERNAL_get_velocity(out Vector3 value) => value = Vector3.zero;

		private static partial Vector3 INTERNAL_CALL_WorldToViewportPoint(Camera self, ref Vector3 position)
		{
			Vector3 cam = self.__W2C.MultiplyPoint3x4(position);
			Vector4 clip = self.__Proj * new Vector4(cam.x, cam.y, cam.z, 1f);
			float w = Mathf.Abs(clip.w) < 1e-9f ? 1e-9f : clip.w;
			return new Vector3((clip.x / w + 1f) * 0.5f, (clip.y / w + 1f) * 0.5f, -cam.z);
		}

		private static partial Vector3 INTERNAL_CALL_WorldToScreenPoint(Camera self, ref Vector3 position)
		{
			Vector3 v = INTERNAL_CALL_WorldToViewportPoint(self, ref position);
			return self.__ViewportToScreen(v);
		}

		internal Vector3 __ViewportToScreen(Vector3 v) =>
			new Vector3((__rect.x + v.x * __rect.width) * Screen.width, (__rect.y + v.y * __rect.height) * Screen.height, v.z);

		internal Vector3 __ScreenToViewport(Vector3 p) =>
			new Vector3((p.x / Screen.width - __rect.x) / __rect.width, (p.y / Screen.height - __rect.y) / __rect.height, p.z);

		private static partial Vector3 INTERNAL_CALL_ViewportToWorldPoint(Camera self, ref Vector3 position)
		{
			// position.z = distance from camera along view direction
			Matrix4x4 inv = (self.__Proj * self.__W2C).inverse;
			float ndcX = position.x * 2f - 1f, ndcY = position.y * 2f - 1f;
			Vector3 near = inv.MultiplyPoint(new Vector3(ndcX, ndcY, -1f));
			Vector3 far = inv.MultiplyPoint(new Vector3(ndcX, ndcY, 1f));
			Vector3 camPos = self.transform.position, fwd = self.transform.forward;
			Vector3 dir = far - near;
			float denom = Vector3.Dot(dir, fwd);
			if (Mathf.Abs(denom) < 1e-9f) return near;
			float t = (position.z - Vector3.Dot(near - camPos, fwd)) / denom;
			return near + dir * t;
		}

		private static partial Vector3 INTERNAL_CALL_ScreenToWorldPoint(Camera self, ref Vector3 position)
		{
			Vector3 v = self.__ScreenToViewport(position);
			return INTERNAL_CALL_ViewportToWorldPoint(self, ref v);
		}

		private static partial Vector3 INTERNAL_CALL_ScreenToViewportPoint(Camera self, ref Vector3 position) => self.__ScreenToViewport(position);
		private static partial Vector3 INTERNAL_CALL_ViewportToScreenPoint(Camera self, ref Vector3 position) => self.__ViewportToScreen(position);

		private static partial Ray INTERNAL_CALL_ViewportPointToRay(Camera self, ref Vector3 position)
		{
			Vector3 a = new Vector3(position.x, position.y, self.__near), b = new Vector3(position.x, position.y, self.__far);
			Vector3 pa = INTERNAL_CALL_ViewportToWorldPoint(self, ref a), pb = INTERNAL_CALL_ViewportToWorldPoint(self, ref b);
			return new Ray(pa, (pb - pa).normalized);
		}

		private static partial Ray INTERNAL_CALL_ScreenPointToRay(Camera self, ref Vector3 position)
		{
			Vector3 v = self.__ScreenToViewport(position);
			return INTERNAL_CALL_ViewportPointToRay(self, ref v);
		}

		public partial float GetScreenWidth() => Screen.width;
		public partial float GetScreenHeight() => Screen.height;
		public partial void DoClear() { }
		public partial void Render() { }
		public partial void RenderWithShader(Shader shader, string replacementTag) { }
		public partial void SetReplacementShader(Shader shader, string replacementTag) { }
		private static partial void INTERNAL_CALL_ResetReplacementShader(Camera self) { }
		public partial void RenderDontRestore() { }
		public static partial void SetupCurrent(Camera cur) => sCurrent = cur;
		private partial bool Internal_RenderToCubemapRT(RenderTexture cubemap, int faceMask) => false;
		private partial bool Internal_RenderToCubemapTexture(Cubemap cubemap, int faceMask) => false;

		public partial void CopyFrom(Camera other)
		{
			__fov = other.__fov; __near = other.__near; __far = other.__far; __ortho = other.__ortho; __orthographic = other.__orthographic;
			__depth = other.__depth; __cull = other.__cull; __clear = other.__clear; __bg = other.__bg; __rect = other.__rect;
		}

		internal partial bool IsFiltered(GameObject go) => (__cull & (1 << go.layer)) == 0;

		internal override void __OnAttach() => Svz.Render.Cameras.Add(this);
		internal override void __OnDestroyNative() => Svz.Render.RemoveCamera(this);

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			CopyFrom((Camera)src);
		}
	}
}

namespace Svz
{
	public static partial class Render
	{
		public static readonly List<Renderer> Renderers = new List<Renderer>();
		public static readonly List<Camera> Cameras = new List<Camera>();
		public static readonly List<UnityEngine.Light> Lights = new List<UnityEngine.Light>();
		private static G.Control sViews;
		private static G.Environment sEnv;
		private static G.WorldEnvironment sWorldEnv;

		public static void Register(Renderer r) => Renderers.Add(r);

		public static void Unregister(Renderer r)
		{
			Renderers.Remove(r);
			if (r.__node != null) { r.__node.QueueFree(); r.__node = null; }
			if (r is SkinnedMeshRenderer s && s.__skel != null) { s.__skel.QueueFree(); s.__skel = null; }
		}

		public static void RemoveCamera(Camera c)
		{
			Cameras.Remove(c);
			c.__view?.QueueFree(); c.__vp?.QueueFree();
			c.__view = null; c.__vp = null; c.__cam = null;
		}

		// ---------------------------------------------------------------- coordinate conversion

		/// Unity (left handed) -> Godot (right handed): mirror Z.
		public static G.Vector3 V(UnityEngine.Vector3 v) => new G.Vector3(v.x, v.y, -v.z);

		public static G.Transform3D M(Matrix4x4 m)
		{
			// S * m * S with S = diag(1,1,-1)
			var basis = new G.Basis(
				new G.Vector3(m.m00, m.m10, -m.m20),
				new G.Vector3(m.m01, m.m11, -m.m21),
				new G.Vector3(-m.m02, -m.m12, m.m22));
			return new G.Transform3D(basis, new G.Vector3(m.m03, m.m13, -m.m23));
		}

		public static G.Color C(Color c) => new G.Color(c.r, c.g, c.b, c.a);

		// ---------------------------------------------------------------- layers

		private static readonly int[] sLayerBit = Enumerable.Repeat(-1, 32).ToArray();
		private static int sNextBit;

		public static uint LayerBit(int unityLayer)
		{
			unityLayer &= 31;
			if (sLayerBit[unityLayer] < 0) sLayerBit[unityLayer] = sNextBit < 20 ? sNextBit++ : 19;
			return 1u << sLayerBit[unityLayer];
		}

		public static uint CullMask(int unityMask)
		{
			uint m = 0;
			for (int l = 0; l < 32; l++)
				if ((unityMask & (1 << l)) != 0) m |= LayerBit(l);
			return m;
		}

		// ---------------------------------------------------------------- frame sync

		public static void OnSceneSettings()
		{
			EnsureEnv();
			var a = SceneSettings.Ambient;
			sEnv.AmbientLightSource = G.Environment.AmbientSource.Color;
			sEnv.AmbientLightColor = new G.Color(a.r, a.g, a.b);
			sEnv.AmbientLightEnergy = 1f;
			sEnv.FogEnabled = SceneSettings.Fog;
			sEnv.FogLightColor = C(SceneSettings.FogColor);
			sEnv.FogDensity = SceneSettings.FogDensity;
		}

		private static void EnsureEnv()
		{
			if (sEnv != null) return;
			sEnv = new G.Environment { BackgroundMode = G.Environment.BGMode.ClearColor, AmbientLightSource = G.Environment.AmbientSource.Color };
			sWorldEnv = new G.WorldEnvironment { Environment = sEnv };
			Main.Instance.World3D.AddChild(sWorldEnv);
		}

		public static void Sync()
		{
			RenderQuality.BeginRender();
			EnsureEnv();
			if (sViews == null)
			{
				var layer = new G.CanvasLayer { Layer = 0, Name = "Views" };
				Main.Instance.AddChild(layer);
				sViews = new G.Control { Name = "ViewStack" };
				sViews.SetAnchorsPreset(G.Control.LayoutPreset.FullRect);
				sViews.MouseFilter = G.Control.MouseFilterEnum.Ignore;
				layer.AddChild(sViews);
			}
			SyncCameras();
			SyncLights();
			for (int i = 0; i < Renderers.Count; i++) SyncRenderer(Renderers[i]);
			RenderQuality.EndRender();
		}

		private static void SyncCameras()
		{
			int sw = Display.Width, sh = Display.Height;
			var active = Cameras.Where(c => c.__enabled && !c.__destroyed && c.gameObject != null && c.gameObject.active).OrderBy(c => c.__depth).ToList();
			int order = 0;
			bool first = true;
			foreach (var c in Cameras)
				if (c.__view != null && !active.Contains(c)) { c.__view.Visible = false; if (c.__vp != null) c.__vp.RenderTargetUpdateMode = G.SubViewport.UpdateMode.Disabled; }
			foreach (var c in active)
			{
				if (c.__vp == null)
				{
					c.__vp = new G.SubViewport { Name = "Cam_" + c.gameObject.name, HandleInputLocally = false, Msaa3D = G.Viewport.Msaa.Disabled };
					c.__cam = new G.Camera3D();
					c.__vp.AddChild(c.__cam);
					c.__view = new G.TextureRect { MouseFilter = G.Control.MouseFilterEnum.Ignore, StretchMode = G.TextureRect.StretchModeEnum.Scale, ExpandMode = G.TextureRect.ExpandModeEnum.IgnoreSize };
					c.__view.AddChild(c.__vp);
					c.__view.Texture = c.__vp.GetTexture();
					sViews.AddChild(c.__view);
					c.__env = new G.Environment();
				}
				var r = c.__rect;
				int pw = Math.Max(1, (int)(r.width * sw)), ph = Math.Max(1, (int)(r.height * sh));
				RenderQuality.Size(c.__vp, new G.Vector2I(pw, ph));
				c.__vp.RenderTargetUpdateMode = G.SubViewport.UpdateMode.Always;
				c.__view.Visible = true;
				c.__view.Position = new G.Vector2(r.x * sw, (1f - r.y - r.height) * sh);
				c.__view.Size = new G.Vector2(pw, ph);
				sViews.MoveChild(c.__view, order++);

				bool clears = first || c.__clear == CameraClearFlags.SolidColor || c.__clear == CameraClearFlags.Skybox;
				c.__vp.TransparentBg = !clears || c.__clear == CameraClearFlags.Depth || c.__clear == CameraClearFlags.Nothing;
				if (first && (c.__clear == CameraClearFlags.Depth || c.__clear == CameraClearFlags.Nothing)) c.__vp.TransparentBg = false;
				var bg = c.__bg;
				c.__env.BackgroundMode = c.__vp.TransparentBg ? G.Environment.BGMode.ClearColor : G.Environment.BGMode.Color;
				c.__env.BackgroundColor = new G.Color(bg.r, bg.g, bg.b, 1f);
				c.__env.AmbientLightSource = G.Environment.AmbientSource.Color;
				c.__env.AmbientLightColor = new G.Color(SceneSettings.Ambient.r, SceneSettings.Ambient.g, SceneSettings.Ambient.b);
				c.__env.FogEnabled = SceneSettings.Fog;
				c.__env.FogLightColor = C(SceneSettings.FogColor);
				c.__env.FogDensity = SceneSettings.FogDensity;
				c.__cam.Environment = c.__env;
				first = false;

				var cam = c.__cam;
				cam.GlobalTransform = M(c.transform.localToWorldMatrix);
				cam.CullMask = CullMask(c.__cull);
				cam.Near = Math.Max(0.01f, c.__near);
				cam.Far = Math.Max(cam.Near + 0.01f, c.__far);
				if (c.__orthographic)
				{
					cam.Projection = G.Camera3D.ProjectionType.Orthogonal;
					cam.Size = c.__ortho * 2f;
				}
				else
				{
					cam.Projection = G.Camera3D.ProjectionType.Perspective;
					cam.Fov = c.__fov;
				}
				cam.KeepAspect = G.Camera3D.KeepAspectEnum.Height;
				cam.Current = true;
			}
		}

		private static void SyncLights()
		{
			foreach (var l in Lights)
			{
				bool on = l.__enabled && l.gameObject != null && l.gameObject.active && !l.__lightmapped;
				if (!on) { if (l.__node != null) l.__node.Visible = false; continue; }
				if (l.__node == null)
				{
					l.__node = l.__type switch
					{
						LightType.Directional => new G.DirectionalLight3D(),
						LightType.Spot => new G.SpotLight3D(),
						_ => new G.OmniLight3D(),
					};
					Main.Instance.World3D.AddChild(l.__node);
				}
				l.__node.Visible = true;
				l.__node.GlobalTransform = M(l.transform.localToWorldMatrix);
				l.__node.LightColor = new G.Color(l.__color.r, l.__color.g, l.__color.b);
				// Unity 3 forward lights: intensity 1 ~ 2x color on lambert; Godot energy 1 ~ color
				l.__node.LightEnergy = l.__intensity * 2f;
				l.__node.LightCullMask = CullMask(l.__cull);
				if (l.__node is G.OmniLight3D o) { o.OmniRange = l.__range; o.OmniAttenuation = 1f; }
				if (l.__node is G.SpotLight3D s) { s.SpotRange = l.__range; s.SpotAngle = l.__spot * 0.5f; }
			}
		}

		/// the camera that renders a layer (for billboards)
		private static Camera CameraFor(int layer)
		{
			Camera best = null;
			foreach (var c in Cameras)
				if (c.__enabled && c.gameObject != null && c.gameObject.__active && (c.__cull & (1 << layer)) != 0 && (best == null || c.__depth > best.__depth)) best = c;
			return best ?? Camera.main;
		}

		private static readonly bool sNoParticles = System.Environment.GetEnvironmentVariable("SVZ_NOPARTICLES") == "1";   // read once, not per renderer per frame

		private static void SyncRenderer(Renderer r)
		{
			var go = r.__go;
			bool vis = r.__enabled && go != null && go.__active && !go.__isAsset && !r.__destroyed;
			if (vis && r is ParticleRenderer && sNoParticles) vis = false;
			if (vis && r is ParticleRenderer pr) Particles.BuildMesh(pr, CameraFor(go.__layer));
			Mesh mesh = vis ? r.__Mesh : null;
			if (mesh == null || mesh.vertexCount == 0) vis = false;
			r.__visible = vis;
			if (!vis)
			{
				if (r.__node != null) RenderQuality.Visible(r.__node, false);
				return;
			}
			if (r.__node == null)
			{
				r.__node = new G.MeshInstance3D { Name = go.name };
				Main.Instance.World3D.AddChild(r.__node);
			}
			var node = r.__node;
			RenderQuality.Visible(node, true);
			bool batched = r.__subsets != null && r.__subsets.Length > 0;
			var gm = batched ? MeshCache.GetSubsets(mesh, r.__subsets) : MeshCache.Get(mesh);
			if (node.Mesh != gm) node.Mesh = gm;
			RenderQuality.Layers(node, LayerBit(go.__layer));
			RenderQuality.Shadow(node, G.GeometryInstance3D.ShadowCastingSetting.Off);

			if (r is SkinnedMeshRenderer smr && mesh.__bind.Length > 0 && smr.__bones.Length > 0)
			{
				Skinning.Sync(smr, node, mesh);
				RenderQuality.Transform(node, G.Transform3D.Identity);
			}
			else if (r is ParticleRenderer) RenderQuality.Transform(node, G.Transform3D.Identity);   // particle mesh is in world space
			else if (batched) RenderQuality.Transform(node, r.__batchRoot != null ? M(r.__batchRoot.localToWorldMatrix) : G.Transform3D.Identity);
			else RenderQuality.Transform(node, M(r.transform.localToWorldMatrix));

			int surfaces = gm.GetSurfaceCount();
			for (int i = 0; i < surfaces; i++)
			{
				Material m = r.__mats.Length == 0 ? null : r.__mats[Math.Min(i, r.__mats.Length - 1)];
				var gmat = Materials.Get(m, r);
				if (node.GetSurfaceOverrideMaterial(i) != gmat) node.SetSurfaceOverrideMaterial(i, gmat);
			}
		}
	}

	/// Unity Mesh -> Godot ArrayMesh (mirrored Z, flipped winding, flipped V)
	public static class MeshCache
	{
		// Particle meshes are rebuilt every frame. Free the Godot interop temporaries right away instead of
		// leaving them to the finalizer: crowded waves otherwise piled up 100k+ tracked disposables and every
		// garbage collection had to finalize them (150-400 ms hitches).
		private static G.Collections.Array<G.Collections.Array> sNoBlendShapes;
		private static G.Collections.Dictionary sNoLods;

		private static void Put(G.Collections.Array arrays, int index, G.Variant value)
		{
			try { arrays[index] = value; } finally { value.Dispose(); }
		}

		private static void AddSurface(G.ArrayMesh mesh, G.Collections.Array arrays)
		{
			try
			{
				mesh.AddSurfaceFromArrays(G.Mesh.PrimitiveType.Triangles, arrays,
					sNoBlendShapes ??= new G.Collections.Array<G.Collections.Array>(), sNoLods ??= new G.Collections.Dictionary());
			}
			finally { arrays.Dispose(); }
		}

		private static readonly Dictionary<(Mesh, string), (G.ArrayMesh mesh, int version)> sSubsets = new Dictionary<(Mesh, string), (G.ArrayMesh, int)>();

		/// a mesh made of selected submeshes of a (static batch) combined mesh
		public static G.ArrayMesh GetSubsets(Mesh m, int[] subsets)
		{
			var key = (m, string.Join(",", subsets));
			var full = Get(m);
			if (sSubsets.TryGetValue(key, out var e) && e.version == m.__version) return e.mesh;
			var gm = new G.ArrayMesh();
			// surfaces of `full` map 1:1 to non-empty submeshes; rebuild index mapping
			var surfaceOf = new Dictionary<int, int>();
			int si = 0;
			for (int i = 0; i < m.__subs.Count; i++) if (m.__subs[i].Length >= 3) surfaceOf[i] = si++;
			foreach (int sub in subsets)
			{
				if (!surfaceOf.TryGetValue(sub, out int surf) || surf >= full.GetSurfaceCount()) continue;
				AddSurface(gm, full.SurfaceGetArrays(surf));
			}
			if (gm.GetSurfaceCount() == 0)
			{
				var arrays = new G.Collections.Array();
				arrays.Resize((int)G.Mesh.ArrayType.Max);
				Put(arrays, (int)G.Mesh.ArrayType.Vertex, new G.Vector3[] { G.Vector3.Zero, G.Vector3.Zero, G.Vector3.Zero });
				AddSurface(gm, arrays);
			}
			sSubsets[key] = (gm, m.__version);
			return gm;
		}

		public static G.ArrayMesh Get(Mesh m)
		{
			if (m.__gmesh != null && m.__gmeshVersion == m.__version) return m.__gmesh;
			int n = m.vertexCount;
			var gm = m.__gmesh ?? new G.ArrayMesh();
			gm.ClearSurfaces();
			var pos = new G.Vector3[n];
			for (int i = 0; i < n; i++) pos[i] = Render.V(m.__v[i]);
			G.Vector3[] nrm = null;
			if (m.__n.Length == n) { nrm = new G.Vector3[n]; for (int i = 0; i < n; i++) nrm[i] = Render.V(m.__n[i]); }
			G.Vector2[] uv = null, uv2 = null;
			if (m.__uv.Length == n) { uv = new G.Vector2[n]; for (int i = 0; i < n; i++) uv[i] = new G.Vector2(m.__uv[i].x, 1f - m.__uv[i].y); }
			if (m.__uv2.Length == n) { uv2 = new G.Vector2[n]; for (int i = 0; i < n; i++) uv2[i] = new G.Vector2(m.__uv2[i].x, 1f - m.__uv2[i].y); }
			G.Color[] col = null;
			if (m.__c.Length == n) { col = new G.Color[n]; for (int i = 0; i < n; i++) col[i] = Render.C(m.__c[i]); }
			int[] bones = null; float[] weights = null;
			if (m.__bw.Length == n && m.__bind.Length > 0)
			{
				bones = new int[n * 4]; weights = new float[n * 4];
				for (int i = 0; i < n; i++)
				{
					var w = m.__bw[i];
					bones[i * 4] = w.boneIndex0; bones[i * 4 + 1] = w.boneIndex1; bones[i * 4 + 2] = w.boneIndex2; bones[i * 4 + 3] = w.boneIndex3;
					weights[i * 4] = w.weight0; weights[i * 4 + 1] = w.weight1; weights[i * 4 + 2] = w.weight2; weights[i * 4 + 3] = w.weight3;
				}
			}
			foreach (var tri in m.__subs)
			{
				if (tri.Length < 3) continue;
				var idx = new int[tri.Length - tri.Length % 3];
				Array.Copy(tri, idx, idx.Length);   // Z mirror already turns Unity winding into Godot front faces
				var arrays = new G.Collections.Array();
				arrays.Resize((int)G.Mesh.ArrayType.Max);
				Put(arrays, (int)G.Mesh.ArrayType.Vertex, pos);
				if (nrm != null) Put(arrays, (int)G.Mesh.ArrayType.Normal, nrm);
				if (uv != null) Put(arrays, (int)G.Mesh.ArrayType.TexUV, uv);
				if (uv2 != null) Put(arrays, (int)G.Mesh.ArrayType.TexUV2, uv2);
				if (col != null) Put(arrays, (int)G.Mesh.ArrayType.Color, col);
				if (bones != null) { Put(arrays, (int)G.Mesh.ArrayType.Bones, bones); Put(arrays, (int)G.Mesh.ArrayType.Weights, weights); }
				Put(arrays, (int)G.Mesh.ArrayType.Index, idx);
				AddSurface(gm, arrays);
			}
			if (gm.GetSurfaceCount() == 0)
			{
				// keep a valid (empty) surface so materials can be assigned
				var arrays = new G.Collections.Array();
				arrays.Resize((int)G.Mesh.ArrayType.Max);
				Put(arrays, (int)G.Mesh.ArrayType.Vertex, new G.Vector3[] { G.Vector3.Zero, G.Vector3.Zero, G.Vector3.Zero });
				AddSurface(gm, arrays);
			}
			// skinned meshes move far from their bind pose; avoid culling issues
			if (bones != null) gm.CustomAabb = new G.Aabb(new G.Vector3(-1e4f, -1e4f, -1e4f), new G.Vector3(2e4f, 2e4f, 2e4f));
			m.__gmesh = gm;
			m.__gmeshVersion = m.__version;
			return gm;
		}
	}

	public static class Skinning
	{
		public static void Sync(SkinnedMeshRenderer smr, G.MeshInstance3D node, Mesh mesh)
		{
			int nb = Math.Min(smr.__bones.Length, mesh.__bind.Length);
			if (smr.__skel == null || smr.__skinVersion != mesh.__version || smr.__skel.GetBoneCount() != nb)
			{
				smr.__skel?.QueueFree();
				var skel = new G.Skeleton3D { Name = smr.gameObject.name + "_skel" };
				Main.Instance.World3D.AddChild(skel);
				var skin = new G.Skin();
				for (int i = 0; i < nb; i++)
				{
					skel.AddBone("b" + i);
					skin.AddBind(i, Render.M(mesh.__bind[i]));
				}
				smr.__skel = skel; smr.__skin = skin; smr.__skinVersion = mesh.__version;
				node.Skin = skin;
				node.Skeleton = node.GetPathTo(skel);
			}
			var sk = smr.__skel;
			sk.GlobalTransform = G.Transform3D.Identity;
			for (int i = 0; i < nb; i++)
			{
				var b = smr.__bones[i];
				if (b == null || b.__destroyed) continue;
				var t = Render.M(b.localToWorldMatrix);
				sk.SetBonePosePosition(i, t.Origin);
				var basis = t.Basis;
				var scale = basis.Scale;
				RenderQuality.BoneScale(sk, i, scale);
				sk.SetBonePoseRotation(i, basis.Orthonormalized().GetRotationQuaternion());
			}
		}
	}
}
