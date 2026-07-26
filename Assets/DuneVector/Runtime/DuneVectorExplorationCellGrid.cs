using System;
using System.Collections;
using System.Collections.Generic;

namespace DuneVector
{
    /// <summary>
    /// Sparse bit-packed storage for persistent map exploration. Each dictionary
    /// entry covers 64x64 logical cells, keeping memory and lookup costs stable
    /// as the explored region grows.
    /// </summary>
    internal sealed class DuneVectorExplorationCellGrid : IEnumerable<long>
    {
        private const int ChunkShift = 6;
        private const int ChunkSize = 1 << ChunkShift;
        private const int ChunkMask = ChunkSize - 1;

        private readonly Dictionary<long, ulong[]> _chunks =
            new Dictionary<long, ulong[]>();

        public int Count { get; private set; }

        public bool Add(long packedCell)
        {
            Unpack(packedCell, out int cellX, out int cellZ);
            int chunkX = cellX >> ChunkShift;
            int chunkZ = cellZ >> ChunkShift;
            long packedChunk = Pack(chunkX, chunkZ);
            if (!_chunks.TryGetValue(packedChunk, out ulong[] rows))
            {
                rows = new ulong[ChunkSize];
                _chunks.Add(packedChunk, rows);
            }

            int localX = cellX & ChunkMask;
            int localZ = cellZ & ChunkMask;
            ulong bit = 1UL << localX;
            if ((rows[localZ] & bit) != 0UL)
            {
                return false;
            }

            rows[localZ] |= bit;
            Count++;
            return true;
        }

        public bool Contains(long packedCell)
        {
            Unpack(packedCell, out int cellX, out int cellZ);
            int chunkX = cellX >> ChunkShift;
            int chunkZ = cellZ >> ChunkShift;
            if (!_chunks.TryGetValue(Pack(chunkX, chunkZ), out ulong[] rows))
            {
                return false;
            }

            int localX = cellX & ChunkMask;
            int localZ = cellZ & ChunkMask;
            return (rows[localZ] & (1UL << localX)) != 0UL;
        }

        public void Clear()
        {
            _chunks.Clear();
            Count = 0;
        }

        public void CopyTo(long[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (destination.Length < Count)
            {
                throw new ArgumentException(
                    "The destination is smaller than the explored-cell count.",
                    nameof(destination));
            }

            int destinationIndex = 0;
            foreach (long packedCell in this)
            {
                destination[destinationIndex++] = packedCell;
            }
        }

        public IEnumerator<long> GetEnumerator()
        {
            foreach (KeyValuePair<long, ulong[]> chunk in _chunks)
            {
                Unpack(chunk.Key, out int chunkX, out int chunkZ);
                int baseCellX = chunkX << ChunkShift;
                int baseCellZ = chunkZ << ChunkShift;
                ulong[] rows = chunk.Value;
                for (int localZ = 0; localZ < ChunkSize; localZ++)
                {
                    ulong row = rows[localZ];
                    if (row == 0UL)
                    {
                        continue;
                    }
                    for (int localX = 0; localX < ChunkSize; localX++)
                    {
                        ulong bit = 1UL << localX;
                        if ((row & bit) != 0UL)
                        {
                            yield return Pack(
                                baseCellX + localX,
                                baseCellZ + localZ);
                        }
                    }
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private static long Pack(int x, int z)
        {
            return ((long)x << 32) | (uint)z;
        }

        private static void Unpack(long packed, out int x, out int z)
        {
            x = (int)(packed >> 32);
            z = unchecked((int)(uint)packed);
        }
    }
}
