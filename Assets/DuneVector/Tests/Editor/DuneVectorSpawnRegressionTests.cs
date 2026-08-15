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
        private const float Stage2DistanceScale = 0.2f;
        private const string RuntimeSettingsPath =
            "Assets/DuneVector/ScriptableObjects/Dune Vector Runtime Settings.asset";

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
                materials?.Dispose();
                UnityEngine.Object.DestroyImmediate(worldObject);
            }
        }

        private static void ConfigureWorld(
            DesertWorldStreamer world,
            DuneVectorRuntimeSettings settings)
        {
            world.WorldSeed = RegressionWorldSeed;
            world.Dunes = JsonUtility.FromJson<DuneFieldSettings>(
                JsonUtility.ToJson(settings.DuneGeneration));
            world.Dunes.WorldSeed = RegressionWorldSeed;
            world.ChunkSize = settings.DuneChunkSize;
            world.ChunkResolution = settings.DuneMeshResolution;
            world.CollisionMeshResolution = settings.WorldStreaming.CollisionMeshResolution;
            world.Rings = settings.Rings;
            world.Clouds = settings.Clouds;
            world.Shrubs = settings.DesertShrubs;
            world.Landmarks = settings.Landmarks;
            world.Cacti = settings.Cacti;
            world.Obelisks = settings.Obelisks;
            world.DarkPyramids = settings.DarkPyramids;
            world.Pyramid2 = settings.Pyramid2;
            world.Geoglyphs = settings.Geoglyphs;
            world.GroundExploders = settings.GroundExploders;
        }

        private static LogicalPosition ResolveStage2RouteOrigin(CourierContractTuning contracts)
        {
            // Stage 2's distance scale changes the delivery leg, not the insertion origin. Keeping
            // the constant here records the complete evaluation reproduction alongside the seed.
            Assert.That(Stage2DistanceScale, Is.EqualTo(0.2f));
            var offerRandom = new System.Random(RegressionWorldSeed ^ contracts.ContractSeedOffset);
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
    }
}
