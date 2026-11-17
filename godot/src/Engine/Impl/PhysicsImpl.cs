using System;
using System.Collections.Generic;
using System.Linq;
using Svz;
using G = Godot;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEngine
{
	public partial class Collider
	{
		internal bool __enabled = true, __trigger;
		internal PhysicMaterial __pmat;
		internal G.CollisionObject3D __body;      // own static body / area (null when part of a rigidbody)
		internal G.CollisionShape3D __shapeNode;
		internal int __shapeVersion = -1;

		public partial bool enabled { get => __enabled; set { __enabled = value; PhysicsWorld.MarkDirty(this); } }
		public partial Rigidbody attachedRigidbody => PhysicsWorld.FindRigidbody(this);
		public partial bool isTrigger { get => __trigger; set { __trigger = value; PhysicsWorld.Rebuild(this); } }
		public partial PhysicMaterial material { get => __pmat; set => __pmat = value; }
		public partial PhysicMaterial sharedMaterial { get => __pmat; set => __pmat = value; }

		private static partial Vector3 INTERNAL_CALL_ClosestPointOnBounds(Collider self, ref Vector3 position)
		{
			var b = self.bounds;
			Vector3 mn = b.min, mx = b.max;
			return new Vector3(Mathf.Clamp(position.x, mn.x, mx.x), Mathf.Clamp(position.y, mn.y, mx.y), Mathf.Clamp(position.z, mn.z, mx.z));
		}

		private partial void INTERNAL_get_bounds(out Bounds value) => value = __WorldBounds();

		internal virtual Bounds __LocalBounds() => new Bounds(Vector3.zero, Vector3.zero);

		internal Bounds __WorldBounds()
		{
			var lb = __LocalBounds();
			var m = transform.localToWorldMatrix;
			Vector3 c = m.MultiplyPoint3x4(lb.center), e = lb.extents;
			Vector3 ax = m.MultiplyVector(new Vector3(e.x, 0, 0)), ay = m.MultiplyVector(new Vector3(0, e.y, 0)), az = m.MultiplyVector(new Vector3(0, 0, e.z));
			Vector3 ext = new Vector3(Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x), Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y), Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
			return new Bounds(c, ext * 2f);
		}

		private static partial bool INTERNAL_CALL_Internal_Raycast(Collider col, ref Ray ray, out RaycastHit hitInfo, float distance)
		{
			hitInfo = default;
			foreach (var h in PhysicsWorld.RaycastAll(ray.origin, ray.direction, distance, -1, col))
			{
				hitInfo = h;
				return true;
			}
			return false;
		}

		/// Godot shape in the collider's local space (Unity scale baked in)
		internal virtual G.Shape3D __MakeShape(out Vector3 localCenter) { localCenter = Vector3.zero; return null; }

		internal override void __OnAttach() => PhysicsWorld.Add(this);
		internal override void __OnDestroyNative() => PhysicsWorld.Remove(this);
		internal override void __OnActiveChanged() => PhysicsWorld.MarkDirty(this);

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			var s = (Collider)src;
			__enabled = s.__enabled; __trigger = s.__trigger; __pmat = s.__pmat;
		}
	}

	public partial class BoxCollider
	{
		internal Vector3 __center, __size = Vector3.one;
		private partial void INTERNAL_get_center(out Vector3 value) => value = __center;
		private partial void INTERNAL_set_center(ref Vector3 value) { __center = value; PhysicsWorld.Rebuild(this); }
		private partial void INTERNAL_get_size(out Vector3 value) => value = __size;
		private partial void INTERNAL_set_size(ref Vector3 value) { __size = value; PhysicsWorld.Rebuild(this); }
		internal override Bounds __LocalBounds() => new Bounds(__center, __size);

		internal override G.Shape3D __MakeShape(out Vector3 localCenter)
		{
			Vector3 s = transform.lossyScale;
			localCenter = __center;
			return new G.BoxShape3D { Size = new G.Vector3(Mathf.Max(0.001f, Mathf.Abs(__size.x * s.x)), Mathf.Max(0.001f, Mathf.Abs(__size.y * s.y)), Mathf.Max(0.001f, Mathf.Abs(__size.z * s.z))) };
		}

		internal override void __CopyFrom(Object src, Cloner c) { base.__CopyFrom(src, c); var s = (BoxCollider)src; __center = s.__center; __size = s.__size; }
	}

	public partial class SphereCollider
	{
		internal Vector3 __center;
		internal float __radius = 0.5f;
		public partial float radius { get => __radius; set { __radius = value; PhysicsWorld.Rebuild(this); } }
		private partial void INTERNAL_get_center(out Vector3 value) => value = __center;
		private partial void INTERNAL_set_center(ref Vector3 value) { __center = value; PhysicsWorld.Rebuild(this); }
		internal override Bounds __LocalBounds() => new Bounds(__center, Vector3.one * __radius * 2f);

		internal override G.Shape3D __MakeShape(out Vector3 localCenter)
		{
			Vector3 s = transform.lossyScale;
			localCenter = __center;
			return new G.SphereShape3D { Radius = Mathf.Max(0.001f, __radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)))) };
		}

		internal override void __CopyFrom(Object src, Cloner c) { base.__CopyFrom(src, c); var s = (SphereCollider)src; __center = s.__center; __radius = s.__radius; }
	}

	public partial class MeshCollider
	{
		internal Mesh __mesh;
		internal bool __convex, __smooth;
		public partial Mesh sharedMesh { get => __mesh; set { __mesh = value; PhysicsWorld.Rebuild(this); } }
		public partial bool convex { get => __convex; set { __convex = value; PhysicsWorld.Rebuild(this); } }
		public partial bool smoothSphereCollisions { get => __smooth; set => __smooth = value; }
		internal override Bounds __LocalBounds() => __mesh != null ? __mesh.bounds : new Bounds(Vector3.zero, Vector3.zero);

		internal override G.Shape3D __MakeShape(out Vector3 localCenter)
		{
			localCenter = Vector3.zero;
			if (__mesh == null || __mesh.vertexCount == 0) return null;
			Vector3 s = transform.lossyScale;
			var v = __mesh.__v;
			var faces = new List<G.Vector3>();
			foreach (var tri in __mesh.__subs)
				for (int i = 0; i + 2 < tri.Length; i += 3)
					for (int k = 0; k < 3; k++)
						faces.Add(Svz.Render.V(Vector3.Scale(v[tri[i + k]], s)));
			if (__convex) return new G.ConvexPolygonShape3D { Points = faces.ToArray() };
			return new G.ConcavePolygonShape3D { Data = faces.ToArray(), BackfaceCollision = true };
		}

		internal override void __CopyFrom(Object src, Cloner c) { base.__CopyFrom(src, c); var s = (MeshCollider)src; __mesh = s.__mesh; __convex = s.__convex; }
	}

	public partial class PhysicMaterial
	{
		internal float __df = 0.6f, __sf = 0.6f, __bounce, __df2, __sf2;
		internal PhysicMaterialCombine __fc, __bc;
		internal Vector3 __fdir2;
		public partial float dynamicFriction { get => __df; set => __df = value; }
		public partial float staticFriction { get => __sf; set => __sf = value; }
		public partial float bounciness { get => __bounce; set => __bounce = value; }
		public partial float dynamicFriction2 { get => __df2; set => __df2 = value; }
		public partial float staticFriction2 { get => __sf2; set => __sf2 = value; }
		public partial PhysicMaterialCombine frictionCombine { get => __fc; set => __fc = value; }
		public partial PhysicMaterialCombine bounceCombine { get => __bc; set => __bc = value; }
		private partial void Internal_CreateDynamicsMaterial(string name) => __name = name ?? "";
		private partial void INTERNAL_get_frictionDirection2(out Vector3 value) => value = __fdir2;
		private partial void INTERNAL_set_frictionDirection2(ref Vector3 value) => __fdir2 = value;
	}

	public partial class Rigidbody
	{
		internal float __mass = 1f, __drag, __angDrag = 0.05f, __maxAng = 7f;
		internal bool __gravity = true, __kinematic, __detect = true;
		internal RigidbodyConstraints __constraints;
		internal CollisionDetectionMode __ccd;
		internal RigidbodyInterpolation __interp;
		internal Vector3 __vel, __angVel;
		internal G.RigidBody3D __rb;

		private G.RigidBody3D B => PhysicsWorld.EnsureRigidbody(this);

		public partial float drag { get => __drag; set { __drag = value; if (__rb != null) __rb.LinearDamp = value; } }
		public partial float angularDrag { get => __angDrag; set { __angDrag = value; if (__rb != null) __rb.AngularDamp = value; } }
		public partial float mass { get => __mass; set { __mass = Mathf.Max(1e-4f, value); if (__rb != null) __rb.Mass = __mass; } }
		public partial bool useGravity { get => __gravity; set { __gravity = value; if (__rb != null) __rb.GravityScale = value ? 1f : 0f; } }
		public partial bool isKinematic { get => __kinematic; set { __kinematic = value; PhysicsWorld.ApplyRigidbodyMode(this); } }
		public partial bool freezeRotation { get => (__constraints & RigidbodyConstraints.FreezeRotation) == RigidbodyConstraints.FreezeRotation; set { __constraints = value ? __constraints | RigidbodyConstraints.FreezeRotation : __constraints & ~RigidbodyConstraints.FreezeRotation; PhysicsWorld.ApplyRigidbodyMode(this); } }
		public partial RigidbodyConstraints constraints { get => __constraints; set { __constraints = value; PhysicsWorld.ApplyRigidbodyMode(this); } }
		public partial CollisionDetectionMode collisionDetectionMode { get => __ccd; set { __ccd = value; if (__rb != null) __rb.ContinuousCd = value != CollisionDetectionMode.Discrete; } }
		public partial bool detectCollisions { get => __detect; set => __detect = value; }
		public partial bool useConeFriction { get => false; set { } }
		public partial RigidbodyInterpolation interpolation { get => __interp; set => __interp = value; }
		public partial int solverIterationCount { get => 6; set { } }
		public partial float sleepVelocity { get => 0.14f; set { } }
		public partial float sleepAngularVelocity { get => 0.14f; set { } }
		public partial float maxAngularVelocity { get => __maxAng; set => __maxAng = value; }

		private partial void INTERNAL_get_velocity(out Vector3 value)
		{
			var r = __rb;
			value = r != null ? PhysicsWorld.U(r.LinearVelocity) : __vel;
		}

		private partial void INTERNAL_set_velocity(ref Vector3 value)
		{
			__vel = value;
			var r = B; if (r != null) r.LinearVelocity = Svz.Render.V(value);
		}

		private partial void INTERNAL_get_angularVelocity(out Vector3 value)
		{
			var r = __rb;
			value = r != null ? -PhysicsWorld.U(r.AngularVelocity) : __angVel;
		}

		private partial void INTERNAL_set_angularVelocity(ref Vector3 value)
		{
			__angVel = value;
			var r = B; if (r != null) r.AngularVelocity = -Svz.Render.V(value);
		}

		private static partial void INTERNAL_CALL_SetDensity(Rigidbody self, float density) { }

		private static void Apply(Rigidbody self, Vector3 f, ForceMode mode, Vector3? at)
		{
			var r = self.B;
			if (r == null) return;
			var gf = Svz.Render.V(f);
			var off = at.HasValue ? Svz.Render.V(at.Value) - r.GlobalPosition : G.Vector3.Zero;
			switch (mode)
			{
				case ForceMode.Force: r.ApplyForce(gf, off); break;
				case ForceMode.Acceleration: r.ApplyForce(gf * r.Mass, off); break;
				case ForceMode.Impulse: r.ApplyImpulse(gf, off); break;
				case ForceMode.VelocityChange: r.ApplyImpulse(gf * r.Mass, off); break;
			}
		}

		private static partial void INTERNAL_CALL_AddForce(Rigidbody self, ref Vector3 force, ForceMode mode) => Apply(self, force, mode, null);
		private static partial void INTERNAL_CALL_AddRelativeForce(Rigidbody self, ref Vector3 force, ForceMode mode) => Apply(self, self.transform.TransformDirection(force), mode, null);
		private static partial void INTERNAL_CALL_AddTorque(Rigidbody self, ref Vector3 torque, ForceMode mode) { var r = self.B; r?.ApplyTorque(-Svz.Render.V(torque)); }
		private static partial void INTERNAL_CALL_AddRelativeTorque(Rigidbody self, ref Vector3 torque, ForceMode mode) { var r = self.B; r?.ApplyTorque(-Svz.Render.V(self.transform.TransformDirection(torque))); }
		private static partial void INTERNAL_CALL_AddForceAtPosition(Rigidbody self, ref Vector3 force, ref Vector3 position, ForceMode mode) => Apply(self, force, mode, position);

		private static partial void INTERNAL_CALL_AddExplosionForce(Rigidbody self, float explosionForce, ref Vector3 explosionPosition, float explosionRadius, float upwardsModifier, ForceMode mode)
		{
			Vector3 d = self.transform.position - explosionPosition;
			float dist = d.magnitude;
			if (explosionRadius > 0 && dist > explosionRadius) return;
			float k = explosionRadius > 0 ? 1f - dist / explosionRadius : 1f;
			Vector3 dir = (d.normalized + Vector3.up * upwardsModifier).normalized;
			Apply(self, dir * explosionForce * k, mode, null);
		}

		private static partial Vector3 INTERNAL_CALL_ClosestPointOnBounds(Rigidbody self, ref Vector3 position) => self.GetComponent<Collider>()?.ClosestPointOnBounds(position) ?? self.transform.position;
		private static partial Vector3 INTERNAL_CALL_GetRelativePointVelocity(Rigidbody self, ref Vector3 relativePoint) => self.velocity;
		private static partial Vector3 INTERNAL_CALL_GetPointVelocity(Rigidbody self, ref Vector3 worldPoint) => self.velocity;
		private partial void INTERNAL_get_centerOfMass(out Vector3 value) => value = Vector3.zero;
		private partial void INTERNAL_set_centerOfMass(ref Vector3 value) { }
		private partial void INTERNAL_get_worldCenterOfMass(out Vector3 value) => value = transform.position;
		private partial void INTERNAL_get_inertiaTensorRotation(out Quaternion value) => value = Quaternion.identity;
		private partial void INTERNAL_set_inertiaTensorRotation(ref Quaternion value) { }
		private partial void INTERNAL_get_inertiaTensor(out Vector3 value) => value = Vector3.one;
		private partial void INTERNAL_set_inertiaTensor(ref Vector3 value) { }
		private partial void INTERNAL_get_position(out Vector3 value) => value = transform.position;
		private partial void INTERNAL_set_position(ref Vector3 value) { transform.position = value; PhysicsWorld.Teleport(this); }
		private partial void INTERNAL_get_rotation(out Quaternion value) => value = transform.rotation;
		private partial void INTERNAL_set_rotation(ref Quaternion value) { transform.rotation = value; PhysicsWorld.Teleport(this); }
		private static partial void INTERNAL_CALL_MovePosition(Rigidbody self, ref Vector3 position) { self.transform.position = position; PhysicsWorld.Teleport(self); }
		private static partial void INTERNAL_CALL_MoveRotation(Rigidbody self, ref Quaternion rot) { self.transform.rotation = rot; PhysicsWorld.Teleport(self); }
		private static partial void INTERNAL_CALL_Sleep(Rigidbody self) { if (self.__rb != null) self.__rb.Sleeping = true; }
		private static partial bool INTERNAL_CALL_IsSleeping(Rigidbody self) => self.__rb == null || self.__rb.Sleeping;
		private static partial void INTERNAL_CALL_WakeUp(Rigidbody self) { if (self.__rb != null) self.__rb.Sleeping = false; }

		private static partial bool INTERNAL_CALL_SweepTest(Rigidbody self, ref Vector3 direction, out RaycastHit hitInfo, float distance)
		{
			hitInfo = default;
			var hits = PhysicsWorld.RaycastAll(self.transform.position, direction, distance, -1, null, self);
			if (hits.Count == 0) return false;
			hitInfo = hits[0];
			return true;
		}

		private static partial RaycastHit[] INTERNAL_CALL_SweepTestAll(Rigidbody self, ref Vector3 direction, float distance) =>
			PhysicsWorld.RaycastAll(self.transform.position, direction, distance, -1, null, self).ToArray();

		internal override void __OnAttach() => PhysicsWorld.AddRigidbody(this);
		internal override void __OnDestroyNative() => PhysicsWorld.RemoveRigidbody(this);

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			var s = (Rigidbody)src;
			__mass = s.__mass; __drag = s.__drag; __angDrag = s.__angDrag; __gravity = s.__gravity; __kinematic = s.__kinematic;
			__constraints = s.__constraints; __ccd = s.__ccd; __interp = s.__interp;
		}
	}

	public partial class Physics
	{
		internal static Vector3 __gravity = new Vector3(0, -9.81f, 0);
		public static partial float minPenetrationForPenalty { get => 0.01f; set { } }
		public static partial float bounceThreshold { get => 2f; set { } }
		public static partial float sleepVelocity { get => 0.15f; set { } }
		public static partial float sleepAngularVelocity { get => 0.14f; set { } }
		public static partial float maxAngularVelocity { get => 7f; set { } }
		public static partial int solverIterationCount { get => 6; set { } }
		public static partial float penetrationPenaltyForce { get => 0f; set { } }
		private static partial void INTERNAL_get_gravity(out Vector3 value) => value = __gravity;
		private static partial void INTERNAL_set_gravity(ref Vector3 value) { __gravity = value; PhysicsWorld.ApplyGravity(); }

		private static partial bool INTERNAL_CALL_Internal_Raycast(ref Vector3 origin, ref Vector3 direction, out RaycastHit hitInfo, float distance, int layermask)
		{
			var hits = PhysicsWorld.RaycastAll(origin, direction, distance, layermask);
			hitInfo = hits.Count > 0 ? hits[0] : default;
			return hits.Count > 0;
		}

		private static partial bool INTERNAL_CALL_Internal_CapsuleCast(ref Vector3 point1, ref Vector3 point2, float radius, ref Vector3 direction, out RaycastHit hitInfo, float distance, int layermask)
		{
			Vector3 o = (point1 + point2) * 0.5f;
			return INTERNAL_CALL_Internal_Raycast(ref o, ref direction, out hitInfo, distance, layermask);
		}

		private static partial bool INTERNAL_CALL_Internal_RaycastTest(ref Vector3 origin, ref Vector3 direction, float distance, int layermask) =>
			PhysicsWorld.RaycastAll(origin, direction, distance, layermask).Count > 0;

		private static partial RaycastHit[] INTERNAL_CALL_RaycastAll(ref Vector3 origin, ref Vector3 direction, float distance, int layermask) =>
			PhysicsWorld.RaycastAll(origin, direction, distance, layermask, all: true).ToArray();

		private static partial Collider[] INTERNAL_CALL_OverlapSphere(ref Vector3 position, float radius, int layerMask) => PhysicsWorld.OverlapSphere(position, radius, layerMask);

		private static partial RaycastHit[] INTERNAL_CALL_CapsuleCastAll(ref Vector3 point1, ref Vector3 point2, float radius, ref Vector3 direction, float distance, int layermask)
		{
			Vector3 o = (point1 + point2) * 0.5f;
			return INTERNAL_CALL_RaycastAll(ref o, ref direction, distance, layermask);
		}

		private static partial bool INTERNAL_CALL_CheckSphere(ref Vector3 position, float radius, int layerMask) => PhysicsWorld.OverlapSphere(position, radius, layerMask).Length > 0;

		private static partial bool INTERNAL_CALL_CheckCapsule(ref Vector3 start, ref Vector3 end, float radius, int layermask)
		{
			Vector3 c = (start + end) * 0.5f;
			return PhysicsWorld.OverlapSphere(c, radius + (end - start).magnitude * 0.5f, layermask).Length > 0;
		}

		public static partial void IgnoreCollision(Collider collider1, Collider collider2, bool ignore) => PhysicsWorld.IgnorePair(collider1, collider2, ignore);
		public static partial void IgnoreLayerCollision(int layer1, int layer2, bool ignore) => PhysicsWorld.IgnoreLayers(layer1, layer2, ignore);
		public static partial bool GetIgnoreLayerCollision(int layer1, int layer2) => PhysicsWorld.LayersIgnored(layer1, layer2);

		internal static void __Step(float dt) => PhysicsWorld.Sync();
	}

	public partial struct RaycastHit
	{
		internal static RaycastHit __Make(Vector3 p, Vector3 n, float d, Collider c) => new RaycastHit { m_Point = p, m_Normal = n, m_Distance = d, m_Collider = c };
		private static partial void INTERNAL_CALL_CalculateRaycastTexCoord(out Vector2 output, Collider col, ref Vector2 uv, ref Vector3 point, int face, int index) => output = Vector2.zero;
	}

	public partial class Collision
	{
		internal static Collision __Make(Vector3 relVel, Rigidbody rb, Collider col, ContactPoint[] contacts) =>
			new Collision { m_RelativeVelocity = relVel, m_Rigidbody = rb, m_Collider = col, m_Contacts = contacts };
	}

	public partial struct ContactPoint
	{
		internal static ContactPoint __Make(Vector3 p, Vector3 n, Collider self, Collider other) =>
			new ContactPoint { m_Point = p, m_Normal = n, m_ThisCollider = self, m_OtherCollider = other };
	}
}

