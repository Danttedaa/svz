using System;
using System.Collections.Generic;
using Svz;

static class Test
{
	static int assertions;
	static void Check(bool value, string name) { if (!value) throw new Exception(name); ++assertions; }

	static void Main()
	{
		var a = new List<string> { "samurai", "zombie" };
		var s1 = ArraySnapshots.Snapshot(a);
		Check(s1.Length == 2 && s1[0] == "samurai" && s1[1] == "zombie", "contents preserved");
		var s2 = ArraySnapshots.Snapshot(a);
		Check(ReferenceEquals(s1,s2), "stable count reuses exact array");
		a[1] = "ally";
		Check(ReferenceEquals(s2, ArraySnapshots.Snapshot(a)) && s2[1] == "ally", "values refreshed");
		a.Add("boss");
		var s3 = ArraySnapshots.Snapshot(a);
		Check(!ReferenceEquals(s2,s3) && s3.Length == 3 && s3[2] == "boss", "growth resizes snapshot");
		a.RemoveAt(0);
		var s4 = ArraySnapshots.Snapshot(a);
		Check(s4.Length == 2 && s4[0] == "ally" && s4[1] == "boss", "shrink discards stale references");
		var b = new List<string> { "other", "list" };
		var sb = ArraySnapshots.Snapshot(b);
		Check(!ReferenceEquals(sb,s4) && sb[0] == "other", "lists have independent snapshots");
		var c = new List<int> { 3, 2, 1 };
		Check(ArraySnapshots.Snapshot(c)[2] == 1, "generic numeric array");
		bool threw = false;
		try { ArraySnapshots.Snapshot<string>(null); } catch (ArgumentNullException) { threw = true; }
		Check(threw, "null rejected");
		for (int i=0;i<10000;i++) { a[0] = i.ToString(); ArraySnapshots.Snapshot(a); }
		Check(ReferenceEquals(ArraySnapshots.Snapshot(a), s4), "array reused for sustained updates");
		Console.WriteLine($"[test] {assertions} snapshot safety tests passed");
	}
}
