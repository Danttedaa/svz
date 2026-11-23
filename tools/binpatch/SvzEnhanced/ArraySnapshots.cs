using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Svz
{
	/// <summary>
	/// Reuses an exact-length array per List<T> without changing iteration order.
	/// Applied only to three known synchronous foreach loops, not to lists whose
	/// snapshots escape their calling method. This avoids per-frame allocations
	/// from animation and particle simulation on crowded waves.
	/// </summary>
	public static class ArraySnapshots
	{
		private sealed class Holder<T> { public T[] Buffer; }
		private static class Cache<T>
		{
			internal static readonly ConditionalWeakTable<List<T>, Holder<T>> Table = new();
		}

		public static T[] Snapshot<T>(List<T> source)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			Holder<T> holder = Cache<T>.Table.GetValue(source, static _ => new Holder<T>());
			int count = source.Count;
			if (holder.Buffer == null || holder.Buffer.Length != count)
				holder.Buffer = new T[count];
			source.CopyTo(holder.Buffer, 0);
			return holder.Buffer;
		}
	}
}
