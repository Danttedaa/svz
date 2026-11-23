using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;

namespace Svz
{
    /// <summary>
    /// The restored game's Unity WaitForSeconds is measured in game time.
    /// A victory sets Time.timeScale to 0.1 and ramps it back up, turning
    /// even a one-second game-time wait into several seconds in real time.
    /// This nested IEnumerator is supported by CoroutineRunner.Step and
    /// never touches rewards, profile saves or the scene-exit callback.
    /// </summary>
    public static class VictoryTiming
    {
        public const double WinDelayRealSeconds = 1.0;

        // Accept the original configured time left on the iterator's IL stack.
        // Kept for SvZPort.dll files patched by M4..M8 builds.
        public static IEnumerator WaitRealSeconds(float originalGameSeconds)
        {
            return CountDown(WinDelayRealSeconds);
        }

        /// <summary>
        /// Wait used by the patched InGameImpl.PostWinDelay (M9): one real second after a normal win,
        /// but the game's own configured time (postWinTime, as real seconds) after the one-time story
        /// ending of classic wave 70, so its "EndGameMessage" is not cut off. <paramref name="game"/>
        /// is the InGameImpl instance; any failure to read the game state keeps the fast wait.
        /// </summary>
        public static IEnumerator WaitAfterWin(float originalGameSeconds, object game)
        {
            bool ending;
            try { ending = IsStoryEnding(game); } catch (Exception) { ending = false; }
            double seconds = ending ? Math.Clamp(originalGameSeconds, WinDelayRealSeconds, 15.0) : WinDelayRealSeconds;
            return CountDown(seconds);
        }

        /// The same condition InGameImpl.CreateWinDialog uses to show "Layouts/EndGameMessage":
        /// classic mode, not a bonus wave, waveToBeat == 70 and wave 70 at level 1.
        internal static Func<object, bool> IsStoryEnding = DefaultIsStoryEnding;

        private static bool DefaultIsStoryEnding(object game)
        {
            try
            {
                if (game == null) return false;
                var asm = game.GetType().Assembly;
                object Instance(string typeName)
                {
                    var t = asm.GetType(typeName);
                    var single = t == null ? null : asm.GetType("Singleton`1")?.MakeGenericType(t);
                    return single?.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                }
                var profile = Instance("Profile");
                var modes = Instance("PlayModesManager");
                if (profile == null || modes == null) return false;
                var pt = profile.GetType();
                if (pt.GetProperty("inBonusWave")?.GetValue(profile) is not bool bonus || bonus) return false;
                if (pt.GetProperty("waveToBeat")?.GetValue(profile) is not int wave || wave != 70) return false;
                if (pt.GetMethod("GetWaveLevel", new[] { typeof(int) })?.Invoke(profile, new object[] { 70 }) is not int level || level != 1) return false;
                return modes.GetType().GetProperty("selectedMode")?.GetValue(modes) as string == "classic";
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static IEnumerator CountDown(double seconds)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < seconds)
                yield return null;
        }
    }
}
