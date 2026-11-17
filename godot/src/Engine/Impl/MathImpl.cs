// Pure-math icalls of Unity 3.4 (left-handed, Euler order Z, X, Y).
using System;

namespace UnityEngine
{
	public partial struct Quaternion
	{
		private static partial Quaternion INTERNAL_CALL_AngleAxis(float angle, ref Vector3 axis)
		{
			return AxisAngleRad(axis, angle * Mathf.Deg2Rad);
		}

		private static partial Quaternion INTERNAL_CALL_AxisAngle(ref Vector3 axis, float angle)
		{
			return AxisAngleRad(axis, angle);
		}

		internal static Quaternion AxisAngleRad(Vector3 axis, float rad)
		{
			float m = axis.magnitude;
			if (m < 1e-6f) return identity;
			axis /= m;
			float s = Mathf.Sin(rad * 0.5f);
			return new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(rad * 0.5f));
		}

		private static partial Quaternion INTERNAL_CALL_FromToRotation(ref Vector3 fromDirection, ref Vector3 toDirection)
		{
			Vector3 a = fromDirection.normalized, b = toDirection.normalized;
			float d = Vector3.Dot(a, b);
			if (d >= 1f - 1e-6f) return identity;
			if (d <= -1f + 1e-6f)
			{
				Vector3 axis = Vector3.Cross(Vector3.right, a);
				if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(Vector3.up, a);
				return AxisAngleRad(axis, Mathf.PI);
			}
			Vector3 c = Vector3.Cross(a, b);
			Quaternion q = new Quaternion(c.x, c.y, c.z, 1f + d);
			return Normalize(q);
		}

		internal static Quaternion Normalize(Quaternion q)
		{
			float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
			if (m < 1e-12f) return identity;
			return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
		}

		private static partial Quaternion INTERNAL_CALL_LookRotation(ref Vector3 forward, ref Vector3 upwards)
		{
			Vector3 f = forward.normalized;
			if (f.sqrMagnitude < 1e-12f) return identity;
			Vector3 r = Vector3.Cross(upwards, f);
			if (r.sqrMagnitude < 1e-12f)
			{
				// forward parallel to up: pick any perpendicular
				r = Vector3.Cross(Mathf.Abs(f.y) < 0.99f ? Vector3.up : Vector3.forward, f);
			}
			r = r.normalized;
			Vector3 u = Vector3.Cross(f, r);
			return FromBasis(r, u, f);
		}

