using System.Collections.Generic;

namespace Svz
{
	/// <summary>
	/// Per-frame lookups of values that never change while the game runs (found while profiling crowded waves).
	/// </summary>
	public static class Reuse
	{
		private static readonly Dictionary<string, string> sEnv = new();
		private static string[] sArgs;

		/// Render.SyncRenderer read the SVZ_NOPARTICLES debug variable once per particle renderer per frame.
		/// The process environment does not change while the game runs: read each variable once.
		public static string CachedEnvironmentVariable(string name)
		{
			if (name == null) return null;
			if (!sEnv.TryGetValue(name, out var v)) sEnv[name] = v = System.Environment.GetEnvironmentVariable(name);
			return v;
		}

		/// Main.DebugAutomation fetched OS.GetCmdlineUserArgs() (a new array through interop) twice per frame
		/// to look for PC debug flags. The command line does not change: fetch it once.
		public static string[] CmdlineUserArgs() => sArgs ??= Godot.OS.GetCmdlineUserArgs();
	}
}
