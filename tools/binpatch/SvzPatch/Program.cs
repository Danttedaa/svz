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
		var victoryMethod = victory.Methods.SingleOrDefault(x => x.Name == "WaitAfterWin" && x.IsPublic && x.IsStatic &&
			x.Parameters.Count == 2 && x.Parameters[0].ParameterType.FullName == "System.Single" &&
			x.Parameters[1].ParameterType.FullName == "System.Object" &&
			x.ReturnType.FullName == "System.Collections.IEnumerator");
		Require(victoryMethod != null, "VictoryTiming.WaitAfterWin(float,object) must return IEnumerator");
		var death = enhanced.GetType("Svz.DeathCleanup") ?? throw new PatchException("Svz.DeathCleanup missing");
		var deathMethod = death.Methods.SingleOrDefault(x => x.Name == "ShouldRemove" && x.IsPublic && x.IsStatic &&
			x.Parameters.Count == 4 && x.Parameters[0].ParameterType.FullName == "System.Object" &&
			x.Parameters[1].ParameterType.FullName == "System.Boolean" &&
			x.Parameters[2].ParameterType.FullName == "System.Single" &&
			x.Parameters[3].ParameterType.FullName == "System.Single" &&
			x.ReturnType.FullName == "System.Boolean");
		Require(deathMethod != null, "DeathCleanup.ShouldRemove(object,bool,float,float) required");
		var animFixes = enhanced.GetType("Svz.AnimationFixes") ?? throw new PatchException("Svz.AnimationFixes missing");
		Require(animFixes.Methods.Any(x => x.Name == "ClipLength" && x.IsPublic && x.IsStatic && x.Parameters.Count == 2 &&
			x.Parameters[0].ParameterType.FullName == "System.Single" && x.Parameters[1].ParameterType.FullName == "System.Object" &&
			x.ReturnType.FullName == "System.Single"), "AnimationFixes.ClipLength(float,object) required");
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
		var animFixRef = new TypeReference("Svz", "AnimationFixes", module, asmRef);
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
		var realtimeWait = M(victoryRef, "WaitAfterWin", ExistingTypeRef(module, "System.Collections.IEnumerator"), ts.Single, ts.Object);
		var shouldRemove = M(deathRef, "ShouldRemove", ts.Boolean, ts.Object, ts.Boolean, ts.Single, ts.Single);
		var clipLength = M(animFixRef, "ClipLength", ts.Single, ts.Single, ts.Object);
		var fontChoose = Method(module, "FontSetFinder", "AutoFindClosestResolutionFolderPath", 1);
		Require(fontChoose.Parameters[0].ParameterType.FullName == "System.Collections.Generic.List`1<System.Int32>",
			"FontSetFinder list parameter");
		var fontSelect = M(fontRef, "SelectResolution", ts.String, fontChoose.Parameters[0].ParameterType, ts.Int32);
		var snaps = enhanced.GetType("Svz.ArraySnapshots") ?? throw new PatchException("ArraySnapshots missing");
		Require(snaps.Methods.Any(x => x.Name == "Snapshot" && x.IsPublic && x.IsStatic &&
			x.GenericParameters.Count == 1 && x.Parameters.Count == 1),
			"ArraySnapshots.Snapshot<T>(List<T>) required");

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
		PatchSnapshots(module, asmRef);
		PatchClipLength(module, clipLength);
		var uiTextRef = new TypeReference("Svz", "UiText", module, asmRef);
		PatchFontLoad(module, M(uiTextRef, "PrepareFont", ts.Void, ts.Object));
		PatchTextHalo(module, uiTextRef);
		PatchPerFrameLookups(module, new TypeReference("Svz", "Reuse", module, asmRef));
		PatchMeshInterop(module, new TypeReference("Svz", "MeshArrays", module, asmRef));
		SuppressLegacyRateMePrompt(module);

		module.Write(output);
		Console.WriteLine($"patched {input} -> {output}");
		if (!Check(output)) throw new PatchException("patched file does not pass the check");
	}



	// The original game presents an obsolete incentivized market-rating dialog
	// at fixed victory waves. Suppress ONLY its platform-type branches within
	// DebriefingImpl.DisplayStats; leave achievements, coins, new unlocks and
	// saved profile state untouched. The rating flag is not written to the save.
	private static void SuppressLegacyRateMePrompt(ModuleDefinition module)
	{
		var m = Method(module, "DebriefingImpl", "DisplayStats", 0);
		var body = m.Body;
		body.SimplifyMacros();
		Require(body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "debrief_victory") &&
			body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "CoinsGained"),
			"rating branch must be in debrief stats with victory and coin award logic");
		var expected = new[] { "amazon", "google" };
		var matches = body.Instructions.Where(i => Calls(i, "ApplicationUtilities", "IsBuildType")).ToArray();
		Require(matches.Length == expected.Length,
			"exactly the amazon and google store-rating checks in DebriefingImpl.DisplayStats");
		var sources = matches.Select(i => i.Previous?.OpCode == OpCodes.Ldstr ? i.Previous.Operand as string : null).ToArray();
		Require(sources.SequenceEqual(expected), "rating checks must use literal amazon, google in order");
		var il = body.GetILProcessor();
		foreach (var call in matches)
		{
			// Original stack: [buildTypeString]; Pop consumes it, Ldc_I4_0
			// supplies the false result expected by the original branch instruction.
			call.OpCode = OpCodes.Pop;
			call.Operand = null;
			il.InsertAfter(call, il.Create(OpCodes.Ldc_I4_0));
		}
		body.OptimizeMacros();
		Require(LegacyRatingSuppressed(module), "obsolete rating popup not suppressed");
	}

	private static bool LegacyRatingSuppressed(ModuleDefinition module)
	{
		var m = Method(module, "DebriefingImpl", "DisplayStats", 0);
		bool HasDisabledCheck(string buildType) => m.Body.Instructions.Any(i =>
			i.OpCode == OpCodes.Ldstr && (i.Operand as string) == buildType &&
			i.Next?.OpCode == OpCodes.Pop && i.Next.Next?.OpCode == OpCodes.Ldc_I4_0);
		return HasDisabledCheck("amazon") && HasDisabledCheck("google") &&
			!m.Body.Instructions.Any(i => Calls(i, "ApplicationUtilities", "IsBuildType")) &&
			m.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (i.Operand as string) == "CoinsGained");
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
			(m.Name == "WaitAfterWin" || m.Name == "WaitRealSeconds")).ToArray();
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
		// M9: also pass the InGameImpl (the iterator's <>4__this) so the one-time wave-70 story ending
		// keeps its configured display time. Stack: [seconds] -> [seconds, game].
		var self = move.DeclaringType.Fields.SingleOrDefault(f => f.Name == "<>4__this" && f.FieldType.Name == "InGameImpl");
		Require(self != null, "PostWinDelay iterator has <>4__this of type InGameImpl");
		var il = body.GetILProcessor();
		il.InsertBefore(call, il.Create(OpCodes.Ldarg_0));
		il.InsertBefore(call, il.Create(OpCodes.Ldfld, self));
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
		var gameTime = module.ImportReference(Method(module, "UnityEngine.Time", "get_time", 0));
		var il = body.GetILProcessor();
		// Existing stack: [character] before isOver; preserve it for ShouldRemove.
		il.InsertBefore(call, il.Create(OpCodes.Dup));
		var load = il.Create(OpCodes.Ldloc, character);
		il.InsertAfter(call, load);
		var health = il.Create(OpCodes.Callvirt, healthGetter);
		il.InsertAfter(load, health);
		var time = il.Create(OpCodes.Call, gameTime);
		il.InsertAfter(health, time);
		il.InsertAfter(time, il.Create(OpCodes.Call, hook));
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


	// Both animation and particle update loops used List<T>.ToArray() every
	// frame to permit safe mutation while iterating. Replace only those three
	// immediate foreach snapshots with same-length reusable buffers.
	private static readonly (string type, string name, int paramsCount)[] SnapshotSites =
	{
		("Svz.Particles", "Update", 1),
		("Svz.AnimationSystem", "Step", 2),
		("Svz.AnimationSystem", "FinishStates", 1),
	};

	private static bool IsListToArray(Instruction i) =>
		(i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Call) &&
		i.Operand is MethodReference m && m.Name == "ToArray" && m.Parameters.Count == 0 &&
		m.DeclaringType is GenericInstanceType list &&
		list.ElementType.FullName == "System.Collections.Generic.List`1" &&
		list.GenericArguments.Count == 1 && m.ReturnType is ArrayType;

	private static bool IsSnapshotHook(Instruction i) =>
		i.OpCode == OpCodes.Call && i.Operand is MethodReference m &&
		m.Name == "Snapshot" && m.DeclaringType.FullName == "Svz.ArraySnapshots" &&
		m.DeclaringType.Scope.Name == Enh;

	private static void PatchSnapshots(ModuleDefinition module, AssemblyNameReference asmRef)
	{
		var helper = new TypeReference("Svz", "ArraySnapshots", module, asmRef);
		foreach (var (type, name, count) in SnapshotSites)
		{
			var method = Method(module, type, name, count);
			method.Body.SimplifyMacros();
			var toArrays = method.Body.Instructions.Where(IsListToArray).ToArray();
			Require(toArrays.Length == 1, type + "." + name + " has exactly one List<T>.ToArray foreach snapshot");
			var call = toArrays[0];
			Require(call.Next?.OpCode == OpCodes.Stloc,
				type + "." + name + " snapshot is immediately stored in a foreach local");

			var old = (MethodReference)call.Operand;
			var instance = (GenericInstanceType)old.DeclaringType;
			var open = new MethodReference("Snapshot", module.TypeSystem.Object, helper) { HasThis = false };
			var generic = new GenericParameter("T", open);
			open.GenericParameters.Add(generic);
			open.ReturnType = new ArrayType(generic);
			var list = new GenericInstanceType(instance.ElementType);
			list.GenericArguments.Add(generic);
			open.Parameters.Add(new ParameterDefinition(list));
			var concrete = new GenericInstanceMethod(open);
			concrete.GenericArguments.Add(instance.GenericArguments[0]);
			call.OpCode = OpCodes.Call;
			call.Operand = concrete;
			method.Body.OptimizeMacros();
		}
		Require(SnapshotsPatched(module), "hot-path snapshot calls not all patched");
	}

	private static bool SnapshotsPatched(ModuleDefinition module) =>
		SnapshotSites.All(site =>
		{
			var instructions = Method(module, site.type, site.name, site.paramsCount).Body.Instructions;
			return instructions.Count(IsSnapshotHook) == 1 && !instructions.Any(IsListToArray);
		});


	// Root cause of frozen corpses: UnityEngine.AnimationClip.get_length returns 0 for a clip with events
	// but no curves. Pass every returned value through AnimationFixes.ClipLength(value, this).
	private static void PatchClipLength(ModuleDefinition module, MethodReference hook)
	{
		var get = Method(module, "UnityEngine.AnimationClip", "get_length", 0);
		Require(get.ReturnType.FullName == "System.Single", "AnimationClip.length returns float");
		Require(get.Body.Instructions.Any(i => (i.OpCode == OpCodes.Ldfld || i.OpCode == OpCodes.Stfld) &&
			i.Operand is FieldReference f && f.Name == "__length") &&
			get.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f && f.Name == "__tracks"),
			"AnimationClip.length is the port's cached curve-length getter");
		Require(get.DeclaringType.Fields.Any(f => f.Name == "__events"), "AnimationClip keeps its events in __events");
		var body = get.Body;
		body.SimplifyMacros();
		var il = body.GetILProcessor();
		var rets = body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
		Require(rets.Count >= 1, "AnimationClip.length returns");
		foreach (var ret in rets)
		{
			// keep `ret` as the branch target: it becomes ldarg.0, followed by call + a new ret
			ret.OpCode = OpCodes.Ldarg_0; ret.Operand = null;
			var call = il.Create(OpCodes.Call, hook);
			il.InsertAfter(ret, call);
			il.InsertAfter(call, il.Create(OpCodes.Ret));
		}
		body.OptimizeMacros();
		Require(ClipLengthPatched(module), "clip length fix not installed");
	}

	private static bool ClipLengthPatched(ModuleDefinition module)
	{
		var ins = Method(module, "UnityEngine.AnimationClip", "get_length", 0).Body.Instructions;
		var rets = ins.Where(i => i.OpCode == OpCodes.Ret).ToList();
		return rets.Count > 0 && rets.All(r => r.Previous is { OpCode: var op, Operand: MethodReference m } && op == OpCodes.Call &&
			m.Name == "ClipLength" && m.DeclaringType.FullName == "Svz.AnimationFixes" && m.DeclaringType.Scope.Name == Enh);
	}


	// SUILabel.initSprite: `font = (Font)Resources.Load(basePath + fontFile, typeof(Font))` (first of its two
	// loads; the second is the Fonts/default fallback). Keep the loaded object on the stack and also pass it
	// to UiText.PrepareFont, which scales the 1536 set's glyph metrics once per Font.
	private static void PatchFontLoad(ModuleDefinition module, MethodReference hook)
	{
		var body = Method(module, "SUILabel", "initSprite", 1).Body;
		body.SimplifyMacros();
		var loads = body.Instructions.Where(i => Calls(i, "UnityEngine.Resources", "Load") &&
			((MethodReference)i.Operand).Parameters.Count == 2).ToList();
		Require(loads.Count == 2, "SUILabel.initSprite loads its font twice (selected set, then default)");
		Require(body.Instructions.Any(i => Calls(i, "FontSetFinder", "get_basePath")), "SUILabel.initSprite uses FontSetFinder.basePath");
		var il = body.GetILProcessor();
		var dup = il.Create(OpCodes.Dup);
		il.InsertAfter(loads[0], dup);
		il.InsertAfter(dup, il.Create(OpCodes.Call, hook));
		body.OptimizeMacros();
		Require(FontLoadPatched(module), "font load hook not installed");
	}

	private static bool FontLoadPatched(ModuleDefinition module) =>
		Method(module, "SUILabel", "initSprite", 1).Body.Instructions.Count(i => i.OpCode == OpCodes.Call &&
			i.Operand is MethodReference r && r.Name == "PrepareFont" && r.DeclaringType.FullName == "Svz.UiText" &&
			r.DeclaringType.Scope.Name == Enh) == 1;

	private static TypeDefinition GuiCanvas(ModuleDefinition module) =>
		(module.GetType("Svz.GuiSystem") ?? throw new PatchException("type Svz.GuiSystem not found"))
			.NestedTypes.SingleOrDefault(t => t.Name == "GuiCanvas") ?? throw new PatchException("Svz.GuiSystem/GuiCanvas not found");

	// GuiCanvas.DrawText draws each glyph with CanvasItem.DrawTextureRectRegion: route it through
	// UiText.DrawGlyph (same arguments, `this` first). GuiCanvas._Draw calls DrawText(t, sh) once per text:
	// call it once more before, between UiText.BeginHaloPass() and EndHaloPass(), so the halo is under all
	// glyphs of the label.
	private static void PatchTextHalo(ModuleDefinition module, TypeReference uiText)
	{
		var canvas = GuiCanvas(module);
		var drawText = canvas.Methods.SingleOrDefault(m => m.Name == "DrawText" && m.Parameters.Count == 2 &&
			m.Parameters[0].ParameterType.FullName == "UnityEngine.GUIText" && m.Parameters[1].ParameterType.FullName == "System.Single")
			?? throw new PatchException("GuiCanvas.DrawText(GUIText, float) not found");
		var body = drawText.Body;
		body.SimplifyMacros();
		var draws = body.Instructions.Where(i => Calls(i, "Godot.CanvasItem", "DrawTextureRectRegion")).ToList();
		Require(draws.Count == 1, "GuiCanvas.DrawText draws glyphs with one DrawTextureRectRegion call");
		var old = (MethodReference)draws[0].Operand;
		Require(old.HasThis && old.Parameters.Count == 6, "DrawTextureRectRegion(texture, rect, src, modulate, transpose, clipUv)");
		var glyph = new MethodReference("DrawGlyph", module.TypeSystem.Void, uiText) { HasThis = false };
		glyph.Parameters.Add(new ParameterDefinition(old.DeclaringType));
		foreach (var p in old.Parameters) glyph.Parameters.Add(new ParameterDefinition(p.ParameterType));
		draws[0].OpCode = OpCodes.Call;
		draws[0].Operand = glyph;
		body.OptimizeMacros();

		var draw = canvas.Methods.Single(m => m.Name == "_Draw" && m.Parameters.Count == 0).Body;
		draw.SimplifyMacros();
		var calls = draw.Instructions.Where(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference r && r.Name == "DrawText" &&
			r.DeclaringType.FullName == canvas.FullName && r.Parameters.Count == 2).ToList();
		Require(calls.Count == 1, "GuiCanvas._Draw calls DrawText once");
		var call = calls[0];
		var a2 = call.Previous; var a1 = a2?.Previous; var a0 = a1?.Previous;
		bool IsLoad(Instruction i) => i != null && (i.OpCode == OpCodes.Ldloc || i.OpCode == OpCodes.Ldarg);
		Require(a0 != null && a0.OpCode == OpCodes.Ldarg && a0.Operand == draw.ThisParameter && IsLoad(a1) && IsLoad(a2),
			"GuiCanvas._Draw: DrawText(this, local text, sh)");
		var il = draw.GetILProcessor();
		// When the optional outline is OFF, skip the entire extra DrawText traversal.
		// Otherwise the first pass would draw the original glyphs and the second pass would
		// draw them again, making letters look thicker and wasting GPU draw calls.
		// Preserve a0 as the original branch target; conditional false jumps to the
		// untouched regular DrawText invocation.
		var begin = new MethodReference("BeginHaloPass", module.TypeSystem.Boolean, uiText) { HasThis = false };
		var end = new MethodReference("EndHaloPass", module.TypeSystem.Void, uiText) { HasThis = false };
		var origThis = il.Create(OpCodes.Ldarg, draw.ThisParameter);
		var a1Op = a1.OpCode; var a1Arg = a1.Operand; var a2Op = a2.OpCode; var a2Arg = a2.Operand;
		a0.OpCode = OpCodes.Call; a0.Operand = begin;
		var seq = new[]
		{
			il.Create(OpCodes.Brfalse, origThis),
			il.Create(OpCodes.Ldarg, draw.ThisParameter), Clone(il, a1Op, a1Arg), Clone(il, a2Op, a2Arg),
			il.Create(OpCodes.Call, drawText), il.Create(OpCodes.Call, end), origThis,
		};
		var at = a0;
		foreach (var ins in seq) { il.InsertAfter(at, ins); at = ins; }
		draw.OptimizeMacros();
		Require(TextHaloPatched(module), "text halo pass not installed");
	}

	private static Instruction Clone(ILProcessor il, OpCode op, object operand) => operand switch
	{
		VariableDefinition v => il.Create(op, v),
		ParameterDefinition p => il.Create(op, p),
		_ => throw new PatchException("unexpected load operand in GuiCanvas._Draw"),
	};

	private static bool TextHaloPatched(ModuleDefinition module)
	{
		var canvas = GuiCanvas(module);
		bool CallsUi(MethodDefinition m, string name) => m.Body.Instructions.Any(i => i.OpCode == OpCodes.Call &&
			i.Operand is MethodReference r && r.Name == name && r.DeclaringType.FullName == "Svz.UiText" && r.DeclaringType.Scope.Name == Enh);
		var drawText = canvas.Methods.Single(m => m.Name == "DrawText" && m.Parameters.Count == 2);
		var draw = canvas.Methods.Single(m => m.Name == "_Draw" && m.Parameters.Count == 0);
		// The extra pass must be truly optional. Without this branch, disabling
		// contrast draws every original glyph twice even though it draws no halo.
		var begin = draw.Body.Instructions.FirstOrDefault(i => Calls(i, "Svz.UiText", "BeginHaloPass"));
		var skip = begin?.Next;
		bool guarded = skip != null &&
			(skip.OpCode == OpCodes.Brfalse || skip.OpCode == OpCodes.Brfalse_S) &&
			skip.Operand is Instruction target &&
			draw.Body.Instructions.Contains(target) &&
			draw.Body.Instructions.SkipWhile(i => i != target).Skip(1).Take(5).Any(i => Calls(i, canvas.FullName, "DrawText"));
		return CallsUi(drawText, "DrawGlyph") && CallsUi(draw, "BeginHaloPass") && CallsUi(draw, "EndHaloPass") && guarded &&
			!drawText.Body.Instructions.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
				i.Operand is MethodReference r && r.Name == "DrawTextureRectRegion");
	}


	// Render.SyncRenderer: Environment.GetEnvironmentVariable per particle renderer per frame;
	// Main.DebugAutomation: OS.GetCmdlineUserArgs() twice per frame. Both values never change.
	private static void PatchPerFrameLookups(ModuleDefinition module, TypeReference reuse)
	{
		var sync = Method(module, "Svz.Render", "SyncRenderer", 1).Body;
		var env = sync.Instructions.Where(i => Calls(i, "System.Environment", "GetEnvironmentVariable") &&
			((MethodReference)i.Operand).Parameters.Count == 1).ToList();
		Require(env.Count == 1, "Render.SyncRenderer reads one environment variable");
		var cachedEnv = new MethodReference("CachedEnvironmentVariable", module.TypeSystem.String, reuse) { HasThis = false };
		cachedEnv.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
		env[0].Operand = cachedEnv;

		var dbg = Method(module, "Svz.Main", "DebugAutomation", 1).Body;
		var args = dbg.Instructions.Where(i => Calls(i, "Godot.OS", "GetCmdlineUserArgs")).ToList();
		Require(args.Count >= 1, "Main.DebugAutomation reads the command line");
		var old = (MethodReference)args[0].Operand;
		var cachedArgs = new MethodReference("CmdlineUserArgs", old.ReturnType, reuse) { HasThis = false };
		foreach (var a in args) a.Operand = cachedArgs;
		Require(PerFrameLookupsPatched(module), "per-frame lookups not cached");
	}

	private static bool PerFrameLookupsPatched(ModuleDefinition module) =>
		!Method(module, "Svz.Render", "SyncRenderer", 1).Body.Instructions.Any(i => Calls(i, "System.Environment", "GetEnvironmentVariable")) &&
		!Method(module, "Svz.Main", "DebugAutomation", 1).Body.Instructions.Any(i => Calls(i, "Godot.OS", "GetCmdlineUserArgs")) &&
		Method(module, "Svz.Render", "SyncRenderer", 1).Body.Instructions.Any(i => i.Operand is MethodReference r && r.Name == "CachedEnvironmentVariable" && r.DeclaringType.Scope.Name == Enh);


	// MeshCache.Get / GetSubsets: `arrays[i] = value` -> MeshArrays.Set(arrays, i, value) (disposes the
	// Variant) and ArrayMesh.AddSurfaceFromArrays(...) -> MeshArrays.AddSurface(mesh, ...) (shared empty
	// defaults, disposes the arrays). Same arguments; `this` becomes the first one.
	private static readonly string[] MeshCacheMethods = { "Get", "GetSubsets" };

	private static void PatchMeshInterop(ModuleDefinition module, TypeReference hooks)
	{
		var type = module.GetType("Svz.MeshCache") ?? throw new PatchException("type Svz.MeshCache not found");
		int sets = 0, adds = 0;
		foreach (var name in MeshCacheMethods)
		{
			var m = type.Methods.SingleOrDefault(x => x.Name == name) ?? throw new PatchException("MeshCache." + name + " not found");
			foreach (var i in m.Body.Instructions)
			{
				if (i.OpCode != OpCodes.Callvirt || i.Operand is not MethodReference r) continue;
				string hook = r.DeclaringType.FullName == "Godot.Collections.Array" && r.Name == "set_Item" && r.Parameters.Count == 2 ? "Set"
					: r.DeclaringType.FullName == "Godot.ArrayMesh" && r.Name == "AddSurfaceFromArrays" && r.Parameters.Count == 5 ? "AddSurface" : null;
				if (hook == null) continue;
				var h = new MethodReference(hook, module.TypeSystem.Void, hooks) { HasThis = false };
				h.Parameters.Add(new ParameterDefinition(r.DeclaringType));
				foreach (var p in r.Parameters) h.Parameters.Add(new ParameterDefinition(p.ParameterType));
				i.OpCode = OpCodes.Call; i.Operand = h;
				if (hook == "Set") sets++; else adds++;
			}
		}
		Require(sets >= 1 && adds >= 2, $"MeshCache builds surfaces with Array[i] = v and AddSurfaceFromArrays (found {sets}, {adds})");
		Require(MeshInteropPatched(module), "mesh interop disposal not installed");
	}

	private static bool MeshInteropPatched(ModuleDefinition module)
	{
		var type = module.GetType("Svz.MeshCache");
		if (type == null) return false;
		var ins = type.Methods.Where(m => MeshCacheMethods.Contains(m.Name) && m.HasBody).SelectMany(m => m.Body.Instructions).ToList();
		return ins.Any(i => i.Operand is MethodReference r && r.Name == "AddSurface" && r.DeclaringType.FullName == "Svz.MeshArrays" && r.DeclaringType.Scope.Name == Enh) &&
			!ins.Any(i => i.Operand is MethodReference r && (r.Name == "AddSurfaceFromArrays" || (r.Name == "set_Item" && r.DeclaringType.FullName == "Godot.Collections.Array")));
	}


	private static bool SameSignatureType(TypeReference a, TypeReference b)
	{
		// Mono.Cecil uses source generic parameter names for definitions and
		// positional !!0 references for imported call sites. Names need not match.
		if (a is GenericParameter ap && b is GenericParameter bp)
			return ap.Position == bp.Position && ap.Type == bp.Type;
		if (a is ArrayType aa && b is ArrayType ba)
			return aa.Rank == ba.Rank && SameSignatureType(aa.ElementType, ba.ElementType);
		if (a is GenericInstanceType ai && b is GenericInstanceType bi)
			return ai.ElementType.FullName == bi.ElementType.FullName &&
				ai.GenericArguments.Count == bi.GenericArguments.Count &&
				ai.GenericArguments.Zip(bi.GenericArguments, SameSignatureType).All(v => v);
		return a.FullName == b.FullName;
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
				SameSignatureType(d.ReturnType, r.ReturnType) &&
				d.Parameters.Count == r.Parameters.Count &&
				d.Parameters.Zip(r.Parameters, (left, right) =>
					SameSignatureType(left.ParameterType, right.ParameterType)).All(v => v));
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
			("animation and particles use reusable snapshots", SnapshotsPatched(m)),
			("event-only animation clips last until their last event", ClipLengthPatched(m)),
			("HD fonts are scaled to the original text size", FontLoadPatched(m)),
			("GUI text is drawn with a contrast halo pass", TextHaloPatched(m)),
			("no per-frame environment / command-line lookups", PerFrameLookupsPatched(m)),
			("mesh rebuilds free their Godot interop temporaries", MeshInteropPatched(m)),
			("obsolete rating popup suppressed, victory rewards retained", LegacyRatingSuppressed(m)),
		};
		if (!quiet) foreach (var (what, ok) in checks) Console.WriteLine($"  [{(ok ? "OK" : "--")}] {what}");
		return checks.All(c => c.ok);
	}
}