		internal static Quaternion FromBasis(Vector3 r, Vector3 u, Vector3 f)
		{
			// columns: r, u, f
			float m00 = r.x, m01 = u.x, m02 = f.x;
			float m10 = r.y, m11 = u.y, m12 = f.y;
			float m20 = r.z, m21 = u.z, m22 = f.z;
			float tr = m00 + m11 + m22;
			Quaternion q;
			if (tr > 0f)
			{
				float s = Mathf.Sqrt(tr + 1f) * 2f;
				q = new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
			}
			else if (m00 > m11 && m00 > m22)
			{
				float s = Mathf.Sqrt(1f + m00 - m11 - m22) * 2f;
				q = new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
			}
			else if (m11 > m22)
			{
				float s = Mathf.Sqrt(1f + m11 - m00 - m22) * 2f;
				q = new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
			}
			else
			{
				float s = Mathf.Sqrt(1f + m22 - m00 - m11) * 2f;
				q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s);
			}
			return Normalize(q);
		}

		private static partial Quaternion INTERNAL_CALL_Slerp(ref Quaternion from, ref Quaternion to, float t)
		{
			return SlerpImpl(from, to, Mathf.Clamp01(t));
		}

		private static partial Quaternion INTERNAL_CALL_UnclampedSlerp(ref Quaternion from, ref Quaternion to, float t)
		{
			return SlerpImpl(from, to, t);
		}

		private static Quaternion SlerpImpl(Quaternion a, Quaternion b, float t)
		{
			float d = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
			if (d < 0f) { d = -d; b = new Quaternion(-b.x, -b.y, -b.z, -b.w); }
			float k0, k1;
			if (d > 0.9995f) { k0 = 1f - t; k1 = t; }
			else
			{
				float th = Mathf.Acos(d), s = Mathf.Sin(th);
				k0 = Mathf.Sin((1f - t) * th) / s; k1 = Mathf.Sin(t * th) / s;
			}
			return Normalize(new Quaternion(a.x * k0 + b.x * k1, a.y * k0 + b.y * k1, a.z * k0 + b.z * k1, a.w * k0 + b.w * k1));
		}

		private static partial Quaternion INTERNAL_CALL_Lerp(ref Quaternion from, ref Quaternion to, float t)
		{
			t = Mathf.Clamp01(t);
			Quaternion b = to;
			if (from.x * b.x + from.y * b.y + from.z * b.z + from.w * b.w < 0f) b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
			return Normalize(new Quaternion(from.x + (b.x - from.x) * t, from.y + (b.y - from.y) * t, from.z + (b.z - from.z) * t, from.w + (b.w - from.w) * t));
		}

		private static partial Quaternion INTERNAL_CALL_Inverse(ref Quaternion rotation)
		{
			float n = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
			if (n < 1e-12f) return identity;
			return new Quaternion(-rotation.x / n, -rotation.y / n, -rotation.z / n, rotation.w / n);
		}

		// Unity applies Z, then X, then Y: q = qy * qx * qz
		private static partial Quaternion INTERNAL_CALL_Internal_FromEulerRad(ref Vector3 euler)
		{
			float cx = Mathf.Cos(euler.x * 0.5f), sx = Mathf.Sin(euler.x * 0.5f);
			float cy = Mathf.Cos(euler.y * 0.5f), sy = Mathf.Sin(euler.y * 0.5f);
			float cz = Mathf.Cos(euler.z * 0.5f), sz = Mathf.Sin(euler.z * 0.5f);
			Quaternion qx = new Quaternion(sx, 0, 0, cx), qy = new Quaternion(0, sy, 0, cy), qz = new Quaternion(0, 0, sz, cz);
			return qy * qx * qz;
		}

		private static partial Vector3 INTERNAL_CALL_Internal_ToEulerRad(ref Quaternion rotation)
		{
			Quaternion q = Normalize(rotation);
			// rotation matrix elements
			float x = q.x, y = q.y, z = q.z, w = q.w;
			float m12 = 2f * (y * z - w * x);
			float m02 = 2f * (x * z + w * y), m22 = 1f - 2f * (x * x + y * y);
			float m10 = 2f * (x * y + w * z), m11 = 1f - 2f * (x * x + z * z);
			float m00 = 1f - 2f * (y * y + z * z), m01 = 2f * (x * y - w * z);
			float ex, ey, ez;
			float sx = -m12;
			if (sx < 0.9999f && sx > -0.9999f)
			{
				ex = Mathf.Asin(sx);
				ey = Mathf.Atan2(m02, m22);
				ez = Mathf.Atan2(m10, m11);
			}
			else
			{
				ex = sx > 0 ? Mathf.PI * 0.5f : -Mathf.PI * 0.5f;
				ey = Mathf.Atan2(-(2f * (x * z - w * y)), m00);
				ez = 0f;
			}
			return new Vector3(Wrap(ex), Wrap(ey), Wrap(ez));
		}

		private static float Wrap(float r)
		{
			const float tau = Mathf.PI * 2f;
			r %= tau;
			if (r < 0f) r += tau;
			return r;
		}

		private static partial void INTERNAL_CALL_Internal_ToAxisAngleRad(ref Quaternion q, out Vector3 axis, out float angle)
		{
			Quaternion n = Normalize(q);
			angle = 2f * Mathf.Acos(Mathf.Clamp(n.w, -1f, 1f));
			float s = Mathf.Sqrt(1f - n.w * n.w);
			axis = s < 1e-6f ? Vector3.right : new Vector3(n.x / s, n.y / s, n.z / s);
		}
	}

	public partial struct Vector3
	{
		private static partial Vector3 INTERNAL_CALL_Slerp(ref Vector3 from, ref Vector3 to, float t)
		{
			t = Mathf.Clamp01(t);
			float la = from.magnitude, lb = to.magnitude;
			if (la < 1e-6f || lb < 1e-6f) return Lerp(from, to, t);
			Vector3 a = from / la, b = to / lb;
			float d = Mathf.Clamp(Dot(a, b), -1f, 1f);
			float th = Mathf.Acos(d) * t;
			Vector3 rel = (b - a * d);
			if (rel.sqrMagnitude < 1e-12f) return Lerp(from, to, t);
			rel = rel.normalized;
			return (a * Mathf.Cos(th) + rel * Mathf.Sin(th)) * Mathf.Lerp(la, lb, t);
		}

		private static partial void INTERNAL_CALL_Internal_OrthoNormalize2(ref Vector3 a, ref Vector3 b)
		{
			a = a.normalized;
			b = (b - Project(b, a)).normalized;
		}

		private static partial void INTERNAL_CALL_Internal_OrthoNormalize3(ref Vector3 a, ref Vector3 b, ref Vector3 c)
		{
			a = a.normalized;
			b = (b - Project(b, a)).normalized;
			c = (c - Project(c, a) - Project(c, b)).normalized;
		}

		private static partial Vector3 INTERNAL_CALL_RotateTowards(ref Vector3 current, ref Vector3 target, float maxRadiansDelta, float maxMagnitudeDelta)
		{
			float la = current.magnitude, lb = target.magnitude;
			if (la < 1e-6f || lb < 1e-6f) return MoveTowards(current, target, maxMagnitudeDelta);
			Vector3 a = current / la, b = target / lb;
			float ang = Mathf.Acos(Mathf.Clamp(Dot(a, b), -1f, 1f));
			float t = ang < 1e-6f ? 1f : Mathf.Min(1f, maxRadiansDelta / ang);
			Vector3 dir = Slerp(a, b, t).normalized;
			float len = Mathf.MoveTowards(la, lb, maxMagnitudeDelta);
			return dir * len;
		}
	}

	public partial struct Mathf
	{
		public static partial int ClosestPowerOfTwo(int value)
		{
			int n = NextPowerOfTwo(value), p = n >> 1;
			return (n - value) < (value - p) ? n : p;
		}

		public static partial bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

		public static partial int NextPowerOfTwo(int value)
		{
			int v = Math.Max(1, value) - 1;
			v |= v >> 1; v |= v >> 2; v |= v >> 4; v |= v >> 8; v |= v >> 16;
			return v + 1;
		}

		public static partial float PerlinNoise(float x, float y) => (Svz.Perlin.Noise(x, y) + 1f) * 0.5f;
	}

	public partial struct Matrix4x4
	{
		public partial bool isIdentity => this == identity;

		private static partial Matrix4x4 INTERNAL_CALL_Inverse(ref Matrix4x4 m)
		{
			INTERNAL_CALL_Invert(ref m, out Matrix4x4 r);
			return r;
		}

		private static partial Matrix4x4 INTERNAL_CALL_Transpose(ref Matrix4x4 m)
		{
			Matrix4x4 r = default;
			for (int i = 0; i < 4; i++)
				for (int j = 0; j < 4; j++)
					r[i, j] = m[j, i];
			return r;
		}

		private static partial bool INTERNAL_CALL_Invert(ref Matrix4x4 inMatrix, out Matrix4x4 dest)
		{
			float[] a = new float[16], inv = new float[16];
			for (int i = 0; i < 16; i++) a[i] = inMatrix[i];
			// column-major index: a[col*4+row]
			inv[0] = a[5] * a[10] * a[15] - a[5] * a[11] * a[14] - a[9] * a[6] * a[15] + a[9] * a[7] * a[14] + a[13] * a[6] * a[11] - a[13] * a[7] * a[10];
			inv[4] = -a[4] * a[10] * a[15] + a[4] * a[11] * a[14] + a[8] * a[6] * a[15] - a[8] * a[7] * a[14] - a[12] * a[6] * a[11] + a[12] * a[7] * a[10];
			inv[8] = a[4] * a[9] * a[15] - a[4] * a[11] * a[13] - a[8] * a[5] * a[15] + a[8] * a[7] * a[13] + a[12] * a[5] * a[11] - a[12] * a[7] * a[9];
			inv[12] = -a[4] * a[9] * a[14] + a[4] * a[10] * a[13] + a[8] * a[5] * a[14] - a[8] * a[6] * a[13] - a[12] * a[5] * a[10] + a[12] * a[6] * a[9];
			inv[1] = -a[1] * a[10] * a[15] + a[1] * a[11] * a[14] + a[9] * a[2] * a[15] - a[9] * a[3] * a[14] - a[13] * a[2] * a[11] + a[13] * a[3] * a[10];
			inv[5] = a[0] * a[10] * a[15] - a[0] * a[11] * a[14] - a[8] * a[2] * a[15] + a[8] * a[3] * a[14] + a[12] * a[2] * a[11] - a[12] * a[3] * a[10];
			inv[9] = -a[0] * a[9] * a[15] + a[0] * a[11] * a[13] + a[8] * a[1] * a[15] - a[8] * a[3] * a[13] - a[12] * a[1] * a[11] + a[12] * a[3] * a[9];
			inv[13] = a[0] * a[9] * a[14] - a[0] * a[10] * a[13] - a[8] * a[1] * a[14] + a[8] * a[2] * a[13] + a[12] * a[1] * a[10] - a[12] * a[2] * a[9];
			inv[2] = a[1] * a[6] * a[15] - a[1] * a[7] * a[14] - a[5] * a[2] * a[15] + a[5] * a[3] * a[14] + a[13] * a[2] * a[7] - a[13] * a[3] * a[6];
			inv[6] = -a[0] * a[6] * a[15] + a[0] * a[7] * a[14] + a[4] * a[2] * a[15] - a[4] * a[3] * a[14] - a[12] * a[2] * a[7] + a[12] * a[3] * a[6];
			inv[10] = a[0] * a[5] * a[15] - a[0] * a[7] * a[13] - a[4] * a[1] * a[15] + a[4] * a[3] * a[13] + a[12] * a[1] * a[7] - a[12] * a[3] * a[5];
			inv[14] = -a[0] * a[5] * a[14] + a[0] * a[6] * a[13] + a[4] * a[1] * a[14] - a[4] * a[2] * a[13] - a[12] * a[1] * a[6] + a[12] * a[2] * a[5];
			inv[3] = -a[1] * a[6] * a[11] + a[1] * a[7] * a[10] + a[5] * a[2] * a[11] - a[5] * a[3] * a[10] - a[9] * a[2] * a[7] + a[9] * a[3] * a[6];
			inv[7] = a[0] * a[6] * a[11] - a[0] * a[7] * a[10] - a[4] * a[2] * a[11] + a[4] * a[3] * a[10] + a[8] * a[2] * a[7] - a[8] * a[3] * a[6];
			inv[11] = -a[0] * a[5] * a[11] + a[0] * a[7] * a[9] + a[4] * a[1] * a[11] - a[4] * a[3] * a[9] - a[8] * a[1] * a[7] + a[8] * a[3] * a[5];
			inv[15] = a[0] * a[5] * a[10] - a[0] * a[6] * a[9] - a[4] * a[1] * a[10] + a[4] * a[2] * a[9] + a[8] * a[1] * a[6] - a[8] * a[2] * a[5];
			float det = a[0] * inv[0] + a[1] * inv[4] + a[2] * inv[8] + a[3] * inv[12];
			dest = zero;
			if (Math.Abs(det) < 1e-20f) return false;
			det = 1f / det;
			for (int i = 0; i < 16; i++) dest[i] = inv[i] * det;
			return true;
		}

		private static partial Matrix4x4 INTERNAL_CALL_TRS(ref Vector3 pos, ref Quaternion q, ref Vector3 s)
		{
			float x = q.x, y = q.y, z = q.z, w = q.w;
			Matrix4x4 m = default;
			m.m00 = (1f - 2f * (y * y + z * z)) * s.x; m.m01 = 2f * (x * y - w * z) * s.y; m.m02 = 2f * (x * z + w * y) * s.z; m.m03 = pos.x;
			m.m10 = 2f * (x * y + w * z) * s.x; m.m11 = (1f - 2f * (x * x + z * z)) * s.y; m.m12 = 2f * (y * z - w * x) * s.z; m.m13 = pos.y;
			m.m20 = 2f * (x * z - w * y) * s.x; m.m21 = 2f * (y * z + w * x) * s.y; m.m22 = (1f - 2f * (x * x + y * y)) * s.z; m.m23 = pos.z;
			m.m33 = 1f;
			return m;
		}

		public static partial Matrix4x4 Ortho(float left, float right, float bottom, float top, float zNear, float zFar)
		{
			Matrix4x4 m = identity;
			m.m00 = 2f / (right - left); m.m03 = -(right + left) / (right - left);
			m.m11 = 2f / (top - bottom); m.m13 = -(top + bottom) / (top - bottom);
			m.m22 = -2f / (zFar - zNear); m.m23 = -(zFar + zNear) / (zFar - zNear);
			return m;
		}

		public static partial Matrix4x4 Perspective(float fov, float aspect, float zNear, float zFar)
		{
			float f = 1f / Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f);
			Matrix4x4 m = zero;
			m.m00 = f / aspect; m.m11 = f;
			m.m22 = (zFar + zNear) / (zNear - zFar); m.m23 = 2f * zFar * zNear / (zNear - zFar);
			m.m32 = -1f;
			return m;
		}
	}

	public partial struct Bounds
	{
		private static partial bool INTERNAL_CALL_Internal_Contains(ref Bounds m, ref Vector3 point)
		{
			Vector3 d = point - m.center, e = m.extents;
			return Mathf.Abs(d.x) <= e.x && Mathf.Abs(d.y) <= e.y && Mathf.Abs(d.z) <= e.z;
		}

		private static partial float INTERNAL_CALL_Internal_SqrDistance(ref Bounds m, ref Vector3 point)
		{
			Vector3 mn = m.min, mx = m.max;
			Vector3 cp = new Vector3(Mathf.Clamp(point.x, mn.x, mx.x), Mathf.Clamp(point.y, mn.y, mx.y), Mathf.Clamp(point.z, mn.z, mx.z));
			Vector3 d = point - cp;
			return d.sqrMagnitude;
		}

		private static partial bool INTERNAL_CALL_Internal_IntersectRay(ref Ray ray, ref Bounds bounds, out float distance)
		{
			return Svz.Geometry.RayAabb(ray.origin, ray.direction, bounds.min, bounds.max, out distance);
		}
	}

	public sealed partial class Random
	{
		private static System.Random rng = new System.Random();

		public static partial int seed
		{
			get => sSeed;
			set { sSeed = value; rng = new System.Random(value); }
		}
		private static int sSeed;

		public static partial float value => (float)rng.NextDouble();

		public static partial Vector3 insideUnitSphere
		{
			get
			{
				Vector3 v;
				do { v = new Vector3(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f)); } while (v.sqrMagnitude > 1f);
				return v;
			}
		}

		public static partial Vector3 onUnitSphere
		{
			get
			{
				Vector3 v;
				do { v = insideUnitSphere; } while (v.sqrMagnitude < 1e-6f);
				return v.normalized;
			}
		}

		public static partial Quaternion rotation => Quaternion.Normalize(new Quaternion(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f)));

		public static partial float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);

		private static partial int RandomRangeInt(int min, int max)
		{
			if (max <= min) return min;
			return rng.Next(min, max);
		}

		private static partial void GetRandomUnitCircle(out Vector2 output)
		{
			Vector2 v;
			do { v = new Vector2(Range(-1f, 1f), Range(-1f, 1f)); } while (v.sqrMagnitude > 1f);
			output = v;
		}
	}
}