namespace Svz
{
	/// Unity colliders/rigidbodies backed by Godot's physics (Jolt / Godot Physics).
	public static class PhysicsWorld
	{
		private static readonly List<Collider> sColliders = new List<Collider>();
		private static readonly List<Rigidbody> sBodies = new List<Rigidbody>();
		private static readonly HashSet<Collider> sDirty = new HashSet<Collider>();
		private static readonly HashSet<(int, int)> sIgnoredLayers = new HashSet<(int, int)>();
		private static G.Node3D sRoot;

		public static Vector3 U(G.Vector3 v) => new Vector3(v.X, v.Y, -v.Z);

		private static G.Node3D Root
		{
			get
			{
				if (sRoot == null)
				{
					sRoot = new G.Node3D { Name = "Physics" };
					Main.Instance.World3D.AddChild(sRoot);
					ApplyGravity();
				}
				return sRoot;
			}
		}

		public static void ApplyGravity()
		{
			if (sRoot == null || !sRoot.IsInsideTree()) return;
			var space = sRoot.GetWorld3D().Space;
			var g = Physics.__gravity;
			G.PhysicsServer3D.AreaSetParam(space, G.PhysicsServer3D.AreaParameter.Gravity, g.magnitude);
			G.PhysicsServer3D.AreaSetParam(space, G.PhysicsServer3D.AreaParameter.GravityVector, g.sqrMagnitude > 0 ? Render.V(g.normalized) : G.Vector3.Down);
		}

