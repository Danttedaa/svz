using System;
using System.Collections.Generic;
using System.Globalization;
using Svz;

namespace UnityEngine
{
	public sealed partial class Time
	{
		internal static float __time, __unscaled, __dt, __fixedTime, __levelLoadTime, __smoothDt = 1f / 30f;
		internal static float __fixedDt = 0.02f, __maxDt = 1f / 3f, __scale = 1f;
		internal static int __frame;

		public static partial float time => __time;
		public static partial float timeSinceLevelLoad => __time - __levelLoadTime;
		public static partial float deltaTime => Clock.InFixed ? __fixedDt : __dt;
		public static partial float fixedTime => __fixedTime;
		public static partial float fixedDeltaTime { get => __fixedDt; set => __fixedDt = Mathf.Max(0.0001f, value); }
		public static partial float maximumDeltaTime { get => __maxDt; set => __maxDt = value; }
		public static partial float smoothDeltaTime => __smoothDt;
		public static partial float timeScale { get => __scale; set => __scale = Mathf.Max(0f, value); }
		public static partial int frameCount => __frame;
		public static partial int renderedFrameCount => __frame;
		public static partial float realtimeSinceStartup => (float)Clock.Real;
		private static int __p_captureFramerate;
		public static partial int captureFramerate { get => __p_captureFramerate; set => __p_captureFramerate = value; }}

	public sealed partial class Application
	{
		internal static int __loadedLevel = -1;
		internal static string[] __levelNames;

		public static partial int loadedLevel => __loadedLevel;
		public static partial string loadedLevelName => __loadedLevel >= 0 && __loadedLevel < LevelNames.Length ? LevelNames[__loadedLevel] : "";
		public static partial bool isLoadingLevel => SceneManager.Pending >= 0;
		public static partial int levelCount => LevelNames.Length;
		public static partial int streamedBytes => 0;
		public static partial bool isPlaying => true;
		public static partial bool isEditor => false;
		public static partial bool isWebPlayer => false;
		public static partial RuntimePlatform platform => RuntimePlatform.Android;
		private static bool __p_runInBackground;
		public static partial bool runInBackground { get => __p_runInBackground; set => __p_runInBackground = value; }
		public static partial string dataPath => "res://";
		public static partial string persistentDataPath => Godot.ProjectSettings.GlobalizePath("user://");
		public static partial string temporaryCachePath => Godot.ProjectSettings.GlobalizePath("user://cache");
		public static partial string srcValue => "";
		public static partial string absoluteURL => "";
		public static partial string unityVersion => "3.4.2f3";
		public static partial bool webSecurityEnabled => false;
		public static partial string webSecurityHostUrl => "";
		public static partial int targetFrameRate { get => Godot.Engine.MaxFps; set => Godot.Engine.MaxFps = value <= 0 ? 60 : value; }
		public static partial SystemLanguage systemLanguage => Platform.Language();
		private static ThreadPriority __p_backgroundLoadingPriority;
		public static partial ThreadPriority backgroundLoadingPriority { get => __p_backgroundLoadingPriority; set => __p_backgroundLoadingPriority = value; }// The port never goes online.
		public static partial NetworkReachability internetReachability => NetworkReachability.NotReachable;

		internal static string[] LevelNames
		{
			get
			{
				if (__levelNames == null) __levelNames = SceneManager.LevelNames();
				return __levelNames;
			}
		}

		public static partial void Quit() => Svz.Main.Instance?.GetTree().Quit();
		public static partial void CancelQuit() { }

		private static partial AsyncOperation LoadLevelAsync(string monoLevelName, int index, bool additive, bool mustCompleteNextFrame)
		{
			if (monoLevelName != null) index = Array.FindIndex(LevelNames, n => string.Equals(n, monoLevelName, StringComparison.OrdinalIgnoreCase));
			if (index < 0) { Debug.LogError("Level not found: " + monoLevelName); return AsyncOperation.__Done(); }
			SceneManager.Request(index, additive);
			return AsyncOperation.__Done();
		}

