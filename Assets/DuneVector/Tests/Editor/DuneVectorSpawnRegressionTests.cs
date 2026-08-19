using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DuneVector.Tests
{
    public sealed class DuneVectorSpawnRegressionTests
    {
        private const int RegressionWorldSeed = 47169;
        private const int PortalRegressionWorldSeed = 49109;
        private const float Stage2DistanceScale = 0.2f;
        private const string RuntimeSettingsPath =
            "Assets/DuneVector/ScriptableObjects/Dune Vector Runtime Settings.asset";

        [Test]
        public void StormPyramidRecurringCadenceVariesByEnemyAndAttack()
        {
            MethodInfo cadencePhase = typeof(StormPyramidEnemy).GetMethod(
                "EvaluateAttackCadencePhase",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(cadencePhase, Is.Not.Null);

            float firstEnemyFirstAttack = (float)cadencePhase.Invoke(null, new object[] { 0, 0 });
            float secondEnemyFirstAttack = (float)cadencePhase.Invoke(null, new object[] { 1, 0 });
            float firstEnemySecondAttack = (float)cadencePhase.Invoke(null, new object[] { 0, 1 });

            Assert.That(secondEnemyFirstAttack, Is.Not.EqualTo(firstEnemyFirstAttack).Within(0.0001f));
            Assert.That(firstEnemySecondAttack, Is.Not.EqualTo(firstEnemyFirstAttack).Within(0.0001f));
            Assert.That(firstEnemyFirstAttack, Is.InRange(0f, 1f));
            Assert.That(secondEnemyFirstAttack, Is.InRange(0f, 1f));
            Assert.That(firstEnemySecondAttack, Is.InRange(0f, 1f));
        }

        [Test]
        public void FullyFramedAirborneGlyphOutranksHigherScoringOrdinarySubject()
        {
            Assert.That(
                InvokeSubjectSelectionReplacement(
                    candidateIsPreferredGlyph: true,
                    candidateScore: 0.2f,
                    foundSelection: true,
                    selectionIsPreferredGlyph: false,
                    selectionScore: 1f),
                Is.True);
            Assert.That(
                InvokeSubjectSelectionReplacement(
                    candidateIsPreferredGlyph: false,
                    candidateScore: 1f,
                    foundSelection: true,
                    selectionIsPreferredGlyph: true,
                    selectionScore: 0.2f),
                Is.False);
        }

        [Test]
        public void ExistingCameraScoreStillChoosesWithinSamePriorityTier()
        {
            Assert.That(
                InvokeSubjectSelectionReplacement(
                    candidateIsPreferredGlyph: false,
                    candidateScore: 0.8f,
                    foundSelection: true,
                    selectionIsPreferredGlyph: false,
                    selectionScore: 0.7f),
                Is.True);
            Assert.That(
                InvokeSubjectSelectionReplacement(
                    candidateIsPreferredGlyph: true,
                    candidateScore: 0.6f,
                    foundSelection: true,
                    selectionIsPreferredGlyph: true,
                    selectionScore: 0.7f),
                Is.False);
        }

        [Test]
        public void Seed47169Stage2DeploymentResolvesActiveColliderSupport()
        {
            DuneVectorRuntimeSettings settings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            Assert.That(settings, Is.Not.Null);

            GameObject worldObject = new GameObject("Seed 47169 Spawn Regression World");
            DuneVectorMaterials materials = null;
            try
            {
                DesertWorldStreamer world = worldObject.AddComponent<DesertWorldStreamer>();
                ConfigureWorld(world, settings);
                materials = new DuneVectorMaterials(settings);
                world.Initialize(materials);

                LogicalPosition routeOrigin = ResolveStage2RouteOrigin(settings.Contracts);
                StageDestinationChunkInactive(world, routeOrigin);
                Assert.That(
                    world.IsVisualTerrainReady(routeOrigin),
                    Is.False,
                    "The regression setup must reproduce a staged destination whose root is inactive.");

                bool prepared = world.TryPreparePlayerTeleportDestination(
                    routeOrigin,
                    settings.WorldHub.DesertInsertionHeight,
                    settings.WorldHub.DeploymentMaximumGroundSlope,
                    out Vector3 supportedPosition);

                Assert.That(prepared, Is.True, "Seed 47169 must resolve final collider support before deployment.");
                Assert.That(world.IsVisualTerrainReady(routeOrigin), Is.True);
                Assert.That(
                    world.HasPreparedTerrainSupport(
                        supportedPosition,
                        settings.WorldHub.DeploymentGroundSupportDistance,
                        settings.WorldHub.DeploymentMaximumGroundSlope),
                    Is.True,
                    "The resolved spawn must retain authoritative terrain support.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
            }
        }

        [Test]
        public void PlayerRelativeEnemyRingsStayOutsideTheirOwnAttackRange()
        {
            DuneVectorRuntimeSettings settings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            Assert.That(settings, Is.Not.Null);

            DuneVectorEnemySpawnClearance.Clear();
            DuneVectorEnemySpawnClearance.Configure(settings.EnemySpawnSafety);
            DuneVectorEnemyEngagementRing.Configure(settings.EnemySpawnSafety);
            GameObject worldObject = new GameObject("Enemy Ring Spawn Horizon Test World");

            try
            {
                DesertWorldStreamer world = worldObject.AddComponent<DesertWorldStreamer>();
                world.ChunkSize = settings.DuneChunkSize;
                world.PreloadRadius = settings.WorldStreaming.PreloadRadius;
                world.EnableCameraFrustumTerrainStreaming =
                    settings.WorldStreaming.EnableCameraFrustumTerrainStreaming;
                world.CameraFrustumMaximumDistance =
                    settings.WorldStreaming.CameraFrustumMaximumDistance;
                AssertAuthoredRings(settings, world);
                Assert.That(
                    DuneVectorEnemyEngagementRing.ResolveDesertDeploymentDistance(0f),
                    Is.EqualTo(settings.EnemySpawnSafety.DesertDeploymentMinimumEnemyDistance)
                        .Within(0.001f),
                    "The first deployment enemy must use the authored inner edge.");
                Assert.That(
                    DuneVectorEnemyEngagementRing.ResolveDesertDeploymentDistance(1f),
                    Is.EqualTo(settings.EnemySpawnSafety.DesertDeploymentMaximumEnemyDistance)
                        .Within(0.001f),
                    "The one-time desert deployment spawn must use the authored close-range cap.");
                Assert.That(
                    DuneVectorEnemyEngagementRing.ResolveDesertDeploymentDistance(0, 3),
                    Is.EqualTo(settings.EnemySpawnSafety.DesertDeploymentMinimumEnemyDistance)
                        .Within(0.001f));
                Assert.That(
                    DuneVectorEnemyEngagementRing.ResolveDesertDeploymentDistance(1, 3),
                    Is.EqualTo(Mathf.Sqrt(
                        ((settings.EnemySpawnSafety.DesertDeploymentMinimumEnemyDistance *
                          settings.EnemySpawnSafety.DesertDeploymentMinimumEnemyDistance) +
                         (settings.EnemySpawnSafety.DesertDeploymentMaximumEnemyDistance *
                          settings.EnemySpawnSafety.DesertDeploymentMaximumEnemyDistance)) * 0.5f))
                        .Within(0.001f));
                Assert.That(
                    DuneVectorEnemyEngagementRing.ResolveDesertDeploymentDistance(2, 3),
                    Is.EqualTo(settings.EnemySpawnSafety.DesertDeploymentMaximumEnemyDistance)
                        .Within(0.001f),
                    "Deployment slots must span the complete authored distance band.");
                Assert.That(
                    DuneVectorEnemyEngagementRing.ResolveDesertDeploymentDistance(2, 3, world),
                    Is.EqualTo(settings.WorldStreaming.CameraFrustumMaximumDistance)
                        .Within(0.001f),
                    "The deployment outer edge must match the loaded terrain and portal horizon.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                DuneVectorEnemyEngagementRing.ResetToDefaults();
            }
        }

        private static void AssertAuthoredRings(
            DuneVectorRuntimeSettings settings,
            DesertWorldStreamer world)
        {
            AssertRingClearsAttackRange(
                "Storm Pyramid",
                settings.StormPyramids.MinimumSpawnDistance,
                settings.StormPyramids.MaximumSpawnDistance,
                settings.StormPyramids.RepositionDistance,
                settings.StormPyramids.DetectionRange,
                world);
            AssertRingClearsAttackRange(
                "Strike Ring",
                settings.PlayerStrikeOrbs.MinimumSpawnDistance,
                settings.PlayerStrikeOrbs.MaximumSpawnDistance,
                settings.PlayerStrikeOrbs.RepositionDistance,
                settings.PlayerStrikeOrbs.EvaluateDetectionRange(
                    settings.PlayerStrikeOrbs.DetectionRangeRankCeiling),
                world);
            AssertRingClearsAttackRange(
                "Vesper Kite",
                settings.VesperKites.MinimumSpawnDistance,
                settings.VesperKites.MaximumSpawnDistance,
                settings.VesperKites.RepositionDistance,
                settings.VesperKites.DetectionRange,
                world);
        }

        private static void AssertRingClearsAttackRange(
            string enemyName,
            float authoredMinimum,
            float authoredMaximum,
            float authoredRepositionDistance,
            float attackRange,
            DesertWorldStreamer world)
        {
            float minimum = DuneVectorEnemyEngagementRing.ResolveMinimumDistance(
                authoredMinimum,
                attackRange,
                world);
            float maximum = DuneVectorEnemyEngagementRing.ResolveMaximumDistance(
                minimum,
                authoredMinimum,
                authoredMaximum);
            float repositionDistance = DuneVectorEnemyEngagementRing.ResolveRepositionDistance(
                authoredRepositionDistance,
                maximum);

            Assert.That(
                minimum,
                Is.GreaterThan(attackRange),
                $"{enemyName} must appear outside its own attack range so it can engage from full range.");
            Assert.That(
                minimum,
                Is.GreaterThanOrEqualTo(world.ChunkSize * world.PreloadRadius),
                $"{enemyName} must appear at least as far away as streamed traversal rings.");
            Assert.That(
                maximum,
                Is.GreaterThanOrEqualTo(minimum),
                $"{enemyName} must keep a non-inverted spawn band.");
            Assert.That(
                maximum - minimum,
                Is.EqualTo(Mathf.Max(0f, authoredMaximum - authoredMinimum)).Within(0.001f),
                $"{enemyName} must keep its authored spawn band width after the ring is pushed out.");
            Assert.That(
                repositionDistance,
                Is.GreaterThan(maximum),
                $"{enemyName} must not reposition an enemy that was just placed on its outer ring.");
        }

        [Test]
        public void Seed49109DeploymentRechecksPortalClearanceAfterStreaming()
        {
            DuneVectorRuntimeSettings settings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            Assert.That(settings, Is.Not.Null);

            GameObject worldObject = new GameObject("Seed 49109 Portal Spawn Regression World");
            DuneVectorMaterials materials = null;
            try
            {
                DesertWorldStreamer world = worldObject.AddComponent<DesertWorldStreamer>();
                ConfigureWorld(world, settings, PortalRegressionWorldSeed);
                materials = new DuneVectorMaterials(settings);
                world.Initialize(materials);

                LogicalPosition routeOrigin = ResolveRouteOrigin(
                    settings.Contracts,
                    PortalRegressionWorldSeed);
                LogicalPosition initialResolution = world.ResolvePlayerSpawnAwayFromObstacles(
                    routeOrigin,
                    Vector3.back);
                Assert.That(initialResolution.X, Is.EqualTo(routeOrigin.X).Within(0.001d));
                Assert.That(initialResolution.Z, Is.EqualTo(routeOrigin.Z).Within(0.001d));

                int latePortalHandle = DuneVectorWorldOccupancy.Register(
                    routeOrigin.X,
                    routeOrigin.Z,
                    settings.Rings.PortalMinimumVisualRadius,
                    WorldOccupancyKind.Portal);

                bool prepared;
                LogicalPosition resolvedPosition;
                Vector3 supportedPosition;
                try
                {
                    prepared = world.TryPreparePlayerTeleportDestinationClearOfObstacles(
                        routeOrigin,
                        Vector3.back,
                        settings.WorldHub.DesertInsertionHeight,
                        settings.WorldHub.DeploymentMaximumGroundSlope,
                        settings.WorldHub.DeploymentGroundRetryCount,
                        out resolvedPosition,
                        out supportedPosition);
                }
                finally
                {
                    DuneVectorWorldOccupancy.Release(latePortalHandle);
                }

                Assert.That(prepared, Is.True, "Seed 49109 must find a supported portal-clear deployment point.");
                Assert.That(
                    Math.Abs(resolvedPosition.X - routeOrigin.X) > 0.001d ||
                    Math.Abs(resolvedPosition.Z - routeOrigin.Z) > 0.001d,
                    Is.True,
                    "The regression setup must reproduce the portal forcing seed 49109 away from its initial point.");
                double portalDeltaX = resolvedPosition.X - routeOrigin.X;
                double portalDeltaZ = resolvedPosition.Z - routeOrigin.Z;
                double requiredPortalClearance = settings.Rings.PortalMinimumVisualRadius +
                    settings.Rings.MinimumDroneSpawnSeparation;
                Assert.That(
                    Math.Sqrt((portalDeltaX * portalDeltaX) + (portalDeltaZ * portalDeltaZ)),
                    Is.GreaterThanOrEqualTo(requiredPortalClearance - 0.001d));
                LogicalPosition clearanceCheck = world.ResolvePlayerSpawnAwayFromObstacles(
                    resolvedPosition,
                    Vector3.back);
                Assert.That(clearanceCheck.X, Is.EqualTo(resolvedPosition.X).Within(0.001d));
                Assert.That(clearanceCheck.Z, Is.EqualTo(resolvedPosition.Z).Within(0.001d));
                Assert.That(
                    world.HasPreparedTerrainSupport(
                        supportedPosition,
                        settings.WorldHub.DeploymentGroundSupportDistance,
                        settings.WorldHub.DeploymentMaximumGroundSlope),
                    Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
            }
        }

        [Test]
        public void DeploymentReservesItsPointAgainstLaterPortalGeneration()
        {
            DuneVectorRuntimeSettings settings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            Assert.That(settings, Is.Not.Null);
            Assert.That(
                settings.Rings.MinimumDroneSpawnSeparation,
                Is.GreaterThan(0f),
                "The authored drone spawn separation is what reserves the deployment point.");

            GameObject worldObject = new GameObject("Deployment Portal Reservation World");
            DuneVectorMaterials materials = null;
            try
            {
                DesertWorldStreamer world = worldObject.AddComponent<DesertWorldStreamer>();
                ConfigureWorld(world, settings, PortalRegressionWorldSeed);
                materials = new DuneVectorMaterials(settings);
                world.Initialize(materials);

                LogicalPosition deployment = new LogicalPosition(4130d, -2870d);
                Assert.That(
                    DuneVectorWorldOccupancy.Overlaps(
                        deployment.X,
                        deployment.Z,
                        0f,
                        WorldOccupancyKind.PlayerDeployment),
                    Is.False,
                    "Nothing may reserve the deployment point before the drone deploys.");

                world.ReservePlayerDeploymentAgainstPortals(deployment);
                try
                {
                    // A portal generated by a chunk that streams in after the teleport asks this
                    // exact question before it is created, so the reservation must answer it.
                    Assert.That(
                        DuneVectorWorldOccupancy.Overlaps(
                            deployment.X,
                            deployment.Z,
                            0f,
                            WorldOccupancyKind.PlayerDeployment),
                        Is.True,
                        "A portal opening on the deployment point must be rejected.");
                    double justOutside = settings.Rings.MinimumDroneSpawnSeparation + 0.01d;
                    Assert.That(
                        DuneVectorWorldOccupancy.Overlaps(
                            deployment.X + justOutside,
                            deployment.Z,
                            0f,
                            WorldOccupancyKind.PlayerDeployment),
                        Is.False,
                        "The reservation must not reach past the authored drone spawn separation.");
                }
                finally
                {
                    world.ClearPlayerDeploymentPortalReservation();
                }

                Assert.That(
                    DuneVectorWorldOccupancy.Overlaps(
                        deployment.X,
                        deployment.Z,
                        0f,
                        WorldOccupancyKind.PlayerDeployment),
                    Is.False,
                    "Returning to the hub must release the deployment reservation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
            }
        }

        private static void ConfigureWorld(
            DesertWorldStreamer world,
            DuneVectorRuntimeSettings settings)
        {
            ConfigureWorld(world, settings, RegressionWorldSeed);
        }

        private static void ConfigureWorld(
            DesertWorldStreamer world,
            DuneVectorRuntimeSettings settings,
            int worldSeed)
        {
            world.WorldSeed = worldSeed;
            world.Dunes = JsonUtility.FromJson<DuneFieldSettings>(
                JsonUtility.ToJson(settings.DuneGeneration));
            world.Dunes.WorldSeed = worldSeed;
            world.ChunkSize = settings.DuneChunkSize;
            world.ChunkResolution = settings.DuneMeshResolution;
            world.CollisionMeshResolution = settings.WorldStreaming.CollisionMeshResolution;
            world.Rings = JsonUtility.FromJson<RingTuning>(JsonUtility.ToJson(settings.Rings));
            world.Rings.GroundRingDensityPerChunk = 0f;
            world.Rings.AerialRingDensityPerChunk = 0f;
            world.Rings.HealthRingDensityPerChunk = 0f;
            world.Rings.CoinRingDensityPerChunk = 0f;
            world.Clouds = settings.Clouds;
            world.Shrubs = settings.DesertShrubs;
            world.Landmarks = settings.Landmarks;
            world.Cacti = settings.Cacti;
            world.Obelisks = settings.Obelisks;
            world.DarkPyramids = settings.DarkPyramids;
            world.Pyramid2 = settings.Pyramid2;
            world.Geoglyphs = settings.Geoglyphs;
            // These tests exercise terrain and portal placement only.
            world.GroundExploders = JsonUtility.FromJson<GroundExploderTuning>(
                JsonUtility.ToJson(settings.GroundExploders));
            world.GroundExploders.Enabled = false;
        }

        private static LogicalPosition ResolveStage2RouteOrigin(CourierContractTuning contracts)
        {
            // Stage 2's distance scale changes the delivery leg, not the insertion origin. Keeping
            // the constant here records the complete evaluation reproduction alongside the seed.
            Assert.That(Stage2DistanceScale, Is.EqualTo(0.2f));
            return ResolveRouteOrigin(contracts, RegressionWorldSeed);
        }

        private static LogicalPosition ResolveRouteOrigin(
            CourierContractTuning contracts,
            int worldSeed)
        {
            var offerRandom = new System.Random(worldSeed ^ contracts.ContractSeedOffset);
            int contractSeed = offerRandom.Next();
            var routeRandom = new System.Random(contractSeed);
            double angle = routeRandom.NextDouble() * Math.PI * 2.0;
            float distance = Mathf.Lerp(
                contracts.MinimumRouteOriginDistance,
                Mathf.Max(contracts.MinimumRouteOriginDistance, contracts.MaximumRouteOriginDistance),
                (float)routeRandom.NextDouble());
            return new LogicalPosition(
                DesertWorldStreamer.StartingLogicalPosition.x + (Math.Cos(angle) * distance),
                DesertWorldStreamer.StartingLogicalPosition.y + (Math.Sin(angle) * distance));
        }

        private static void StageDestinationChunkInactive(
            DesertWorldStreamer world,
            LogicalPosition destination)
        {
            var coordinate = new Vector2Int(
                Mathf.FloorToInt((float)(destination.X / world.ChunkSize)),
                Mathf.FloorToInt((float)(destination.Z / world.ChunkSize)));
            MethodInfo generateChunk = typeof(DesertWorldStreamer).GetMethod(
                "GenerateChunkImmediate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(generateChunk, Is.Not.Null);
            generateChunk.Invoke(world, new object[] { coordinate, true, true });
        }

        private static bool InvokeSubjectSelectionReplacement(
            bool candidateIsPreferredGlyph,
            float candidateScore,
            bool foundSelection,
            bool selectionIsPreferredGlyph,
            float selectionScore)
        {
            Type detectorType = typeof(PhotographyTuning).Assembly.GetType(
                "DuneVector.DuneVectorSubjectDetector");
            Assert.That(detectorType, Is.Not.Null);
            MethodInfo method = detectorType.GetMethod(
                "ShouldReplaceSelection",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (bool)method.Invoke(
                null,
                new object[]
                {
                    candidateIsPreferredGlyph,
                    candidateScore,
                    foundSelection,
                    selectionIsPreferredGlyph,
                    selectionScore,
                });
        }
    }
}