namespace Svz
{
	public static class Geometry
	{
		public static bool RayAabb(UnityEngine.Vector3 o, UnityEngine.Vector3 d, UnityEngine.Vector3 min, UnityEngine.Vector3 max, out float dist)
		{
			float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;
			for (int i = 0; i < 3; i++)
			{
				if (Math.Abs(d[i]) < 1e-9f)
				{
					if (o[i] < min[i] || o[i] > max[i]) { dist = 0; return false; }
				}
				else
				{
					float t1 = (min[i] - o[i]) / d[i], t2 = (max[i] - o[i]) / d[i];
					if (t1 > t2) (t1, t2) = (t2, t1);
					tmin = Math.Max(tmin, t1); tmax = Math.Min(tmax, t2);
					if (tmin > tmax) { dist = 0; return false; }
				}
			}
			if (tmax < 0) { dist = 0; return false; }
			dist = tmin >= 0 ? tmin : 0f;
			return true;
		}

		/// Moller-Trumbore, both faces
		public static bool RayTriangle(UnityEngine.Vector3 o, UnityEngine.Vector3 d, UnityEngine.Vector3 a, UnityEngine.Vector3 b, UnityEngine.Vector3 c, out float t)
		{
			t = 0;
			UnityEngine.Vector3 e1 = b - a, e2 = c - a, p = UnityEngine.Vector3.Cross(d, e2);
			float det = UnityEngine.Vector3.Dot(e1, p);
			if (Math.Abs(det) < 1e-9f) return false;
			float inv = 1f / det;
			UnityEngine.Vector3 s = o - a;
			float u = UnityEngine.Vector3.Dot(s, p) * inv;
			if (u < 0 || u > 1) return false;
			UnityEngine.Vector3 q = UnityEngine.Vector3.Cross(s, e1);
			float v = UnityEngine.Vector3.Dot(d, q) * inv;
			if (v < 0 || u + v > 1) return false;
			t = UnityEngine.Vector3.Dot(e2, q) * inv;
			return t >= 0;
		}
	}

