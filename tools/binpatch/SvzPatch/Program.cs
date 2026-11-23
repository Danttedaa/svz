// svzpatch: put the SvZ Enhanced frame pacing into a prebuilt SvZ port (SvZPort.dll from a released APK),
// without the original game APK and without recompiling the game.
//
//   svzpatch patch IN/SvZPort.dll SvzEnhanced.dll OUT/SvZPort.dll
//   svzpatch check SvZPort.dll [SvzEnhanced.dll]     exit 0 = all patches present (and, with SvzEnhanced.dll,
//                                                    every call into it resolves to a public static method)
//
// The enhancement code itself lives in SvzEnhanced.dll (godot/src/Engine/Core/FramePacing*.cs, compiled
// unchanged); SvZPort.dll gets only call-site changes, each matched against the exact original IL first:
//   Main._Ready          `Engine.MaxFps = 60`         -> FramePacing.Init(this, Toast.Show)
//   Main._Process        start                        -> FramePacing.Frame(delta, Input.touchCount)
//   Main._Process        Lerp(smooth, dt, 0.1f)       -> FramePacingPolicy.SmoothDeltaTime(smooth, dt)
//   Main._Notification   start                        -> FramePacing.OnNotification(what)
//   Application.targetFrameRate get/set               -> FramePacing.EffectiveFps / GameRequested(value)
// Any mismatch (another port version) aborts without writing.
using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

internal static class Program
{
	private const string Enh = "SvzEnhanced";

	private static int Main(string[] args)
	{
		try
		{
			if (args.Length == 4 && args[0] == "patch") { Patch(args[1], args[2], args[3]); return 0; }
			if (args.Length == 2 && args[0] == "check") return Check(args[1]) ? 0 : 1;
			if (args.Length == 3 && args[0] == "check") return Check(args[1]) && Resolves(args[1], args[2]) ? 0 : 1;
			Console.Error.WriteLine("usage: svzpatch patch IN.dll SvzEnhanced.dll OUT.dll | svzpatch check DLL");
			return 2;
		}
		catch (PatchException e)
		{
			Console.Error.WriteLine("svzpatch: " + e.Message);
			return 3;
		}
	}

	private sealed class PatchException : Exception { public PatchException(string m) : base(m) { } }

	private static void Require(bool cond, string what) { if (!cond) throw new PatchException("unexpected IL: " + what); }

	private static MethodDefinition Method(ModuleDefinition m, string type, string name, int paramCount)
	{
		var t = m.GetType(type) ?? throw new PatchException($"type {type} not found");
		var md = t.Methods.SingleOrDefault(x => x.Name == name && x.Parameters.Count == paramCount);
		return md ?? throw new PatchException($"method {type}::{name}/{paramCount} not found");
	}

