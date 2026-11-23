using System;
using Svz;

static class Test
{
	static int passed;
	static void Check(bool test, string name) { if (!test) throw new Exception(name); ++passed; }
	static void Main()
	{
		double now = 100;
		DeathCleanup.Now = () => now;
		DeathCleanup.ResetForTests();
		var enemy = new object();
		var other = new object();
		Check(!DeathCleanup.ShouldRemove(enemy, false, 0), "new corpse");
		now += 11.9;
		Check(!DeathCleanup.ShouldRemove(enemy, false, 0), "not earlier than 12 seconds");
		now += 0.2;
		Check(DeathCleanup.ShouldRemove(enemy, false, 0), "stale corpse after 12 seconds");
		Check(DeathCleanup.ShouldRemove(other, true, 0), "normal death completion remains immediate");
		Check(!DeathCleanup.ShouldRemove(null, false, 0), "null is not stale");
		now += 1;
		Check(!DeathCleanup.ShouldRemove(other, false, 15), "revived character never despawns");
		Check(!DeathCleanup.ShouldRemove(other, false, float.NaN), "unknown health is fail-closed");
		Check(!DeathCleanup.ShouldRemove(enemy, false, 0), "newly dying instance is tracked from now");
		now += 6;
		Check(!DeathCleanup.ShouldRemove(enemy, false, 10), "revival resets the timer");
		now += 6;
		Check(!DeathCleanup.ShouldRemove(enemy, false, 0), "new death starts fresh clock");
		now += 11;
		Check(!DeathCleanup.ShouldRemove(enemy, false, 0), "new death still inside grace");
		now += 1.1;
		Check(DeathCleanup.ShouldRemove(enemy, false, 0), "new death now stale");
		Console.WriteLine($"[test] {passed} stuck-corpse safety cases passed");
	}
}
