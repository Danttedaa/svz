using System;
using System.Collections.Generic;

namespace Svz
{
    /// <summary>
    /// Reuses the last mesh-owned array when a particle system produces the same
    /// vertex count on successive frames. The caller still rewrites ALL elements
    /// before the Godot mesh upload. Nothing is shared between particle renderers.
    /// </summary>
    public static class ParticleArrayReuse
    {
        public static T[] Reuse<T>(int length, T[] previous)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            return previous != null && previous.Length == length ? previous : new T[length];
        }

        /// <summary>
        /// Reuse only the single index buffer belonging to this mesh's previous
        /// submesh. No global pooling, no cross-emitter aliases, no stale lengths.
        /// </summary>
        public static int[] ReuseIndices(int length, List<int[]> priorSubmeshes)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            int[] previous = priorSubmeshes != null && priorSubmeshes.Count == 1
                ? priorSubmeshes[0] : null;
            return previous != null && previous.Length == length ? previous : new int[length];
        }
    }
}
