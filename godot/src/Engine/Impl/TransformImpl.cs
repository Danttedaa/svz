using System.Collections.Generic;
using Svz;

namespace UnityEngine
{
	// Managed transform hierarchy in Unity space. Godot only sees the resulting world matrices.
	public partial class Transform
	{
		internal Vector3 __lp = Vector3.zero;
		internal Quaternion __lr = Quaternion.identity;
		internal Vector3 __ls = Vector3.one;
		internal Transform __parent;
		internal readonly List<Transform> __children = new List<Transform>();

		// world matrix cache: valid while __ver == global hierarchy version
		private Matrix4x4 __l2w;
		private int __cacheVer = -1;
		internal static int __version;

		internal static Transform __New() => new Transform();

		private static void Dirty() => __version++;

		public partial Transform parent
		{
			get => __parent;
			set => __SetParent(value, true);
		}

		internal void __SetParent(Transform p, bool keepWorld)
		{
			if (p == __parent) return;
			for (Transform t = p; t != null; t = t.__parent)
				if (t == this) { Debug.LogError("Cannot parent a transform to its own child"); return; }
			Vector3 wp = position; Quaternion wr = rotation; Vector3 ws = lossyScale;
			__parent?.__children.Remove(this);
			__parent = p;
			p?.__children.Add(this);
			if (keepWorld)
			{
				if (p == null) { __lp = wp; __lr = wr; __ls = ws; }
				else
				{
					__lp = p.InverseTransformPoint(wp);
					__lr = Quaternion.Inverse(p.rotation) * wr;
					Vector3 ps = p.lossyScale;
					__ls = new Vector3(Div(ws.x, ps.x), Div(ws.y, ps.y), Div(ws.z, ps.z));
				}
			}
			Dirty();
			Runtime.OnParentChanged(this);
		}

		private static float Div(float a, float b) => Mathf.Abs(b) < 1e-9f ? a : a / b;

		public partial Transform root
		{
			get
			{
				Transform t = this;
				while (t.__parent != null) t = t.__parent;
				return t;
			}
		}

		public partial int childCount => __children.Count;

		internal Matrix4x4 __L2W
		{
			get
			{
				if (__cacheVer != __version)
				{
					Matrix4x4 local = Matrix4x4.TRS(__lp, __lr, __ls);
					__l2w = __parent == null ? local : __parent.__L2W * local;
					__cacheVer = __version;
				}
				return __l2w;
			}
		}

		private partial void INTERNAL_get_position(out Vector3 value)
		{
			Matrix4x4 m = __L2W;
			value = new Vector3(m.m03, m.m13, m.m23);
		}

		private partial void INTERNAL_set_position(ref Vector3 value)
		{
			__lp = __parent == null ? value : __parent.InverseTransformPoint(value);
			Dirty();
		}

		private partial void INTERNAL_get_localPosition(out Vector3 value) => value = __lp;

		private partial void INTERNAL_set_localPosition(ref Vector3 value)
		{
			__lp = value;
			Dirty();
		}

		private partial void INTERNAL_get_localEulerAngles(out Vector3 value) => value = __lr.eulerAngles;

		private partial void INTERNAL_set_localEulerAngles(ref Vector3 value)
		{
			__lr = Quaternion.Euler(value);
			Dirty();
		}

		internal Quaternion __WorldRot
		{
			get
			{
				Quaternion q = __lr;
				for (Transform t = __parent; t != null; t = t.__parent) q = t.__lr * q;
				return q;
			}
		}

		private partial void INTERNAL_get_rotation(out Quaternion value) => value = __WorldRot;

		private partial void INTERNAL_set_rotation(ref Quaternion value)
		{
			__lr = __parent == null ? value : Quaternion.Inverse(__parent.__WorldRot) * value;
			Dirty();
		}

		private partial void INTERNAL_get_localRotation(out Quaternion value) => value = __lr;

		private partial void INTERNAL_set_localRotation(ref Quaternion value)
		{
			__lr = value;
			Dirty();
		}

		private partial void INTERNAL_get_localScale(out Vector3 value) => value = __ls;

		private partial void INTERNAL_set_localScale(ref Vector3 value)
		{
			__ls = value;
			Dirty();
		}

		private partial void INTERNAL_get_worldToLocalMatrix(out Matrix4x4 value) => value = __L2W.inverse;

		private partial void INTERNAL_get_localToWorldMatrix(out Matrix4x4 value) => value = __L2W;

