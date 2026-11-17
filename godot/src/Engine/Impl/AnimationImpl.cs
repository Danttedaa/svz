using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Svz;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEngine
{
	public partial class TrackedReference
	{
		private static int sNextPtr = 1;
		internal void __AssignPtr() => m_Ptr = (IntPtr)(sNextPtr++);
	}

	public sealed partial class AnimationCurve
	{
		internal List<Keyframe> __keys = new List<Keyframe>();
		internal WrapMode __pre = WrapMode.ClampForever, __post = WrapMode.ClampForever;

		public partial int length => __keys.Count;
		public partial WrapMode preWrapMode { get => __pre; set => __pre = value; }
		public partial WrapMode postWrapMode { get => __post; set => __post = value; }
		private partial void Cleanup() { }

		public partial float Evaluate(float time)
		{
			int n = __keys.Count;
			if (n == 0) return 0f;
			if (n == 1) return __keys[0].value;
			float t0 = __keys[0].time, t1 = __keys[n - 1].time;
			time = CurveMath.Wrap(time, t0, t1, time < t0 ? __pre : __post);
			int i = 0;
			while (i < n - 2 && time > __keys[i + 1].time) i++;
			var a = __keys[i]; var b = __keys[i + 1];
			return CurveMath.Hermite(time, a.time, a.value, a.outTangent, b.time, b.value, b.inTangent);
		}

		public partial int AddKey(float time, float value) => INTERNAL_CALL_AddKey_Internal(this, ref Unsafe(new Keyframe(time, value)));
		private static ref Keyframe Unsafe(Keyframe k) { sTmp = k; return ref sTmp; }
		[ThreadStatic] private static Keyframe sTmp;

		private static partial int INTERNAL_CALL_AddKey_Internal(AnimationCurve self, ref Keyframe key)
		{
			for (int i = 0; i < self.__keys.Count; i++)
				if (Mathf.Approximately(self.__keys[i].time, key.time)) return -1;
			float kt = key.time;
			int at = self.__keys.FindIndex(k => k.time > kt);
			if (at < 0) at = self.__keys.Count;
			self.__keys.Insert(at, key);
			return at;
		}

		private static partial int INTERNAL_CALL_MoveKey(AnimationCurve self, int index, ref Keyframe key)
		{
			self.__keys.RemoveAt(index);
			return INTERNAL_CALL_AddKey_Internal(self, ref key);
		}

		public partial void RemoveKey(int index) => __keys.RemoveAt(index);
		private partial void SetKeys(Keyframe[] keys) => __keys = new List<Keyframe>((keys ?? new Keyframe[0]).OrderBy(k => k.time));
		private partial Keyframe GetKey_Internal(int index) => __keys[index];
		private partial Keyframe[] GetKeys() => __keys.ToArray();
		public partial void SmoothTangents(int index, float weight) { }
		private partial void Init(Keyframe[] keys) => SetKeys(keys);

		internal static AnimationCurve __FromJson(JsonElement e)
		{
			var c = new AnimationCurve();
			if (e.ValueKind != JsonValueKind.Object) return c;
			foreach (var k in e.Get("m_Curve").EnumerateArray())
				c.__keys.Add(new Keyframe(k.Float("time"), k.Float("value"), k.Float("inSlope"), k.Float("outSlope")));
			c.__pre = CurveMath.InfinityToWrap(e.Int("m_PreInfinity", 2));
			c.__post = CurveMath.InfinityToWrap(e.Int("m_PostInfinity", 2));
			return c;
		}
	}

	public sealed partial class AnimationEvent
	{
		internal string __data = "", __string = "", __function = "";
		internal float __float, __time;
		internal int __int;
		internal Object __obj;
		internal SendMessageOptions __opts;
		internal AnimationState __state;

		partial void __Init() { }
		private partial void Destroy() { }
		public partial string data { get => __data; set => __data = value; }
		public partial string stringParameter { get => __string; set => __string = value; }
		public partial float floatParameter { get => __float; set => __float = value; }
		public partial int intParameter { get => __int; set => __int = value; }
		public partial Object objectReferenceParameter { get => __obj; set => __obj = value; }
		public partial string functionName { get => __function; set => __function = value; }
		public partial float time { get => __time; set => __time = value; }
		public partial SendMessageOptions messageOptions { get => __opts; set => __opts = value; }
		public partial AnimationState animationState => __state;

		internal AnimationEvent __Clone() => new AnimationEvent
		{
			__data = __data, __string = __string, __function = __function, __float = __float, __time = __time, __int = __int, __obj = __obj, __opts = __opts,
		};
	}

	public sealed partial class AnimationClip
	{
		internal float __length = -1f, __rate = 30f;
		internal WrapMode __wrap = WrapMode.Default;
		internal readonly List<AnimTrack> __tracks = new List<AnimTrack>();
		internal readonly List<AnimationEvent> __events = new List<AnimationEvent>();
		internal Bounds __bounds;

		partial void __Init() { }
		public partial float length => __length >= 0 ? __length : (__length = __tracks.Count == 0 ? 0f : __tracks.Max(t => t.End));
		public partial float frameRate => __rate;
		public partial WrapMode wrapMode { get => __wrap; set => __wrap = value; }

		public partial void SetCurve(string relativePath, Type type, string propertyName, AnimationCurve curve)
		{
			__length = -1f;
			relativePath ??= "";
			if (type == typeof(Transform))
			{
				string[] p = propertyName.Split('.');
				int kind = p[0] switch { "localPosition" or "m_LocalPosition" => 0, "localRotation" or "m_LocalRotation" => 1, "localScale" or "m_LocalScale" => 2, _ => -1 };
				if (kind >= 0 && p.Length == 2)
				{
					int comp = "xyzw".IndexOf(p[1][0]);
					var tr = __tracks.FirstOrDefault(t => t.kind == kind && t.path == relativePath);
					if (tr == null) { tr = new AnimTrack { kind = kind, path = relativePath, comps = new AnimationCurve[kind == 1 ? 4 : 3] }; __tracks.Add(tr); }
					tr.comps[comp] = curve;
					return;
				}
			}
			__tracks.Add(new AnimTrack { kind = 3, path = relativePath, attr = propertyName, targetType = type, comps = new[] { curve } });
		}

		private static partial void INTERNAL_CALL_EnsureQuaternionContinuity(AnimationClip self) { }
		private static partial void INTERNAL_CALL_ClearCurves(AnimationClip self) { self.__tracks.Clear(); self.__length = -1f; }
		public partial void AddEvent(AnimationEvent evt) { __events.Add(evt); __events.Sort((a, b) => a.__time.CompareTo(b.__time)); }
		private partial void INTERNAL_get_localBounds(out Bounds value) => value = __bounds;
		private partial void INTERNAL_set_localBounds(ref Bounds value) => __bounds = value;
	}

	public sealed partial class AnimationState
	{
		internal Animation __anim;
		internal AnimationClip __clip;
		internal string __stateName = "";
		internal bool __on;
		internal float __weight, __target = 1f, __fadeSpeed, __time, __speed = 1f, __prevTime = -1f;
		internal WrapMode __wrap = WrapMode.Default;
		internal int __layer;
		internal AnimationBlendMode __blend;
		internal HashSet<Transform> __mix;
		internal bool __stopWhenFaded, __isClone, __queued, __finishing;
		internal QueueMode __queueMode;
		internal float __rangeStart, __rangeEnd = -1f;

		internal AnimationState() { __AssignPtr(); }

		public partial bool enabled
		{
			get => __on;
			set
			{
				if (value && !__on) __prevTime = __time - 1e-5f;
				__on = value;
			}
		}

		public partial float weight { get => __weight; set { __weight = value; __target = value; __fadeSpeed = 0; } }
		public partial WrapMode wrapMode { get => __wrap; set => __wrap = value; }
		public partial float time { get => __time; set { __time = value; __prevTime = value - 1e-5f; } }
		public partial float normalizedTime { get => __Length > 0 ? __time / __Length : 0f; set => time = value * __Length; }
		public partial float speed { get => __speed; set => __speed = value; }
		public partial float normalizedSpeed { get => __Length > 0 ? __speed / __Length : 0f; set => __speed = value * __Length; }
		public partial float length => __Length;
		public partial int layer { get => __layer; set => __layer = value; }
		public partial AnimationClip clip => __clip;
		public partial string name { get => __stateName; set => __stateName = value; }
		public partial AnimationBlendMode blendMode { get => __blend; set => __blend = value; }

		internal float __Length => __rangeEnd >= 0 ? __rangeEnd - __rangeStart : (__clip?.length ?? 0f);

		internal WrapMode __EffectiveWrap
		{
			get
			{
				if (__wrap != WrapMode.Default) return __wrap;
				if (__clip != null && __clip.__wrap != WrapMode.Default) return __clip.__wrap;
				return __anim != null ? __anim.__wrapMode : WrapMode.Once;
			}
		}

		public partial void AddMixingTransform(Transform mix, bool recursive)
		{
			__mix ??= new HashSet<Transform>();
			void Add(Transform t) { __mix.Add(t); if (recursive) foreach (var c in t.__children) Add(c); }
			if (mix != null) Add(mix);
		}

		public partial void RemoveMixingTransform(Transform mix) => __mix?.Remove(mix);
	}

	public sealed partial class Animation
	{
		internal AnimationClip __defaultClip;
		internal readonly List<AnimationState> __states = new List<AnimationState>();
		internal bool __playAuto = true, __animatePhysics, __onlyIfVisible = true;
		internal WrapMode __wrapMode = WrapMode.Default;
		internal AnimationCullingType __culling;
		internal Bounds __bounds;
		internal readonly Dictionary<AnimationClip, AnimBinding> __bindings = new Dictionary<AnimationClip, AnimBinding>();

		public partial AnimationClip clip { get => __defaultClip; set => __defaultClip = value; }
		public partial bool playAutomatically { get => __playAuto; set => __playAuto = value; }

		public partial WrapMode wrapMode
		{
			get => __wrapMode;
			set { __wrapMode = value; foreach (var s in __states) s.__wrap = value; }
		}

		public partial bool isPlaying => __states.Any(s => s.__on);
		public partial bool animatePhysics { get => __animatePhysics; set => __animatePhysics = value; }
		public partial bool animateOnlyIfVisible { get => __onlyIfVisible; set => __onlyIfVisible = value; }
		public partial AnimationCullingType cullingType { get => __culling; set => __culling = value; }

		private static partial void INTERNAL_CALL_Stop(Animation self)
		{
			foreach (var s in self.__states) { s.__on = false; s.__time = 0; s.__weight = 0; }
			self.__states.RemoveAll(s => s.__isClone);
		}

		private partial void Internal_StopByName(string name)
		{
			foreach (var s in __states.Where(s => s.__stateName == name || (s.__isClone && s.__clip == GetState(name)?.__clip)).ToList())
			{
				s.__on = false; s.__time = 0; s.__weight = 0;
				if (s.__isClone) __states.Remove(s);
			}
		}

		private partial void Internal_RewindByName(string name) { var s = GetState(name); if (s != null) s.time = 0; }
		private static partial void INTERNAL_CALL_Rewind(Animation self) { foreach (var s in self.__states) s.time = 0; }
		private static partial void INTERNAL_CALL_Sample(Animation self) => AnimationSystem.Apply(self);
		public partial bool IsPlaying(string name) => __states.Any(s => s.__on && (s.__stateName == name || s.__isClone && s.__clip == GetState(name)?.__clip));

		public partial bool Play(string animation, PlayMode mode)
		{
			var st = GetState(animation);
			if (st == null) return false;
			foreach (var s in __states)
				if (s != st && (mode == PlayMode.StopAll || s.__layer == st.__layer)) { s.__on = false; s.__weight = 0; }
			__states.RemoveAll(s => s.__isClone && !s.__on);
			bool wasPlaying = st.__on;
			st.enabled = true;
			st.__weight = 1f; st.__target = 1f; st.__fadeSpeed = 0f; st.__stopWhenFaded = false;
			if (!wasPlaying || st.__time >= st.__Length && st.__EffectiveWrap == WrapMode.Once) st.time = 0f;
			return true;
		}

		public partial void CrossFade(string animation, float fadeLength, PlayMode mode)
		{
			var st = GetState(animation);
			if (st == null) return;
			if (fadeLength <= 0f) { Play(animation, mode); return; }
			foreach (var s in __states)
			{
				if (s == st || !(mode == PlayMode.StopAll || s.__layer == st.__layer) || !s.__on) continue;
				s.__target = 0f; s.__fadeSpeed = 1f / fadeLength; s.__stopWhenFaded = true;
			}
			if (!st.__on) { st.enabled = true; st.__weight = 0f; st.time = 0f; }
			else if (st.__time >= st.__Length && st.__EffectiveWrap == WrapMode.Once) st.time = 0f;
			st.__target = 1f; st.__fadeSpeed = 1f / fadeLength; st.__stopWhenFaded = false;
		}

		public partial void Blend(string animation, float targetWeight, float fadeLength)
		{
			var st = GetState(animation);
			if (st == null) return;
			if (!st.__on) { st.enabled = true; st.__weight = 0f; }
			st.__target = targetWeight;
			st.__stopWhenFaded = false;
			if (fadeLength <= 0f) { st.__weight = targetWeight; st.__fadeSpeed = 0; } else st.__fadeSpeed = 1f / fadeLength;
		}

		private AnimationState Queue(string animation, QueueMode queue, PlayMode mode, float fade)
		{
			var src = GetState(animation);
			if (src == null) return null;
			var st = new AnimationState
			{
				__anim = this, __clip = src.__clip, __stateName = src.__stateName + " - Queued Clone", __layer = src.__layer,
				__wrap = src.__wrap, __speed = src.__speed, __isClone = true, __mix = src.__mix,
				__rangeStart = src.__rangeStart, __rangeEnd = src.__rangeEnd,
			};
			__states.Add(st);
			if (queue == QueueMode.PlayNow || !__states.Any(s => s.__on && s.__layer == st.__layer && s != st))
				Activate(st, mode, fade);
			else { st.__queued = true; st.__queueMode = queue; st.__fadeSpeed = fade > 0 ? 1f / fade : 0f; }
			return st;
		}

		internal void Activate(AnimationState st, PlayMode mode, float fade)
		{
			st.__queued = false;
			foreach (var s in __states)
			{
				if (s == st || !s.__on || !(mode == PlayMode.StopAll || s.__layer == st.__layer)) continue;
				if (fade > 0) { s.__target = 0; s.__fadeSpeed = 1f / fade; s.__stopWhenFaded = true; }
				else { s.__on = false; s.__weight = 0; }
			}
			st.enabled = true; st.time = 0f;
			if (fade > 0) { st.__weight = 0; st.__target = 1; st.__fadeSpeed = 1f / fade; } else { st.__weight = 1; st.__target = 1; }
		}

		public partial AnimationState CrossFadeQueued(string animation, float fadeLength, QueueMode queue, PlayMode mode) => Queue(animation, queue, mode, fadeLength);
		public partial AnimationState PlayQueued(string animation, QueueMode queue, PlayMode mode) => Queue(animation, queue, mode, 0f);

		public partial void AddClip(AnimationClip clip, string newName, int firstFrame, int lastFrame, bool addLoopFrame)
		{
			if (clip == null) return;
			__states.RemoveAll(s => s.__stateName == newName && !s.__isClone);
			var st = new AnimationState { __anim = this, __clip = clip, __stateName = newName ?? clip.name };
			if (firstFrame != int.MinValue && lastFrame != int.MaxValue && (firstFrame > 0 || lastFrame > 0))
			{
				st.__rangeStart = firstFrame / clip.__rate;
				st.__rangeEnd = lastFrame / clip.__rate;
			}
			__states.Add(st);
		}

		public partial void RemoveClip(AnimationClip clip) => __states.RemoveAll(s => s.__clip == clip);
		public partial int GetClipCount() => __states.Count(s => !s.__isClone);
		private partial void RemoveClip2(string clipName) => __states.RemoveAll(s => s.__stateName == clipName);

		private partial bool PlayDefaultAnimation(PlayMode mode)
		{
			if (__defaultClip == null) return false;
			var st = __states.FirstOrDefault(s => s.__clip == __defaultClip && !s.__isClone);
			if (st == null) { AddClip(__defaultClip, __defaultClip.name, int.MinValue, int.MaxValue, false); st = __states.Last(); }
			return Play(st.__stateName, mode);
		}

		private static partial void INTERNAL_CALL_SyncLayer(Animation self, int layer) { }
		internal partial AnimationState GetState(string name) => __states.FirstOrDefault(s => s.__stateName == name && !s.__isClone) ?? __states.FirstOrDefault(s => s.__stateName == name);
		internal partial AnimationState GetStateAtIndex(int index) => __states[index];
		internal partial int GetStateCount() => __states.Count;
		private partial void INTERNAL_get_localBounds(out Bounds value) => value = __bounds;
		private partial void INTERNAL_set_localBounds(ref Bounds value) => __bounds = value;

		internal override void __OnAttach() => AnimationSystem.All.Add(this);
		internal override void __OnDestroyNative() => AnimationSystem.All.Remove(this);

		internal override void __OnLoaded()
		{
			if (__go.__isAsset || !__playAuto || __defaultClip == null) return;
			if (__go.__active && __enabled) PlayDefaultAnimation(PlayMode.StopSameLayer);
		}

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			var s = (Animation)src;
			__defaultClip = s.__defaultClip; __playAuto = s.__playAuto; __animatePhysics = s.__animatePhysics; __wrapMode = s.__wrapMode;
			__states.Clear();
			foreach (var st in s.__states.Where(x => !x.__isClone))
				__states.Add(new AnimationState
				{
					__anim = this, __clip = st.__clip, __stateName = st.__stateName, __layer = st.__layer, __wrap = st.__wrap, __speed = st.__speed,
					__blend = st.__blend, __rangeStart = st.__rangeStart, __rangeEnd = st.__rangeEnd,
					__mix = st.__mix == null ? null : new HashSet<Transform>(st.__mix.Select(t => c != null ? c.Remap(t) : t)),
				});
		}
	}

	public sealed partial class QualitySettings
	{
		private static QualityLevel sLevel = QualityLevel.Good;
		public static partial QualityLevel currentLevel { get => sLevel; set => sLevel = value; }
		public static partial int pixelLightCount { get => 2; set { } }
		public static partial ShadowProjection shadowProjection { get => ShadowProjection.CloseFit; set { } }
		public static partial int shadowCascades { get => 1; set { } }
		public static partial float shadowDistance { get => 0f; set { } }
		public static partial int masterTextureLimit { get => 0; set { } }
		public static partial AnisotropicFiltering anisotropicFiltering { get => AnisotropicFiltering.Disable; set { } }
		public static partial bool softVegetation { get => false; set { } }
		public static partial int maxQueuedFrames { get => 2; set { } }
		public static partial int vSyncCount { get => 1; set { } }
		public static partial int antiAliasing { get => 0; set { } }
		public static partial void IncreaseLevel() { if (sLevel < QualityLevel.Fantastic) sLevel++; }
		public static partial void DecreaseLevel() { if (sLevel > QualityLevel.Fastest) sLevel--; }
	}
}

