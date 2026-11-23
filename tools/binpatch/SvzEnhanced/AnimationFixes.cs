using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Svz
{
	/// <summary>
	/// Root cause of the "frozen zombie" defect in the prebuilt port. The port computes a legacy
	/// AnimationClip's length from its curves only. The game's shared tagged "dieexplode" clip has no
	/// curves, just events (OnAnimStart at 0 s, OnAnimDone at 1/30 s), so the port gave it length 0:
	/// its AnimationState never reached normalizedTime 1 (the port returns 0 for zero-length states)
	/// and its events never fired. CharacterModelController.readyToVanish therefore stayed false and
	/// every enemy killed by an explosion, by the sonic attack, or as the last enemy of a wave stayed
	/// on the battlefield. Unity 3.4 played such a clip until its last event.
	///
	/// Called at every return of UnityEngine.AnimationClip.get_length with the port's value.
	/// Clips with curves (all but two clips in the game) take the first branch unchanged.
	/// </summary>
	public static class AnimationFixes
	{
		private sealed class Box { public readonly float Value; public Box(float v) { Value = v; } }
		private static readonly ConditionalWeakTable<object, Box> sEventLength = new();
		private static FieldInfo sEvents, sTime;

		public static float ClipLength(float computed, object clip)
		{
			if (computed > 0f || clip == null) return computed;
			return sEventLength.GetValue(clip, c => new Box(LastEventTime(c))).Value;
		}

		/// Time of the clip's last AnimationEvent; 0 when it has none or the port's layout is unexpected.
		internal static float LastEventTime(object clip)
		{
			try
			{
				sEvents ??= clip.GetType().GetField("__events", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				if (sEvents?.GetValue(clip) is not IEnumerable events) return 0f;
				float last = 0f;
				foreach (var e in events)
				{
					if (e == null) continue;
					sTime ??= e.GetType().GetField("__time", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
					if (sTime?.GetValue(e) is float t && t > last && !float.IsNaN(t) && !float.IsInfinity(t)) last = t;
				}
				return last;
			}
			catch (Exception)
			{
				return 0f;   // fall back to the port's original value
			}
		}
	}
}