	private static bool Calls(Instruction i, string declaringType, string name) =>
		(i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is MethodReference r &&
		r.DeclaringType.FullName == declaringType && r.Name == name;

	private static TypeReference ExistingTypeRef(ModuleDefinition m, string fullName) =>
		m.GetTypeReferences().FirstOrDefault(t => t.FullName == fullName)
		?? throw new PatchException($"SvZPort.dll has no reference to {fullName}");

	private static void Patch(string input, string enhancedPath, string output)
	{
		var module = ModuleDefinition.ReadModule(input, new ReaderParameters { ReadSymbols = false, InMemory = true });
		var enhanced = ModuleDefinition.ReadModule(enhancedPath);
		Require(!Check(module, quiet: true), "SvZPort.dll is already patched");

		// the API the call sites use must exist in SvzEnhanced.dll with these exact signatures
		var fp = enhanced.GetType("Svz.FramePacing") ?? throw new PatchException("Svz.FramePacing missing in SvzEnhanced.dll");
		var pol = enhanced.GetType("Svz.FramePacingPolicy") ?? throw new PatchException("Svz.FramePacingPolicy missing in SvzEnhanced.dll");
		void Expect(TypeDefinition t, string name, string sig) =>
			Require(t.Methods.Any(x => x.Name == name && x.IsStatic && x.IsPublic && x.FullName.EndsWith(sig)), $"SvzEnhanced {t.Name}.{name}{sig}");
		Expect(fp, "Init", "::Init(Godot.Node,System.Action`1<System.String>)");
		Expect(fp, "Frame", "::Frame(System.Double,System.Int32)");
		Expect(fp, "OnNotification", "::OnNotification(System.Int32)");
		Expect(fp, "GameRequested", "::GameRequested(System.Int32)");
		Expect(fp, "get_EffectiveFps", "::get_EffectiveFps()");
		Expect(pol, "SmoothDeltaTime", "::SmoothDeltaTime(System.Single,System.Single)");
		var victory = enhanced.GetType("Svz.VictoryTiming") ?? throw new PatchException("Svz.VictoryTiming missing in SvzEnhanced.dll");
		var victoryMethod = victory.Methods.SingleOrDefault(x => x.Name == "WaitRealSeconds" && x.IsPublic && x.IsStatic &&
			x.Parameters.Count == 1 && x.Parameters[0].ParameterType.FullName == "System.Single" &&
			x.ReturnType.FullName == "System.Collections.IEnumerator");
		Require(victoryMethod != null, "VictoryTiming.WaitRealSeconds(float) must return IEnumerator");
		var death = enhanced.GetType("Svz.DeathCleanup") ?? throw new PatchException("Svz.DeathCleanup missing");
		var deathMethod = death.Methods.SingleOrDefault(x => x.Name == "ShouldRemove" && x.IsPublic && x.IsStatic &&
			x.Parameters.Count == 3 && x.Parameters[0].ParameterType.FullName == "System.Object" &&
			x.Parameters[1].ParameterType.FullName == "System.Boolean" &&
			x.Parameters[2].ParameterType.FullName == "System.Single" &&
			x.ReturnType.FullName == "System.Boolean");
		Require(deathMethod != null, "DeathCleanup.ShouldRemove(object,bool,float) required");
		var fonts = enhanced.GetType("Svz.UiFontQuality") ?? throw new PatchException("Svz.UiFontQuality missing");
		Require(fonts.Methods.Any(x => x.Name == "SelectResolution" && x.IsStatic && x.IsPublic &&
			x.Parameters.Count == 2 && x.ReturnType.FullName == "System.String" &&
			x.Parameters[0].ParameterType.FullName == "System.Collections.Generic.List`1<System.Int32>" &&
			x.Parameters[1].ParameterType.FullName == "System.Int32"),
			"UiFontQuality.SelectResolution(List<int>,int) required");




		// references built from types SvZPort.dll already uses (no second System.Runtime reference)
		var asmRef = new AssemblyNameReference(Enh, enhanced.Assembly.Name.Version);
		module.AssemblyReferences.Add(asmRef);
		var fpRef = new TypeReference("Svz", "FramePacing", module, asmRef);
		var polRef = new TypeReference("Svz", "FramePacingPolicy", module, asmRef);
		var victoryRef = new TypeReference("Svz", "VictoryTiming", module, asmRef);
		var deathRef = new TypeReference("Svz", "DeathCleanup", module, asmRef);
		var fontRef = new TypeReference("Svz", "UiFontQuality", module, asmRef);
		var ts = module.TypeSystem;
		var nodeRef = ExistingTypeRef(module, "Godot.Node");
		var actionOpen = module.GetTypeReferences().FirstOrDefault(t => t.FullName == "System.Action`1");
		if (actionOpen == null)
		{
			// not referenced yet: add it in the same core library that System.Object comes from
			actionOpen = new TypeReference("System", "Action`1", module, module.TypeSystem.CoreLibrary);
			actionOpen.GenericParameters.Add(new GenericParameter("T", actionOpen));
		}
		var actionString = new GenericInstanceType(actionOpen);
		actionString.GenericArguments.Add(ts.String);

		MethodReference M(TypeReference owner, string name, TypeReference ret, params TypeReference[] ps)
		{
			var r = new MethodReference(name, ret, owner) { HasThis = false };
			foreach (var p in ps) r.Parameters.Add(new ParameterDefinition(p));
			return r;
		}
		var init = M(fpRef, "Init", ts.Void, nodeRef, actionString);
		var frame = M(fpRef, "Frame", ts.Void, ts.Double, ts.Int32);
		var onNotification = M(fpRef, "OnNotification", ts.Void, ts.Int32);
		var gameRequested = M(fpRef, "GameRequested", ts.Void, ts.Int32);
		var effectiveFps = M(fpRef, "get_EffectiveFps", ts.Int32);
		var smooth = M(polRef, "SmoothDeltaTime", ts.Single, ts.Single, ts.Single);
		var realtimeWait = M(victoryRef, "WaitRealSeconds", ExistingTypeRef(module, "System.Collections.IEnumerator"), ts.Single);
		var shouldRemove = M(deathRef, "ShouldRemove", ts.Boolean, ts.Object, ts.Boolean, ts.Single);
		var fontChoose = Method(module, "FontSetFinder", "AutoFindClosestResolutionFolderPath", 1);
		Require(fontChoose.Parameters[0].ParameterType.FullName == "System.Collections.Generic.List`1<System.Int32>",
			"FontSetFinder list parameter");
		var fontSelect = M(fontRef, "SelectResolution", ts.String, fontChoose.Parameters[0].ParameterType, ts.Int32);
		var screenHeight = module.ImportReference(Method(module, "UnityEngine.Screen", "get_height", 0));

		var actionCtor = new MethodReference(".ctor", ts.Void, actionString) { HasThis = true };
		actionCtor.Parameters.Add(new ParameterDefinition(ts.Object));
		actionCtor.Parameters.Add(new ParameterDefinition(ts.IntPtr));
		var toastShow = Method(module, "Svz.Toast", "Show", 1);
		var touchCount = module.ImportReference(Method(module, "UnityEngine.Input", "get_touchCount", 0));

		// 1. Main._Ready: `ldc.i4.s 60; call Engine::set_MaxFps` -> FramePacing.Init(this, Toast.Show)
		{
			var body = Method(module, "Svz.Main", "_Ready", 0).Body;
			body.SimplifyMacros();
			var hits = body.Instructions.Where(i => Calls(i, "Godot.Engine", "set_MaxFps")).ToList();
			Require(hits.Count == 1, "Main._Ready sets Engine.MaxFps once");
			var call = hits[0];
			Require(call.Previous.OpCode == OpCodes.Ldc_I4 && (int)call.Previous.Operand == 60, "Main._Ready: Engine.MaxFps = 60");
			var il = body.GetILProcessor();
			var first = call.Previous;
			// keep `first` as the instruction any branch might target: turn it into ldarg.0
			first.OpCode = OpCodes.Ldarg_0; first.Operand = null;
			il.InsertBefore(call, il.Create(OpCodes.Ldnull));
			il.InsertBefore(call, il.Create(OpCodes.Ldftn, toastShow));
			il.InsertBefore(call, il.Create(OpCodes.Newobj, actionCtor));
			call.OpCode = OpCodes.Call; call.Operand = init;
			body.OptimizeMacros();
		}

		// 2+3. Main._Process
		{
			var body = Method(module, "Svz.Main", "_Process", 1).Body;
			body.SimplifyMacros();
			var lerps = body.Instructions.Where(i => Calls(i, "UnityEngine.Mathf", "Lerp")).ToList();
			Require(lerps.Count == 1, "Main._Process has one Mathf.Lerp (smoothDeltaTime)");
			var lerp = lerps[0];
			Require(lerp.Previous.OpCode == OpCodes.Ldc_R4 && (float)lerp.Previous.Operand == 0.1f, "Lerp(.., 0.1f)");
			Require(lerp.Next.OpCode == OpCodes.Stsfld && ((FieldReference)lerp.Next.Operand).Name == "__smoothDt", "Lerp result stored in Time.__smoothDt");
			var il = body.GetILProcessor();
			var constant = lerp.Previous;
			constant.OpCode = OpCodes.Nop; constant.Operand = null;   // keep the instruction object (branch targets)
			lerp.Operand = smooth;
			var start = body.Instructions[0];
			Require(Calls(start, "Svz.Movie", "get_Playing"), "Main._Process starts with Movie.Playing");
			il.InsertBefore(start, il.Create(OpCodes.Ldarg_1));
			il.InsertBefore(start, il.Create(OpCodes.Call, touchCount));
			il.InsertBefore(start, il.Create(OpCodes.Call, frame));
			body.OptimizeMacros();
		}

		// 4. Main._Notification: forward `what`
		{
			var body = Method(module, "Svz.Main", "_Notification", 1).Body;
			body.SimplifyMacros();
			var start = body.Instructions[0];
			Require(body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_I4 && (int)i.Operand == 2015), "Main._Notification handles NotificationApplicationResumed (2015)");
			var il = body.GetILProcessor();
			il.InsertBefore(start, il.Create(OpCodes.Ldarg_1));
			il.InsertBefore(start, il.Create(OpCodes.Call, onNotification));
			body.OptimizeMacros();
		}

		// 5. Application.targetFrameRate
		{
			var get = Method(module, "UnityEngine.Application", "get_targetFrameRate", 0);
			Require(get.Body.Instructions.Any(i => Calls(i, "Godot.Engine", "get_MaxFps")), "targetFrameRate getter reads Engine.MaxFps");
			get.Body.Instructions.Clear(); get.Body.Variables.Clear(); get.Body.ExceptionHandlers.Clear();
			var gi = get.Body.GetILProcessor();
			gi.Append(gi.Create(OpCodes.Call, effectiveFps));
			gi.Append(gi.Create(OpCodes.Ret));

			var set = Method(module, "UnityEngine.Application", "set_targetFrameRate", 1);
			Require(set.Body.Instructions.Any(i => Calls(i, "Godot.Engine", "set_MaxFps")), "targetFrameRate setter writes Engine.MaxFps");
			set.Body.Instructions.Clear(); set.Body.Variables.Clear(); set.Body.ExceptionHandlers.Clear();
			var si = set.Body.GetILProcessor();
			si.Append(si.Create(OpCodes.Ldarg_0));
			si.Append(si.Create(OpCodes.Call, gameRequested));
			si.Append(si.Create(OpCodes.Ret));
		}

		// Shorten only the regular win coroutine's wait. Reward registration and the exit
		// handler are left in their original order; abort if the compiled game's shape changed.
		PatchWinDelay(module, realtimeWait);
		PatchStuckCorpses(module, shouldRemove);
		PatchFontSelection(module, fontSelect, screenHeight);

		module.Write(output);
		Console.WriteLine($"patched {input} -> {output}");
		if (!Check(output)) throw new PatchException("patched file does not pass the check");
	}