namespace Svz
{
	public static class CurveMath
	{
		public static WrapMode InfinityToWrap(int i) => i switch { 0 => WrapMode.PingPong, 1 => WrapMode.Loop, _ => WrapMode.ClampForever };

		public static float Wrap(float t, float t0, float t1, WrapMode m)
		{
			float len = t1 - t0;
			if (len <= 0) return t0;
			switch (m)
			{
				case WrapMode.Loop: return t0 + Mod(t - t0, len);
				case WrapMode.PingPong:
				{
					float x = Mod(t - t0, len * 2f);
					return t0 + (x > len ? len * 2f - x : x);
				}
				default: return Mathf.Clamp(t, t0, t1);
			}
		}

		private static float Mod(float a, float b) { float r = a % b; return r < 0 ? r + b : r; }

		public static float Hermite(float t, float t0, float v0, float m0, float t1, float v1, float m1)
		{
			float dt = t1 - t0;
			if (dt <= 0f) return v0;
			if (Math.Abs(m0) > 1e30f || Math.Abs(m1) > 1e30f) return t >= t1 ? v1 : v0;   // stepped
			float s = (t - t0) / dt, s2 = s * s, s3 = s2 * s;
			float h00 = 2 * s3 - 3 * s2 + 1, h10 = s3 - 2 * s2 + s, h01 = -2 * s3 + 3 * s2, h11 = s3 - s2;
			return h00 * v0 + h10 * dt * m0 + h01 * v1 + h11 * dt * m1;
		}
	}