		public static void Add(Collider c) { sColliders.Add(c); sDirty.Add(c); }
		public static void Remove(Collider c)
		{
			sColliders.Remove(c);
			c.__body?.QueueFree(); c.__body = null;
			if (c.__shapeNode != null && G.GodotObject.IsInstanceValid(c.__shapeNode)) c.__shapeNode.QueueFree();
			c.__shapeNode = null;
		}

		public static void AddRigidbody(Rigidbody r) { sBodies.Add(r); foreach (var c in r.GetComponentsInChildren<Collider>(true)) Rebuild(c); }
		public static void RemoveRigidbody(Rigidbody r)
		{
			sBodies.Remove(r);
			r.__rb?.QueueFree(); r.__rb = null;
			foreach (var c in sColliders) if (c.__shapeNode != null && c.__body == null) sDirty.Add(c);
		}

		public static void MarkDirty(Collider c) => sDirty.Add(c);

		public static void Rebuild(Collider c)
		{
			c.__body?.QueueFree(); c.__body = null;
			if (c.__shapeNode != null && G.GodotObject.IsInstanceValid(c.__shapeNode)) c.__shapeNode.QueueFree();
			c.__shapeNode = null;
			sDirty.Add(c);
		}

		public static Rigidbody FindRigidbody(Collider c)
		{
			for (Transform t = c.transform; t != null; t = t.parent)
			{
				var rb = t.gameObject.__Find<Rigidbody>();
				if (rb != null) return rb;
			}
			return null;
		}