		private static partial float GetStreamProgressForLevelByName(string levelName) => 1f;
		public static partial float GetStreamProgressForLevel(int levelIndex) => 1f;
		private static partial bool CanStreamedLevelBeLoadedByName(string levelName) => true;
		public static partial bool CanStreamedLevelBeLoaded(int levelIndex) => true;
		public static partial void CaptureScreenshot(string filename) => Svz.Main.Instance?.Screenshot(filename);
		internal static partial bool HasProLicense() => true;
		internal static partial bool HasAdvancedLicense() => true;
		public static partial void DontDestroyOnLoad(Object mono) => Object.DontDestroyOnLoad(mono);
		private static partial void Internal_ExternalCall(string script) { }
		internal static partial int GetBuildUnityVersion() => 0x03042000;
		internal static partial int GetNumericUnityVersion(string version) => 0x03042000;
		// links to stores/ads are dropped (no browser launches from the port)
		// links to stores/ads are dropped; only the port's own page may be opened
		public static partial void OpenURL(string url)
		{
			if (url != null && url.StartsWith("https://github.com/mesflit/", StringComparison.OrdinalIgnoreCase)) Godot.OS.ShellOpen(url);
			else Godot.GD.Print("[svz] OpenURL ignored: " + url);
		}
		public static partial void CommitSuicide(int mode) => Quit();
		private static partial void SetLogCallbackDefined(bool defined, bool threaded) { }
	}

	public sealed partial class Screen
	{
		public static partial Resolution[] resolutions => new[] { currentResolution };
		public static partial Resolution currentResolution => Resolution.__Make(width, height, 60);
		private static bool __p_showCursor = true;
		public static partial bool showCursor { get => __p_showCursor; set => __p_showCursor = value; }
		private static bool __p_lockCursor;
		public static partial bool lockCursor { get => __p_lockCursor; set => __p_lockCursor = value; }
		public static partial int width => Display.Width;
		public static partial int height => Display.Height;
		public static partial bool fullScreen { get => true; set { } }
		private static bool __p_autorotateToPortrait;
		public static partial bool autorotateToPortrait { get => __p_autorotateToPortrait; set => __p_autorotateToPortrait = value; }
		private static bool __p_autorotateToPortraitUpsideDown;
		public static partial bool autorotateToPortraitUpsideDown { get => __p_autorotateToPortraitUpsideDown; set => __p_autorotateToPortraitUpsideDown = value; }
		private static bool __p_autorotateToLandscapeLeft = true;
		public static partial bool autorotateToLandscapeLeft { get => __p_autorotateToLandscapeLeft; set => __p_autorotateToLandscapeLeft = value; }
		private static bool __p_autorotateToLandscapeRight = true;
		public static partial bool autorotateToLandscapeRight { get => __p_autorotateToLandscapeRight; set => __p_autorotateToLandscapeRight = value; }
		private static ScreenOrientation __p_orientation = ScreenOrientation.LandscapeLeft;
		public static partial ScreenOrientation orientation { get => __p_orientation; set => __p_orientation = value; }
		private static float __p_sleepTimeout;
		public static partial float sleepTimeout { get => __p_sleepTimeout; set => __p_sleepTimeout = value; }
		public static partial void SetResolution(int width, int height, bool fullscreen, int preferredRefreshRate) { }
	}

	public partial struct Resolution
	{
		internal static Resolution __Make(int w, int h, int r) => new Resolution { m_Width = w, m_Height = h, m_RefreshRate = r };
	}

