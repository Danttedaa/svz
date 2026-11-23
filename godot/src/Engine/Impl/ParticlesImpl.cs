using System;
using System.Collections.Generic;
using System.Linq;
using Svz;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace UnityEngine
{
	public sealed partial class ParticleEmitter
	{
		// EllipsoidParticleEmitter / MeshParticleEmitter are native-only subclasses in Unity 3.4;
		// ParticleEmitter is sealed in the managed API, so the shape lives here.
		internal Vector3 __ellipsoid = Vector3.one;
		internal float __minRange;
		internal Mesh __emitMesh;
		internal bool __systematic;
		private int __nextVertex;
		internal bool __emit = true, __world, __rndRot, __oneShot, __enabledP = true;
		internal float __minSize = 0.1f, __maxSize = 0.1f, __minEnergy = 3f, __maxEnergy = 3f, __minEm = 50f, __maxEm = 50f;
		internal float __emitterVelScale = 0.05f, __angVel, __rndAngVel;
		internal Vector3 __worldVel, __localVel, __rndVel, __tangentVel;
		internal readonly List<Particle> __ps = new List<Particle>();
		internal float __emitAcc;
		internal bool __everEmitted;
		internal Vector3 __lastPos;
		internal bool __hasLastPos;

		internal Vector3 __SpawnPoint()
		{
			if (__emitMesh != null)
			{
				if (__emitMesh.vertexCount == 0) return Vector3.zero;
				var v = __emitMesh.__v;
				return v[__systematic ? (__nextVertex++ % v.Length) : Random.Range(0, v.Length)];
			}
			Vector3 p;
			int guard = 0;
			do { p = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)); }
			while ((p.sqrMagnitude > 1f || (__minRange > 0 && p.magnitude < __minRange)) && guard++ < 16);
			return Vector3.Scale(p, __ellipsoid * 0.5f);
		}

		public partial bool emit { get => __emit; set => __emit = value; }
		public partial float minSize { get => __minSize; set => __minSize = value; }
		public partial float maxSize { get => __maxSize; set => __maxSize = value; }
		public partial float minEnergy { get => __minEnergy; set => __minEnergy = value; }
		public partial float maxEnergy { get => __maxEnergy; set => __maxEnergy = value; }
		public partial float minEmission { get => __minEm; set => __minEm = value; }
		public partial float maxEmission { get => __maxEm; set => __maxEm = value; }
		public partial float emitterVelocityScale { get => __emitterVelScale; set => __emitterVelScale = value; }
		public partial bool useWorldSpace { get => __world; set => __world = value; }
		public partial bool rndRotation { get => __rndRot; set => __rndRot = value; }
		public partial float angularVelocity { get => __angVel; set => __angVel = value; }
		public partial float rndAngularVelocity { get => __rndAngVel; set => __rndAngVel = value; }
		public partial Particle[] particles { get => __ps.ToArray(); set { __ps.Clear(); if (value != null) __ps.AddRange(value.Where(p => p.energy > 0)); } }
		public partial int particleCount => __ps.Count;
		public partial bool enabled { get => __enabledP; set => __enabledP = value; }
		private partial void INTERNAL_get_worldVelocity(out Vector3 value) => value = __worldVel;
		private partial void INTERNAL_set_worldVelocity(ref Vector3 value) => __worldVel = value;
		private partial void INTERNAL_get_localVelocity(out Vector3 value) => value = __localVel;
		private partial void INTERNAL_set_localVelocity(ref Vector3 value) => __localVel = value;
		private partial void INTERNAL_get_rndVelocity(out Vector3 value) => value = __rndVel;
		private partial void INTERNAL_set_rndVelocity(ref Vector3 value) => __rndVel = value;
		private static partial void INTERNAL_CALL_ClearParticles(ParticleEmitter self) => self.__ps.Clear();
		private partial void Emit2(int count) { for (int i = 0; i < count; i++) __Spawn(); }

		private partial void Emit3(ref InternalEmitParticleArguments args)
		{
			var p = new Particle
			{
				position = args.pos, velocity = args.velocity, size = args.size, energy = args.energy, startEnergy = args.energy,
				color = args.color, rotation = args.rotation, angularVelocity = args.angularVelocity,
			};
			__ps.Add(p);
			__everEmitted = true;
		}

		public partial void Simulate(float deltaTime) => Particles.Step(this, deltaTime);

		internal void __Spawn()
		{
			var t = transform;
			Vector3 local = __SpawnPoint();
			Vector3 vel = __worldVel + t.TransformDirection(__localVel)
				+ new Vector3(Random.Range(-1f, 1f) * __rndVel.x, Random.Range(-1f, 1f) * __rndVel.y, Random.Range(-1f, 1f) * __rndVel.z);
			if (__hasLastPos && Time.deltaTime > 0f) vel += (t.position - __lastPos) / Time.deltaTime * __emitterVelScale;
			float e = Random.Range(__minEnergy, __maxEnergy);
			var p = new Particle
			{
				size = Random.Range(__minSize, __maxSize),
				energy = e, startEnergy = e,
				color = Color.white,
				rotation = __rndRot ? Random.Range(0f, 360f) : 0f,
				angularVelocity = __angVel + Random.Range(-1f, 1f) * __rndAngVel,
			};
			if (__world) { p.position = t.TransformPoint(local); p.velocity = vel; }
			else { p.position = local; p.velocity = t.InverseTransformDirection(vel); }
			__ps.Add(p);
			__everEmitted = true;
		}

		internal override void __OnAttach() => Particles.Emitters.Add(this);
		internal override void __OnDestroyNative() => Particles.Emitters.Remove(this);

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			var s = (ParticleEmitter)src;
			__emit = s.__emit; __world = s.__world; __rndRot = s.__rndRot; __oneShot = s.__oneShot; __enabledP = s.__enabledP;
			__minSize = s.__minSize; __maxSize = s.__maxSize; __minEnergy = s.__minEnergy; __maxEnergy = s.__maxEnergy;
			__minEm = s.__minEm; __maxEm = s.__maxEm; __emitterVelScale = s.__emitterVelScale; __angVel = s.__angVel; __rndAngVel = s.__rndAngVel;
			__worldVel = s.__worldVel; __localVel = s.__localVel; __rndVel = s.__rndVel; __tangentVel = s.__tangentVel;
			__ellipsoid = s.__ellipsoid; __minRange = s.__minRange; __emitMesh = s.__emitMesh; __systematic = s.__systematic;
		}
	}

	public sealed partial class ParticleAnimator
	{
		internal bool __animColor = true, __autodestruct, __stop;
		internal Color[] __colors = { Color.white, Color.white, Color.white, Color.white, Color.white };
		internal Vector3 __worldAxis, __localAxis, __rndForce, __force;
		internal float __sizeGrow, __damping = 1f;

		public partial bool doesAnimateColor { get => __animColor; set => __animColor = value; }
		public partial float sizeGrow { get => __sizeGrow; set => __sizeGrow = value; }
		public partial float damping { get => __damping; set => __damping = value; }
		public partial bool autodestruct { get => __autodestruct; set => __autodestruct = value; }
		public partial Color[] colorAnimation { get => (Color[])__colors.Clone(); set { if (value != null && value.Length > 0) __colors = (Color[])value.Clone(); } }
		private partial void INTERNAL_get_worldRotationAxis(out Vector3 value) => value = __worldAxis;
		private partial void INTERNAL_set_worldRotationAxis(ref Vector3 value) => __worldAxis = value;
		private partial void INTERNAL_get_localRotationAxis(out Vector3 value) => value = __localAxis;
		private partial void INTERNAL_set_localRotationAxis(ref Vector3 value) => __localAxis = value;
		private partial void INTERNAL_get_rndForce(out Vector3 value) => value = __rndForce;
		private partial void INTERNAL_set_rndForce(ref Vector3 value) => __rndForce = value;
		private partial void INTERNAL_get_force(out Vector3 value) => value = __force;
		private partial void INTERNAL_set_force(ref Vector3 value) => __force = value;

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			var s = (ParticleAnimator)src;
			__animColor = s.__animColor; __autodestruct = s.__autodestruct; __stop = s.__stop; __colors = (Color[])s.__colors.Clone();
			__worldAxis = s.__worldAxis; __localAxis = s.__localAxis; __rndForce = s.__rndForce; __force = s.__force;
			__sizeGrow = s.__sizeGrow; __damping = s.__damping;
		}
	}

	public sealed partial class ParticleRenderer
	{
		internal ParticleRenderMode __mode = ParticleRenderMode.Billboard;
		internal float __lengthScale = 2f, __velScale, __camVelScale, __maxSize = 0.25f;
		internal int __xTile = 1, __yTile = 1;
		internal float __cycles = 1f;
		internal Rect[] __uvTiles;
		internal readonly Mesh __pmesh = new Mesh();
		internal AnimationCurve __wc, __hc, __rc;

		public partial ParticleRenderMode particleRenderMode { get => __mode; set => __mode = value; }
		public partial float lengthScale { get => __lengthScale; set => __lengthScale = value; }
		public partial float velocityScale { get => __velScale; set => __velScale = value; }
		public partial float cameraVelocityScale { get => __camVelScale; set => __camVelScale = value; }
		public partial float maxParticleSize { get => __maxSize; set => __maxSize = value; }
		public partial int uvAnimationXTile { get => __xTile; set => __xTile = Math.Max(1, value); }
		public partial int uvAnimationYTile { get => __yTile; set => __yTile = Math.Max(1, value); }
		public partial float uvAnimationCycles { get => __cycles; set => __cycles = value; }
		public partial Rect[] uvTiles { get => __uvTiles; set => __uvTiles = value; }
		public partial AnimationCurve widthCurve { get => __wc; set => __wc = value; }
		public partial AnimationCurve heightCurve { get => __hc; set => __hc = value; }
		public partial AnimationCurve rotationCurve { get => __rc; set => __rc = value; }

		internal override Mesh __Mesh => __pmesh;

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			var s = (ParticleRenderer)src;
			__mode = s.__mode; __lengthScale = s.__lengthScale; __velScale = s.__velScale; __maxSize = s.__maxSize;
			__xTile = s.__xTile; __yTile = s.__yTile; __cycles = s.__cycles; __uvTiles = s.__uvTiles;
		}
	}
}