	/// one animated property group of a clip
	public sealed class AnimTrack
	{
		public int kind;              // 0 position, 1 rotation, 2 scale, 3 float
		public string path = "";
		public string attr;           // float curves
		public int classId;
		public Type targetType;
		public AnimationCurve[] comps;
		public float End => comps.Where(c => c != null && c.__keys.Count > 0).Select(c => c.__keys[c.__keys.Count - 1].time).DefaultIfEmpty(0f).Max();
	}

	/// resolved targets of one clip on one Animation component
	public sealed class AnimBinding
	{
		public Transform[] transforms;
		public Action<float>[] setters;
	}

	public static partial class AnimationSystem
	{
		public static readonly List<Animation> All = new List<Animation>();

		public static void Update(float dt)
		{
			for (int i = 0; i < All.Count; i++)
			{
				var a = All[i];
				if (a.__destroyed || !a.__enabled || a.__go == null || !a.__go.__active || a.__go.__isAsset || a.__states.Count == 0) continue;
				try { Step(a, dt); Apply(a); }
				catch (Exception e) { Log.Exception(e, "animation " + a.__go.name); }
			}
		}

		private static void Step(Animation a, float dt)
		{
			var fired = new List<(AnimationState, AnimationEvent)>();
			foreach (var s in a.__states.ToArray())
			{
				if (!s.__on) continue;
				// fades
				if (s.__fadeSpeed > 0f)
				{
					s.__weight = Mathf.MoveTowards(s.__weight, s.__target, s.__fadeSpeed * dt);
					if (Mathf.Approximately(s.__weight, s.__target))
					{
						s.__fadeSpeed = 0f;
						if (s.__stopWhenFaded && s.__weight <= 0f) { s.__on = false; s.__time = 0; if (s.__isClone) a.__states.Remove(s); continue; }
					}
				}
				float len = s.__Length;
				float prev = s.__time;
				s.__time += dt * s.__speed;
				// legacy Animation does not fire events of states with zero weight
				if (s.__clip != null && s.__clip.__events.Count > 0 && s.__weight > 0f) CollectEvents(s, s.__prevTime, s.__time, len, fired);
				s.__prevTime = s.__time;
				var wrap = s.__EffectiveWrap;
				if (len > 0f && (wrap == WrapMode.Once || wrap == WrapMode.Default) && (s.__speed >= 0 ? s.__time >= len : s.__time <= 0f))
				{
					// sample the last frame this update, then stop
					s.__finishing = true;
				}
			}
			// queued states start when the others in the layer have finished
			foreach (var s in a.__states.Where(x => x.__queued).ToList())
			{
				bool othersDone = !a.__states.Any(o => o != s && o.__on && !o.__finishing && o.__layer == s.__layer);
				if (s.__queueMode == QueueMode.PlayNow || othersDone)
					a.Activate(s, PlayMode.StopSameLayer, s.__fadeSpeed > 0 ? 1f / s.__fadeSpeed : 0f);
			}
			foreach (var (st, ev) in fired) Fire(a, st, ev);
		}

