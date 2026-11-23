using System;

namespace Svz
{
    /// <summary>
    /// A mesh owned by a ParticleRenderer is rebuilt synchronously every frame.
    /// Reuse equal-sized managed buffers instead of allocating vertex/UV/color/index
    /// arrays for every particle effect on every frame. The GPU still gets new data.
    /// This does not share arrays between different emitters and never changes counts.
    /// </summary>
    public static class ParticleBuffers
    {
        public static T[] EnsureSize<T>(T[] existing, int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            return existing != null && existing.Length == length ? existing : new T[length];
        }

        public static bool IsAlreadyEmpty(bool loaded, int vertexCount, int submeshCount, int indexCount) =>
            loaded && vertexCount == 0 && submeshCount == 1 && indexCount == 0;
    }
}