namespace Svz
{
	public static partial class Particles
	{
		public static readonly List<ParticleEmitter> Emitters = new List<ParticleEmitter>();

		public static void Update(float dt)
		{
			foreach (var e in Emitters.ToArray())
			{
				if (e.__destroyed || e.__go == null || e.__go.__isAsset || !e.__go.__active) continue;
				try { Step(e, dt); }
				catch (Exception ex) { Log.Exception(ex, "particles " + e.__go.name); }
			}
		}

		public static void Step(ParticleEmitter e, float dt)
		{
			var anim = e.__go.__Find<ParticleAnimator>();
			// emission
			if (e.__enabledP && e.__emit)
			{
				if (e.__oneShot)
				{
					if (e.__ps.Count == 0)
					{
						int n = Mathf.RoundToInt(Random.Range(e.__minEm, e.__maxEm));
						for (int i = 0; i < n; i++) e.__Spawn();
					}
				}
				else
				{
					e.__emitAcc += Random.Range(e.__minEm, e.__maxEm) * dt;
					int n = (int)e.__emitAcc;
					e.__emitAcc -= n;
					for (int i = 0; i < n && e.__ps.Count < 2000; i++) e.__Spawn();
				}
			}
			e.__lastPos = e.transform.position;
			e.__hasLastPos = true;

			// simulation
			var t = e.transform;
			for (int i = e.__ps.Count - 1; i >= 0; i--)
			{
				var p = e.__ps[i];
				p.energy -= dt;
				if (p.energy <= 0f) { e.__ps.RemoveAt(i); continue; }
				if (anim != null && !anim.__stop)
				{
					Vector3 force = anim.__force + new Vector3(Random.Range(-1f, 1f) * anim.__rndForce.x, Random.Range(-1f, 1f) * anim.__rndForce.y, Random.Range(-1f, 1f) * anim.__rndForce.z);
					if (!e.__world) force = t.InverseTransformDirection(force);
					p.velocity += force * dt;
					if (anim.__damping != 1f) p.velocity *= Mathf.Pow(Mathf.Max(0f, anim.__damping), dt);
					if (anim.__sizeGrow != 0f) p.size *= 1f + anim.__sizeGrow * dt;
					if (anim.__animColor && anim.__colors.Length > 0)
					{
						float age = p.startEnergy > 0 ? 1f - p.energy / p.startEnergy : 1f;
						float f = Mathf.Clamp01(age) * (anim.__colors.Length - 1);
						int k = Math.Min((int)f, anim.__colors.Length - 2);
						p.color = anim.__colors.Length == 1 ? anim.__colors[0] : Color.Lerp(anim.__colors[k], anim.__colors[k + 1], f - k);
					}
				}
				p.position += p.velocity * dt;
				p.rotation += p.angularVelocity * dt;
				e.__ps[i] = p;
			}

			if (anim != null && anim.__autodestruct && e.__everEmitted && e.__ps.Count == 0 && (!e.__emit || e.__oneShot))
				Runtime.Destroy(e.gameObject, 0f);
		}

