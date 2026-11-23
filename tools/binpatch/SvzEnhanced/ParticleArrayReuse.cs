using System;

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
    }
}