		private static void CollectEvents(AnimationState s, float from, float to, float len, List<(AnimationState, AnimationEvent)> outList)
		{
			if (to < from || len <= 0f) return;
			var wrap = s.__EffectiveWrap;
			bool loops = wrap == WrapMode.Loop || wrap == WrapMode.PingPong;
			float start = s.__rangeStart;
			foreach (var ev in s.__clip.__events)
			{
				float et = ev.__time - start;
				if (et < 0 || loops && et > len + 1e-4f) continue;   // Unity still fires events past the last key of a clamped clip
				if (!loops)
				{
					if (et > from && et <= to) outList.Add((s, ev));
					continue;
				}
				// every occurrence of et + k*len in (from, to]
				float k0 = Mathf.Floor((from - et) / len) + 1;
				for (float k = k0; et + k * len <= to && k < k0 + 8; k++)
					if (et + k * len > from) outList.Add((s, ev));
			}
		}

		private static void Fire(Animation a, AnimationState st, AnimationEvent ev)
		{
			if (string.IsNullOrEmpty(ev.__function) || a.__go == null || a.__go.__destroyed) return;
			var e = ev.__Clone();
			e.__state = st;
			bool any = false;
			foreach (var c in a.__go.__components.ToArray())
			{
				if (!(c is MonoBehaviour mb) || mb.__destroyed || !mb.__awoken) continue;
				var m = Messaging.FindMethod(mb.GetType(), ev.__function, -1);
				if (m == null) continue;
				any = true;
				var ps = m.GetParameters();
				object arg = null;
				if (ps.Length == 1)
				{
					Type t = ps[0].ParameterType;
					if (t == typeof(AnimationEvent)) arg = e;
					else if (t == typeof(string)) arg = e.__string;
					else if (t == typeof(float)) arg = e.__float;
					else if (t == typeof(int)) arg = e.__int;
					else if (typeof(Object).IsAssignableFrom(t)) arg = e.__obj;
				}
				try
				{
					var r = m.Invoke(mb, ps.Length == 0 ? null : new[] { arg });
					if (r is System.Collections.IEnumerator en) CoroutineRunner.Start(mb, en, ev.__function);
				}
				catch (TargetInvocationException ex) { Log.Exception(ex.InnerException ?? ex, $"anim event {ev.__function}"); }
			}
			if (!any && ev.__opts == SendMessageOptions.RequireReceiver)
				Debug.LogWarning($"AnimationEvent '{ev.__function}' has no receiver on {a.__go.name}");
		}

