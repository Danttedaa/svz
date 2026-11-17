using System;
using System.Collections.Generic;
using System.Linq;
using Svz;

namespace UnityEngine
{
	public partial class Object
	{
		internal string __name = string.Empty;
		internal bool __destroyed;
		internal bool __isAsset;          // loaded from a data file (prefab/material/...), not part of the scene
		internal HideFlags __hideFlags;

		public Object()
		{
			m_UnityRuntimeReferenceData.instanceID = Runtime.NextInstanceId();
		}

		internal static bool __IsNull(Object o) => (object)o == null || o.__destroyed;

		public partial string name
		{
			get => __name;
			set => __name = value ?? string.Empty;
		}

		public partial HideFlags hideFlags
		{
			get => __hideFlags;
			set => __hideFlags = value;
		}

		private static partial bool CompareBaseObjects(Object lhs, Object rhs)
		{
			bool ln = __IsNull(lhs), rn = __IsNull(rhs);
			if (ln || rn) return ln && rn;
			return ReferenceEquals(lhs, rhs);
		}

		private static partial Object Internal_CloneSingle(Object data) => Cloner.Instantiate(data, null, null);

		private static partial Object INTERNAL_CALL_Internal_InstantiateSingle(Object data, ref Vector3 pos, ref Quaternion rot) => Cloner.Instantiate(data, pos, rot);

		public static partial void Destroy(Object obj, float t) => Runtime.Destroy(obj, t);

		public static partial void DestroyImmediate(Object obj, bool allowDestroyingAssets) => Runtime.DestroyNow(obj);

		public static partial void DestroyObject(Object obj, float t) => Runtime.Destroy(obj, t);

		public static partial Object[] FindObjectsOfType(Type type) => Runtime.FindObjectsOfType(type, false);

		public static partial Object[] FindSceneObjectsOfType(Type type) => Runtime.FindObjectsOfType(type, false);

		public static partial Object[] FindObjectsOfTypeIncludingAssets(Type type) => Runtime.FindObjectsOfType(type, true);

		public static partial void DontDestroyOnLoad(Object target)
		{
			GameObject go = target as GameObject ?? (target as Component)?.gameObject;
			if (go != null) go.transform.root.gameObject.__dontDestroy = true;
		}

		public override partial string ToString() => $"{__name} ({GetType().Name})";

		/// Copy the serialized state of `src` into this fresh object (used by Instantiate).
		internal virtual void __CopyFrom(Object src, Cloner c)
		{
			__name = src.__name;
			__hideFlags = src.__hideFlags;
		}
	}

	public partial class Component
	{
		internal GameObject __go;

		public partial GameObject gameObject => __go;
		public partial Transform transform => __go?.transform;
		public partial Rigidbody rigidbody => __go?.rigidbody;
		public partial Camera camera => __go?.camera;
		public partial Light light => __go?.light;
		public partial Animation animation => __go?.animation;
		public partial ConstantForce constantForce => __go?.constantForce;
		public partial Renderer renderer => __go?.renderer;
		public partial AudioSource audio => __go?.audio;
		public partial GUIText guiText => __go?.guiText;
		public partial NetworkView networkView => null;
		public partial GUIElement guiElement => __go?.guiElement;
		public partial GUITexture guiTexture => __go?.guiTexture;
		public partial Collider collider => __go?.collider;
		public partial HingeJoint hingeJoint => null;
		public partial ParticleEmitter particleEmitter => __go?.particleEmitter;

		public partial bool active
		{
			get => __go != null && __go.active;
			set { if (__go != null) __go.active = value; }
		}

		public partial string tag
		{
			get => __go?.tag;
			set { if (__go != null) __go.tag = value; }
		}