	public sealed partial class PlayerPrefs
	{
		private static partial bool TrySetInt(string key, int value) { Prefs.Set(key, value); return true; }
		private static partial bool TrySetFloat(string key, float value) { Prefs.Set(key, value); return true; }
		private static partial bool TrySetSetString(string key, string value) { Prefs.Set(key, value ?? ""); return true; }
		public static partial int GetInt(string key, int defaultValue) => Prefs.Get(key, out var v) && v is long l ? (int)l : v is int i ? i : defaultValue;
		public static partial float GetFloat(string key, float defaultValue) => Prefs.Get(key, out var v) && v is double d ? (float)d : v is float f ? f : defaultValue;
		public static partial string GetString(string key, string defaultValue) => Prefs.Get(key, out var v) && v is string s ? s : defaultValue;
		public static partial bool HasKey(string key) => Prefs.Get(key, out _);
		public static partial void DeleteKey(string key) => Prefs.Delete(key);
		public static partial void DeleteAll() => Prefs.Clear();
		public static partial void Save() => Prefs.Flush();
	}

	public sealed partial class Resources
	{
		public static partial Object[] FindObjectsOfTypeAll(Type type) => Runtime.FindObjectsOfType(type, true);
		public static partial Object Load(string path, Type type) => AssetDb.LoadResource(path, type ?? typeof(Object));
		public static partial Object[] LoadAll(string path, Type type) => AssetDb.LoadAllResources(path, type ?? typeof(Object));
		public static partial Object GetBuiltinResource(Type type, string path) => Builtin.Get(type, path);
		public static partial Object LoadAssetAtPath(string assetPath, Type type) => null;
		public static partial AsyncOperation UnloadUnusedAssets() => AsyncOperation.__Done();
	}

	public partial class AsyncOperation
	{
		internal static AsyncOperation __Done() => new AsyncOperation();
		public partial bool isDone => true;
		public partial float progress => 1f;
		private int __p_priority;
		public partial int priority { get => __p_priority; set => __p_priority = value; }
		private partial void InternalDestroy() { }
	}

	public partial struct Touch
	{
		internal static Touch __Make(int id, Vector2 pos, Vector2 delta, float dt, int taps, TouchPhase phase) =>
			new Touch { m_FingerId = id, m_Position = pos, m_PositionDelta = delta, m_TimeDelta = dt, m_TapCount = taps, m_Phase = phase };
	}

	public sealed partial class Input
	{
		public static partial bool isGyroAvailable => false;
		public static partial Vector3 mousePosition => InputState.MousePosition;
		public static partial bool anyKey => InputState.AnyKey;
		public static partial bool anyKeyDown => InputState.AnyKeyDown;
		public static partial string inputString => InputState.InputString;
		public static partial Vector3 acceleration => new Vector3(0f, -1f, 0f);
		public static partial int accelerationEventCount => 0;
		public static partial int touchCount => InputState.Touches.Count;
		private static bool __p_eatKeyPressOnTextFieldFocus;
		public static partial bool eatKeyPressOnTextFieldFocus { get => __p_eatKeyPressOnTextFieldFocus; set => __p_eatKeyPressOnTextFieldFocus = value; }
		private static bool __p_multiTouchEnabled = true;
		public static partial bool multiTouchEnabled { get => __p_multiTouchEnabled; set => __p_multiTouchEnabled = value; }
		public static partial DeviceOrientation deviceOrientation => DeviceOrientation.LandscapeLeft;
		private static IMECompositionMode __p_imeCompositionMode;
		public static partial IMECompositionMode imeCompositionMode { get => __p_imeCompositionMode; set => __p_imeCompositionMode = value; }
		public static partial string compositionString => "";
		private static partial int mainGyroIndex_Internal() => 0;
		private static partial bool GetKeyInt(int key) => InputState.KeyState((KeyCode)key, 0);
		private static partial bool GetKeyString(string name) => InputState.KeyState(InputState.KeyFromName(name), 0);
		private static partial bool GetKeyUpInt(int key) => InputState.KeyState((KeyCode)key, 2);
		private static partial bool GetKeyUpString(string name) => InputState.KeyState(InputState.KeyFromName(name), 2);
		private static partial bool GetKeyDownInt(int key) => InputState.KeyState((KeyCode)key, 1);
		private static partial bool GetKeyDownString(string name) => InputState.KeyState(InputState.KeyFromName(name), 1);
		public static partial float GetAxis(string axisName) => InputState.Axis(axisName);
		public static partial float GetAxisRaw(string axisName) => InputState.Axis(axisName);
		public static partial bool GetButton(string buttonName) => InputState.Button(buttonName, 0);
		public static partial bool GetButtonDown(string buttonName) => InputState.Button(buttonName, 1);
		public static partial bool GetButtonUp(string buttonName) => InputState.Button(buttonName, 2);
		public static partial string[] GetJoystickNames() => new string[0];
		public static partial bool GetMouseButton(int button) => InputState.Mouse(button, 0);
		public static partial bool GetMouseButtonDown(int button) => InputState.Mouse(button, 1);
		public static partial bool GetMouseButtonUp(int button) => InputState.Mouse(button, 2);
		public static partial void ResetInputAxes() { }
		public static partial AccelerationEvent GetAccelerationEvent(int index) => default;
		public static partial Touch GetTouch(int index) => InputState.Touches[index];
		public static partial Quaternion GetRotation(int deviceID) => Quaternion.identity;
		public static partial Vector3 GetPosition(int deviceID) => Vector3.zero;
		private static partial void INTERNAL_get_compositionCursorPos(out Vector2 value) => value = Vector2.zero;
		private static partial void INTERNAL_set_compositionCursorPos(ref Vector2 value) { }
	}

