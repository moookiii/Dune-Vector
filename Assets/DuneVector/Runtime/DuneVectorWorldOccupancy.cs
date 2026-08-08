using System.Collections.Generic;
using UnityEngine;

namespace DuneVector
{
    public enum WorldOccupancyKind
    {
        Structure = 0,
        Portal = 1,
    }

    /// <summary>
    /// World-space registry of solid scenery footprints and portal clearance spheres.
    /// Chunks, buildings, and portals stream in independently, so per-chunk exclusion
    /// lists cannot see each other across a chunk border. Every solid mesh registers a
    /// logical-space footprint here and every portal registers the sphere it sweeps
    /// through while it faces the camera, so whichever spawns second can yield.
    /// </summary>
    public static class DuneVectorWorldOccupancy
    {
        private const double CellSize = 64d;
        private const int KindCount = 2;

        private sealed class Entry
        {
            public double X;
            public double Z;
            public float Radius;
            public WorldOccupancyKind Kind;
            public Vector2Int Cell;
        }

        private static readonly Dictionary<int, Entry> EntriesByHandle = new Dictionary<int, Entry>();
        private static readonly Dictionary<Vector2Int, List<int>> HandlesByCell =
            new Dictionary<Vector2Int, List<int>>();
        private static readonly float[] LargestRadiusByKind = new float[KindCount];
        private static int _nextHandle = 1;

        public const int InvalidHandle = 0;

        public static int Register(double logicalX, double logicalZ, float radius, WorldOccupancyKind kind)
        {
            radius = Mathf.Max(0f, radius);
            Entry entry = new Entry
            {
                X = logicalX,
                Z = logicalZ,
                Radius = radius,
                Kind = kind,
                Cell = ToCell(logicalX, logicalZ),
            };

            int handle = _nextHandle++;
            EntriesByHandle.Add(handle, entry);
            if (!HandlesByCell.TryGetValue(entry.Cell, out List<int> handles))
            {
                handles = new List<int>(8);
                HandlesByCell.Add(entry.Cell, handles);
            }
            handles.Add(handle);

            int kindIndex = (int)kind;
            if (radius > LargestRadiusByKind[kindIndex])
            {
                LargestRadiusByKind[kindIndex] = radius;
            }
            return handle;
        }

        public static void Release(int handle)
        {
            if (handle == InvalidHandle || !EntriesByHandle.TryGetValue(handle, out Entry entry))
            {
                return;
            }

            EntriesByHandle.Remove(handle);
            if (HandlesByCell.TryGetValue(entry.Cell, out List<int> handles))
            {
                handles.Remove(handle);
                if (handles.Count == 0)
                {
                    HandlesByCell.Remove(entry.Cell);
                }
            }
        }

        public static void ReleaseAll(List<int> handles)
        {
            if (handles == null)
            {
                return;
            }

            for (int i = 0; i < handles.Count; i++)
            {
                Release(handles[i]);
            }
            handles.Clear();
        }

        public static bool Overlaps(
            double logicalX,
            double logicalZ,
            float radius,
            WorldOccupancyKind kind)
        {
            radius = Mathf.Max(0f, radius);
            double reach = radius + LargestRadiusByKind[(int)kind];
            if (reach <= 0d)
            {
                return false;
            }

            Vector2Int center = ToCell(logicalX, logicalZ);
            int cellReach = Mathf.Max(1, Mathf.CeilToInt((float)(reach / CellSize)));
            for (int z = -cellReach; z <= cellReach; z++)
            {
                for (int x = -cellReach; x <= cellReach; x++)
                {
                    if (!HandlesByCell.TryGetValue(
                            new Vector2Int(center.x + x, center.y + z),
                            out List<int> handles))
                    {
                        continue;
                    }

                    for (int i = 0; i < handles.Count; i++)
                    {
                        if (!EntriesByHandle.TryGetValue(handles[i], out Entry entry) ||
                            entry.Kind != kind)
                        {
                            continue;
                        }

                        double deltaX = entry.X - logicalX;
                        double deltaZ = entry.Z - logicalZ;
                        double separation = entry.Radius + radius;
                        if ((deltaX * deltaX) + (deltaZ * deltaZ) < separation * separation)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public static void Clear()
        {
            EntriesByHandle.Clear();
            HandlesByCell.Clear();
            for (int i = 0; i < KindCount; i++)
            {
                LargestRadiusByKind[i] = 0f;
            }
            _nextHandle = 1;
        }

        private static Vector2Int ToCell(double logicalX, double logicalZ)
        {
            return new Vector2Int(
                Mathf.FloorToInt((float)(logicalX / CellSize)),
                Mathf.FloorToInt((float)(logicalZ / CellSize)));
        }
    }
}
