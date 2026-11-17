using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Svz
{
	/// Loads the exported Unity data (res://data) and turns it into live UnityEngine objects.
	public static partial class AssetDb
	{
		public static string[] Files;
		private static readonly Dictionary<string, int> sFileKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<int, SFile> sParsed = new Dictionary<int, SFile>();
		/// assets (everything not loaded as part of a scene) - shared and never unloaded
		private static readonly Dictionary<(int, long), Object> sAssets = new Dictionary<(int, long), Object>();

		public sealed class SFile
		{
			public int key;
			public string name;
			public JsonDocument doc;
			public Dictionary<long, JsonElement> objects = new Dictionary<long, JsonElement>();
		}

		/// a scene being loaded: objects of `sceneFile` are fresh instances
		public sealed class Context
		{
			public int sceneFile = -1;
			public readonly Dictionary<long, Object> scene = new Dictionary<long, Object>();
			public readonly List<GameObject> built = new List<GameObject>();
		}

		public static void Init()
		{
			Files = JsonSerializer.Deserialize<string[]>(ReadText("res://data/files.json"));
			for (int i = 0; i < Files.Length; i++) sFileKey[Files[i]] = i;
		}

		/// path id of the object currently being populated (for file-backed payloads)
		[ThreadStatic] public static long CurrentPid;

		private static Dictionary<string, (int, long)> sShaderIndex;

		public static void EnsureShadersIndexed()
		{
			if (sShaderIndex != null) return;
			sShaderIndex = new Dictionary<string, (int, long)>(StringComparer.OrdinalIgnoreCase);
			try
			{
				using var doc = JsonDocument.Parse(ReadText("res://data/shaders.json"));
				foreach (var p in doc.RootElement.EnumerateObject())
					sShaderIndex[p.Name] = (p.Value[0].GetInt32(), p.Value[1].GetInt64());
			}
			catch (Exception e) { Godot.GD.PrintErr("[svz] shaders.json: " + e.Message); }
			foreach (var kv in sShaderIndex) Get(kv.Value.Item1, kv.Value.Item2, null);
		}

		public static int FileKey(string name) => sFileKey.TryGetValue(name, out int k) ? k : -1;

		public static byte[] ReadBytes(string path)
		{
			using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
			if (f == null) throw new FileNotFoundException(path);
			return f.GetBuffer((long)f.GetLength());
		}

		public static string ReadText(string path) => System.Text.Encoding.UTF8.GetString(ReadBytes(path));

		public static SFile File(int key)
		{
			if (sParsed.TryGetValue(key, out var sf)) return sf;
			byte[] gz = ReadBytes($"res://data/sf/{key}.json.gz");
			using var ms = new MemoryStream(gz);
			using var z = new GZipStream(ms, CompressionMode.Decompress);
			var doc = JsonDocument.Parse(z);
			sf = new SFile { key = key, name = Files[key], doc = doc };
			foreach (var p in doc.RootElement.GetProperty("objects").EnumerateObject())
				sf.objects[long.Parse(p.Name)] = p.Value;
			sParsed[key] = sf;
			return sf;
		}

		public static bool TryGetRaw(int file, long pid, out string cls, out JsonElement data)
		{
			cls = null; data = default;
			if (file < 0 || file >= Files.Length) return false;
			var sf = File(file);
			if (!sf.objects.TryGetValue(pid, out var o)) return false;
			cls = o.GetProperty("c").GetString();
			data = o.GetProperty("d");
			return true;
		}

		public static IEnumerable<Object> LoadedOfType(Type t) => sAssets.Values.Where(o => o != null && !o.__destroyed && t.IsInstanceOfType(o));

		// ------------------------------------------------------------------ object creation

		public static Object Get(JsonElement pptr, Context ctx)
		{
			if (pptr.ValueKind != JsonValueKind.Array || pptr.GetArrayLength() != 2) return null;
			var f = pptr[0];
			if (f.ValueKind != JsonValueKind.Number) return null;   // "builtin:..." externals we don't have
			int file = f.GetInt32();
			long pid = pptr[1].GetInt64();
			return Get(file, pid, ctx);
		}

		public static Object Get(int file, long pid, Context ctx)
		{
			if (file < 0 || pid == 0) return null;
			bool scene = ctx != null && file == ctx.sceneFile;
			if (scene) { if (ctx.scene.TryGetValue(pid, out var so)) return so; }
			else if (sAssets.TryGetValue((file, pid), out var ao)) return ao;
			if (!TryGetRaw(file, pid, out string cls, out JsonElement d)) return null;

			Object obj = Create(cls, d, ctx);
			if (obj == null)
			{
				if (scene) ctx.scene[pid] = null; else sAssets[(file, pid)] = null;
				return null;
			}
			obj.__isAsset = !scene;
			if (scene) ctx.scene[pid] = obj; else sAssets[(file, pid)] = obj;
			var ld = new Loader(file, ctx, !scene);
			long prevPid = CurrentPid;
			CurrentPid = pid;
			try
			{
				Populate(cls, obj, d, ld);
			}
			catch (Exception e)
			{
				Log.Exception(e, $"loading {cls} {Files[file]}:{pid}");
			}
			finally { CurrentPid = prevPid; }
			if (obj is GameObject go && (scene || ctx != null)) ctx?.built.Add(go);
			return obj;
		}

		private static Object Create(string cls, JsonElement d, Context ctx)
		{
			switch (cls)
			{
				case "GameObject":
				{
					Runtime.CreatingBareGameObject = true;
					try { return new GameObject(d.Str("m_Name")); }
					finally { Runtime.CreatingBareGameObject = false; }
				}
				case "MonoBehaviour":
				{
					Type t = ResolveScript(d);
					if (t == null || !typeof(Component).IsAssignableFrom(t) && !typeof(ScriptableObject).IsAssignableFrom(t)) return null;
					if (t.IsAbstract) return null;
					return (Object)Activator.CreateInstance(t, true);
				}
				default:
				{
					Type t = TypeResolver.Find(cls);
					if (t == null || t.IsAbstract) return null;
					if (t == typeof(Transform)) return Transform.__New();
					try { return (Object)Activator.CreateInstance(t, true); }
					catch (Exception e) { Log.Exception(e, "create " + cls); return null; }
				}
			}
		}

		private static readonly Dictionary<(int, long), Type> sScriptTypes = new Dictionary<(int, long), Type>();

		private static Type ResolveScript(JsonElement mb)
		{
			if (!mb.TryGetProperty("m_Script", out var s) || s.ValueKind != JsonValueKind.Array) return null;
			if (s[0].ValueKind != JsonValueKind.Number) return null;
			var key = (s[0].GetInt32(), s[1].GetInt64());
			if (sScriptTypes.TryGetValue(key, out var t)) return t;
			t = null;
			if (TryGetRaw(key.Item1, key.Item2, out var cls, out var ms) && cls == "MonoScript")
			{
				string ns = ms.Str("m_Namespace"), name = ms.Str("m_ClassName");
				t = TypeResolver.Find(string.IsNullOrEmpty(ns) ? name : ns + "." + name) ?? TypeResolver.Find(name);
				if (t == null) Godot.GD.PrintErr($"[svz] missing script type {ns}.{name}");
			}
			sScriptTypes[key] = t;
			return t;
		}

		// ------------------------------------------------------------------ population

		public delegate void PopulateFn(Object o, JsonElement d, Loader ld);
		private static readonly Dictionary<string, PopulateFn> sPopulators = new Dictionary<string, PopulateFn>();

		public static void Register(string cls, PopulateFn fn) => sPopulators[cls] = fn;

		private static void Populate(string cls, Object obj, JsonElement d, Loader ld)
		{
			if (d.TryGetProperty("m_Name", out var n) && n.ValueKind == JsonValueKind.String) obj.__name = n.GetString();
			switch (obj)
			{
				case GameObject go:
					PopulateGameObject(go, d, ld);
					return;
				case MonoBehaviour mb:
					PopulateComponentBase(mb, d, ld);
					mb.__enabled = d.Bool("m_Enabled", true);
					Deserializer.FillFields(mb, d, ld, typeof(MonoBehaviour));
					return;
				case ScriptableObject so:
					Deserializer.FillFields(so, d, ld, typeof(ScriptableObject));
					return;
			}
			if (obj is Component c) PopulateComponentBase(c, d, ld);
			if (obj is Behaviour b && d.TryGetProperty("m_Enabled", out var en)) b.__enabled = en.AsBool();
			if (sPopulators.TryGetValue(cls, out var fn)) fn(obj, d, ld);
		}

		private static void PopulateComponentBase(Component c, JsonElement d, Loader ld)
		{
			// the owning GameObject attaches the component (keeps component order)
			if (d.TryGetProperty("m_GameObject", out var g)) ld.Ref<GameObject>(g);
		}

		private static void PopulateGameObject(GameObject go, JsonElement d, Loader ld)
		{
			// Unity 3.4 stores every GameObject inside asset files (prefabs) with m_IsActive = 0
			go.__active = ld.asset || d.Bool("m_IsActive", true);
			go.__layer = d.Int("m_Layer");
			int tag = d.Int("m_Tag");
			go.__tag = Tags.Name(tag);
			if (d.TryGetProperty("m_Component", out var comps))
			{
				foreach (var pair in comps.EnumerateArray())
				{
					var c = ld.Ref<Component>(pair[1]);
					if (c == null || c.__go == go) continue;
					Runtime.AttachLoaded(go, c);
				}
			}
			if (go.__transform == null)
			{
				var t = Transform.__New();
				go.__Attach(t);
			}
		}

		// ------------------------------------------------------------------ scenes & resources

		public static List<GameObject> LoadScene(int levelIndex)
		{
			// Unity 3.4 players: scene 0 lives in mainData, scene N in level(N-1)
			string name = levelIndex == 0 ? "mainData" : "level" + (levelIndex - 1);
			int key = FileKey(name);
			var ctx = new Context { sceneFile = key };
			if (key < 0) { Godot.GD.PrintErr("[svz] no scene " + name); return ctx.built; }
			var sf = File(key);
			Runtime.BuildDepth++;
			try
			{
				foreach (var (pid, o) in sf.objects)
					if (o.GetProperty("c").GetString() == "GameObject") Get(key, pid, ctx);
				// scene settings (RenderSettings etc.)
				foreach (var (pid, o) in sf.objects)
				{
					string cls = o.GetProperty("c").GetString();
					if (cls == "RenderSettings") SceneSettings.Apply(o.GetProperty("d"), new Loader(key, ctx, false));
					if (cls == "LightmapSettings") LoadLightmaps(o.GetProperty("d"), new Loader(key, ctx, false));
				}
			}
			finally { Runtime.BuildDepth--; }
			Runtime.EndBuild(ctx.built);
			return ctx.built;
		}

		private static Dictionary<string, List<(int file, long pid)>> sResources;

		private static void InitResources()
		{
			sResources = new Dictionary<string, List<(int, long)>>(StringComparer.OrdinalIgnoreCase);
			int main = FileKey("mainData");
			var sf = File(main);
			foreach (var o in sf.objects.Values)
			{
				if (o.GetProperty("c").GetString() != "ResourceManager") continue;
				foreach (var pair in o.GetProperty("d").GetProperty("m_Container").EnumerateArray())
				{
					string path = pair[0].GetString();
					var p = pair[1];
					if (p[0].ValueKind != JsonValueKind.Number) continue;
					if (!sResources.TryGetValue(path, out var l)) sResources[path] = l = new List<(int, long)>();
					l.Add((p[0].GetInt32(), p[1].GetInt64()));
				}
			}
		}

		public static Object LoadResource(string path, Type type)
		{
			if (sResources == null) InitResources();
			path = path.Replace('\\', '/').Trim('/');
			if (!sResources.TryGetValue(path, out var list)) return null;
			Runtime.BuildDepth++;
			var ctx = new Context();
			Object best = null;
			try
			{
				// Unity returns the main asset: for prefabs that is the GameObject
				var ordered = list.OrderBy(e => TryGetRaw(e.file, e.pid, out string c0, out _) && c0 == "GameObject" ? 0 : 1).ThenBy(e => e.pid).ToList();
				foreach (var (file, pid) in ordered)
				{
					if (!TryGetRaw(file, pid, out string cls, out _)) continue;
					// cheap type check before loading
					Type ct = cls == "MonoBehaviour" ? null : TypeResolver.Find(cls);
					if (ct != null && !type.IsAssignableFrom(ct) && !(type.IsSubclassOf(typeof(Component)) && ct == typeof(GameObject))) continue;
					var o = Get(file, pid, ctx);
					if (o == null) continue;
					if (type.IsInstanceOfType(o)) { best = o; break; }
					if (o is GameObject g && typeof(Component).IsAssignableFrom(type))
					{
						var comp = g.GetComponent(type);
						if (comp != null) { best = comp; break; }
					}
				}
			}
			finally { Runtime.BuildDepth--; }
			Runtime.EndBuild(ctx.built);   // assets: only __OnLoaded hooks run
			if (Main.Verbose) Godot.GD.Print($"[svz] Resources.Load {path} ({type.Name}) -> {best?.GetType().Name} '{best?.name}' children={(best as GameObject)?.transform.childCount}");
			return best;
		}

		public static Object[] LoadAllResources(string path, Type type)
		{
			if (sResources == null) InitResources();
			path = path.Replace('\\', '/').Trim('/');
			var res = new List<Object>();
			foreach (var kv in sResources)
			{
				if (path.Length > 0 && !(kv.Key.Equals(path, StringComparison.OrdinalIgnoreCase) || kv.Key.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))) continue;
				var o = LoadResource(kv.Key, type);
				if (o != null && !res.Contains(o)) res.Add(o);
			}
			return res.ToArray();
		}
	}

	/// Resolves PPtrs relative to the file being loaded.
	public readonly struct Loader
	{
		public readonly int file;
		public readonly AssetDb.Context ctx;
		public readonly bool asset;

		public Loader(int file, AssetDb.Context ctx, bool asset)
		{
			this.file = file; this.ctx = ctx; this.asset = asset;
		}

		public T Ref<T>(JsonElement pptr) where T : Object => AssetDb.Get(pptr, ctx) as T;

		public Object Ref(JsonElement pptr) => AssetDb.Get(pptr, ctx);

		public List<T> Refs<T>(JsonElement arr) where T : Object
		{
			var l = new List<T>();
			if (arr.ValueKind == JsonValueKind.Array)
				foreach (var e in arr.EnumerateArray()) l.Add(Ref<T>(e));
			return l;
		}
	}

	public static class Tags
	{
		private static string[] sUser;

		public static string Name(int tag)
		{
			switch (tag)
			{
				case 0: return "Untagged";
				case 1: return "Respawn";
				case 2: return "Finish";
				case 3: return "EditorOnly";
				case 5: return "MainCamera";
				case 6: return "Player";
				case 7: return "GameController";
			}
			if (sUser == null)
			{
				sUser = new string[0];
				int main = AssetDb.FileKey("mainData");
				foreach (var o in AssetDb.File(main).objects.Values)
					if (o.GetProperty("c").GetString() == "TagManager")
						sUser = o.GetProperty("d").GetProperty("tags").EnumerateArray().Select(e => e.GetString()).ToArray();
			}
			int i = tag - 20000;
			return i >= 0 && i < sUser.Length ? sUser[i] : "Untagged";
		}
	}

	public static class JsonExt
	{
		public static string Str(this JsonElement e, string name, string def = "") =>
			e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : def;

		public static int Int(this JsonElement e, string name, int def = 0) =>
			e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.AsInt() : def;

		public static float Float(this JsonElement e, string name, float def = 0f) =>
			e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : def;

		public static bool Bool(this JsonElement e, string name, bool def = false) =>
			e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.AsBool() : def;

		public static JsonElement Get(this JsonElement e, string name) =>
			e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

		public static bool AsBool(this JsonElement v) => v.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Number => v.GetDouble() != 0,
			_ => false,
		};

		public static int AsInt(this JsonElement v) => v.ValueKind switch
		{
			JsonValueKind.Number => v.TryGetInt32(out int i) ? i : (int)v.GetDouble(),
			JsonValueKind.True => 1,
			_ => 0,
		};

		public static Vector2 V2(this JsonElement e) => new Vector2(e.Float("x"), e.Float("y"));
		public static Vector3 V3(this JsonElement e) => new Vector3(e.Float("x"), e.Float("y"), e.Float("z"));
		public static Vector4 V4(this JsonElement e) => new Vector4(e.Float("x"), e.Float("y"), e.Float("z"), e.Float("w"));
		public static Quaternion Q(this JsonElement e) => new Quaternion(e.Float("x"), e.Float("y"), e.Float("z"), e.Float("w", 1f));
		public static Color Col(this JsonElement e)
		{
			if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("rgba", out var p))
			{
				uint v = (uint)p.GetInt64();
				return new Color((v & 255) / 255f, ((v >> 8) & 255) / 255f, ((v >> 16) & 255) / 255f, ((v >> 24) & 255) / 255f);
			}
			return new Color(e.Float("r"), e.Float("g"), e.Float("b"), e.Float("a", 1f));
		}
		public static Rect R(this JsonElement e) => new Rect(e.Float("x"), e.Float("y"), e.Float("width"), e.Float("height"));

		public static byte[] Bytes(this JsonElement e)
		{
			if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("$b64", out var b)) return Convert.FromBase64String(b.GetString());
			if (e.ValueKind == JsonValueKind.Array) return e.EnumerateArray().Select(x => (byte)x.AsInt()).ToArray();
			if (e.ValueKind == JsonValueKind.String) return System.Text.Encoding.UTF8.GetBytes(e.GetString());
			return new byte[0];
		}
	}

	/// Unity-style serialized field population for scripts (MonoBehaviour / ScriptableObject / [Serializable]).
	public static class Deserializer
	{
		private const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
		private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> sFields = new Dictionary<Type, Dictionary<string, FieldInfo>>();
		private static readonly HashSet<string> sSkip = new HashSet<string> { "m_GameObject", "m_Enabled", "m_Script", "m_Name", "m_EditorHideFlags", "m_ObjectHideFlags" };

		public static Dictionary<string, FieldInfo> Fields(Type t, Type stop)
		{
			if (sFields.TryGetValue(t, out var d)) return d;
			d = new Dictionary<string, FieldInfo>();
			for (Type cur = t; cur != null && cur != stop && cur != typeof(object); cur = cur.BaseType)
				foreach (var f in cur.GetFields(F))
					if (!f.IsStatic && !f.IsInitOnly && !d.ContainsKey(f.Name)) d[f.Name] = f;
			sFields[t] = d;
			return d;
		}

		public static void FillFields(object target, JsonElement d, Loader ld, Type stop)
		{
			var fields = Fields(target.GetType(), stop);
			foreach (var p in d.EnumerateObject())
			{
				if (sSkip.Contains(p.Name)) continue;
				if (!fields.TryGetValue(p.Name, out var f)) continue;
				try { f.SetValue(target, Convert(p.Value, f.FieldType, ld)); }
				catch (Exception e) { Godot.GD.PrintErr($"[svz] field {target.GetType().Name}.{p.Name}: {e.Message}"); }
			}
		}

		public static object Convert(JsonElement v, Type t, Loader ld)
		{
			if (t == typeof(string)) return v.ValueKind == JsonValueKind.String ? v.GetString() : "";
			if (t == typeof(bool)) return v.AsBool();
			if (t.IsEnum) return Enum.ToObject(t, v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0);
			if (t.IsPrimitive)
			{
				if (v.ValueKind != JsonValueKind.Number) return Activator.CreateInstance(t);
				if (t == typeof(float)) return (float)v.GetDouble();
				if (t == typeof(double)) return v.GetDouble();
				if (t == typeof(int)) return v.AsInt();
				if (t == typeof(char)) return (char)v.AsInt();
				return System.Convert.ChangeType(v.GetDouble(), t);
			}
			if (typeof(Object).IsAssignableFrom(t))
			{
				var o = ld.Ref(v);
				if (o == null) return null;
				if (t.IsInstanceOfType(o)) return o;
				if (o is GameObject g && typeof(Component).IsAssignableFrom(t)) return g.GetComponent(t);
				return null;
			}
			if (t == typeof(Vector2)) return v.V2();
			if (t == typeof(Vector3)) return v.V3();
			if (t == typeof(Vector4)) return v.V4();
			if (t == typeof(Quaternion)) return v.Q();
			if (t == typeof(Color)) return v.Col();
			if (t == typeof(Color32)) { var c = v.Col(); return (Color32)c; }
			if (t == typeof(Rect)) return v.R();
			if (t == typeof(Bounds)) return new Bounds(v.Get("m_Center").V3(), v.Get("m_Extent").V3() * 2f);
			if (t == typeof(LayerMask)) return (LayerMask)v.Int("m_Bits");
			if (t == typeof(AnimationCurve)) return AnimationCurve.__FromJson(v);
			if (t == typeof(byte[])) return v.Bytes();
			if (t.IsArray)
			{
				Type et = t.GetElementType();
				if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("$b64", out _))
				{
					var bytes = v.Bytes();
					var a0 = Array.CreateInstance(et, bytes.Length);
					for (int i = 0; i < bytes.Length; i++) a0.SetValue(System.Convert.ChangeType(bytes[i], et), i);
					return a0;
				}
				if (v.ValueKind != JsonValueKind.Array) return Array.CreateInstance(et, 0);
				var arr = Array.CreateInstance(et, v.GetArrayLength());
				int k = 0;
				foreach (var e in v.EnumerateArray()) arr.SetValue(Convert(e, et, ld), k++);
				return arr;
			}
			if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
			{
				Type et = t.GetGenericArguments()[0];
				var list = (IList)Activator.CreateInstance(t);
				if (v.ValueKind == JsonValueKind.Array)
					foreach (var e in v.EnumerateArray()) list.Add(Convert(e, et, ld));
				return list;
			}
			if (v.ValueKind == JsonValueKind.Object && (t.IsClass || t.IsValueType))
			{
				object inst;
				try { inst = Activator.CreateInstance(t, true); }
				catch { return null; }
				FillFields(inst, v, ld, typeof(object));
				return inst;
			}
			return t.IsValueType ? Activator.CreateInstance(t) : null;
		}
	}

	/// Instantiate(): Unity-like deep clone of an object hierarchy with reference remapping.
	public sealed class Cloner
	{
		private readonly Dictionary<Object, Object> map = new Dictionary<Object, Object>();

		public static Object Instantiate(Object original, Vector3? pos, Quaternion? rot)
		{
			if (Object.__IsNull(original)) return null;
			var c = new Cloner();
			Runtime.BuildDepth++;
			Object result;
			var built = new List<GameObject>();
			try
			{
				switch (original)
				{
					case GameObject go:
						result = c.CloneHierarchy(go, built);
						break;
					case Component comp:
						var root = c.CloneHierarchy(comp.gameObject, built);
						result = c.map.TryGetValue(comp, out var r) ? r : root;
						break;
					default:
						result = c.CloneAsset(original);
						break;
				}
				// second pass: fill fields now that every clone exists
				foreach (var kv in c.map.ToArray())
				{
					if (kv.Key is GameObject || kv.Key is Transform) continue;
					kv.Value.__CopyFrom(kv.Key, c);
					if (kv.Value is MonoBehaviour || kv.Value is ScriptableObject) c.CopySerialized(kv.Key, kv.Value);
				}
				if (result is GameObject rg || (result is Component rc && (rg = rc.gameObject) != null))
				{
					var t = rg.transform;
					if (pos.HasValue) t.position = pos.Value;
					if (rot.HasValue) t.rotation = rot.Value;
				}
			}
			finally { Runtime.BuildDepth--; }
			Runtime.EndBuild(built);
			if (Main.Verbose) Godot.GD.Print($"[svz] Instantiate {original.name} ({original.GetType().Name}) -> {built.Count} objects, root comps: {(built.Count > 0 ? string.Join(",", built[0].__components.Select(x => x.GetType().Name + (x is MonoBehaviour m ? (m.__awoken ? "+" : "-") : ""))) : "-")} active={(built.Count > 0 ? built[0].__active : false)} asset={(built.Count > 0 ? built[0].__isAsset : false)}");
			return result;
		}

		private GameObject CloneHierarchy(GameObject src, List<GameObject> built)
		{
			var clone = CloneGo(src, built);
			// parent of the root clone stays null (Unity: Instantiate makes a new root)
			Link(src.transform, clone.transform);
			return clone;
		}

		private GameObject CloneGo(GameObject src, List<GameObject> built)
		{
			Runtime.CreatingBareGameObject = true;
			GameObject go;
			try { go = new GameObject(src.__name); }
			finally { Runtime.CreatingBareGameObject = false; }
			go.__CopyFrom(src, this);
			go.__name = src.__name + (built.Count == 0 ? "(Clone)" : "");
			map[src] = go;
			built.Add(go);
			foreach (var c in src.__components)
			{
				if (c.__destroyed) continue;
				Component nc = Runtime.Construct(c.GetType());
				map[c] = nc;
				if (c is Transform st) nc.__CopyFrom(st, this);
				Runtime.AttachLoaded(go, nc);
				if (nc is Behaviour nb) nb.__enabled = ((Behaviour)c).__enabled;
			}
			foreach (var ch in src.transform.__children)
				if (!ch.__destroyed) CloneGo(ch.gameObject, built);
			return go;
		}

		private void Link(Transform src, Transform dst)
		{
			foreach (var ch in src.__children)
			{
				if (ch.__destroyed || !map.TryGetValue(ch, out var o)) continue;
				var nt = (Transform)o;
				nt.__parent = dst;
				dst.__children.Add(nt);
				Link(ch, nt);
			}
		}

		private Object CloneAsset(Object src)
		{
			// Instantiate of a non-GameObject asset (Material, Mesh, ScriptableObject ...)
			Object o = src is ScriptableObject ? (Object)Activator.CreateInstance(src.GetType(), true) : (Object)Activator.CreateInstance(src.GetType(), true);
			map[src] = o;
			o.__name = src.__name + "(Clone)";
			return o;
		}

		/// remap a reference that may point into the cloned hierarchy
		public T Remap<T>(T o) where T : Object
		{
			if (o == null) return null;
			return map.TryGetValue(o, out var r) ? (T)r : o;
		}

		private void CopySerialized(Object src, Object dst)
		{
			Type stop = src is MonoBehaviour ? typeof(MonoBehaviour) : typeof(ScriptableObject);
			foreach (var f in Deserializer.Fields(src.GetType(), stop).Values)
			{
				if (!IsSerialized(f)) continue;
				f.SetValue(dst, CopyValue(f.GetValue(src), f.FieldType));
			}
		}

		private static bool IsSerialized(FieldInfo f)
		{
			if (f.IsNotSerialized) return false;
			if (!f.IsPublic && f.GetCustomAttribute<SerializeField>() == null) return false;
			return IsSerializableType(f.FieldType);
		}

		private static bool IsSerializableType(Type t)
		{
			if (t.IsPrimitive || t.IsEnum || t == typeof(string)) return true;
			if (typeof(Object).IsAssignableFrom(t)) return true;
			if (t.Namespace == "UnityEngine" && t.IsValueType) return true;
			if (t == typeof(AnimationCurve)) return true;
			if (t.IsArray) return t.GetArrayRank() == 1 && IsSerializableType(t.GetElementType());
			if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return IsSerializableType(t.GetGenericArguments()[0]);
			return t.IsSerializable && !t.IsAbstract && !typeof(Delegate).IsAssignableFrom(t);
		}

		private object CopyValue(object v, Type t)
		{
			if (v == null) return null;
			switch (v)
			{
				case string _: return v;
				case Object o: return Remap(o);
			}
			if (t.IsPrimitive || t.IsEnum || v.GetType().IsValueType && v.GetType().Namespace == "UnityEngine") return v;
			if (v is AnimationCurve ac) return new AnimationCurve(ac.keys) { preWrapMode = ac.preWrapMode, postWrapMode = ac.postWrapMode };
			if (v is Array a)
			{
				Type et = a.GetType().GetElementType();
				var na = Array.CreateInstance(et, a.Length);
				for (int i = 0; i < a.Length; i++) na.SetValue(CopyValue(a.GetValue(i), et), i);
				return na;
			}
			if (v is IList l && v.GetType().IsGenericType)
			{
				Type et = v.GetType().GetGenericArguments()[0];
				var nl = (IList)Activator.CreateInstance(v.GetType());
				foreach (var e in l) nl.Add(CopyValue(e, et));
				return nl;
			}
			Type vt = v.GetType();
			if (vt.IsValueType && !vt.IsSerializable) return v;
			object inst;
			try { inst = Activator.CreateInstance(vt, true); } catch { return v; }
			foreach (var f in Deserializer.Fields(vt, typeof(object)).Values)
				if (IsSerialized(f)) f.SetValue(inst, CopyValue(f.GetValue(v), f.FieldType));
			return inst;
		}
	}
}
