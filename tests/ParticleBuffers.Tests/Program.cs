using System;
using Svz;

static class Test
{
    private static int sPass;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        ++sPass;
    }

    public static void Main()
    {
        int[] first = ParticleBuffers.EnsureSize<int>(null, 60);
        first[0] = 42;
        var same = ParticleBuffers.EnsureSize(first, 60);
        Check(ReferenceEquals(first, same) && same[0] == 42,
            "same-size array reused without losing values");
        var grown = ParticleBuffers.EnsureSize(first, 90);
        Check(!ReferenceEquals(first, grown) && grown.Length == 90 && grown[0] == 0,
            "new size allocates a fresh buffer without stale values");
        var otherEmitter = ParticleBuffers.EnsureSize<int>(null, 60);
        Check(!ReferenceEquals(otherEmitter, first),
            "buffers do not get shared across emitters");
        var zero = ParticleBuffers.EnsureSize<int>(grown, 0);
        Check(zero.Length == 0 && !ReferenceEquals(zero, grown),
            "no particles releases the previous large mesh array");
        Check(ParticleBuffers.IsAlreadyEmpty(true, 0, 1, 0),
            "previously cleared mesh remains empty");
        Check(!ParticleBuffers.IsAlreadyEmpty(false, 0, 1, 0),
            "fresh uninitialized mesh must be processed at least once");
        Check(!ParticleBuffers.IsAlreadyEmpty(true, 4, 1, 6),
            "last particle death requires a zero-vertex upload");
        Check(!ParticleBuffers.IsAlreadyEmpty(true, 0, 0, 0),
            "unexpected submesh topology cannot be silently skipped");
        bool throws = false;
        try { ParticleBuffers.EnsureSize<int>(null, -1); }
        catch (ArgumentOutOfRangeException) { throws = true; }
        Check(throws, "invalid negative size is rejected");
        Console.WriteLine($"[test] {sPass} particle mesh reuse assertions passed");
    }
}
