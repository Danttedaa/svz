// usage: Behavior <dir with patched SvZPort.dll + SvzEnhanced.dll + GodotSharp.dll>
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

static class Program
{
	static int passed;
	static void Check(bool ok, string what) { if (!ok) throw new Exception("FAIL: " + what); passed++; Console.WriteLine("  [OK] " + what); }
	const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

	static int Main(string[] args)
	{
		string dir = Path.GetFullPath(args[0]);
		var ctx = new AssemblyLoadContext("patched");
		ctx.Resolving += (c, name) => { var p = Path.Combine(dir, name.Name + ".dll"); return File.Exists(p) ? c.LoadFromAssemblyPath(p) : null; };
		var port = ctx.LoadFromAssemblyPath(Path.Combine(dir, "SvZPort.dll"));

		// 1. event-only clip (the shared "dieexplode" shape) lasts until its last event
		var clipT = port.GetType("UnityEngine.AnimationClip");
		var evT = port.GetType("UnityEngine.AnimationEvent");
		object Clip(float[] tracks, float[] events)
		{
			var c = Activator.CreateInstance(clipT, true);
			var tl = (System.Collections.Generic.List<float>)clipT.GetField("__tracks", F).GetValue(c);
			tl.AddRange(tracks);
			var el = (IList)clipT.GetField("__events", F).GetValue(c);
			foreach (var t in events) { var e = Activator.CreateInstance(evT, true); evT.GetField("__time", F).SetValue(e, t); el.Add(e); }
			return c;
		}
		float Len(object c) => (float)clipT.GetProperty("length").GetValue(c);
		Check(Math.Abs(Len(Clip(new float[0], new[] { 0f, 1f / 30f })) - 1f / 30f) < 1e-6, "event-only clip length = last event time (1/30 s)");
		Check(Len(Clip(new[] { 1.5f }, new[] { 0f, 3f })) == 1.5f, "clip with curves keeps the port's curve length");
		Check(Len(Clip(new float[0], new float[0])) == 0f, "empty clip stays 0");
		var cached = Clip(new float[0], new[] { 0.5f });
		Check(Len(cached) == 0.5f && Len(cached) == 0.5f, "repeated reads are stable");

		// 2. win coroutine: one real-second nested wait, then RegisterWin once, then onFinishGameRequest once
		var gameT = port.GetType("Svz.InGameImpl");
		var game = Activator.CreateInstance(gameT, true);
		var it = (IEnumerator)gameT.GetMethod("PostWinDelay", F).Invoke(game, null);
		Check(it.MoveNext(), "PostWinDelay yields a wait");
		Check(it.Current is IEnumerator, "wait is the nested real-time IEnumerator (not WaitForSeconds)");
		var wait = (IEnumerator)it.Current;
		var sw = System.Diagnostics.Stopwatch.StartNew();
		int frames = 0;
		while (wait.MoveNext()) { System.Threading.Thread.Sleep(10); frames++; if (sw.Elapsed.TotalSeconds > 5) break; }
		Check(sw.Elapsed.TotalSeconds >= 0.95 && sw.Elapsed.TotalSeconds < 3, $"normal win waits ~1 real second ({sw.Elapsed.TotalSeconds:0.00} s)");
		Check((int)gameT.GetProperty("Registered").GetValue(game) == 0, "rewards not registered before the wait ends");
		Check(!it.MoveNext(), "coroutine finishes after the wait");
		Check((int)gameT.GetProperty("Registered").GetValue(game) == 1 && (int)gameT.GetProperty("Finished").GetValue(game) == 1,
			"RegisterWin and onFinishGameRequest each ran exactly once");

		// 3. A completed wave must still record rewards without showing the
		// obsolete market rating popup (even though the fake build is "google").
		var resultsType = port.GetType("DebriefingImpl");
		var results = Activator.CreateInstance(resultsType, true);
		resultsType.GetMethod("DisplayStats", F).Invoke(results, null);
		Check((int)resultsType.GetProperty("RewardChecks").GetValue(results) == 2,
			"debrief rewards/statistics still execute");
		Check((int)resultsType.GetProperty("RatingPrompts").GetValue(results) == 0,
			"obsolete market rating prompt stays hidden");

		// 4. Shipped-style particle mesh builder reuses same-sized buffers but
		// reallocates safely when particle counts change. Inspect actual patched
		// method behavior, not only its three Cecil call-site signatures.
		var rendererT = port.GetType("UnityEngine.ParticleRenderer");
		var meshT = port.GetType("UnityEngine.Mesh");
		var particlesT = port.GetType("Svz.Particles");
		var build = particlesT.GetMethod("BuildMesh", F);
		var countProp = rendererT.GetProperty("ParticleCount");
		object NewRenderer(int count)
		{
			var obj = Activator.CreateInstance(rendererT, true);
			countProp.SetValue(obj, count);
			return obj;
		}
		var renderer = NewRenderer(3);
		object Build(object r) => build.Invoke(null, new object[] { r, null });
		var mesh = Build(renderer);
		Array Field(string name) => (Array)meshT.GetField(name, F).GetValue(mesh);
		var verts = Field("__v");
		var uvs = Field("__uv");
		var colors = Field("__c");
		int[] Indices() => ((System.Collections.Generic.List<int[]>)meshT.GetField("__subs", F).GetValue(mesh))[0];
		var indices = Indices();
		Check(indices.Length == 18, "particle triangle mesh has six indices per particle");
		Check(verts.Length == 12 && uvs.Length == 12 && colors.Length == 12,
			"particle mesh generates all four corners for each particle");
		Check(ReferenceEquals(mesh, Build(renderer)) &&
			ReferenceEquals(verts, Field("__v")) &&
			ReferenceEquals(uvs, Field("__uv")) &&
			ReferenceEquals(colors, Field("__c")) && ReferenceEquals(indices, Indices()),
			"same-sized frames reuse all four previous renderer-owned arrays");
		countProp.SetValue(renderer, 5);
		Build(renderer);
		Check(Field("__v").Length == 20 && Indices().Length == 30 &&
			!ReferenceEquals(verts, Field("__v")) && !ReferenceEquals(indices, Indices()),
			"particle count growth resizes vertex and triangle-index arrays safely");
		countProp.SetValue(renderer, 0);
		Build(renderer);
		Check(Field("__v").Length == 0 && Field("__uv").Length == 0 &&
			Field("__c").Length == 0 && Indices().Length == 0,
			"particle death clears previous mesh vertices, colors, and triangle indices");
		var secondRenderer = NewRenderer(3);
		var secondMesh = Build(secondRenderer);
		Check(!ReferenceEquals(mesh, secondMesh) &&
			!ReferenceEquals(verts, meshT.GetField("__v", F).GetValue(secondMesh)),
			"different particle renderers never share mutable buffers");

		Console.WriteLine($"[test] {passed} patched-behaviour checks passed");
		return 0;
	}
}