		public static G.RigidBody3D EnsureRigidbody(Rigidbody r)
		{
			if (r.__destroyed || r.__go == null || r.__go.__isAsset) return null;
			if (r.__rb == null)
			{
				r.__rb = new G.RigidBody3D
				{
					Name = "RB_" + r.gameObject.name, Mass = r.__mass, LinearDamp = r.__drag, AngularDamp = r.__angDrag,
					GravityScale = r.__gravity ? 1f : 0f, ContactMonitor = true, MaxContactsReported = 4,
					LinearDampMode = G.RigidBody3D.DampMode.Replace, AngularDampMode = G.RigidBody3D.DampMode.Replace,
				};
				r.__rb.SetMeta("svz_rb", r.GetInstanceID());
				Root.AddChild(r.__rb);
				r.__rb.GlobalTransform = Render.M(Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one));
				var rbref = r;
				r.__rb.BodyEntered += other => OnBodyEntered(rbref, other);
				ApplyRigidbodyMode(r);
				foreach (var c in r.GetComponentsInChildren<Collider>(true)) Rebuild(c);
			}
			return r.__rb;
		}

		public static void ApplyRigidbodyMode(Rigidbody r)
		{
			var b = r.__rb;
			if (b == null) return;
			b.Freeze = r.__kinematic;
			b.FreezeMode = G.RigidBody3D.FreezeModeEnum.Kinematic;
			b.AxisLockAngularX = (r.__constraints & RigidbodyConstraints.FreezeRotationX) != 0;
			b.AxisLockAngularY = (r.__constraints & RigidbodyConstraints.FreezeRotationY) != 0;
			b.AxisLockAngularZ = (r.__constraints & RigidbodyConstraints.FreezeRotationZ) != 0;
			b.AxisLockLinearX = (r.__constraints & RigidbodyConstraints.FreezePositionX) != 0;
			b.AxisLockLinearY = (r.__constraints & RigidbodyConstraints.FreezePositionY) != 0;
			b.AxisLockLinearZ = (r.__constraints & RigidbodyConstraints.FreezePositionZ) != 0;
		}