		private static partial void INTERNAL_CALL_LookAt(Transform self, ref Vector3 worldPosition, ref Vector3 worldUp)
		{
			Vector3 f = worldPosition - self.position;
			if (f.sqrMagnitude < 1e-12f) return;
			self.rotation = Quaternion.LookRotation(f, worldUp);
		}

		private static partial Vector3 INTERNAL_CALL_TransformDirection(Transform self, ref Vector3 direction) => self.__WorldRot * direction;

		private static partial Vector3 INTERNAL_CALL_InverseTransformDirection(Transform self, ref Vector3 direction) => Quaternion.Inverse(self.__WorldRot) * direction;

		private static partial Vector3 INTERNAL_CALL_TransformPoint(Transform self, ref Vector3 position) => self.__L2W.MultiplyPoint3x4(position);

		private static partial Vector3 INTERNAL_CALL_InverseTransformPoint(Transform self, ref Vector3 position) => self.__L2W.inverse.MultiplyPoint3x4(position);

		public partial void DetachChildren()
		{
			foreach (var c in __children.ToArray()) c.__SetParent(null, true);
		}

		public partial Transform Find(string name)
		{
			Transform cur = this;
			foreach (string part in name.Split('/'))
			{
				if (part.Length == 0) continue;
				Transform next = null;
				foreach (var c in cur.__children)
					if (c.gameObject != null && c.gameObject.name == part) { next = c; break; }
				if (next == null) return null;
				cur = next;
			}
			return cur;
		}

		private partial void INTERNAL_get_lossyScale(out Vector3 value)
		{
			Vector3 s = __ls;
			for (Transform t = __parent; t != null; t = t.__parent) s = Vector3.Scale(s, t.__ls);
			value = s;
		}

		public partial bool IsChildOf(Transform parent)
		{
			for (Transform t = this; t != null; t = t.__parent)
				if (t == parent) return true;
			return false;
		}

		private static partial void INTERNAL_CALL_RotateAround(Transform self, ref Vector3 axis, float angle)
		{
			self.rotation = Quaternion.AxisAngleRad(axis, angle) * self.rotation;
		}

		private static partial void INTERNAL_CALL_RotateAroundLocal(Transform self, ref Vector3 axis, float angle)
		{
			self.localRotation = self.__lr * Quaternion.AxisAngleRad(axis, angle);
		}

		public partial Transform GetChild(int index) => __children[index];

		public partial int GetChildCount() => __children.Count;

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			var s = (Transform)src;
			__lp = s.__lp; __lr = s.__lr; __ls = s.__ls;
			Dirty();
		}
	}

	public partial class MonoBehaviour
	{
		internal bool __awoken, __started, __startQueued, __enabledCalled;
		internal Svz.Lifecycle.Callbacks __cb;

		partial void __Init() { }

		private bool __p_useGUILayout = true;

		public partial bool useGUILayout { get => __p_useGUILayout; set => __p_useGUILayout = value; }

		private partial void Internal_CancelInvokeAll() => Svz.Invoker.Cancel(this, null);
		private partial bool Internal_IsInvokingAll() => Svz.Invoker.IsInvoking(this, null);
		public partial void Invoke(string methodName, float time) => Svz.Invoker.Add(this, methodName, time, -1f);
		public partial void InvokeRepeating(string methodName, float time, float repeatRate) => Svz.Invoker.Add(this, methodName, time, repeatRate);
		public partial void CancelInvoke(string methodName) => Svz.Invoker.Cancel(this, methodName);
		public partial bool IsInvoking(string methodName) => Svz.Invoker.IsInvoking(this, methodName);

		public partial Coroutine StartCoroutine_Auto(System.Collections.IEnumerator routine) => Svz.CoroutineRunner.Start(this, routine, null);

		public partial Coroutine StartCoroutine(string methodName, object value)
		{
			var e = Svz.Messaging.InvokeForResult(this, methodName, value) as System.Collections.IEnumerator;
			if (e == null) { Debug.LogError($"StartCoroutine: {methodName} not found or not a coroutine"); return null; }
			return Svz.CoroutineRunner.Start(this, e, methodName);
		}

		public partial void StopCoroutine(string methodName) => Svz.CoroutineRunner.Stop(this, methodName);
		public partial void StopAllCoroutines() => Svz.CoroutineRunner.Stop(this, null);
	}
}