		private struct Acc { public Vector3 pos, scale; public Vector4 rot; public float wp, wr, ws; }

		public static void Apply(Animation a)
		{
			var active = a.__states.Where(s => s.__on && s.__weight > 0f && s.__clip != null).ToList();
			if (active.Count == 0) { FinishStates(a); return; }
			// layer weighting (higher layers first), weights normalized within a layer
			var weights = new Dictionary<AnimationState, float>();
			float remaining = 1f;
			foreach (var layer in active.GroupBy(s => s.__layer).OrderByDescending(g => g.Key))
			{
				float sum = layer.Sum(s => s.__weight);
				float scale = sum > 1f ? 1f / sum : 1f;
				float used = Mathf.Min(remaining, sum * scale);
				float k = sum > 0 ? used / (sum * scale) : 0f;
				foreach (var s in layer) weights[s] = s.__weight * scale * k;
				remaining -= used;
				if (remaining <= 0f) break;
			}
			var acc = new Dictionary<Transform, Acc>();
			var floats = new Dictionary<Action<float>, (float v, float w)>();
			foreach (var s in active)
			{
				if (!weights.TryGetValue(s, out float w) || w <= 0f) continue;
				var clip = s.__clip;
				if (!a.__bindings.TryGetValue(clip, out var bind)) a.__bindings[clip] = bind = Bind(a, clip);
				float t = SampleTime(s);
				for (int i = 0; i < clip.__tracks.Count; i++)
				{
					var tr = clip.__tracks[i];
					if (tr.kind == 3)
					{
						var set = bind.setters[i];
						if (set == null) continue;
						float v = tr.comps[0].Evaluate(t);
						floats[set] = floats.TryGetValue(set, out var fv) ? (fv.v + v * w, fv.w + w) : (v * w, w);
						continue;
					}
					var target = bind.transforms[i];
					if (target == null || s.__mix != null && !s.__mix.Contains(target)) continue;
					acc.TryGetValue(target, out var cur);
					switch (tr.kind)
					{
						case 0: cur.pos += Eval3(tr, t) * w; cur.wp += w; break;
						case 2: cur.scale += Eval3(tr, t) * w; cur.ws += w; break;
						case 1:
						{
							var q = Eval4(tr, t);
							if (cur.wr > 0 && Vector4.Dot(q, cur.rot) < 0) q = -q;
							cur.rot += q * w; cur.wr += w;
							break;
						}
					}
					acc[target] = cur;
				}
			}
			foreach (var kv in acc)
			{
				var t = kv.Key; var c = kv.Value;
				if (t.__destroyed) continue;
				if (c.wp > 0) t.__lp = c.wp >= 0.999f ? c.pos / c.wp : Vector3.Lerp(t.__lp, c.pos / c.wp, c.wp);
				if (c.ws > 0) t.__ls = c.ws >= 0.999f ? c.scale / c.ws : Vector3.Lerp(t.__ls, c.scale / c.ws, c.ws);
				if (c.wr > 0)
				{
					var r = c.rot / c.wr;
					var q = Quaternion.Normalize(new Quaternion(r.x, r.y, r.z, r.w));
					t.__lr = c.wr >= 0.999f ? q : Quaternion.Slerp(t.__lr, q, c.wr);
				}
			}
			Transform.__version++;
			foreach (var kv in floats)
				try { kv.Key(kv.Value.w > 0 ? kv.Value.v / kv.Value.w : 0f); }
				catch (Exception e) { Log.Exception(e, "animated property"); }
			FinishStates(a);
		}

