using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DuneVector.Tests
{
    public sealed class DuneVectorSpawnRegressionTests
    {
        private sealed class FakeMassiveCloudParameter
        {
            public bool RelativeHeight;
            public float FromHeight;
            public float ToHeight;
        }

        [Test]
        public void CourierProgress_CompletingDeliveryMessageKeepsPostContractPresentationPending()
        {
            string saveDirectory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"DuneVectorPostContractTest-{Guid.NewGuid():N}");
            string savePath = System.IO.Path.Combine(saveDirectory, "CourierProgress.dat");
            System.IO.Directory.CreateDirectory(saveDirectory);
            GameObject progressObject = new GameObject("Courier Progress Regression Test");

            try
            {
                DuneVectorCourierProgress progress =
                    progressObject.AddComponent<DuneVectorCourierProgress>();
                typeof(DuneVectorCourierProgress)
                    .GetField("_savePath", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(progress, savePath);

                progress.RecordCompletion(100, 1, assignDeliveryMessage: true);
                Assert.That(progress.PostContractPresentationPending, Is.True);
                Assert.That(progress.PendingDeliveryMessageIndex, Is.Zero);

                Assert.That(progress.CompletePendingDeliveryMessage(0), Is.True);
                Assert.That(
                    progress.PostContractPresentationPending,
                    Is.True,
                    "Finishing the pre-rail message must not consume the post-rail hub notices.");

                progress.CompletePostContractPresentation();
                Assert.That(progress.PostContractPresentationPending, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(progressObject);
                if (System.IO.Directory.Exists(saveDirectory))
                {
                    System.IO.Directory.Delete(saveDirectory, recursive: true);
                }
            }
        }

        [TestCase(0, 100f)]
        [TestCase(10, 150f)]
        [TestCase(20, 200f)]
        [TestCase(30, 200f)]
        public void RouteEncounterSkyPiercer_ShotRangeScalesAcrossRisk(
            int risk,
            float expectedRange)
        {
            var settings = new RouteEncounterTuning
            {
                ShotRangeAtRiskZero = 100f,
                ShotRangeAtRiskCeiling = 200f,
                ShotRangeRiskCeiling = 20,
            };

            Assert.That(settings.EvaluateShotRange(risk), Is.EqualTo(expectedRange).Within(0.001f));
        }

        [Test]
        public void RuntimeSettings_DunesRetainLightInsideWorldShadows()
        {
            DuneVectorRuntimeSettings settings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);

            Assert.That(settings, Is.Not.Null);
            Assert.That(
                settings.DuneMinimumShadowAttenuation,
                Is.GreaterThan(0f),
                "WORLD dune shadow attenuation must stay above zero or built terrain can render black beneath large shadow casters.");
        }

        [Test]
        public void RailShooter_RestingAimMapsFlightBoundsIntoCenteredHalfScreenRegion()
        {
            Vector2 bounds = new Vector2(28f, 15f);

            Assert.That(
                DuneVectorRailShooterController.CalculateRestingAimViewport(
                    Vector2.zero,
                    bounds,
                    0.5f),
                Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(
                DuneVectorRailShooterController.CalculateRestingAimViewport(
                    new Vector2(-bounds.x, bounds.y),
                    bounds,
                    0.5f),
                Is.EqualTo(new Vector2(0.25f, 0.75f)));
        }

        [Test]
        public void Audio_RailPlaylistIncludesSky2kWithRequestedMusicPlayerLabel()
        {
            DuneVectorRuntimeSettings runtimeSettings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            AudioTuning settings = runtimeSettings != null ? runtimeSettings.Audio : null;

            Assert.That(settings, Is.Not.Null);
            MusicPlaylistTrack sky2k = null;
            MusicPlaylistTrack[] tracks = settings.RailSubgameMusicPlaylist;
            for (int i = 0; tracks != null && i < tracks.Length; i++)
            {
                MusicPlaylistTrack track = tracks[i];
                if (track != null && string.Equals(track.FmodEventPath, "event:/sky2k", StringComparison.Ordinal))
                {
                    sky2k = track;
                    break;
                }
            }

            Assert.That(sky2k, Is.Not.Null);
            Assert.That(sky2k.DisplayName, Is.EqualTo("dreamloader - sky2k"));
        }

        [Test]
        public void RailShooter_EnemyHitUsesDroneDamageEvent()
        {
            DuneVectorRuntimeSettings runtimeSettings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            RailShooterTuning settings = runtimeSettings != null ? runtimeSettings.RailShooter : null;

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.EnemyHitEvent, Is.EqualTo("event:/Drone_Damage"));
        }

        [Test]
        public void RailShooter_BossPulseRetainsAuthoredVisualScale()
        {
            Vector3 authoredScale = Vector3.one * 5.5f;

            Assert.That(
                DuneVectorRailShooterController.CalculateBossVisualScale(authoredScale, 1f),
                Is.EqualTo(authoredScale));
            Vector3 pulsedScale =
                DuneVectorRailShooterController.CalculateBossVisualScale(authoredScale, 1.2f);
            Assert.That(pulsedScale.x, Is.EqualTo(6.6f).Within(0.0001f));
            Assert.That(pulsedScale.y, Is.EqualTo(6.6f).Within(0.0001f));
            Assert.That(pulsedScale.z, Is.EqualTo(6.6f).Within(0.0001f));
        }

        [Test]
        public void RailShooter_ScreenSpacePlayAreaUsesOneViewport()
        {
            Vector2 halfExtents =
                DuneVectorRailShooterController.CalculateScreenSpaceFlightHalfExtents(
                    13.5f,
                    68f,
                    4f / 3f,
                    1f);
            float expectedVerticalHalfExtent =
                13.5f * Mathf.Tan(68f * Mathf.Deg2Rad * 0.5f);

            Assert.That(halfExtents.y, Is.EqualTo(expectedVerticalHalfExtent).Within(0.001f));
            Assert.That(halfExtents.x, Is.EqualTo(expectedVerticalHalfExtent * (4f / 3f)).Within(0.001f));
        }

        [Test]
        public void RailShooter_RingPlacementUsesTheDronePlayBoundary()
        {
            Vector2 oneScreen =
                DuneVectorRailShooterController.CalculateScreenSpaceFlightHalfExtents(
                    13.5f,
                    68f,
                    4f / 3f,
                    1f);
            Vector2 ringBoundary =
                DuneVectorRailShooterController.CalculateScreenSpaceRingPlacementHalfExtents(
                    13.5f,
                    68f,
                    4f / 3f,
                    3f);

            Assert.That(ringBoundary.x, Is.EqualTo(oneScreen.x * 3f).Within(0.001f));
            Assert.That(ringBoundary.y, Is.EqualTo(oneScreen.y * 3f).Within(0.001f));
        }

        [Test]
        public void RailShooter_CameraPanStopsWhenTheViewportReachesThePlayAreaEdge()
        {
            Vector2 cameraOffset =
                DuneVectorRailShooterController.CalculateScreenSpaceCameraOffset(
                    new Vector2(20f, -20f),
                    new Vector2(5f, 3f),
                    1f);

            Assert.That(cameraOffset, Is.EqualTo(new Vector2(5f, -3f)));
            Assert.That(
                DuneVectorRailShooterController.CalculateScreenSpaceCameraOffset(
                    new Vector2(20f, -20f),
                    new Vector2(5f, 3f),
                    0f),
                Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void RailShooter_BoostSuppressesCameraShakeDuringBoundaryPan()
        {
            Vector3 shakeSample = new Vector3(0.8f, -0.6f, 0.25f);

            Assert.That(
                DuneVectorRailShooterController.CalculateRailCameraShakeOffset(
                    true,
                    shakeSample,
                    2f),
                Is.EqualTo(Vector3.zero));
            Assert.That(
                DuneVectorRailShooterController.CalculateRailCameraShakeOffset(
                    false,
                    shakeSample,
                    2f),
                Is.EqualTo(shakeSample * 2f));
        }

        [Test]
        public void RailShooter_MovementSmoothingIsAuthored()
        {
            DuneVectorRuntimeSettings runtimeSettings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            RailShooterTuning settings = runtimeSettings != null ? runtimeSettings.RailShooter : null;

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.MovementSmoothing, Is.GreaterThan(0f));
        }

        [Test]
        public void RailShooter_CoinPickupFacesTheScreenFacingPortal()
        {
            DuneVectorRuntimeSettings runtimeSettings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            RailShooterTuning settings = runtimeSettings != null ? runtimeSettings.RailShooter : null;

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.PickupCoinEulerAngles, Is.EqualTo(new Vector3(90f, 0f, 0f)));
        }

        [Test]
        public void RailShooter_CoinRingsMatchRecurringHealthRingSize()
        {
            DuneVectorRuntimeSettings runtimeSettings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            RailShooterTuning settings = runtimeSettings != null ? runtimeSettings.RailShooter : null;

            Assert.That(settings, Is.Not.Null);
            Assert.That(
                DuneVectorRailShooterController.CalculateRailPickupRingRadius(
                    true,
                    settings.PickupRadius,
                    settings.GateRadius),
                Is.EqualTo(settings.GateRadius).Within(0.001f));
        }

        [Test]
        public void RailShooter_SoftBoundaryDoesNotMoveAStationaryDrone()
        {
            Vector2 velocity = Vector2.zero;
            Vector2 position = new Vector2(9.5f, -4.5f);

            Vector2 bounded = DuneVectorRailShooterController.CalculateSoftBoundedFlightOffset(
                position,
                ref velocity,
                1f / 60f,
                new Vector2(10f, 5f),
                2f);

            Assert.That(bounded, Is.EqualTo(position));
            Assert.That(velocity, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void RailShooter_SoftBoundarySlowsOnlyOutwardMotion()
        {
            Vector2 outwardVelocity = new Vector2(12f, 0f);
            Vector2 inwardVelocity = new Vector2(-12f, 0f);
            Vector2 position = new Vector2(9f, 0f);

            Vector2 outward = DuneVectorRailShooterController.CalculateSoftBoundedFlightOffset(
                position,
                ref outwardVelocity,
                1f / 60f,
                new Vector2(10f, 5f),
                2f);
            Vector2 inward = DuneVectorRailShooterController.CalculateSoftBoundedFlightOffset(
                position,
                ref inwardVelocity,
                1f / 60f,
                new Vector2(10f, 5f),
                2f);

            Assert.That(outward.x - position.x, Is.LessThan(12f / 60f));
            Assert.That(inward.x - position.x, Is.EqualTo(-12f / 60f).Within(0.0001f));
            Assert.That(outwardVelocity.x, Is.LessThan(12f));
            Assert.That(inwardVelocity.x, Is.EqualTo(-12f).Within(0.0001f));
        }

        [Test]
        public void RailShooter_AimReticlesUseSeparateAuthoredDistancesAlongAimRay()
        {
            Vector3 origin = new Vector3(4f, -2f, 8f);
            Vector3 aimDirection = new Vector3(1f, 0.5f, 3f).normalized;

            DuneVectorRailShooterController.CalculateAimReticleWorldPositions(
                origin,
                aimDirection,
                35f,
                105f,
                out Vector3 nearPosition,
                out Vector3 farPosition);

            Assert.That(Vector3.Distance(origin, nearPosition), Is.EqualTo(35f).Within(0.001f));
            Assert.That(Vector3.Distance(origin, farPosition), Is.EqualTo(105f).Within(0.001f));
            Assert.That(Vector3.Cross(nearPosition - origin, aimDirection).sqrMagnitude,
                Is.LessThan(0.0001f));
            Assert.That(Vector3.Cross(farPosition - origin, aimDirection).sqrMagnitude,
                Is.LessThan(0.0001f));
        }

        [Test]
        public void RailShooter_NeutralManeuverInputStartsABarrelRoll()
        {
            Assert.That(
                DuneVectorRailShooterController.ResolveTrickForMove(Vector2.zero),
                Is.EqualTo(RailShooterTrick.BarrelRollRight));
        }

        [Test]
        public void RailShooter_MassiveCloudHeightsRestoreAfterSubgameOverride()
        {
            var layer = new FakeMassiveCloudParameter
            {
                RelativeHeight = false,
                FromHeight = 920f,
                ToHeight = 1695f,
            };
            System.Collections.IList parameters = new System.Collections.ArrayList { layer };
            var snapshots = new System.Collections.Generic.List<
                DuneVectorRailShooterController.MassiveCloudParameterSnapshot>();

            DuneVectorRailShooterController.CaptureAndOverrideMassiveCloudParameters(
                parameters,
                snapshots,
                -2188.507f,
                -891.1193f);
            DuneVectorRailShooterController.RestoreMassiveCloudParameterValues(parameters, snapshots);

            Assert.That(layer.RelativeHeight, Is.False);
            Assert.That(layer.FromHeight, Is.EqualTo(920f).Within(0.001f));
            Assert.That(layer.ToHeight, Is.EqualTo(1695f).Within(0.001f));
        }

        [Test]
        public void RailShooter_AimProjectionIgnoresPresentationCameraShake()
        {
            Vector3 cameraBasePosition = new Vector3(10f, 5f, -20f);
            Vector2 viewport = new Vector2(0.68f, 0.37f);
            Vector3 worldPoint = DuneVectorRailShooterController.CalculateViewportWorldPoint(
                cameraBasePosition,
                Quaternion.identity,
                viewport,
                80f,
                60f,
                16f / 9f);

            bool visible = DuneVectorRailShooterController.TryCalculateWorldGuiPosition(
                worldPoint,
                cameraBasePosition,
                Quaternion.identity,
                60f,
                16f / 9f,
                new Vector2(1920f, 1080f),
                out Vector2 guiPosition);

            Assert.That(visible, Is.True);
            Assert.That(guiPosition.x, Is.EqualTo(viewport.x * 1920f).Within(0.001f));
            Assert.That(guiPosition.y, Is.EqualTo((1f - viewport.y) * 1080f).Within(0.001f));
        }

        [Test]
        public void RailShooter_NormalBulletsFollowAimWithoutLockSteering()
        {
            Vector3 aimDirection = new Vector3(2f, -1f, 7f);

            Vector3 shotDirection =
                DuneVectorRailShooterController.ResolveRegularShotDirection(aimDirection);

            Assert.That(Vector3.Angle(shotDirection, aimDirection), Is.LessThan(0.001f));
        }

        [Test]
        public void RailShooter_LockAcquisitionWaitsForChargedShotThreshold()
        {
            const float minimumChargeDuration = 0.7f;

            Assert.That(
                DuneVectorRailShooterController.CanAcquireChargeLock(0.699f, minimumChargeDuration),
                Is.False);
            Assert.That(
                DuneVectorRailShooterController.CanAcquireChargeLock(0.7f, minimumChargeDuration),
                Is.True);
        }

        [Test]
        public void RailShooter_SigilDrawingGuideIsThickAndWhite()
        {
            DuneVectorRuntimeSettings runtimeSettings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            RailSigilTuning settings = runtimeSettings != null ? runtimeSettings.RailShooter.Sigils : null;

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.DrawingGuideThickness, Is.EqualTo(8f).Within(0.001f));
            Assert.That(settings.DrawingGuideColor.r, Is.EqualTo(1f).Within(0.001f));
            Assert.That(settings.DrawingGuideColor.g, Is.EqualTo(1f).Within(0.001f));
            Assert.That(settings.DrawingGuideColor.b, Is.EqualTo(1f).Within(0.001f));
            Assert.That(settings.DrawingGuideColor.a, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void RailShooter_BlackNavigationRingsGrantATimedBoost()
        {
            DuneVectorRuntimeSettings runtimeSettings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            RailShooterTuning settings = runtimeSettings != null ? runtimeSettings.RailShooter : null;

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.BlackRingBoostSpeedMultiplier, Is.GreaterThan(1f));
            Assert.That(settings.BlackRingBoostDuration, Is.GreaterThan(0f));
        }

        [Test]
        public void RailShooter_FlightAndUpperFlightRingColorsAreSwapped()
        {
            DuneVectorRuntimeSettings runtimeSettings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            RailShooterTuning railSettings = runtimeSettings != null ? runtimeSettings.RailShooter : null;
            RingTuning ringSettings = runtimeSettings != null ? runtimeSettings.Rings : null;

            Assert.That(railSettings, Is.Not.Null);
            Assert.That(ringSettings, Is.Not.Null);
            Assert.That(
                railSettings.NavigationRingColor.r,
                Is.EqualTo(ringSettings.UpperFlightRingEmissionColor.r).Within(0.001f));
            Assert.That(
                railSettings.NavigationRingColor.g,
                Is.EqualTo(ringSettings.UpperFlightRingEmissionColor.g).Within(0.001f));
            Assert.That(
                railSettings.NavigationRingColor.b,
                Is.EqualTo(ringSettings.UpperFlightRingEmissionColor.b).Within(0.001f));
            Assert.That(railSettings.NavigationRingColor.a, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void RailShooter_HalfOfNavigationRingSlotsBecomeHealthRings()
        {
            DuneVectorRuntimeSettings runtimeSettings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            RailShooterTuning settings = runtimeSettings != null ? runtimeSettings.RailShooter : null;

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.NavigationHealthRingFraction, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(settings.HealthRingKeptFraction, Is.EqualTo(1f).Within(0.001f));
            Assert.That(settings.SpawnNonHealthNavigationRings, Is.False);

            int ringCount = Mathf.Max(1, settings.EnvironmentSegmentCount);
            int healthRingCount = 0;
            for (int i = 0; i < ringCount; i++)
            {
                if (DuneVectorRailShooterController.ShouldUseRailHealthRing(
                        i,
                        ringCount,
                        settings.NavigationHealthRingFraction))
                {
                    healthRingCount++;
                }
            }

            Assert.That(healthRingCount, Is.EqualTo(Mathf.RoundToInt(ringCount * 0.5f)));
        }

        [Test]
        public void RailShooter_BlackNavigationRingPassUsesTheRingOpening()
        {
            Assert.That(
                DuneVectorRailShooterController.HasPassedRailNavigationRing(
                    new Vector3(0f, 0f, -10f),
                    new Vector3(0f, 0f, 10f),
                    Vector3.zero,
                    2f),
                Is.True);
            Assert.That(
                DuneVectorRailShooterController.HasPassedRailNavigationRing(
                    new Vector3(4f, 0f, -10f),
                    new Vector3(4f, 0f, 10f),
                    Vector3.zero,
                    2f),
                Is.False);
        }

        [Test]
        public void PlayerTuning_MovementSmoothingIsAuthoredForDesertAndHub()
        {
            DuneVectorRuntimeSettings runtimeSettings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            DroneTuning settings = runtimeSettings != null ? runtimeSettings.PlayerTuning : null;

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.MovementSmoothing, Is.GreaterThan(0f));
            Assert.That(
                settings.MovementSmoothing,
                Is.EqualTo(8.5f).Within(0.001f),
                "The shared grounded drone response must remain authored for desert and hub traversal.");
        }

        [Test]
        public void FreeRoam_WarpGatesAreAuthoredAsRareBottomEntryDiscoveries()
        {
            DuneVectorRuntimeSettings runtimeSettings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            WarpGateTuning settings = runtimeSettings != null ? runtimeSettings.WarpGates : null;
            RingTuning ringSettings = runtimeSettings != null ? runtimeSettings.Rings : null;

            Assert.That(settings, Is.Not.Null);
            Assert.That(ringSettings, Is.Not.Null);
            Assert.That(settings.Enabled, Is.True);
            Assert.That(settings.PrefabResourcePath, Is.EqualTo("WarpGatePrefab"));
            Assert.That(settings.SpawnChancePerChunk, Is.GreaterThan(0f));
            Assert.That(
                !settings.ClampHeightToUpperFlightRingBand ||
                (settings.GateHeightAboveTerrain >= ringSettings.UpperFlightRingMinimumHeight &&
                 settings.GateHeightAboveTerrain <= ringSettings.UpperFlightRingMaximumHeight),
                Is.True,
                "A clamped gate height must already sit inside the second-flight-ring altitude band.");
            Assert.That(
                settings.GateHeightAboveTerrain,
                Is.EqualTo(112.5f).Within(0.001f),
                "Warp gates should sit midway through the second-flight-ring altitude band.");
        }

        [Test]
        public void FreeRoam_WarpGateOnlyAcceptsAnUpwardBottomCrossing()
        {
            Assert.That(
                DuneVectorWarpGate.HasBottomEntryCrossing(
                    new Vector3(0f, -5f, 0f),
                    new Vector3(0f, 5f, 0f),
                    Vector3.zero,
                    Vector3.up,
                    2f),
                Is.True);
            Assert.That(
                DuneVectorWarpGate.HasBottomEntryCrossing(
                    new Vector3(0f, 5f, 0f),
                    new Vector3(0f, -5f, 0f),
                    Vector3.zero,
                    Vector3.up,
                    2f),
                Is.False);
            Assert.That(
                DuneVectorWarpGate.HasBottomEntryCrossing(
                    new Vector3(3f, -5f, 0f),
                    new Vector3(3f, 5f, 0f),
                    Vector3.zero,
                    Vector3.up,
                    2f),
                Is.False);
        }

        [Test]
        public void RailShooter_BlackRouteGateIsOnlyAvailableOnce()
        {
            Assert.That(
                DuneVectorRailShooterController.CanShowBlackRouteGate(0),
                Is.True);
            Assert.That(
                DuneVectorRailShooterController.CanShowBlackRouteGate(1),
                Is.False);
            Assert.That(
                DuneVectorRailShooterController.CanShowBlackRouteGate(2),
                Is.False);
        }


        [Test]
        public void RailShooter_CombatStartsAtFiftyMetersWithoutRouteGates()
        {
            DuneVectorRuntimeSettings runtimeSettings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            RailShooterTuning settings = runtimeSettings != null ? runtimeSettings.RailShooter : null;

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.FirstWaveDistance, Is.EqualTo(50f).Within(0.001f));
            Assert.That(settings.BranchGateCount, Is.Zero);
        }

        [Test]
        public void RailShooter_SatelliteFieldCoversThePreRebaseSquare()
        {
            DuneVectorRuntimeSettings runtimeSettings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            RailShooterTuning settings = runtimeSettings != null ? runtimeSettings.RailShooter : null;

            Assert.That(settings, Is.Not.Null);
            Assert.That(
                settings.FlightRebaseDistance,
                Is.EqualTo(100f).Within(0.001f),
                "The rail floating origin should rebase at the requested 100-unit lateral extent.");
            Assert.That(
                settings.SatellitePlaneHalfExtent,
                Is.GreaterThanOrEqualTo(settings.FlightRebaseDistance),
                "The satellite field must cover the complete XY area available before a lateral rebase.");
            Assert.That(
                settings.ScreenSpacePlayAreaMultiplier,
                Is.EqualTo(3f).Within(0.001f));
            Assert.That(
                settings.CameraLateralFollowFraction,
                Is.EqualTo(1f).Within(0.001f),
                "The rail camera must pan laterally with the drone during the subgame.");
        }

        [Test]
        public void RailShooter_TemporaryHullDepletionDoesNotKillPersistentPlayer()
        {
            GameObject player = new GameObject("Rail Shooter Health Test");
            try
            {
                DroneHealth health = player.AddComponent<DroneHealth>();
                health.Initialize(100f, 0f);
                bool died = false;
                bool temporaryPoolDepleted = false;
                health.Died += () => died = true;
                health.TemporaryHealthPoolDepleted += () => temporaryPoolDepleted = true;

                Assert.That(health.BeginTemporaryHealthPool(40f), Is.True);
                Assert.That(health.TakeDamage(40f, "test"), Is.True);
                Assert.That(temporaryPoolDepleted, Is.True);
                Assert.That(died, Is.False);

                Assert.That(health.EndTemporaryHealthPool(), Is.True);
                Assert.That(health.CurrentHealth, Is.EqualTo(100f));
                Assert.That(health.MaximumHealth, Is.EqualTo(100f));
                Assert.That(health.IsDead, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void RailShooter_EnemyProjectileCannotTunnelThroughDroneBetweenFrames()
        {
            Assert.That(
                DuneVectorRailShooterController.DoesRailProjectileHitDrone(
                    new Vector3(0f, 0f, 5f),
                    new Vector3(0f, 0f, -5f),
                    Vector3.zero,
                    1f),
                Is.True,
                "Enemy bullets must test their complete frame-to-frame path, not only their final position.");
            Assert.That(
                DuneVectorRailShooterController.DoesRailProjectileHitDrone(
                    new Vector3(2f, 0f, 5f),
                    new Vector3(2f, 0f, -5f),
                    Vector3.zero,
                    1f),
                Is.False);
        }

        [Test]
        public void RailShooter_EnemyBulletsUseABriefArmingWindow()
        {
            DuneVectorRuntimeSettings runtimeSettings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            RailShooterTuning settings = runtimeSettings != null ? runtimeSettings.RailShooter : null;

            Assert.That(settings, Is.Not.Null);
            Assert.That(
                settings.BulletArmDuration,
                Is.EqualTo(0.16f).Within(0.001f),
                "Gameplay rail bullets must become dangerous after their brief spawn flash, before crossing the drone.");
        }

        [Test]
        public void GraphicsSettings_RuntimeShadersSurviveTheBuild()
        {
            SerializedObject graphicsSettings = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            SerializedProperty includedShaders =
                graphicsSettings.FindProperty("m_AlwaysIncludedShaders");
            Assert.That(includedShaders, Is.Not.Null);

            HashSet<string> included = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < includedShaders.arraySize; i++)
            {
                if (includedShaders.GetArrayElementAtIndex(i).objectReferenceValue is Shader shader)
                {
                    included.Add(shader.name);
                }
            }

            // Every shader below is only ever reached through Shader.Find on a material the
            // game builds at runtime. Nothing authored references them, so a player build
            // drops them unless they are listed here, and the runtime fallback material then
            // paints over the world.
            foreach (string shaderName in RuntimeOnlyShaderNames)
            {
                Assert.That(
                    included,
                    Contains.Item(shaderName),
                    $"'{shaderName}' is created at runtime, so it must stay in Always Included Shaders or Shader.Find returns null in a player build.");
            }
        }

        [Test]
        public void GroundHeatShaders_UseRenderableUrpRefractionPasses()
        {
            AssertRenderableRefractionShader(
                "Assets/DuneVector/Runtime/DuneVectorDuneHeatDistortion.shader");
            AssertRenderableRefractionShader(
                "Assets/DuneVector/Runtime/DuneVectorHeatPlumeDistortion.shader");
        }

        private static void AssertRenderableRefractionShader(string assetPath)
        {
            string source = System.IO.File.ReadAllText(assetPath);
            StringAssert.Contains("\"LightMode\" = \"UniversalForward\"", source);
            StringAssert.Contains("DeclareOpaqueTexture.hlsl", source);
            StringAssert.Contains("SampleSceneColor", source);
            StringAssert.DoesNotContain("\"LightMode\" = \"DuneVectorLegacyDistortion\"", source);
        }

        private static readonly string[] RuntimeOnlyShaderNames =
        {
            "DuneVector/URP Dune Heat Distortion",
            "DuneVector/URP Heat Plume Distortion",
            "DuneVector/URP Sand Fracture",
            "DuneVector/URP Sand Macro Variation",
            "DuneVector/URP Weather Particle",
            "DuneVector/URP Portal Energy",
            "DuneVector/URP Music Reactive Additive",
            "DuneVector/URP World Geoglyph Overlay",
        };

        private const int RegressionWorldSeed = 47169;
        private const int PortalRegressionWorldSeed = 49109;
        private const float Stage2DistanceScale = 0.2f;
        private const string RuntimeSettingsPath =
            "Assets/DuneVector/ScriptableObjects/Dune Vector Runtime Settings.asset";

        [TestCase(100d, 100d, 125d, 100d, 50f, false)]
        [TestCase(100d, 100d, 150d, 100d, 50f, true)]
        [TestCase(100d, 100d, 160d, 100d, 50f, true)]
        public void DustDevilSpawn_RequiresAuthoredClearanceFromDrone(
            double tornadoX,
            double tornadoZ,
            double playerX,
            double playerZ,
            float clearance,
            bool expectedClear)
        {
            Assert.That(
                DuneVectorDustDevilSystem.HasPlayerSpawnClearance(
                    new LogicalPosition(tornadoX, tornadoZ),
                    new LogicalPosition(playerX, playerZ),
                    clearance),
                Is.EqualTo(expectedClear));
        }

        [Test]
        public void RuntimeSettings_DustDevilSpawnClearanceExceedsInteractionRadius()
        {
            DuneVectorRuntimeSettings settings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.DustDevils, Is.Not.Null);
            Assert.That(
                settings.DustDevils.PlayerDeploymentClearance,
                Is.GreaterThan(settings.DustDevils.InteractionRadius),
                "WORLD tornado streaming must keep a newly spawned funnel outside its drone interaction radius.");
        }

        [Test]
        public void ShieldConsumesOneOtherwiseValidHitWithoutLosingHealth()
        {
            GameObject droneObject = new GameObject("Shield Test Drone");
            try
            {
                DroneHealth health = droneObject.AddComponent<DroneHealth>();
                health.Initialize(100f, 0f);

                Assert.That(health.GrantShield(), Is.True);
                Assert.That(health.HasShield, Is.True);
                Assert.That(health.TakeDamage(25f, "Shield test"), Is.True);
                Assert.That(health.HasShield, Is.False);
                Assert.That(health.CurrentHealth, Is.EqualTo(100f));

                Assert.That(health.TakeDamage(25f, "Unshielded test"), Is.True);
                Assert.That(health.CurrentHealth, Is.EqualTo(75f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(droneObject);
            }
        }

        [Test]
        public void NonDamagingAttemptDoesNotConsumeShield()
        {
            GameObject droneObject = new GameObject("Shield Validation Test Drone");
            try
            {
                DroneHealth health = droneObject.AddComponent<DroneHealth>();
                health.Initialize(100f, 0f);
                health.GrantShield();

                Assert.That(health.TakeDamage(0f, "No damage"), Is.False);
                Assert.That(health.HasShield, Is.True);
                Assert.That(health.CurrentHealth, Is.EqualTo(100f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(droneObject);
            }
        }

        [Test]
        public void ShieldPickupEffectUsesAuthoredLocalOffset()
        {
            GameObject droneObject = new GameObject("Shield Offset Test Drone");
            try
            {
                Vector3 expectedOffset = new Vector3(0.35f, -0.4f, 0.2f);
                PlayerHealthTuning settings = new PlayerHealthTuning
                {
                    ShieldEffectOffset = expectedOffset,
                };
                DroneHealth health = droneObject.AddComponent<DroneHealth>();
                health.Initialize(100f, 0f);
                health.ConfigureOutOfCombatRepair(settings);

                Assert.That(health.GrantShield(), Is.True);
                Transform shield = droneObject.transform.Find("BlueSparkleShield");
                Assert.That(shield, Is.Not.Null);
                Assert.That(shield.localPosition.x, Is.EqualTo(expectedOffset.x).Within(0.0001f));
                Assert.That(shield.localPosition.y, Is.EqualTo(expectedOffset.y).Within(0.0001f));
                Assert.That(shield.localPosition.z, Is.EqualTo(expectedOffset.z).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(droneObject);
            }
        }

        [Test]
        public void GeoglyphImageCoordinatesConvertToUnityUvCoordinates()
        {
            Vector2 converted = GeoglyphArtworkPlacement.ImageUvToUnityUv(
                new Vector2(0.25f, 0.8f));

            Assert.That(converted.x, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(converted.y, Is.EqualTo(0.2f).Within(0.0001f));
        }

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
        public void ImmediatePlayerCollisionReactivatesAStagedChunkRoot()
        {
            DuneVectorRuntimeSettings settings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            Assert.That(settings, Is.Not.Null);

            GameObject worldObject = new GameObject("Staged Player Collision Regression World");
            DuneVectorMaterials materials = null;
            try
            {
                DesertWorldStreamer world = worldObject.AddComponent<DesertWorldStreamer>();
                ConfigureWorld(world, settings);
                materials = new DuneVectorMaterials(settings);
                world.Initialize(materials);

                LogicalPosition destination = ResolveStage2RouteOrigin(settings.Contracts);
                StageDestinationChunkInactive(world, destination);
                Assert.That(
                    world.IsVisualTerrainReady(destination),
                    Is.False,
                    "The regression setup must begin with an inactive staged chunk root.");

                GenerateImmediatePlayerCollision(world, destination);

                Assert.That(
                    world.IsVisualTerrainReady(destination),
                    Is.True,
                    "Immediate player collision must reactivate the staged root so its collider enters physics.");
                Vector3 surfacePosition = world.LogicalToLocal(
                    destination.X,
                    world.HeightField.SampleHeight(destination.X, destination.Z) + 0.1d,
                    destination.Z);
                Assert.That(
                    world.HasPreparedTerrainSupport(surfacePosition, 1f, 89f),
                    Is.True,
                    "The reactivated staged chunk must provide solid terrain support.");
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
            world.WarpGates = new WarpGateTuning { Enabled = false };
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

        private static void GenerateImmediatePlayerCollision(
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
            generateChunk.Invoke(world, new object[] { coordinate, false, true });
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
