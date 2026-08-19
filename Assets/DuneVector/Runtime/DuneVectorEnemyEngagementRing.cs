using UnityEngine;

namespace DuneVector
{
    /// <summary>
    /// Keeps player-relative enemy spawn and reposition rings outside the enemy's own attack range.
    /// Enemies live in a bubble around the drone, so an authored ring that sits inside the attack
    /// range makes the enemy materialise already inside its engagement envelope: it never gets to
    /// acquire the drone from full range, and the drone never sees it close the distance.
    /// </summary>
    public static class DuneVectorEnemyEngagementRing
    {
        private const float DefaultAttackRangeMargin = 1.2f;
        private const float DefaultRepositionHeadroom = 1.15f;
        private const float DefaultDesertDeploymentMaximumDistance = 360f;

        /// <summary>Fraction of the attack range kept clear beyond it when placing an enemy.</summary>
        public static float AttackRangeMargin { get; private set; } = DefaultAttackRangeMargin;

        /// <summary>How far past the outer ring the reposition threshold must sit.</summary>
        public static float RepositionHeadroom { get; private set; } = DefaultRepositionHeadroom;

        /// <summary>Outer edge of the one-time enemy band used when arriving from the hub.</summary>
        public static float DesertDeploymentMaximumDistance { get; private set; } =
            DefaultDesertDeploymentMaximumDistance;

        /// <summary>Applies the authored clearance margins. Call once while building the world.</summary>
        public static void Configure(EnemySpawnSafetyTuning settings)
        {
            AttackRangeMargin = settings != null
                ? Mathf.Max(0.1f, settings.EnemyAttackRangeMargin)
                : DefaultAttackRangeMargin;
            RepositionHeadroom = settings != null
                ? Mathf.Max(1f, settings.EnemyRepositionHeadroom)
                : DefaultRepositionHeadroom;
            DesertDeploymentMaximumDistance = settings != null
                ? Mathf.Max(0f, settings.DesertDeploymentMaximumEnemyDistance)
                : DefaultDesertDeploymentMaximumDistance;
        }

        /// <summary>Restores the built-in margins. Used by tests between cases.</summary>
        public static void ResetToDefaults()
        {
            AttackRangeMargin = DefaultAttackRangeMargin;
            RepositionHeadroom = DefaultRepositionHeadroom;
            DesertDeploymentMaximumDistance = DefaultDesertDeploymentMaximumDistance;
        }

        /// <summary>
        /// Places an enemy in the close band used only while completing a hub-to-desert teleport.
        /// The protected player clearance radius remains the inner edge.
        /// </summary>
        public static float ResolveDesertDeploymentDistance(float distance01)
        {
            float minimumDistance = DuneVectorEnemySpawnClearance.ApplyMinimumDistance(0f);
            float maximumDistance = Mathf.Max(
                minimumDistance,
                DesertDeploymentMaximumDistance);
            return Mathf.Lerp(minimumDistance, maximumDistance, Mathf.Clamp01(distance01));
        }

        /// <summary>
        /// Raises the authored inner ring so it clears both the drone deployment point and the
        /// enemy's attack range.
        /// </summary>
        public static float ResolveMinimumDistance(float authoredMinimum, float attackRange)
        {
            return Mathf.Max(
                DuneVectorEnemySpawnClearance.ApplyMinimumDistance(authoredMinimum),
                Mathf.Max(0f, attackRange) * AttackRangeMargin);
        }

        /// <summary>
        /// Raises the enemy ring to the same radial horizon used to preload streamed traversal
        /// rings. This keeps persistent aerial enemies from materialising inside an area whose
        /// traversal rings have already been visible to the player for several seconds.
        /// </summary>
        public static float ResolveMinimumDistance(
            float authoredMinimum,
            float attackRange,
            DesertWorldStreamer world)
        {
            float traversalRingSpawnDistance = world != null
                ? Mathf.Max(0f, world.ChunkSize) * Mathf.Max(1, world.PreloadRadius)
                : 0f;
            return Mathf.Max(
                ResolveMinimumDistance(authoredMinimum, attackRange),
                traversalRingSpawnDistance);
        }

        /// <summary>
        /// Rebuilds the outer ring from a resolved inner ring, preserving the authored band width so
        /// pushing the ring out never collapses its spread.
        /// </summary>
        public static float ResolveMaximumDistance(
            float minimumDistance,
            float authoredMinimum,
            float authoredMaximum)
        {
            float authoredWidth = Mathf.Max(0f, authoredMaximum - authoredMinimum);
            return minimumDistance + authoredWidth;
        }

        /// <summary>
        /// Raises the authored reposition threshold above the outer ring so an enemy placed at the
        /// far edge is not immediately repositioned again.
        /// </summary>
        public static float ResolveRepositionDistance(
            float authoredRepositionDistance,
            float maximumDistance)
        {
            return Mathf.Max(authoredRepositionDistance, maximumDistance * RepositionHeadroom);
        }
    }
}