	public static class Perlin
	{
		private static readonly int[] P = Build();

		private static int[] Build()
		{
			var r = new System.Random(1337);
			int[] p = new int[512], b = new int[256];
			for (int i = 0; i < 256; i++) b[i] = i;
			for (int i = 255; i > 0; i--) { int j = r.Next(i + 1); (b[i], b[j]) = (b[j], b[i]); }
			for (int i = 0; i < 512; i++) p[i] = b[i & 255];
			return p;
		}

		private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
		private static float Grad(int h, float x, float y) => ((h & 1) == 0 ? x : -x) + ((h & 2) == 0 ? y : -y);

		public static float Noise(float x, float y)
		{
			int xi = (int)Math.Floor(x) & 255, yi = (int)Math.Floor(y) & 255;
			x -= (float)Math.Floor(x); y -= (float)Math.Floor(y);
			float u = Fade(x), v = Fade(y);
			int aa = P[P[xi] + yi], ab = P[P[xi] + yi + 1], ba = P[P[xi + 1] + yi], bb = P[P[xi + 1] + yi + 1];
			float l1 = Grad(aa, x, y) + u * (Grad(ba, x - 1, y) - Grad(aa, x, y));
			float l2 = Grad(ab, x, y - 1) + u * (Grad(bb, x - 1, y - 1) - Grad(ab, x, y - 1));
			return l1 + v * (l2 - l1);
		}
	}
}