		/// rebuild the billboard mesh of a particle renderer for the given camera (world space, Unity coords)
		public static Mesh BuildMesh(ParticleRenderer r, Camera cam)
		{
			var mesh = r.__pmesh;
			var e = r.__go.__Find<ParticleEmitter>();
			int n = e?.__ps.Count ?? 0;
			int vertexCount = n * 4, indexCount = n * 6;
			int oldIndexCount = mesh.__subs.Count == 1 ? mesh.__subs[0].Length : -1;
			// Render.SyncRenderer hides zero-vertex meshes. Once cleared, an unchanged
			// empty emitter needs neither a fresh allocation nor a new GPU upload.
			if (n == 0 && ParticleBuffers.IsAlreadyEmpty(mesh.__loaded, mesh.__v.Length,
				mesh.__subs.Count, oldIndexCount)) return mesh;
			// Buffers belong to this renderer's private mesh. The previous GPU
			// upload copied their contents; they can be overwritten next frame.
			var v = ParticleBuffers.EnsureSize(mesh.__v, vertexCount);
			var uv = ParticleBuffers.EnsureSize(mesh.__uv, vertexCount);
			var col = ParticleBuffers.EnsureSize(mesh.__c, vertexCount);
			var tri = ParticleBuffers.EnsureSize(mesh.__subs.Count == 1 ? mesh.__subs[0] : null, indexCount);
			Vector3 camRight = cam != null ? cam.transform.right : Vector3.right;
			Vector3 camUp = cam != null ? cam.transform.up : Vector3.up;
			Vector3 camFwd = cam != null ? cam.transform.forward : Vector3.forward;
			var t = r.transform;
			int tiles = r.__xTile * r.__yTile;
			for (int i = 0; i < n; i++)
			{
				var p = e.__ps[i];
				Vector3 pos = e.__world ? p.position : t.TransformPoint(p.position);
				Vector3 vel = e.__world ? p.velocity : t.TransformDirection(p.velocity);
				float h = p.size * 0.5f;
				// Unity clamps a particle's on-screen size to maxParticleSize * viewport height
				if (cam != null && r.__maxSize > 0f)
				{
					float viewH = cam.__orthographic ? cam.__ortho * 2f
						: 2f * Mathf.Max(0.01f, Vector3.Dot(pos - cam.transform.position, camFwd)) * Mathf.Tan(cam.__fov * 0.5f * Mathf.Deg2Rad);
					float maxWorld = r.__maxSize * viewH;
					if (p.size > maxWorld) h = maxWorld * 0.5f;
				}
				Vector3 right, up;
				switch (r.__mode)
				{
					case ParticleRenderMode.Stretch:
					{
						Vector3 dir = vel.sqrMagnitude > 1e-8f ? vel.normalized : camUp;
						float len = h * 2f * r.__lengthScale + vel.magnitude * r.__velScale;
						Vector3 side = Vector3.Cross(dir, camFwd);
						side = side.sqrMagnitude > 1e-8f ? side.normalized : camRight;
						right = side * h;
						up = -dir * len * 0.5f;   // tail behind the particle
						pos -= dir * len * 0.5f;
						break;
					}
					case ParticleRenderMode.HorizontalBillboard: right = Vector3.right * h; up = Vector3.forward * h; break;
					case ParticleRenderMode.VerticalBillboard:
					{
						Vector3 f = new Vector3(camFwd.x, 0, camFwd.z);
						Vector3 rr = f.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, f.normalized) : Vector3.right;
						right = rr * h; up = Vector3.up * h;
						break;
					}
					default:
					{
						float rad = p.rotation * Mathf.Deg2Rad;
						float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
						right = (camRight * cs + camUp * sn) * h;
						up = (-camRight * sn + camUp * cs) * h;
						break;
					}
				}
				int b = i * 4;
				v[b] = pos - right - up; v[b + 1] = pos - right + up; v[b + 2] = pos + right + up; v[b + 3] = pos + right - up;
				// uv animation frame
				Rect uvr = new Rect(0, 0, 1, 1);
				if (tiles > 1)
				{
					float age = p.startEnergy > 0 ? 1f - p.energy / p.startEnergy : 0f;
					int frame = (int)(age * r.__cycles * tiles) % tiles;
					int fx = frame % r.__xTile, fy = frame / r.__xTile;
					float w = 1f / r.__xTile, hh = 1f / r.__yTile;
					uvr = new Rect(fx * w, 1f - (fy + 1) * hh, w, hh);
				}
				uv[b] = new Vector2(uvr.xMin, uvr.yMin); uv[b + 1] = new Vector2(uvr.xMin, uvr.yMax);
				uv[b + 2] = new Vector2(uvr.xMax, uvr.yMax); uv[b + 3] = new Vector2(uvr.xMax, uvr.yMin);
				col[b] = col[b + 1] = col[b + 2] = col[b + 3] = p.color;
				int k = i * 6;
				tri[k] = b; tri[k + 1] = b + 1; tri[k + 2] = b + 2; tri[k + 3] = b; tri[k + 4] = b + 2; tri[k + 5] = b + 3;
			}
			mesh.__loaded = true;
			mesh.__v = v; mesh.__uv = uv; mesh.__c = col; mesh.__n = Array.Empty<Vector3>();
			if (mesh.__subs.Count != 1 || !ReferenceEquals(mesh.__subs[0], tri))
				mesh.__subs = new List<int[]>(1) { tri };
			mesh.__version++;
			return mesh;
		}
	}

	public static partial class Builtins
	{
		static partial void RegisterParticles()
		{
			AssetDb.PopulateFn emitter = (o, d, ld) =>
			{
				var e = (ParticleEmitter)o;
				e.__enabledP = d.Bool("m_Enabled", true);
				e.__emit = d.Bool("m_Emit", true);
				e.__minSize = d.Float("minSize"); e.__maxSize = d.Float("maxSize");
				e.__minEnergy = d.Float("minEnergy"); e.__maxEnergy = d.Float("maxEnergy");
				e.__minEm = d.Float("minEmission"); e.__maxEm = d.Float("maxEmission");
				e.__worldVel = d.Get("worldVelocity").V3(); e.__localVel = d.Get("localVelocity").V3();
				e.__rndVel = d.Get("rndVelocity").V3(); e.__tangentVel = d.Get("tangentVelocity").V3();
				e.__emitterVelScale = d.Float("emitterVelocityScale");
				e.__angVel = d.Float("angularVelocity"); e.__rndAngVel = d.Float("rndAngularVelocity");
				e.__rndRot = d.Bool("rndRotation");
				e.__world = d.Bool("Simulate in Worldspace?");
				e.__oneShot = d.Bool("m_OneShot");
				if (d.TryGetProperty("m_Ellipsoid", out var ell)) { e.__ellipsoid = ell.V3(); e.__minRange = d.Float("m_MinEmitterRange"); }
				if (d.TryGetProperty("m_Mesh", out var em)) { e.__emitMesh = ld.Ref<Mesh>(em); e.__systematic = d.Bool("m_Systematic"); }
			};
			AssetDb.Register("EllipsoidParticleEmitter", emitter);
			AssetDb.Register("MeshParticleEmitter", emitter);
			AssetDb.Register("ParticleAnimator", (o, d, ld) =>
			{
				var a = (ParticleAnimator)o;
				a.__animColor = d.Bool("Does Animate Color?", true);
				var cols = new List<Color>();
				for (int i = 0; i < 5; i++) cols.Add(d.Get($"colorAnimation[{i}]").Col());
				a.__colors = cols.ToArray();
				a.__worldAxis = d.Get("worldRotationAxis").V3(); a.__localAxis = d.Get("localRotationAxis").V3();
				a.__sizeGrow = d.Float("sizeGrow"); a.__rndForce = d.Get("rndForce").V3(); a.__force = d.Get("force").V3();
				a.__damping = d.Float("damping", 1f); a.__stop = d.Bool("stopSimulation"); a.__autodestruct = d.Bool("autodestruct");
			});
			AssetDb.Register("ParticleRenderer", (o, d, ld) =>
			{
				var r = (ParticleRenderer)o;
				r.__enabled = d.Bool("m_Enabled", true);
				r.__mats = ld.Refs<Material>(d.Get("m_Materials")).ToArray();
				r.__camVelScale = d.Float("m_CameraVelocityScale");
				r.__mode = (ParticleRenderMode)d.Int("m_StretchParticles");
				r.__lengthScale = d.Float("m_LengthScale", 2f); r.__velScale = d.Float("m_VelocityScale");
				r.__maxSize = d.Float("m_MaxParticleSize", 0.25f);
				var uva = d.Get("UV Animation");
				r.__xTile = Math.Max(1, uva.Int("x Tile", 1)); r.__yTile = Math.Max(1, uva.Int("y Tile", 1)); r.__cycles = uva.Float("cycles", 1f);
			});
		}
	}
}