	public sealed partial class Debug
	{
		public static partial bool isDebugBuild => false;
		private static partial void INTERNAL_CALL_DrawLine(ref Vector3 start, ref Vector3 end, ref Color color, float duration) { }
		public static partial void Break() { }
		public static partial void DebugBreak() { }

		private static partial void Internal_Log(int level, string msg, Object obj)
		{
			if (level == 0) { if (Svz.Main.Verbose) Godot.GD.Print("[unity] " + msg); }
			else if (level == 1) Godot.GD.Print("[unity:warn] " + msg);
			else Godot.GD.PrintErr("[unity:error] " + msg);
		}
	}

	public sealed partial class SystemInfo
	{
		public static partial string operatingSystem => Godot.OS.GetName() + " " + Godot.OS.GetVersion();
		public static partial string processorType => "ARM64";
		public static partial int processorCount => Godot.OS.GetProcessorCount();
		public static partial int systemMemorySize => 4096;
		public static partial int graphicsMemorySize => 1024;
		public static partial string graphicsDeviceName => Godot.RenderingServer.GetVideoAdapterName();
		public static partial string graphicsDeviceVendor => Godot.RenderingServer.GetVideoAdapterVendor();
		public static partial int graphicsDeviceID => 0;
		public static partial int graphicsDeviceVendorID => 0;
		public static partial string graphicsDeviceVersion => "OpenGL ES 3.0";
		public static partial int graphicsShaderLevel => 30;
		public static partial int graphicsPixelFillrate => 100000;
		public static partial bool supportsShadows => false;
		public static partial bool supportsRenderTextures => true;
		public static partial bool supportsImageEffects => false;
		public static partial bool supportsVertexPrograms => true;
		public static partial string deviceUniqueIdentifier => "svz-port-device";
		public static partial string deviceName => "Android";
		public static partial string deviceModel => Godot.OS.GetModelName();
		public static partial bool SupportsRenderTextureFormat(RenderTextureFormat format) => true;
	}