		private static void FinishStates(Animation a)
		{
			foreach (var s in a.__states.ToArray())
			{
				if (!s.__finishing) continue;
				s.__finishing = false;
				s.__on = false;
				s.__time = 0f;
				s.__weight = 0f;
				if (s.__isClone) a.__states.Remove(s);
			}
		}

		private static float SampleTime(AnimationState s)
		{
			float len = s.__Length;
			float t = s.__time;
			switch (s.__EffectiveWrap)
			{
				case WrapMode.Loop: t = len > 0 ? t - Mathf.Floor(t / len) * len : 0f; break;
				case WrapMode.PingPong:
				{
					if (len <= 0) { t = 0; break; }
					float x = t - Mathf.Floor(t / (2 * len)) * 2 * len;
					t = x > len ? 2 * len - x : x;
					break;
				}
				default: t = Mathf.Clamp(t, 0f, len); break;
			}
			return t + s.__rangeStart;
		}

		private static Vector3 Eval3(AnimTrack tr, float t) =>
			new Vector3(tr.comps[0]?.Evaluate(t) ?? 0f, tr.comps[1]?.Evaluate(t) ?? 0f, tr.comps[2]?.Evaluate(t) ?? 0f);

		private static Vector4 Eval4(AnimTrack tr, float t) =>
			new Vector4(tr.comps[0]?.Evaluate(t) ?? 0f, tr.comps[1]?.Evaluate(t) ?? 0f, tr.comps[2]?.Evaluate(t) ?? 0f, tr.comps[3]?.Evaluate(t) ?? 1f);

