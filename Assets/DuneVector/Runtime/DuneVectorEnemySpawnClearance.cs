using UnityEngine;

namespace DuneVector
{
    /// <summary>
    /// Keeps a radius around the drone's desert deployment point free of enemies so contract
    /// and free roam arrivals never drop the player on top of a threat.
    /// </summary>
    public static class DuneVectorEnemySpawnClearance
    {
        public static bool HasSpawnPoint { get; private set; }
        public static float Radius { get; private set; }

        private static double _logicalX;
        private static double _logicalZ;

        /// <summary>Applies the authored clearance radius. Call once while building the world.</summary>
        public static void Configure(EnemySpawnSafetyTuning settings)
        {
            Radius = settings != null ? Mathf.Max(0f, settings.PlayerSpawnClearanceRadius) : 0f;
            HasSpawnPoint = HasSpawnPoint && Radius > 0f;
        }

        public static void SetSpawnPoint(double logicalX, double logicalZ)
        {
            _logicalX = logicalX;
            _logicalZ = logicalZ;
            HasSpawnPoint = Radius > 0f;
        }

        public static void Clear()
        {
            HasSpawnPoint = false;
        }

        /// <summary>True when the logical ground position sits inside the protected spawn radius.</summary>
        public static bool IsBlocked(double logicalX, double logicalZ)
        {
            if (!HasSpawnPoint)
            {
                return false;
            }

            double dx = logicalX - _logicalX;
            double dz = logicalZ - _logicalZ;
            return (dx * dx) + (dz * dz) < Radius * (double)Radius;
        }

        /// <summary>Raises a player-relative spawn distance so it clears the protected radius.</summary>
        public static float ApplyMinimumDistance(float minimumDistance)
        {
            return Mathf.Max(minimumDistance, Radius);
        }
    }
}