	public sealed partial class iPhoneSettings
	{
		private static iPhoneScreenOrientation __p_screenOrientation = iPhoneScreenOrientation.LandscapeLeft;
		public static partial iPhoneScreenOrientation screenOrientation { get => __p_screenOrientation; set => __p_screenOrientation = value; }
		private static bool __p_verticalOrientation;
		public static partial bool verticalOrientation { get => __p_verticalOrientation; set => __p_verticalOrientation = value; }
		private static bool __p_screenCanDarken;
		public static partial bool screenCanDarken { get => __p_screenCanDarken; set => __p_screenCanDarken = value; }
		public static partial string uniqueIdentifier => SystemInfo.deviceUniqueIdentifier;
		public static partial string name => "Android";
		public static partial string model => SystemInfo.deviceModel;
		public static partial string systemName => "Android";
		public static partial string systemVersion => Godot.OS.GetVersion();
		public static partial iPhoneNetworkReachability internetReachability => iPhoneNetworkReachability.NotReachable;
		public static partial iPhoneGeneration generation => iPhoneGeneration.Unknown;
		public static partial LocationServiceStatus locationServiceStatus => LocationServiceStatus.Stopped;
		public static partial bool locationServiceEnabledByUser => false;
		public static partial void StartLocationServiceUpdates(float desiredAccuracyInMeters, float updateDistanceInMeters) { }
		public static partial void StopLocationServiceUpdates() { }
	}
}

namespace Svz
{
	public static class Clock
	{
		public static bool InFixed;
		private static readonly System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
		public static double Real => sw.Elapsed.TotalSeconds;
	}

	public static class Platform
	{
		public static UnityEngine.SystemLanguage Language()
		{
			string l = Godot.OS.GetLocaleLanguage();
			return l switch
			{
				"de" => UnityEngine.SystemLanguage.German,
				"fr" => UnityEngine.SystemLanguage.French,
				"es" => UnityEngine.SystemLanguage.Spanish,
				"it" => UnityEngine.SystemLanguage.Italian,
				"ja" => UnityEngine.SystemLanguage.Japanese,
				"ko" => UnityEngine.SystemLanguage.Korean,
				"zh" => UnityEngine.SystemLanguage.Chinese,
				"ru" => UnityEngine.SystemLanguage.Russian,
				"pt" => UnityEngine.SystemLanguage.Portuguese,
				_ => UnityEngine.SystemLanguage.English,
			};
		}
	}

	/// PlayerPrefs stored in user://prefs.json
	public static class Prefs
	{
		private static Dictionary<string, object> sData;
		private static bool sDirty;
		private const string Path = "user://playerprefs.json";

		private static void Load()
		{
			if (sData != null) return;
			sData = new Dictionary<string, object>();
			if (!Godot.FileAccess.FileExists(Path)) return;
			try
			{
				using var doc = System.Text.Json.JsonDocument.Parse(AssetDb.ReadText(Path));
				foreach (var p in doc.RootElement.EnumerateObject())
				{
					var v = p.Value;
					string t = v[0].GetString();
					sData[p.Name] = t == "i" ? (object)v[1].GetInt64() : t == "f" ? v[1].GetDouble() : v[1].GetString();
				}
			}
			catch (Exception e) { Godot.GD.PrintErr("[svz] prefs: " + e.Message); }
		}

		public static void Set(string k, object v) { Load(); sData[k] = v is int i ? (long)i : v is float f ? (double)f : v; sDirty = true; }
		public static bool Get(string k, out object v) { Load(); return sData.TryGetValue(k, out v); }
		public static void Delete(string k) { Load(); sDirty |= sData.Remove(k); }
		public static void Clear() { Load(); sData.Clear(); sDirty = true; }

		public static void Flush()
		{
			if (!sDirty || sData == null) return;
			sDirty = false;
			var sb = new System.Text.StringBuilder("{");
			bool first = true;
			foreach (var kv in sData)
			{
				if (!first) sb.Append(',');
				first = false;
				sb.Append(System.Text.Json.JsonSerializer.Serialize(kv.Key)).Append(':');
				switch (kv.Value)
				{
					case long l: sb.Append("[\"i\",").Append(l).Append(']'); break;
					case double d: sb.Append("[\"f\",").Append(d.ToString("R", CultureInfo.InvariantCulture)).Append(']'); break;
					default: sb.Append("[\"s\",").Append(System.Text.Json.JsonSerializer.Serialize(kv.Value?.ToString() ?? "")).Append(']'); break;
				}
			}
			sb.Append('}');
			using var f = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
			f?.StoreString(sb.ToString());
		}
	}
}
