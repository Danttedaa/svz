using System;
using System.Collections.Generic;
using Svz;

internal static class Test
{
	static void Main()
	{
		var supported = new List<int> { 320, 480, 640, 768, 1536 };
		void Check(int height, string want)
		{
			string actual = UiFontQuality.SelectResolution(supported, height);
			if (actual != want) throw new Exception($"{height}: expected {want}, got {actual}");
		}
		Check(320, "320");
		Check(480, "480");
		Check(640, "640");
		Check(699, "640");
		Check(709, "1536");
		Check(768, "1536");
		Check(1080, "1536");
		Check(1440, "1536");
		Check(1536, "1536");
		Check(1600, "1536");
		Check(540, "480");
		var reduced = new List<int> {320, 480, 640, 768};
		if (UiFontQuality.SelectResolution(reduced, 1080) != "768") throw new Exception("missing atlas fallback");
		if (supported.Count != 5) throw new Exception("input list mutated");
		Console.WriteLine("[test] 13 HD font selection assertions passed");
	}
}
