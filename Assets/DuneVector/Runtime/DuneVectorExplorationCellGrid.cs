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

        /// <summary>
        /// Number of 64x64 cell blocks currently held. Persistence is sized by this
        /// rather than by cell count, so it is the figure worth capping.
        /// </summary>
        public int ChunkCount => _chunks.Count;

        /// <summary>Cells along one edge of a chunk.</summary>
        public static int ChunkCellSpan => ChunkSize;

        public IEnumerable<KeyValuePair<long, ulong[]>> Chunks => _chunks;

        public static long PackChunkKey(int chunkX, int chunkZ)
        {
            return Pack(chunkX, chunkZ);
        }

        public static void UnpackChunkKey(long packedChunk, out int chunkX, out int chunkZ)
        {
            Unpack(packedChunk, out chunkX, out chunkZ);
        }

        public static long ChunkKeyForCell(long packedCell)
        {
            Unpack(packedCell, out int cellX, out int cellZ);
            return Pack(cellX >> ChunkShift, cellZ >> ChunkShift);
        }

        /// <summary>
        /// Adds a cell only when it lands in an existing chunk or the grid still has
        /// room for a new one. Returns false when the cell was already known or the
        /// chunk budget is spent, so callers can tell growth from a no-op.
        /// </summary>
        public bool Add(long packedCell, int maximumChunks, out bool blockedByCapacity)
        {
            blockedByCapacity = false;
            if (maximumChunks > 0 &&
                _chunks.Count >= maximumChunks &&
                !_chunks.ContainsKey(ChunkKeyForCell(packedCell)))
            {
                blockedByCapacity = true;
                return false;
            }
            return Add(packedCell);
        }

        /// <summary>
        /// Replaces a whole chunk, used when loading the bit-packed file straight
        /// into storage instead of replaying it one cell at a time.
        /// </summary>
        public void SetChunkRows(long packedChunk, ulong[] rows)
        {
            if (rows == null || rows.Length != ChunkSize)
            {
                throw new ArgumentException(
                    $"Exploration chunks must supply exactly {ChunkSize} rows.",
                    nameof(rows));
            }

            if (_chunks.TryGetValue(packedChunk, out ulong[] existing))
            {
                Count -= CountBits(existing);
            }
            _chunks[packedChunk] = rows;
            Count += CountBits(rows);
        }

        public bool RemoveChunk(long packedChunk)
        {
            if (!_chunks.TryGetValue(packedChunk, out ulong[] rows))
            {
                return false;
            }
            Count -= CountBits(rows);
            return _chunks.Remove(packedChunk);
        }

        /// <summary>
        /// Deep copy for background threads. Costs one array per chunk rather than
        /// one entry per explored cell, which is what keeps atlas builds affordable.
        /// </summary>
        public DuneVectorExplorationCellGrid CreateSnapshot()
        {
            DuneVectorExplorationCellGrid snapshot = new DuneVectorExplorationCellGrid();
            foreach (KeyValuePair<long, ulong[]> chunk in _chunks)
            {
                ulong[] rows = new ulong[ChunkSize];
                Array.Copy(chunk.Value, rows, ChunkSize);
                snapshot._chunks.Add(chunk.Key, rows);
            }
            snapshot.Count = Count;
            return snapshot;
        }

        private static int CountBits(ulong[] rows)
        {
            int total = 0;
            for (int index = 0; index < rows.Length; index++)
            {
                ulong row = rows[index];
                while (row != 0UL)
                {
                    row &= row - 1UL;
                    total++;
                }
            }
            return total;
        }

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
