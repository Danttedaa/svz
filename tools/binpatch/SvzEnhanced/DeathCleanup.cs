using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Svz
{
	/// <summary>
	/// Conservative recovery for enemies whose completed kill is already recorded
	/// but whose death animation or melt callback never reaches readyToVanish.
	/// Called ONLY in CharactersManager's existing mDyingCharacters cleanup pass.
	/// The normal isOver path, and every reward/drop operation, stay untouched.
	/// </summary>
	public static class DeathCleanup
	{
		public const double StuckCorpseSeconds = 12.0;
		private sealed class FirstSeen { public readonly double Time; public FirstSeen(double now) { Time = now; } }
		private static ConditionalWeakTable<object, FirstSeen> sDead = new();
		internal static Func<double> Now = () => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

		public static bool ShouldRemove(object character, bool normalIsOver, float health)
		{
			if (character == null) return normalIsOver;
			if (normalIsOver)
			{
				sDead.Remove(character);
				return true;
			}
			// Don't prematurely delete revived enemies. The game's existing
			// health > 0 branch then transfers them back to the active list.
			if (!(health <= 0f) || float.IsNaN(health))
			{
				sDead.Remove(character);
				return false;
			}
			double now = Now();
			var first = sDead.GetValue(character, _ => new FirstSeen(now));
			if (now - first.Time < StuckCorpseSeconds) return false;
			sDead.Remove(character);
			Console.WriteLine("[svz-death] cleaning a stuck corpse after 12 real seconds in the dying list");
			return true;
		}

		internal static void ResetForTests()
		{
			sDead = new ConditionalWeakTable<object, FirstSeen>();
		}
	}
}
