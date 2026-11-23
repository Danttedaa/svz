using System;
using System.Collections.Generic;
using Svz;

static class Test
{
	static int passed;
	static void Check(bool test, string name) { if (!test) throw new Exception(name); ++passed; }
	static void Main()
	{
		var log = new List<string>();
		DeathCleanup.Log = log.Add;
		DeathCleanup.ResetForTests();
		float t = 100f;   // UnityEngine.Time.time (scaled game time)
		var enemy = new object();
		var other = new object();
		Check(!DeathCleanup.ShouldRemove(enemy, false, 0, t), "new corpse");
		t += 11.9f;
		Check(!DeathCleanup.ShouldRemove(enemy, false, 0, t), "not earlier than 12 game-seconds");
		t += 0.2f;
		Check(DeathCleanup.ShouldRemove(enemy, false, 0, t), "stale corpse after 12 game-seconds");
		Check(DeathCleanup.Cleaned == 1 && log.Count == 1 && log[0].StartsWith("[svz-death]"), "removal is counted and logged");
		Check(DeathCleanup.ShouldRemove(other, true, 0, t), "normal death completion remains immediate");
		Check(!DeathCleanup.ShouldRemove(null, false, 0, t), "null is not stale");
		Check(DeathCleanup.ShouldRemove(null, true, 0, t), "null keeps the game's own decision");
		t += 1;
		Check(!DeathCleanup.ShouldRemove(other, false, 15, t), "revived character never despawns");
		Check(!DeathCleanup.ShouldRemove(other, false, float.NaN, t), "unknown health is fail-closed");
		Check(!DeathCleanup.ShouldRemove(enemy, false, 0, t), "newly dying instance is tracked from now");
		t += 6;
		Check(!DeathCleanup.ShouldRemove(enemy, false, 10, t), "revival resets the timer");
		t += 6;
		Check(!DeathCleanup.ShouldRemove(enemy, false, 0, t), "new death starts fresh clock");
		t += 11;
		Check(!DeathCleanup.ShouldRemove(enemy, false, 0, t), "new death still inside grace");
		t += 1.1f;
		Check(DeathCleanup.ShouldRemove(enemy, false, 0, t), "new death now stale");

		// Game time stands still while the game is paused or a blocking dialog is open: no removal,
		// however long the player waits in real time.
		var paused = new object();
		Check(!DeathCleanup.ShouldRemove(paused, false, 0, 50f), "corpse before pause");
		for (int frame = 0; frame < 10000; frame++)
			Check(!DeathCleanup.ShouldRemove(paused, false, 0, 50f), "paused game never cleans up");
		// Slow-motion victory: Time.time advances slowly, the longest legitimate death (~7.1 s) is kept.
		var slow = new object();
		float s = 0f;
		for (int frame = 0; frame < 71; frame++) { Check(!DeathCleanup.ShouldRemove(slow, false, 0, s), "legit death kept"); s += 0.1f; }
		// A new level resets Time.time: a smaller clock restarts the wait instead of removing early or never.
		var carried = new object();
		Check(!DeathCleanup.ShouldRemove(carried, false, 0, 500f), "tracked at 500");
		Check(!DeathCleanup.ShouldRemove(carried, false, 0, 1f), "clock reset restarts");
		Check(!DeathCleanup.ShouldRemove(carried, false, 0, 12.9f), "11.9 s after restart kept");
		Check(DeathCleanup.ShouldRemove(carried, false, 0, 13.1f), "removed 12 s after restart");
		Console.WriteLine($"[test] {passed} stuck-corpse safety cases passed");
	}
}