		private static AnimBinding Bind(Animation a, AnimationClip clip)
		{
			var b = new AnimBinding { transforms = new Transform[clip.__tracks.Count], setters = new Action<float>[clip.__tracks.Count] };
			var root = a.transform;
			for (int i = 0; i < clip.__tracks.Count; i++)
			{
				var tr = clip.__tracks[i];
				var target = string.IsNullOrEmpty(tr.path) ? root : root.Find(tr.path);
				if (target == null) continue;
				if (tr.kind != 3) { b.transforms[i] = target; continue; }
				b.setters[i] = FloatSetter(target.gameObject, tr);
			}
			return b;
		}

		private static Action<float> FloatSetter(GameObject go, AnimTrack tr)
		{
			string attr = tr.attr ?? "";
			switch (tr.classId)
			{
				case 114:
				{
					foreach (var c in go.__components)
					{
						if (!(c is MonoBehaviour mb)) continue;
						if (tr.targetType != null && !tr.targetType.IsInstanceOfType(mb)) continue;
						if (attr == "m_Enabled") return v => mb.enabled = v > 0.5f;
						var f = Deserializer.Fields(mb.GetType(), typeof(MonoBehaviour)).TryGetValue(attr, out var fi) ? fi : null;
						if (f == null) continue;
						if (f.FieldType == typeof(float)) return v => f.SetValue(mb, v);
						if (f.FieldType == typeof(bool)) return v => f.SetValue(mb, v > 0.5f);
						if (f.FieldType == typeof(int)) return v => f.SetValue(mb, (int)v);
					}
					return null;
				}
				case 88:   // EllipsoidParticleEmitter
				{
					var e = go.GetComponent<ParticleEmitter>();
					if (e == null) return null;
					if (attr == "minEmission") return v => e.minEmission = v;
					if (attr == "maxEmission") return v => e.maxEmission = v;
					if (attr == "m_Enabled" || attr == "emit") return v => e.emit = v > 0.5f;
					return null;
				}
				case 21:   // material property on the renderer, e.g. _TintColor.a
				{
					var r = go.GetComponent<Renderer>();
					if (r == null) return null;
					string[] p = attr.Split('.');
					if (p.Length == 2)
					{
						int comp = "rgbaxyzw".IndexOf(p[1][0]) % 4;
						return v => { var m = r.material; var c = m.GetColor(p[0]); c[comp] = v; m.SetColor(p[0], c); };
					}
					return v => r.material.SetFloat(attr, v);
				}
				case 1:    // GameObject m_IsActive
					if (attr == "m_IsActive") return v => go.active = v > 0.5f;
					return null;
			}
			if (tr.targetType != null && typeof(Component).IsAssignableFrom(tr.targetType))
			{
				var c = go.GetComponent(tr.targetType);
				var p = c?.GetType().GetProperty(attr);
				if (p != null && p.PropertyType == typeof(float)) return v => p.SetValue(c, v);
			}
			return null;
		}