		public partial Component GetComponent(Type type) => __go?.GetComponent(type);
		public partial Component[] GetComponents(Type type) => __go?.GetComponents(type) ?? new Component[0];
		private partial Component[] GetComponentsWithCorrectReturnType(Type type) => __go?.__GetComponentsTyped(type);
		public partial bool CompareTag(string tag) => __go != null && __go.CompareTag(tag);
		public partial void SendMessageUpwards(string methodName, object value, SendMessageOptions options) => __go?.SendMessageUpwards(methodName, value, options);
		public partial void SendMessage(string methodName, object value, SendMessageOptions options) => __go?.SendMessage(methodName, value, options);
		public partial void BroadcastMessage(string methodName, object parameter, SendMessageOptions options) => __go?.BroadcastMessage(methodName, parameter, options);

		/// Called once after the component is attached (before Awake for scripts).
		internal virtual void __OnAttach() { }
		/// Called when the component (or its GameObject) is destroyed.
		internal virtual void __OnDestroyNative() { }
		/// Called after the whole object graph of a scene / clone has been built.
		internal virtual void __OnLoaded() { }
	}

	public partial class Behaviour
	{
		internal bool __enabled = true;

		public partial bool enabled
		{
			get => __enabled;
			set
			{
				if (__enabled == value) return;
				__enabled = value;
				if (this is MonoBehaviour mb) Runtime.OnBehaviourEnabledChanged(mb);
				__OnEnabledChanged();
			}
		}

