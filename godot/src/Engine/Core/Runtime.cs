using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Svz
{
	/// Scene registry and Unity 3.4 lifecycle (Awake/OnEnable/Start/Update/LateUpdate/OnDisable/OnDestroy).
	public static class Runtime
	{
		private static int sNextId = 1;
		public static int NextInstanceId() => sNextId++;

		[ThreadStatic] public static bool CreatingBareGameObject;

		public static readonly List<GameObject> GameObjects = new List<GameObject>(4096);
		public static readonly List<MonoBehaviour> Behaviours = new List<MonoBehaviour>(1024);
		private static readonly List<MonoBehaviour> sStartQueue = new List<MonoBehaviour>();
		private static readonly List<(Object obj, float at)> sDelayedDestroy = new List<(Object, float)>();
		private static readonly List<Object> sDestroyQueue = new List<Object>();

		/// set while a scene/clone is being built: Awake is deferred until the graph is complete
		public static int BuildDepth;
		private static readonly List<GameObject> sPendingAwake = new List<GameObject>();

		public static Transform CreateTransform() => Transform.__New();

		public static void RegisterGameObject(GameObject go)
		{
			GameObjects.Add(go);
		}

		// ---------------------------------------------------------------- components

		public static Component AddComponent(GameObject go, Type type)
		{
			if (type == null || !typeof(Component).IsAssignableFrom(type) || type.IsAbstract)
			{
				Debug.LogError("AddComponent: invalid type " + type);
				return null;
			}
			if (type == typeof(Transform)) return go.transform;
			// RequireComponent
			foreach (RequireComponent rc in type.GetCustomAttributes(typeof(RequireComponent), true))
			{
				foreach (var req in new[] { rc.m_Type0, rc.m_Type1, rc.m_Type2 })
					if (req != null && go.GetComponent(req) == null) AddComponent(go, req);
			}
			Component c = Construct(type);
			go.__Attach(c);
			if (c is MonoBehaviour mb)
			{
				Lifecycle.Bind(mb);
				Behaviours.Add(mb);
				if (!go.__isAsset && BuildDepth == 0 && go.__active) Awake(mb);
			}
			return c;
		}

		public static Component Construct(Type type)
		{
			if (type == typeof(Transform)) return Transform.__New();
			return (Component)Activator.CreateInstance(type, true);
		}

		/// Register an already constructed component created by the loader / cloner.
		public static void AttachLoaded(GameObject go, Component c)
		{
			go.__Attach(c);
			if (c is MonoBehaviour mb)
			{
				Lifecycle.Bind(mb);
				Behaviours.Add(mb);
			}
		}

		// ---------------------------------------------------------------- lifecycle

		public static void EndBuild(IEnumerable<GameObject> built)
		{
			foreach (var go in built)
				foreach (var c in go.__components.ToArray())
					c.__OnLoaded();
			foreach (var go in built)
			{
				if (go.__isAsset || !go.__active || go.__destroyed) continue;
				foreach (var c in go.__components.ToArray())
					if (c is MonoBehaviour mb && !mb.__destroyed) Awake(mb);
			}
		}

		private static void Awake(MonoBehaviour mb)
		{
			if (mb.__awoken) return;
			mb.__awoken = true;
			Safe(mb, mb.__cb.Awake, "Awake");
			if (mb.__destroyed) return;
			if (mb.__enabled && mb.__go.__active) Enable(mb);
		}

		private static void Enable(MonoBehaviour mb)
		{
			if (mb.__enabledCalled) return;
			mb.__enabledCalled = true;
			Safe(mb, mb.__cb.OnEnable, "OnEnable");
			if (!mb.__started && !mb.__startQueued)
			{
				mb.__startQueued = true;
				sStartQueue.Add(mb);
			}
		}

		private static void Disable(MonoBehaviour mb)
		{
			if (!mb.__enabledCalled) return;
			mb.__enabledCalled = false;
			Safe(mb, mb.__cb.OnDisable, "OnDisable");
		}

		public static void OnBehaviourEnabledChanged(MonoBehaviour mb)
		{
			if (mb.__go == null || mb.__go.__isAsset || !mb.__go.__active) return;
			if (!mb.__awoken) { if (mb.__enabled && BuildDepth == 0) Awake(mb); return; }
			if (mb.__enabled) Enable(mb); else Disable(mb);
		}

		public static void OnGameObjectActiveChanged(GameObject go)
		{
			if (go.__isAsset) return;
			if (go.__active)
			{
				foreach (var c in go.__components.ToArray())
				{
					if (c is MonoBehaviour mb && !mb.__destroyed)
					{
						if (!mb.__awoken) { if (BuildDepth == 0) Awake(mb); }
						else if (mb.__enabled) Enable(mb);
					}
				}
			}
			else
			{
				foreach (var c in go.__components.ToArray())
					if (c is MonoBehaviour mb && !mb.__destroyed) Disable(mb);
				CoroutineRunner.StopAllOn(go);   // Unity stops coroutines of deactivated objects
			}
			foreach (var c in go.__components) c.__OnActiveChanged();
		}

		public static void OnParentChanged(Transform t) { }

		internal static void Safe(MonoBehaviour mb, Action a, string what)
		{
			if (a == null) return;
			try { a(); }
			catch (Exception e) { Log.Exception(e, $"{mb?.GetType().Name}.{what} on '{mb?.__go?.__name}'"); }
		}

		private static bool Runs(MonoBehaviour mb) => !mb.__destroyed && mb.__enabled && mb.__awoken && mb.__go != null && mb.__go.__active;

		public static void RunStarts()
		{
			while (sStartQueue.Count > 0)
			{
				var batch = sStartQueue.ToArray();
				sStartQueue.Clear();
				foreach (var mb in batch)
				{
					mb.__startQueued = false;
					if (mb.__started || mb.__destroyed) continue;
					if (!Runs(mb)) { mb.__startQueued = true; sDeferredStarts.Add(mb); continue; }
					mb.__started = true;
					if (mb.__cb.StartCoroutine != null)
					{
						try { var e = mb.__cb.StartCoroutine(); if (e != null) CoroutineRunner.Start(mb, e, "Start"); }
						catch (Exception e) { Log.Exception(e, $"{mb.GetType().Name}.Start"); }
					}
					else Safe(mb, mb.__cb.Start, "Start");
				}
			}
			if (sDeferredStarts.Count > 0) { sStartQueue.AddRange(sDeferredStarts); sDeferredStarts.Clear(); }
		}

		private static readonly List<MonoBehaviour> sDeferredStarts = new List<MonoBehaviour>();

		public static void RunUpdate()
		{
			for (int i = 0; i < Behaviours.Count; i++)
			{
				var mb = Behaviours[i];
				if (mb.__cb.Update != null && mb.__started && Runs(mb)) Safe(mb, mb.__cb.Update, "Update");
			}
		}

		public static void RunFixedUpdate()
		{
			for (int i = 0; i < Behaviours.Count; i++)
			{
				var mb = Behaviours[i];
				if (mb.__cb.FixedUpdate != null && mb.__started && Runs(mb)) Safe(mb, mb.__cb.FixedUpdate, "FixedUpdate");
			}
		}

		public static void RunLateUpdate()
		{
			for (int i = 0; i < Behaviours.Count; i++)
			{
				var mb = Behaviours[i];
				if (mb.__cb.LateUpdate != null && mb.__started && Runs(mb)) Safe(mb, mb.__cb.LateUpdate, "LateUpdate");
			}
		}

		public static void RunOnGUI()
		{
			for (int i = 0; i < Behaviours.Count; i++)
			{
				var mb = Behaviours[i];
				if (mb.__cb.OnGUI != null && Runs(mb)) Safe(mb, mb.__cb.OnGUI, "OnGUI");
			}
		}

		public static void Broadcast(string message, object arg)
		{
			foreach (var mb in Behaviours.ToArray())
				if (Runs(mb)) Messaging.InvokeOn(mb, message, arg);
		}

		// ---------------------------------------------------------------- destroy

		public static void Destroy(Object obj, float t)
		{
			if (Object.__IsNull(obj)) return;
			if (t > 0f) sDelayedDestroy.Add((obj, UnityEngine.Time.time + t));
			else if (!sDestroyQueue.Contains(obj)) sDestroyQueue.Add(obj);
		}

		public static void ProcessDestroyQueue()
		{
			float now = UnityEngine.Time.time;
			for (int i = sDelayedDestroy.Count - 1; i >= 0; i--)
			{
				if (sDelayedDestroy[i].at <= now)
				{
					sDestroyQueue.Add(sDelayedDestroy[i].obj);
					sDelayedDestroy.RemoveAt(i);
				}
			}
			while (sDestroyQueue.Count > 0)
			{
				var batch = sDestroyQueue.ToArray();
				sDestroyQueue.Clear();
				foreach (var o in batch) DestroyNow(o);
			}
			if (sCompact) Compact();
		}

		private static bool sCompact;

		public static void DestroyNow(Object obj)
		{
			if (Object.__IsNull(obj)) return;
			switch (obj)
			{
				case GameObject go: DestroyGameObject(go); break;
				case Transform t: DestroyGameObject(t.gameObject); break;
				case Component c: DestroyComponent(c); break;
				default: obj.__destroyed = true; break;
			}
			sCompact = true;
		}

		private static void DestroyGameObject(GameObject go)
		{
			if (go == null || go.__destroyed) return;
			foreach (var child in go.transform.__children.ToArray())
				DestroyGameObject(child.gameObject);
			foreach (var c in go.__components.ToArray())
				if (!(c is Transform)) DestroyComponent(c);
			go.transform.__parent?.__children.Remove(go.transform);
			go.transform.__parent = null;
			go.transform.__OnDestroyNative();
			go.transform.__destroyed = true;
			go.__destroyed = true;
			Transform.__version++;
		}

		private static void DestroyComponent(Component c)
		{
			if (c.__destroyed) return;
			if (c is MonoBehaviour mb)
			{
				if (mb.__go != null && mb.__go.__active && mb.__awoken) Disable(mb);
				if (mb.__awoken) Safe(mb, mb.__cb.OnDestroy, "OnDestroy");
				CoroutineRunner.Stop(mb, null);
				Invoker.Cancel(mb, null);
			}
			c.__OnDestroyNative();
			c.__destroyed = true;
			c.__go?.__components.Remove(c);
		}

		private static void Compact()
		{
			sCompact = false;
			GameObjects.RemoveAll(g => g.__destroyed);
			Behaviours.RemoveAll(b => b.__destroyed);
		}

		// ---------------------------------------------------------------- find

		public static bool IsLiveSceneObject(GameObject go) => !go.__destroyed && !go.__isAsset;

		public static Object[] FindObjectsOfType(Type type, bool includeAssets)
		{
			var res = new List<Object>();
			if (typeof(GameObject).IsAssignableFrom(type))
			{
				foreach (var go in GameObjects)
					if (IsLiveSceneObject(go) && go.__active) res.Add(go);
			}
			else if (typeof(Component).IsAssignableFrom(type))
			{
				foreach (var go in GameObjects)
				{
					if (!IsLiveSceneObject(go) || !go.__active) continue;
					foreach (var c in go.__components)
						if (!c.__destroyed && type.IsInstanceOfType(c)) res.Add(c);
				}
			}
			if (includeAssets || !typeof(Component).IsAssignableFrom(type) && !typeof(GameObject).IsAssignableFrom(type))
				res.AddRange(AssetDb.LoadedOfType(type));
			var arr = (Object[])Array.CreateInstance(type, res.Count);
			for (int i = 0; i < res.Count; i++) arr[i] = res[i];
			return arr;
		}

		public static IEnumerable<GameObject> FindGameObjectsWithTag(string tag)
		{
			foreach (var go in GameObjects)
				if (IsLiveSceneObject(go) && go.__active && go.__tag == tag) yield return go;
		}

		public static GameObject FindGameObject(string name)
		{
			if (string.IsNullOrEmpty(name)) return null;
			if (name.Contains('/'))
			{
				bool rootOnly = name.StartsWith("/");
				string[] parts = name.Trim('/').Split('/');
				foreach (var go in GameObjects)
				{
					if (!IsLiveSceneObject(go) || go.__name != parts[0]) continue;
					if (rootOnly && go.transform.parent != null) continue;
					Transform t = parts.Length == 1 ? go.transform : go.transform.Find(string.Join("/", parts, 1, parts.Length - 1));
					if (t != null && t.gameObject.__active) return t.gameObject;
				}
				return null;
			}
			foreach (var go in GameObjects)
				if (IsLiveSceneObject(go) && go.__active && go.__name == name) return go;
			return null;
		}

		// ---------------------------------------------------------------- scene switch

		public static void UnloadScene()
		{
			foreach (var go in GameObjects.ToArray())
			{
				if (go.__destroyed || go.__isAsset) continue;
				if (go.transform.root.gameObject.__dontDestroy) continue;
				if (go.transform.parent == null) DestroyGameObject(go);
			}
			sStartQueue.RemoveAll(m => m.__destroyed);
			Compact();
		}
	}

	/// Per-behaviour delegates for Unity messages, bound once per instance.
	public static class Lifecycle
	{
		public struct Callbacks
		{
			public Action Awake, OnEnable, Start, Update, FixedUpdate, LateUpdate, OnGUI, OnDisable, OnDestroy;
			public Func<IEnumerator> StartCoroutine;
		}

		private static readonly Dictionary<Type, MethodInfo[]> sCache = new Dictionary<Type, MethodInfo[]>();
		private static readonly string[] Names = { "Awake", "OnEnable", "Start", "Update", "FixedUpdate", "LateUpdate", "OnGUI", "OnDisable", "OnDestroy" };

		public static void Bind(MonoBehaviour mb)
		{
			Type t = mb.GetType();
			if (!sCache.TryGetValue(t, out var ms))
			{
				ms = new MethodInfo[Names.Length];
				for (int i = 0; i < Names.Length; i++) ms[i] = Messaging.FindMethod(t, Names[i], 0);
				sCache[t] = ms;
			}
			Action D(MethodInfo m) => m != null && m.ReturnType == typeof(void) ? (Action)Delegate.CreateDelegate(typeof(Action), mb, m) : null;
			var cb = new Callbacks
			{
				Awake = D(ms[0]), OnEnable = D(ms[1]), Update = D(ms[3]), FixedUpdate = D(ms[4]),
				LateUpdate = D(ms[5]), OnGUI = D(ms[6]), OnDisable = D(ms[7]), OnDestroy = D(ms[8]),
			};
			if (ms[2] != null)
			{
				if (typeof(IEnumerator).IsAssignableFrom(ms[2].ReturnType))
					cb.StartCoroutine = (Func<IEnumerator>)Delegate.CreateDelegate(typeof(Func<IEnumerator>), mb, ms[2]);
				else cb.Start = D(ms[2]);
			}
			// Awake/OnEnable may also be coroutines in UnityScript code - ignore that corner case
			mb.__cb = cb;
		}
	}

	public static class Messaging
	{
		private static readonly Dictionary<(Type, string, int), MethodInfo> sCache = new Dictionary<(Type, string, int), MethodInfo>();
		private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

		/// arity -1: any (prefers 1 param when an argument is given)
		public static MethodInfo FindMethod(Type t, string name, int arity)
		{
			var key = (t, name, arity);
			if (sCache.TryGetValue(key, out var mi)) return mi;
			mi = null;
			for (Type cur = t; cur != null && cur != typeof(MonoBehaviour) && cur != typeof(object); cur = cur.BaseType)
			{
				foreach (var m in cur.GetMethods(Flags))
				{
					if (m.Name != name || m.IsGenericMethodDefinition) continue;
					int n = m.GetParameters().Length;
					if (arity >= 0 ? n == arity : n <= 1)
					{
						if (mi == null || (arity < 0 && n == 1)) mi = m;
					}
				}
				if (mi != null) break;
			}
			sCache[key] = mi;
			return mi;
		}

		public static bool InvokeOn(MonoBehaviour mb, string name, object arg)
		{
			var m = FindMethod(mb.GetType(), name, -1);
			if (m == null) return false;
			try
			{
				var ps = m.GetParameters();
				object r = m.Invoke(mb, ps.Length == 0 ? null : new[] { Coerce(arg, ps[0].ParameterType) });
				if (r is IEnumerator e) CoroutineRunner.Start(mb, e, name);
			}
			catch (TargetInvocationException e) { Log.Exception(e.InnerException ?? e, $"{mb.GetType().Name}.{name}"); }
			catch (Exception e) { Log.Exception(e, $"{mb.GetType().Name}.{name}"); }
			return true;
		}

		public static object InvokeForResult(MonoBehaviour mb, string name, object arg)
		{
			var m = FindMethod(mb.GetType(), name, -1);
			if (m == null) return null;
			var ps = m.GetParameters();
			try { return m.Invoke(mb, ps.Length == 0 ? null : new[] { Coerce(arg, ps[0].ParameterType) }); }
			catch (TargetInvocationException e) { Log.Exception(e.InnerException ?? e, $"{mb.GetType().Name}.{name}"); return null; }
		}

		private static object Coerce(object v, Type t)
		{
			if (v == null) return t.IsValueType ? Activator.CreateInstance(t) : null;
			if (t.IsInstanceOfType(v)) return v;
			try { return Convert.ChangeType(v, t); } catch { return v; }
		}

		public static bool Send(GameObject go, string name, object arg)
		{
			if (go == null || go.__destroyed) return false;
			bool any = false;
			foreach (var c in go.__components.ToArray())
				if (c is MonoBehaviour mb && !mb.__destroyed && InvokeOn(mb, name, arg)) any = true;
			return any;
		}

		public static bool Broadcast(GameObject go, string name, object arg)
		{
			if (go == null || go.__destroyed || !go.__active) return false;
			bool any = Send(go, name, arg);
			foreach (var ch in go.transform.__children.ToArray()) any |= Broadcast(ch.gameObject, name, arg);
			return any;
		}
	}

	public static class Invoker
	{
		private class Entry { public MonoBehaviour mb; public string name; public float at, repeat; }
		private static readonly List<Entry> sList = new List<Entry>();

		public static void Add(MonoBehaviour mb, string name, float t, float repeat) =>
			sList.Add(new Entry { mb = mb, name = name, at = UnityEngine.Time.time + Math.Max(0, t), repeat = repeat });

		public static void Cancel(MonoBehaviour mb, string name) => sList.RemoveAll(e => e.mb == mb && (name == null || e.name == name));

		public static bool IsInvoking(MonoBehaviour mb, string name) => sList.Exists(e => e.mb == mb && (name == null || e.name == name));

		public static void Run()
		{
			float now = UnityEngine.Time.time;
			foreach (var e in sList.ToArray())
			{
				if (e.mb.__destroyed) { sList.Remove(e); continue; }
				if (e.at > now) continue;
				if (e.repeat > 0f) e.at += e.repeat; else sList.Remove(e);
				if (e.mb.__go != null && e.mb.__go.__active)  // Unity 3 still invokes on disabled behaviours
					Messaging.InvokeOn(e.mb, e.name, null);
			}
		}
	}

	public static class CoroutineRunner
	{
		public class Routine
		{
			public MonoBehaviour owner;
			public string name;
			public Stack<IEnumerator> stack = new Stack<IEnumerator>();
			public float waitUntil;
			public bool waitFixed, waitEndOfFrame, done;
			public Routine waitFor;
			public Func<bool> waitCond;
			public Coroutine handle;
			public int startedFrame;
		}

		private static readonly List<Routine> sAll = new List<Routine>();

		public static Coroutine Start(MonoBehaviour mb, IEnumerator e, string name)
		{
			if (e == null) return null;
			var r = new Routine { owner = mb, name = name };
			r.stack.Push(e);
			r.handle = Coroutine.__Create();
			r.handle.__routine = r;
			sAll.Add(r);
			Step(r);    // runs synchronously until the first yield
			return r.handle;
		}

		public static void Stop(MonoBehaviour mb, string name)
		{
			foreach (var r in sAll)
				if (r.owner == mb && (name == null || r.name == name)) r.done = true;
		}

		public static void StopAllOn(GameObject go)
		{
			foreach (var r in sAll)
				if (r.owner != null && r.owner.__go == go) r.done = true;
		}

		private static bool Ready(Routine r, int phase)
		{
			if (r.waitFor != null && !r.waitFor.done) return false;
			if (r.waitCond != null && !r.waitCond()) return false;
			if (r.waitFixed) return phase == 1;
			if (r.waitEndOfFrame) return phase == 2;
			if (phase != 0) return false;
			return UnityEngine.Time.time >= r.waitUntil;
		}

		/// phase 0 = after Update, 1 = after FixedUpdate, 2 = end of frame
		public static void Run(int phase)
		{
			for (int i = 0; i < sAll.Count; i++)
			{
				var r = sAll[i];
				if (r.done) continue;
				if (r.owner == null || r.owner.__destroyed) { r.done = true; continue; }
				if (r.startedFrame == UnityEngine.Time.frameCount && phase == 0) continue;
				if (Ready(r, phase)) Step(r);
			}
			sAll.RemoveAll(r => r.done);
		}

		private static void Step(Routine r)
		{
			r.waitUntil = 0; r.waitFixed = r.waitEndOfFrame = false; r.waitFor = null; r.waitCond = null;
			r.startedFrame = UnityEngine.Time.frameCount;
			while (r.stack.Count > 0)
			{
				var e = r.stack.Peek();
				bool more;
				try { more = e.MoveNext(); }
				catch (Exception ex)
				{
					Log.Exception(ex, $"coroutine {r.owner?.GetType().Name}.{r.name}");
					r.stack.Clear();
					break;
				}
				if (!more) { r.stack.Pop(); continue; }
				object y = e.Current;
				switch (y)
				{
					case null: return;
					case WaitForSeconds w: r.waitUntil = UnityEngine.Time.time + w.m_Seconds; return;
					case WaitForFixedUpdate _: r.waitFixed = true; return;
					case WaitForEndOfFrame _: r.waitEndOfFrame = true; return;
					case Coroutine c: if (c.__routine != null && !c.__routine.done) { r.waitFor = c.__routine; return; } continue;
					case WWW www: r.waitCond = () => www.isDone; return;
					case AsyncOperation _: return;
					case IEnumerator nested: r.stack.Push(nested); continue;
					default: return;
				}
			}
			r.done = true;
		}
	}

	internal static class RoutineExt { }

	public static class TypeResolver
	{
		private static Dictionary<string, Type> sByName;

		public static Type Find(string name)
		{
			if (string.IsNullOrEmpty(name)) return null;
			if (sByName == null)
			{
				sByName = new Dictionary<string, Type>();
				foreach (var t in typeof(TypeResolver).Assembly.GetTypes())
				{
					if (!typeof(Object).IsAssignableFrom(t)) continue;
					if (t.Namespace == null || t.Namespace == "UnityEngine")
						sByName.TryAdd(t.Name, t);
					sByName.TryAdd(t.FullName, t);
				}
			}
			if (name == "EllipsoidParticleEmitter" || name == "MeshParticleEmitter") return typeof(ParticleEmitter);
			sByName.TryGetValue(name, out var r);
			return r;
		}
	}

	public static class Log
	{
		public static void Exception(Exception e, string where)
		{
			Godot.GD.PrintErr($"[svz] exception in {where}: {e}");
		}
	}
}