	private static IEnumerable<TypeDefinition> Descendants(IEnumerable<TypeDefinition> types)
	{
		foreach (var t in types)
		{
			yield return t;
			foreach (var nested in Descendants(t.NestedTypes)) yield return nested;
		}
	}

	private static bool IsWinWait(Instruction i) =>
		i.OpCode == OpCodes.Newobj && i.Operand is MethodReference m &&
		m.DeclaringType.FullName == "UnityEngine.WaitForSeconds" && m.Name == ".ctor" &&
		m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Single";

	private static MethodDefinition WinMoveNext(ModuleDefinition module)
	{
		var matches = Descendants(module.Types)
			.Where(t => t.Name.Contains("PostWinDelay", StringComparison.Ordinal))
			.SelectMany(t => t.Methods.Where(m => m.Name == "MoveNext" && m.HasBody))
			.ToArray();
		return matches.Length == 1 ? matches[0] : null;
	}

	private static bool WinDelayPatched(ModuleDefinition module)
	{
		var move = WinMoveNext(module);
		if (move == null) return false;
		var waits = move.Body.Instructions.Where(i =>
			i.OpCode == OpCodes.Call && i.Operand is MethodReference m &&
			m.DeclaringType.FullName == "Svz.VictoryTiming" &&
			m.DeclaringType.Scope.Name == Enh &&
			m.Name == "WaitRealSeconds").ToArray();
		return waits.Length == 1 && !move.Body.Instructions.Any(IsWinWait);
	}