		public static void SampleClip(GameObject go, AnimationClip clip, float time, float weight)
		{
			if (go == null || clip == null) return;
			var tmp = new Animation();
			tmp.__go = go;
			tmp.AddClip(clip, "__sample", int.MinValue, int.MaxValue, false);
			var st = tmp.__states[0];
			st.__on = true; st.__weight = 1f; st.__time = time; st.__wrap = WrapMode.ClampForever;
			Apply(tmp);
		}
	}

	public static partial class Builtins
	{
		static partial void RegisterAnimation()
		{
			AssetDb.Register("AnimationClip", (o, d, ld) =>
			{
				var c = (AnimationClip)o;
				c.__rate = d.Float("m_SampleRate", 30f);
				c.__wrap = (WrapMode)d.Int("m_WrapMode");
				void Vec(string key, int kind, int ncomp, bool quat)
				{
					foreach (var tc in d.Get(key).EnumerateArray())
					{
						var curve = tc.Get("curve");
						var comps = new AnimationCurve[ncomp];
						for (int k = 0; k < ncomp; k++) comps[k] = new AnimationCurve();
						string[] cn = { "x", "y", "z", "w" };
						foreach (var key2 in curve.Get("m_Curve").EnumerateArray())
						{
							float time = key2.Float("time");
							var v = key2.Get("value"); var ins = key2.Get("inSlope"); var outs = key2.Get("outSlope");
							for (int k = 0; k < ncomp; k++)
								comps[k].__keys.Add(new Keyframe(time, v.Float(cn[k]), ins.Float(cn[k]), outs.Float(cn[k])));
						}
						var pre = CurveMath.InfinityToWrap(curve.Int("m_PreInfinity", 2)); var post = CurveMath.InfinityToWrap(curve.Int("m_PostInfinity", 2));
						foreach (var cc in comps) { cc.__pre = pre; cc.__post = post; }
						c.__tracks.Add(new AnimTrack { kind = kind, path = tc.Str("path"), comps = comps });
					}
				}
				Vec("m_PositionCurves", 0, 3, false);
				Vec("m_RotationCurves", 1, 4, true);
				Vec("m_ScaleCurves", 2, 3, false);
				foreach (var fc in d.Get("m_FloatCurves").EnumerateArray())
				{
					var tr = new AnimTrack
					{
						kind = 3, path = fc.Str("path"), attr = fc.Str("attribute"), classId = fc.Int("classID"),
						comps = new[] { AnimationCurve.__FromJson(fc.Get("curve")) },
					};
					var script = fc.Get("script");
					if (tr.classId == 114 && script.ValueKind == JsonValueKind.Array)
					{
						var ms = ld.Ref(script);
						if (ms != null && ms is MonoScript msc) tr.targetType = msc.__type;
					}
					c.__tracks.Add(tr);
				}
				foreach (var ev in d.Get("m_Events").EnumerateArray())
				{
					c.__events.Add(new AnimationEvent
					{
						__time = ev.Float("time"), __function = ev.Str("functionName"), __string = ev.Str("data"),
						__float = ev.Float("floatParameter"), __int = ev.Int("intParameter"),
						__obj = ld.Ref(ev.Get("objectReferenceParameter")), __opts = (SendMessageOptions)ev.Int("messageOptions"),
					});
				}
				c.__events.Sort((x, y) => x.__time.CompareTo(y.__time));
				c.__length = -1f;
			});
			AssetDb.Register("Animation", (o, d, ld) =>
			{
				var a = (Animation)o;
				a.__defaultClip = ld.Ref<AnimationClip>(d.Get("m_Animation"));
				a.__playAuto = d.Bool("m_PlayAutomatically", true);
				a.__animatePhysics = d.Bool("m_AnimatePhysics");
				a.__wrapMode = (WrapMode)d.Int("m_WrapMode");
				a.__culling = (AnimationCullingType)d.Int("m_CullingType");
				foreach (var cl in ld.Refs<AnimationClip>(d.Get("m_Animations")))
					if (cl != null) a.AddClip(cl, cl.name, int.MinValue, int.MaxValue, false);
				if (a.__defaultClip != null && a.GetState(a.__defaultClip.name) == null)
					a.AddClip(a.__defaultClip, a.__defaultClip.name, int.MinValue, int.MaxValue, false);
			});
			AssetDb.Register("MonoScript", (o, d, ld) =>
			{
				var m = (MonoScript)o;
				string ns = d.Str("m_Namespace"), n = d.Str("m_ClassName");
				m.__type = TypeResolver.Find(string.IsNullOrEmpty(ns) ? n : ns + "." + n) ?? TypeResolver.Find(n);
			});
		}
	}
}

namespace UnityEngine
{
	/// runtime stand-in for the editor MonoScript asset (script type reference)
	public sealed class MonoScript : Object { internal Type __type; }
}
