using System;
using System.Collections;
using System.Diagnostics;

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
        // Keep the signature float -> IEnumerator; nothing else about the
        // game's win coroutine needs to change.
        public static IEnumerator WaitRealSeconds(float originalGameSeconds)
        {
            return CountDown(WinDelayRealSeconds);
        }

        private static IEnumerator CountDown(double seconds)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < seconds)
                yield return null;
        }
    }
}