		internal virtual void __OnEnabledChanged() { }

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			__enabled = ((Behaviour)src).__enabled;
		}
	}

	public partial class GameObject
	{
		internal readonly List<Component> __components = new List<Component>(4);
		internal Transform __transform;
		internal bool __active = true;
		internal int __layer;
		internal string __tag = "Untagged";
		internal bool __dontDestroy;
		internal bool __isStatic;

		private static partial void Internal_CreateGameObject(GameObject mono, string name)
		{
			mono.__name = name ?? "New Game Object";
			if (!Runtime.CreatingBareGameObject)
			{
				mono.__transform = Runtime.CreateTransform();
				mono.__Attach(mono.__transform);
			}
			Runtime.RegisterGameObject(mono);
		}

		internal void __Attach(Component c)
		{
			c.__go = this;
			if (c is Transform t) __transform = t;
			__components.Add(c);
			c.__OnAttach();
		}

		public partial bool isStatic { get => __isStatic; set => __isStatic = value; }
		public partial Transform transform => __transform;
		public partial Rigidbody rigidbody => __Find<Rigidbody>();
		public partial Camera camera => __Find<Camera>();
		public partial Light light => __Find<Light>();
		public partial Animation animation => __Find<Animation>();
		public partial ConstantForce constantForce => __Find<ConstantForce>();
		public partial Renderer renderer => __Find<Renderer>();
		public partial AudioSource audio => __Find<AudioSource>();
		public partial GUIText guiText => __Find<GUIText>();
		public partial NetworkView networkView => null;
		public partial GUIElement guiElement => __Find<GUIElement>();
		public partial GUITexture guiTexture => __Find<GUITexture>();
		public partial Collider collider => __Find<Collider>();
		public partial HingeJoint hingeJoint => null;
		public partial ParticleEmitter particleEmitter => __Find<ParticleEmitter>();

		internal T __Find<T>() where T : Component
		{
			var list = __components;
			for (int i = 0; i < list.Count; i++)
				if (list[i] is T t && !t.__destroyed) return t;
			return null;
		}

		public partial int layer { get => __layer; set => __layer = value; }

		public partial bool active
		{
			get => __active;
			set
			{
				if (__active == value) return;
				__active = value;
				Runtime.OnGameObjectActiveChanged(this);
			}
		}

		public partial string tag
		{
			get => __tag;
			set => __tag = value ?? "Untagged";
		}

		public static partial GameObject CreatePrimitive(PrimitiveType type)
		{
			var go = new GameObject(type.ToString());
			go.AddComponent<MeshFilter>().sharedMesh = Primitives.Get(type);
			go.AddComponent<MeshRenderer>().sharedMaterial = Material.__Default();
			return go;
		}

		public partial Component GetComponent(Type type)
		{
			var list = __components;
			for (int i = 0; i < list.Count; i++)
			{
				var c = list[i];
				if (!c.__destroyed && type.IsInstanceOfType(c)) return c;
			}
			return null;
		}

		private partial Component GetComponentByName(string type)
		{
			Type t = TypeResolver.Find(type);
			if (t != null) return GetComponent(t);
			foreach (var c in __components)
				if (!c.__destroyed && c.GetType().Name == type) return c;
			return null;
		}

		public partial Component[] GetComponents(Type type) => __GetComponentsTyped(type);

		private partial Component[] GetComponentsWithCorrectReturnType(Type type) => __GetComponentsTyped(type);

		internal Component[] __GetComponentsTyped(Type type)
		{
			var l = new List<Component>();
			foreach (var c in __components)
				if (!c.__destroyed && type.IsInstanceOfType(c)) l.Add(c);
			var arr = (Component[])Array.CreateInstance(type.IsSubclassOf(typeof(Component)) || type == typeof(Component) ? type : typeof(Component), l.Count);
			for (int i = 0; i < l.Count; i++) arr[i] = l[i];
			return arr;
		}

		public partial bool CompareTag(string tag) => __tag == tag;

		public static partial GameObject FindGameObjectWithTag(string tag) => Runtime.FindGameObjectsWithTag(tag).FirstOrDefault();

		public static partial GameObject[] FindGameObjectsWithTag(string tag) => Runtime.FindGameObjectsWithTag(tag).ToArray();

		public partial void SendMessageUpwards(string methodName, object value, SendMessageOptions options)
		{
			bool any = false;
			for (Transform t = __transform; t != null; t = t.parent)
				any |= Messaging.Send(t.gameObject, methodName, value);
			if (!any && options == SendMessageOptions.RequireReceiver) Debug.LogError($"SendMessage {methodName} has no receiver!");
		}

		public partial void SendMessage(string methodName, object value, SendMessageOptions options)
		{
			bool any = Messaging.Send(this, methodName, value);
			if (!any && options == SendMessageOptions.RequireReceiver) Debug.LogError($"SendMessage {methodName} has no receiver!");
		}

		public partial void BroadcastMessage(string methodName, object parameter, SendMessageOptions options)
		{
			bool any = Messaging.Broadcast(this, methodName, parameter);
			if (!any && options == SendMessageOptions.RequireReceiver) Debug.LogError($"BroadcastMessage {methodName} has no receiver!");
		}

		public partial Component AddComponent(string className)
		{
			Type t = TypeResolver.Find(className);
			if (t == null) { Debug.LogError("AddComponent: unknown type " + className); return null; }
			return Internal_AddComponentWithType(t);
		}

		private partial Component Internal_AddComponentWithType(Type componentType) => Runtime.AddComponent(this, componentType);

		public partial void SampleAnimation(AnimationClip animation, float time) => AnimationSystem.SampleClip(this, animation, time, 1f);
		public partial void PlayAnimation(AnimationClip animation) { }
		public partial void StopAnimation() { }

		public static partial GameObject Find(string name) => Runtime.FindGameObject(name);

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			var s = (GameObject)src;
			__active = s.__active; __layer = s.__layer; __tag = s.__tag; __isStatic = s.__isStatic;
		}
	}

	public sealed partial class Coroutine
	{
		internal Svz.CoroutineRunner.Routine __routine;
		internal static Coroutine __Create() => new Coroutine();
		private partial void ReleaseCoroutine() { }
	}

	public partial class ScriptableObject
	{
		partial void __Init() { }
		private static partial void INTERNAL_CALL_SetDirty(ScriptableObject self) { }
		public static partial ScriptableObject CreateInstance(string className) => CreateInstanceFromType(TypeResolver.Find(className));
		private static partial ScriptableObject CreateInstanceFromType(Type type) => (ScriptableObject)Activator.CreateInstance(type);
	}
}
