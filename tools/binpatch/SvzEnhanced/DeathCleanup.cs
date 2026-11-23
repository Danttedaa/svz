using System;
using System.Runtime.CompilerServices;

namespace Svz
{
	/// <summary>
	/// Safety net for enemies whose kill is already recorded but whose death never completes.
	/// The known cause (zero-length "dieexplode" clip) is fixed in <see cref="AnimationFixes"/>; this
	/// only catches unknown future cases. Called ONLY in CharactersManager's existing mDyingCharacters
	/// cleanup pass, after kill credit and drops were awarded; the normal isOver path, rewards and the
	/// isPlayer/isBase guards of that pass stay untouched.
	///
	/// Time is the game's own scaled clock (UnityEngine.Time.time, passed by the call site), so pauses,
	/// dialogs and the slow-motion victory finish never shorten a death animation. The longest legitimate
	/// death in the game is about 7.1 game-seconds (shock 0.97 s + "die" 4.63 s + ascend 1.43 s).
	/// </summary>
	public static class DeathCleanup
	{
		public const float StuckCorpseGameSeconds = 12f;
		private sealed class FirstSeen { public readonly float Time; public FirstSeen(float t) { Time = t; } }
		private static ConditionalWeakTable<object, FirstSeen> sDead = new();

		/// Number of corpses removed by the safety net in this session (diagnostics).
		public static int Cleaned { get; private set; }

		public static bool ShouldRemove(object character, bool normalIsOver, float health, float gameTime)
		{
			if (character == null) return normalIsOver;
			if (normalIsOver)
			{
				sDead.Remove(character);
				return true;
			}
			// Revived characters: the game's own health > 0 branch moves them back to the active list.
			if (!(health <= 0f) || float.IsNaN(health))
			{
				sDead.Remove(character);
				return false;
			}
			var first = sDead.GetValue(character, _ => new FirstSeen(gameTime));
			// a clock that went backwards (new level resets Time.time) restarts the wait
			if (gameTime < first.Time)
			{
				sDead.Remove(character);
				sDead.GetValue(character, _ => new FirstSeen(gameTime));
				return false;
			}
			if (gameTime - first.Time < StuckCorpseGameSeconds) return false;
			sDead.Remove(character);
			Cleaned++;
			Log($"[svz-death] removed a corpse still dying after {gameTime - first.Time:0.0} game-seconds (safety net #{Cleaned})");
			return true;
		}

		internal static Action<string> Log = s =>
		{
#if SVZ_GODOT
			Godot.GD.Print(s);   // reaches logcat on Android
#else
			Console.WriteLine(s);
#endif
		};

		internal static void ResetForTests()
		{
			sDead = new ConditionalWeakTable<object, FirstSeen>();
			Cleaned = 0;
		}
	}
}
