using Godot;
using GArray = Godot.Collections.Array;

namespace Svz
{
	/// <summary>
	/// Cause of the crowded-wave garbage-collection hitches (measured on the real port, wave 70): MeshCache
	/// rebuilds every particle mesh every frame, and each surface left Godot interop objects for the finalizer:
	/// one Variant per vertex attribute (each owning a native copy of the array), the arrays Godot.Collections.Array,
	/// and an empty blend-shape array and LOD dictionary that the AddSurfaceFromArrays binding allocates for
	/// its null defaults. GodotSharp's DisposablesTracker held 100,000-450,000 of them between collections and
	/// each collection had to finalize them all (150-400 ms frames).
	///
	/// The patched MeshCache.Get/GetSubsets call these instead of Array[index] = value and
	/// ArrayMesh.AddSurfaceFromArrays: the temporaries are disposed as soon as Godot has its own copy, and the
	/// empty defaults are shared. Meshes and surfaces are the same.
	/// </summary>
	public static class MeshArrays
	{
		private static Godot.Collections.Array<GArray> sNoBlendShapes;
		private static Godot.Collections.Dictionary sNoLods;
		public static long DisposedVariants, DisposedArrays;

		/// arrays[index] = value; then free the temporary Variant (the array keeps its own reference).
		public static void Set(GArray arrays, int index, Variant value)
		{
			try { arrays[index] = value; }
			finally { value.Dispose(); DisposedVariants++; }
		}

		/// mesh.AddSurfaceFromArrays(...); then free the arrays (only ever built for this one call).
		public static void AddSurface(ArrayMesh mesh, Mesh.PrimitiveType primitive, GArray arrays,
			Godot.Collections.Array<GArray> blendShapes, Godot.Collections.Dictionary lods, Mesh.ArrayFormat flags)
		{
			try
			{
				mesh.AddSurfaceFromArrays(primitive, arrays, blendShapes ?? (sNoBlendShapes ??= new Godot.Collections.Array<GArray>()),
					lods ?? (sNoLods ??= new Godot.Collections.Dictionary()), flags);
			}
			finally
			{
				if (arrays != null) { arrays.Dispose(); DisposedArrays++; }
			}
		}
	}
}