		public static void Teleport(Rigidbody r)
		{
			if (r.__rb != null) r.__rb.GlobalTransform = Render.M(Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one));
		}

		private static uint LayerBit(int layer) => 1u << (layer & 31);

		private static bool Live(Component c) => !c.__destroyed && c.__go != null && c.__go.__active && !c.__go.__isAsset;

		/// called every fixed step: create/update bodies, write back rigidbody poses
		public static void Sync()
		{
			if (sColliders.Count == 0 && sBodies.Count == 0) return;
			var root = Root;
			foreach (var c in sColliders)
			{
				if (c.__destroyed) continue;
				bool live = Live(c) && c.__enabled;
				var rb = live ? FindRigidbody(c) : null;
				if (rb != null && !Live(rb)) rb = null;
				if (live && c.__shapeNode == null) Build(c, rb);
				if (c.__shapeNode == null) continue;
				c.__shapeNode.Disabled = !live;
				if (c.__body != null)
				{
					// static colliders / triggers follow their transform
					var m = c.transform.localToWorldMatrix;
					var t = Matrix4x4.TRS(c.transform.position, c.transform.rotation, Vector3.one);
					c.__body.GlobalTransform = Render.M(t);
					uint bit = LayerBit(c.gameObject.layer);
					c.__body.CollisionLayer = bit;
					c.__body.CollisionMask = 0xFFFFFFFF;
				}
			}
			foreach (var r in sBodies)
			{
				if (!Live(r) || r.__rb == null) continue;
				if (r.__kinematic) { Teleport(r); continue; }
				var gt = r.__rb.GlobalTransform;
				var tr = r.transform;
				tr.position = U(gt.Origin);
				var q = gt.Basis.GetRotationQuaternion();
				tr.rotation = new Quaternion(-q.X, -q.Y, q.Z, q.W);
				r.__rb.CollisionLayer = LayerBit(r.gameObject.layer);
			}
		}

		private static void Build(Collider c, Rigidbody rb)
		{
			var shape = c.__MakeShape(out Vector3 localCenter);
			if (shape == null) return;
			var node = new G.CollisionShape3D { Shape = shape };
			c.__shapeNode = node;
			if (rb != null && !c.__trigger)
			{
				var body = EnsureRigidbody(rb);
				if (body == null) return;
				body.AddChild(node);
				// collider pose relative to the rigidbody (no scale: baked into the shape)
				var rel = rb.transform.worldToLocalMatrix * c.transform.localToWorldMatrix;
				Vector3 offset = rb.transform.InverseTransformPoint(c.transform.TransformPoint(localCenter));
				var rot = Quaternion.Inverse(rb.transform.rotation) * c.transform.rotation;
				node.Transform = Render.M(Matrix4x4.TRS(Vector3.Scale(offset, rb.transform.lossyScale), rot, Vector3.one));
				node.SetMeta("svz_col", c.GetInstanceID());
				sById[c.GetInstanceID()] = c;
				return;
			}
			G.CollisionObject3D owner = c.__trigger ? new G.Area3D { Monitoring = true, Monitorable = true } : new G.StaticBody3D();
			owner.Name = (c.__trigger ? "Trig_" : "Col_") + c.gameObject.name;
			Root.AddChild(owner);
			owner.AddChild(node);
			Vector3 center = Vector3.Scale(localCenter, c.transform.lossyScale);
			node.Transform = new G.Transform3D(G.Basis.Identity, Render.V(center));
			owner.SetMeta("svz_col", c.GetInstanceID());
			sById[c.GetInstanceID()] = c;
			c.__body = owner;
			if (owner is G.Area3D area)
			{
				var cref = c;
				area.BodyEntered += other => OnTrigger(cref, other, true);
				area.BodyExited += other => OnTrigger(cref, other, false);
			}
			if (owner is G.StaticBody3D sb && c.__pmat != null)
				sb.PhysicsMaterialOverride = new G.PhysicsMaterial { Friction = c.__pmat.__df, Bounce = c.__pmat.__bounce };
		}

		private static readonly Dictionary<int, Collider> sById = new Dictionary<int, Collider>();

		private static Collider ColliderOf(G.Node n)
		{
			if (n == null) return null;
			if (n.HasMeta("svz_col") && sById.TryGetValue(n.GetMeta("svz_col").AsInt32(), out var c)) return c;
			if (n is G.RigidBody3D rbn)
				foreach (var ch in rbn.GetChildren())
					if (ch.HasMeta("svz_col") && sById.TryGetValue(ch.GetMeta("svz_col").AsInt32(), out var c2)) return c2;
			return null;
		}

		private static Rigidbody RigidbodyOf(G.Node n)
		{
			if (n is G.RigidBody3D rb && rb.HasMeta("svz_rb"))
				foreach (var r in sBodies) if (r.GetInstanceID() == rb.GetMeta("svz_rb").AsInt32()) return r;
			return null;
		}

		private static void OnBodyEntered(Rigidbody self, G.Node other)
		{
			var otherCol = ColliderOf(other);
			var selfCol = self.GetComponentInChildren<Collider>();
			if (otherCol == null || selfCol == null) return;
			var vel = self.velocity - (RigidbodyOf(other)?.velocity ?? Vector3.zero);
			var contact = new[] { ContactPoint.__Make(self.transform.position, -vel.normalized, selfCol, otherCol) };
			Messaging.Send(self.gameObject, "OnCollisionEnter", Collision.__Make(vel, RigidbodyOf(other), otherCol, contact));
			Messaging.Send(otherCol.gameObject, "OnCollisionEnter", Collision.__Make(-vel, self, selfCol, contact));
		}

		private static void OnTrigger(Collider trigger, G.Node other, bool enter)
		{
			var otherCol = ColliderOf(other);
			if (otherCol == null || trigger.__destroyed || otherCol.__destroyed) return;
			string msg = enter ? "OnTriggerEnter" : "OnTriggerExit";
			Messaging.Send(trigger.gameObject, msg, otherCol);
			Messaging.Send(otherCol.gameObject, msg, trigger);
			var rb = FindRigidbody(otherCol);
			if (rb != null && rb.gameObject != otherCol.gameObject) Messaging.Send(rb.gameObject, msg, trigger);
		}

		public static List<RaycastHit> RaycastAll(Vector3 origin, Vector3 dir, float dist, int mask, Collider only = null, Rigidbody exclude = null, bool all = false)
		{
			var res = new List<RaycastHit>();
			if (dir.sqrMagnitude < 1e-12f) return res;
			dir = dir.normalized;
			if (float.IsInfinity(dist) || dist > 1e6f) dist = 1e6f;
			// analytic tests against collider shapes (works without stepping the physics engine)
			foreach (var c in sColliders)
			{
				if (!Live(c) || !c.__enabled || c.__trigger && only == null) continue;
				if (only != null && c != only) continue;
				if (exclude != null && FindRigidbody(c) == exclude) continue;
				if ((mask & (1 << c.gameObject.layer)) == 0) continue;
				if (Hit(c, origin, dir, dist, out var h)) res.Add(h);
			}
			res.Sort((a, b) => a.distance.CompareTo(b.distance));
			return res;
		}

		private static bool Hit(Collider c, Vector3 o, Vector3 d, float maxDist, out RaycastHit hit)
		{
			hit = default;
			var w2l = c.transform.worldToLocalMatrix;
			var l2w = c.transform.localToWorldMatrix;
			Vector3 lo = w2l.MultiplyPoint3x4(o), ld = w2l.MultiplyVector(d);
			float t;
			Vector3 ln;
			switch (c)
			{
				case BoxCollider b:
				{
					Vector3 mn = b.__center - b.__size * 0.5f, mx = b.__center + b.__size * 0.5f;
					if (!Geometry.RayAabb(lo, ld, mn, mx, out t)) return false;
					Vector3 p = lo + ld * t, rel = p - b.__center;
					Vector3 h = b.__size * 0.5f;
					ln = Mathf.Abs(Mathf.Abs(rel.x) - h.x) < 1e-3f ? new Vector3(Mathf.Sign(rel.x), 0, 0) : Mathf.Abs(Mathf.Abs(rel.y) - h.y) < 1e-3f ? new Vector3(0, Mathf.Sign(rel.y), 0) : new Vector3(0, 0, Mathf.Sign(rel.z));
					break;
				}
				case SphereCollider s:
				{
					Vector3 oc = lo - s.__center;
					float a = Vector3.Dot(ld, ld), bq = 2 * Vector3.Dot(oc, ld), cq = Vector3.Dot(oc, oc) - s.__radius * s.__radius;
					float disc = bq * bq - 4 * a * cq;
					if (disc < 0) return false;
					t = (-bq - Mathf.Sqrt(disc)) / (2 * a);
					if (t < 0) t = (-bq + Mathf.Sqrt(disc)) / (2 * a);
					if (t < 0) return false;
					ln = (lo + ld * t - s.__center).normalized;
					break;
				}
				case MeshCollider mc:
				{
					var m = mc.__mesh;
					if (m == null || m.vertexCount == 0) return false;
					t = float.MaxValue; ln = Vector3.up;
					var v = m.__v;
					foreach (var tri in m.__subs)
						for (int i = 0; i + 2 < tri.Length; i += 3)
						{
							Vector3 a = v[tri[i]], b = v[tri[i + 1]], cc = v[tri[i + 2]];
							if (Geometry.RayTriangle(lo, ld, a, b, cc, out float tt) && tt < t) { t = tt; ln = Vector3.Cross(b - a, cc - a).normalized; }
						}
					if (t == float.MaxValue) return false;
					break;
				}
				default: return false;
			}
			Vector3 wp = l2w.MultiplyPoint3x4(lo + ld * t);
			float wd = (wp - o).magnitude;
			if (wd > maxDist) return false;
			hit = RaycastHit.__Make(wp, l2w.MultiplyVector(ln).normalized, wd, c);
			return true;
		}

		public static Collider[] OverlapSphere(Vector3 p, float r, int mask)
		{
			var res = new List<Collider>();
			foreach (var c in sColliders)
			{
				if (!Live(c) || !c.__enabled || (mask & (1 << c.gameObject.layer)) == 0) continue;
				var b = c.__WorldBounds();
				Vector3 mn = b.min, mx = b.max;
				Vector3 q = new Vector3(Mathf.Clamp(p.x, mn.x, mx.x), Mathf.Clamp(p.y, mn.y, mx.y), Mathf.Clamp(p.z, mn.z, mx.z));
				if ((q - p).sqrMagnitude <= r * r) res.Add(c);
			}
			return res.ToArray();
		}

		public static void IgnorePair(Collider a, Collider b, bool ignore)
		{
			var ba = (G.PhysicsBody3D)(a.__body as G.PhysicsBody3D ?? FindRigidbody(a)?.__rb);
			var bb = (G.PhysicsBody3D)(b.__body as G.PhysicsBody3D ?? FindRigidbody(b)?.__rb);
			if (ba == null || bb == null) return;
			if (ignore) ba.AddCollisionExceptionWith(bb); else ba.RemoveCollisionExceptionWith(bb);
		}

		public static void IgnoreLayers(int a, int b, bool ignore) { if (ignore) sIgnoredLayers.Add((Math.Min(a, b), Math.Max(a, b))); else sIgnoredLayers.Remove((Math.Min(a, b), Math.Max(a, b))); }
		public static bool LayersIgnored(int a, int b) => sIgnoredLayers.Contains((Math.Min(a, b), Math.Max(a, b)));
	}

	public static partial class Builtins
	{
		static partial void RegisterPhysics()
		{
			AssetDb.Register("BoxCollider", (o, d, ld) =>
			{
				var c = (BoxCollider)o;
				c.__trigger = d.Bool("m_IsTrigger"); c.__enabled = d.Bool("m_Enabled", true);
				c.__pmat = ld.Ref<PhysicMaterial>(d.Get("m_Material"));
				c.__size = d.Get("m_Size").V3(); c.__center = d.Get("m_Center").V3();
			});
			AssetDb.Register("SphereCollider", (o, d, ld) =>
			{
				var c = (SphereCollider)o;
				c.__trigger = d.Bool("m_IsTrigger"); c.__enabled = d.Bool("m_Enabled", true);
				c.__pmat = ld.Ref<PhysicMaterial>(d.Get("m_Material"));
				c.__radius = d.Float("m_Radius", 0.5f); c.__center = d.Get("m_Center").V3();
			});
			AssetDb.Register("MeshCollider", (o, d, ld) =>
			{
				var c = (MeshCollider)o;
				c.__trigger = d.Bool("m_IsTrigger"); c.__enabled = d.Bool("m_Enabled", true);
				c.__pmat = ld.Ref<PhysicMaterial>(d.Get("m_Material"));
				c.__mesh = ld.Ref<Mesh>(d.Get("m_Mesh")); c.__convex = d.Bool("m_Convex");
			});
			AssetDb.Register("Rigidbody", (o, d, ld) =>
			{
				var r = (Rigidbody)o;
				r.__mass = d.Float("m_Mass", 1f); r.__drag = d.Float("m_Drag"); r.__angDrag = d.Float("m_AngularDrag", 0.05f);
				r.__gravity = d.Bool("m_UseGravity", true); r.__kinematic = d.Bool("m_IsKinematic");
				r.__constraints = (RigidbodyConstraints)d.Int("m_Constraints");
				r.__ccd = (CollisionDetectionMode)d.Int("m_CollisionDetection");
			});
			AssetDb.Register("PhysicMaterial", (o, d, ld) =>
			{
				var m = (PhysicMaterial)o;
				m.__df = d.Float("dynamicFriction", 0.6f); m.__sf = d.Float("staticFriction", 0.6f); m.__bounce = d.Float("bounciness");
			});
		}
	}
}