	private static void PatchWinDelay(ModuleDefinition module, MethodReference realtimeWait)
	{
		var move = WinMoveNext(module);
		Require(move != null, "exactly one PostWinDelay iterator state machine");
		var body = move.Body;
		body.SimplifyMacros();

		// The wait must be followed by the game's original reward/save and exit calls.
		// We never bypass, duplicate or reorder those calls.
		bool CallsGame(string name) => body.Instructions.Any(i =>
			(i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
			i.Operand is MethodReference m && m.Name == name &&
			m.DeclaringType.Name == "InGameImpl");
		Require(CallsGame("RegisterWin") && CallsGame("onFinishGameRequest"),
			"PostWinDelay does not retain RegisterWin + onFinishGameRequest");
		var waits = body.Instructions.Where(IsWinWait).ToArray();
		Require(waits.Length == 1, "PostWinDelay must construct exactly one WaitForSeconds");
		// A WaitForSeconds count is scaled by the game's slow-motion timeScale,
		// so even 1 game-second may require many real seconds after a victory.
		// Replace the yield expression with a nested IEnumerator that measures
		// real elapsed time; CoroutineRunner.Step handles nested IEnumerators.
		var call = waits[0];
		call.OpCode = OpCodes.Call;
		call.Operand = realtimeWait;
		body.OptimizeMacros();
		Require(WinDelayPatched(module), "PostWinDelay real-time wait not installed");
	}


	/// Target only the dying-character cleanup gate. The game has already
	/// registered kills and awarded drops before adding a Character to this list.
	/// Normal completed death animations follow the original isOver path.
	private static void PatchStuckCorpses(ModuleDefinition module, MethodReference hook)
	{
		var target = Method(module, "CharactersManager", "UpdateBattle", 0);
		var body = target.Body;
		body.SimplifyMacros();
		var hits = body.Instructions.Where(i => Calls(i, "Character", "get_isOver")).ToArray();
		Require(hits.Length == 1, "CharactersManager.UpdateBattle contains one dying-character isOver check");
		var call = hits[0];
		Require(call.Previous.OpCode == OpCodes.Ldloc && call.Previous.Operand is VariableDefinition,
			"dying-character isOver getter receiver is a local variable");
		var character = (VariableDefinition)call.Previous.Operand;
		var healthGetter = module.ImportReference(Method(module, "Character", "get_health", 0));
		var il = body.GetILProcessor();
		// Existing stack: [character] before isOver; preserve it for ShouldRemove.
		il.InsertBefore(call, il.Create(OpCodes.Dup));
		var load = il.Create(OpCodes.Ldloc, character);
		il.InsertAfter(call, load);
		var health = il.Create(OpCodes.Callvirt, healthGetter);
		il.InsertAfter(load, health);
		il.InsertAfter(health, il.Create(OpCodes.Call, hook));
		body.OptimizeMacros();
		Require(StuckCorpsesPatched(module), "dying-character cleanup gate not patched");
	}

	private static bool StuckCorpsesPatched(ModuleDefinition module)
	{
		var target = Method(module, "CharactersManager", "UpdateBattle", 0);
		var calls = target.Body.Instructions.Where(i => i.OpCode == OpCodes.Call &&
			i.Operand is MethodReference m && m.DeclaringType.FullName == "Svz.DeathCleanup" &&
			m.DeclaringType.Scope.Name == Enh && m.Name == "ShouldRemove").ToArray();
		return calls.Length == 1 &&
			target.Body.Instructions.Any(i => Calls(i, "Character", "get_isOver")) &&
			target.Body.Instructions.Any(i => Calls(i, "Character", "get_health"));
	}


	// Preserve the existing asset database and font renderer; only change the
	// closest-resolution policy so medium landscape devices load sharper atlases.
	private static void PatchFontSelection(ModuleDefinition module, MethodReference selector, MethodReference height)
	{
		var m = Method(module, "FontSetFinder", "AutoFindClosestResolutionFolderPath", 1);
		Require(m.ReturnType.FullName == "System.String", "font selector return type");
		Require(m.HasBody && m.Body.Instructions.Any(i => Calls(i, "UnityEngine.Mathf", "Abs")) &&
			m.Body.Instructions.Any(i => Calls(i, "UnityEngine.Screen", "get_height")),
			"font selector still uses original nearest-resolution algorithm");
		m.Body.Instructions.Clear();
		m.Body.Variables.Clear();
		m.Body.ExceptionHandlers.Clear();
		var il = m.Body.GetILProcessor();
		il.Append(il.Create(OpCodes.Ldarg_1));
		il.Append(il.Create(OpCodes.Call, height));
		il.Append(il.Create(OpCodes.Call, selector));
		il.Append(il.Create(OpCodes.Ret));
		Require(FontSelectionPatched(module), "high-resolution font selector not installed");
	}

	private static bool FontSelectionPatched(ModuleDefinition module)
	{
		var m = Method(module, "FontSetFinder", "AutoFindClosestResolutionFolderPath", 1);
		return m.Body.Instructions.Count(i => i.OpCode == OpCodes.Call &&
			i.Operand is MethodReference r && r.DeclaringType.FullName == "Svz.UiFontQuality" &&
			r.DeclaringType.Scope.Name == Enh && r.Name == "SelectResolution") == 1;
	}

	/// Every member reference into SvzEnhanced must match a public static method there (name, return, parameters).
	private static bool Resolves(string patched, string enhancedPath)
	{
		var m = ModuleDefinition.ReadModule(patched, new ReaderParameters { InMemory = true });
		var enh = ModuleDefinition.ReadModule(enhancedPath, new ReaderParameters { InMemory = true });
		bool all = true;
		foreach (var r in m.GetMemberReferences().OfType<MethodReference>().Where(r => r.DeclaringType.Scope.Name == Enh))
		{
			var t = enh.GetType(r.DeclaringType.FullName);
			var match = t?.Methods.FirstOrDefault(d => d.Name == r.Name && d.IsPublic && d.IsStatic &&
				d.ReturnType.FullName == r.ReturnType.FullName &&
				d.Parameters.Select(x => x.ParameterType.FullName).SequenceEqual(r.Parameters.Select(x => x.ParameterType.FullName)));
			Console.WriteLine($"  [{(match != null ? "OK" : "--")}] resolves {r.FullName}");
			all &= match != null;
		}
		return all;
	}

	private static bool Check(string path) => Check(ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true }), quiet: false);

	private static bool Check(ModuleDefinition m, bool quiet)
	{
		bool CallsEnh(string type, string method, int pc, string target) =>
			Method(m, type, method, pc).Body.Instructions.Any(i => (i.OpCode == OpCodes.Call) && i.Operand is MethodReference r &&
				r.DeclaringType.Scope.Name == Enh && r.Name == target);
		var checks = new (string what, bool ok)[]
		{
			("Main._Ready -> FramePacing.Init", CallsEnh("Svz.Main", "_Ready", 0, "Init")),
			("no Engine.MaxFps = 60 left in Main._Ready", !Method(m, "Svz.Main", "_Ready", 0).Body.Instructions.Any(i => Calls(i, "Godot.Engine", "set_MaxFps"))),
			("Main._Process -> FramePacing.Frame", CallsEnh("Svz.Main", "_Process", 1, "Frame")),
			("Main._Process -> SmoothDeltaTime", CallsEnh("Svz.Main", "_Process", 1, "SmoothDeltaTime")),
			("Main._Notification -> FramePacing.OnNotification", CallsEnh("Svz.Main", "_Notification", 1, "OnNotification")),
			("Application.targetFrameRate getter", CallsEnh("UnityEngine.Application", "get_targetFrameRate", 0, "get_EffectiveFps")),
			("Application.targetFrameRate setter", CallsEnh("UnityEngine.Application", "set_targetFrameRate", 1, "GameRequested")),
			("assembly reference SvzEnhanced", m.AssemblyReferences.Any(a => a.Name == Enh)),
			("PostWinDelay uses unscaled real-time wait", WinDelayPatched(m)),
			("dying enemy cleanup has safe timeout", StuckCorpsesPatched(m)),
			("font selector uses optional HD atlas policy", FontSelectionPatched(m)),
		};
		if (!quiet) foreach (var (what, ok) in checks) Console.WriteLine($"  [{(ok ? "OK" : "--")}] {what}");
		return checks.All(c => c.ok);
	}
}
