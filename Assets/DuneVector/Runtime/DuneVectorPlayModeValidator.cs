#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using KinematicCharacterController;
using UnityEditor;
using UnityEngine;

namespace DuneVector
{
    [Serializable]
    internal sealed class DuneVectorValidationReport
    {
        public bool Passed;
        public float RuntimeSeconds;
        public int RuntimeErrorCount;
        public float PeakSpeed;
        public float PeakCameraFollowError;
        public float PeakJumpClearance;
        public float PeakVisualBank;
        public int GeneratedChunks;
        public int UnloadedChunks;
        public int PeakActiveChunks;
        public int Rebases;
        public List<string> PassedChecks = new List<string>();
        public List<string> FailedChecks = new List<string>();
        public List<string> RuntimeErrors = new List<string>();
    }

    [DefaultExecutionOrder(5000)]
    internal sealed class DuneVectorPlayModeValidator : MonoBehaviour
    {
        private readonly DuneVectorValidationReport _report = new DuneVectorValidationReport();
        private float _startedAt;
        private string _projectRoot;

        private void Awake()
        {
            _startedAt = Time.realtimeSinceStartup;
            _projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            Application.logMessageReceived += OnLogMessage;
        }

        private void Start()
        {
            StartCoroutine(RunValidation());
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLogMessage;
        }

        private IEnumerator RunValidation()
        {
            yield return null;
            yield return new WaitForSeconds(0.4f);

            DuneVectorBootstrap bootstrap = DuneVectorBootstrap.Instance;
            Check(bootstrap != null, "Bootstrap created the playable prototype", "DuneVectorBootstrap.Instance was null.");
            if (bootstrap == null)
            {
                Finish();
                yield break;
            }

            DroneCharacterController drone = bootstrap.Drone;
            DronePlayer player = bootstrap.Player;
            DroneCameraController camera = bootstrap.DroneCamera;
            DesertWorldStreamer world = bootstrap.World;
            KinematicCharacterMotor motor = drone != null ? drone.Motor : null;

            Check(drone != null && motor != null, "Drone and KinematicCharacterMotor exist", "Drone or KCC motor was missing.");
            Check(motor != null && ReferenceEquals(motor.CharacterController, drone), "KCC uses DroneCharacterController through ICharacterController", "Motor.CharacterController was not the drone controller.");
            Check(drone != null && drone.GetComponent<Rigidbody>() == null, "No Rigidbody competes with KCC locomotion", "A Rigidbody was found on DroneRoot.");
            Check(drone != null && drone.GetComponent<CharacterController>() == null, "No Unity CharacterController competes with KCC", "A Unity CharacterController was found on DroneRoot.");
            Check(camera != null && camera.FollowingSharpness > 0f && camera.FollowingSharpness < 20f, "Camera exposes a deliberately visible FollowingSharpness delay", "FollowingSharpness was missing or effectively rigid.");
            Check(world != null && world.ActiveChunkCount >= 9, "Initial collision chunks are ready before traversal", $"Only {world?.ActiveChunkCount ?? 0} chunks were active.");

            if (drone == null || player == null || camera == null || world == null || motor == null)
            {
                Finish();
                yield break;
            }

            CaptureScreenshot(camera.Camera, Path.Combine(_projectRoot, "Logs", "DuneVector-PlayMode.png"));

            if (bootstrap.CourierGame != null)
            {
                yield return ValidateCourierVerticalSlice(bootstrap);
                Finish();
                yield break;
            }

            Vector3 movementStart = motor.TransientPosition;
            bool sawBoost = false;
            bool jumpSent = false;
            bool enteredFlight = false;
            float routeTimer = 0f;
            while (routeTimer < 8f && !enteredFlight)
            {
                LogicalPosition logical = world.LogicalPlayerPosition;
                bool jumpNow = !jumpSent && logical.Z >= 42.5;
                if (jumpNow)
                {
                    jumpSent = true;
                }

                player.SetAutomatedInput(new DroneRawInputFrame
                {
                    Move = Vector2.up,
                    JumpPressed = jumpNow,
                    JumpHeld = jumpNow,
                });

                sawBoost |= drone.IsBoosting;
                enteredFlight |= drone.CurrentMode == DroneTraversalMode.Flight;
                SampleTelemetry(drone, camera, world);
                routeTimer += Time.deltaTime;
                yield return null;
            }

            float movedDistance = Vector3.ProjectOnPlane(motor.TransientPosition - movementStart, Vector3.up).magnitude;
            Check(movedDistance > 18f, "WASD input drives responsive camera-relative KCC movement", $"Drone moved only {movedDistance:0.0} m.");
            Check(sawBoost, "Starter ground ring activates a KCC-compatible boost once crossed", "The starter boost ring was not activated.");
            Check(_report.PeakSpeed > drone.MaxGroundSpeed * 1.15f, "Boost raises speed beyond normal traversal", $"Peak speed was {_report.PeakSpeed:0.0} m/s.");
            Check(_report.PeakCameraFollowError > 0.65f, "FollowingSharpness creates visible positional separation", $"Peak camera follow error was {_report.PeakCameraFollowError:0.00} m.");
            Check(jumpSent && _report.PeakJumpClearance > 2.1f, "Space jump preserves momentum and clears dune height", $"Peak clearance was {_report.PeakJumpClearance:0.00} m.");
            Check(enteredFlight, "Reachable elevated ring transitions normal traversal into flight", "The starter aerial ring was not reached during the boosted jump route.");

            if (enteredFlight)
            {
                player.SetAutomatedInput(new DroneRawInputFrame
                {
                    Move = new Vector2(1f, 1f).normalized,
                    LookDelta = new Vector2(320f, 0f),
                });
                yield return null;

                float bankingTimer = 0f;
                while (bankingTimer < 3.2f && drone.CurrentMode == DroneTraversalMode.Flight)
                {
                    player.SetAutomatedInput(new DroneRawInputFrame { Move = new Vector2(1f, 1f).normalized });
                    SampleTelemetry(drone, camera, world);
                    bankingTimer += Time.deltaTime;
                    yield return null;
                }

                Check(_report.PeakVisualBank > 4f, "Flight yaw produces visual-only banking", $"Peak visual bank was {_report.PeakVisualBank:0.0} degrees.");
                float authoritativeRoll = Mathf.Abs(Vector3.Dot(motor.CharacterRight, Vector3.up));
                Check(authoritativeRoll < 0.01f, "Visual bank does not roll the authoritative KCC capsule", $"Motor right/up dot was {authoritativeRoll:0.000}.");

                player.SetAutomatedInput(new DroneRawInputFrame
                {
                    Move = Vector2.up,
                    LookDelta = new Vector2(0f, -900f),
                });
                yield return null;

                float landingTimer = 0f;
                while (landingTimer < 13f && drone.CurrentMode == DroneTraversalMode.Flight)
                {
                    player.SetAutomatedInput(new DroneRawInputFrame { Move = Vector2.up });
                    SampleTelemetry(drone, camera, world);
                    landingTimer += Time.deltaTime;
                    yield return null;
                }
                Check(drone.CurrentMode == DroneTraversalMode.Normal, "Flight descends and restores normal KCC ground traversal", "Flight did not complete its automatic landing transition.");
            }

            LogicalPosition logicalBeforeRebase = world.LogicalPlayerPosition;
            Vector3 velocityBeforeRebase = motor.BaseVelocity;
            world.RebaseNow(new Vector3(world.ChunkSize, 0f, 0f));
            yield return null;
            LogicalPosition logicalAfterRebase = world.LogicalPlayerPosition;
            double logicalDelta = Math.Sqrt(
                Math.Pow(logicalAfterRebase.X - logicalBeforeRebase.X, 2.0)
                + Math.Pow(logicalAfterRebase.Z - logicalBeforeRebase.Z, 2.0));
            Check(logicalDelta < 0.02, "Floating-origin rebase preserves logical world position", $"Logical position changed by {logicalDelta:0.000} m.");
            Check((motor.BaseVelocity - velocityBeforeRebase).magnitude < 0.02f, "Floating-origin rebase preserves KCC velocity", "Motor velocity changed during rebase.");
            Check(IsFinite(camera.transform.position) && camera.FollowingError < 40f, "Camera state remains coherent through world rebasing", "Camera state became invalid or exploded after rebase.");

            Vector3 postRebaseStart = motor.TransientPosition;
            float postRebaseTimer = 0f;
            while (postRebaseTimer < 13f && world.UnloadedChunkCount == 0)
            {
                player.SetAutomatedInput(new DroneRawInputFrame { Move = Vector2.up });
                SampleTelemetry(drone, camera, world);
                postRebaseTimer += Time.deltaTime;
                yield return null;
            }
            Check((motor.TransientPosition - postRebaseStart).magnitude > 1f, "KCC continues simulating after rebasing", "The motor did not move after the origin shift.");

            int maximumBoundedChunks = ((world.UnloadRadius * 2 + 1) * (world.UnloadRadius * 2 + 1))
                + world.MaximumCameraFrustumTerrainChunks;
            Check(world.GeneratedChunkCount > 9, "Streaming generates chunks ahead of traversal", $"Only {world.GeneratedChunkCount} chunks were generated.");
            Check(world.ActiveChunkCount <= maximumBoundedChunks && world.PeakActiveChunkCount <= maximumBoundedChunks + 8, "Streaming keeps active chunk count bounded", $"Active/peak chunks were {world.ActiveChunkCount}/{world.PeakActiveChunkCount}.");
            Check(world.UnloadedChunkCount > 0, "Distant chunks unload behind the player", "No chunks were unloaded during multi-chunk traversal.");

            double seamX = world.CurrentLogicalChunk.x * (double)world.ChunkSize;
            double seamHeightA = world.HeightField.SampleHeight(seamX, 37.125);
            double seamHeightB = world.HeightField.SampleHeight(seamX, 37.125);
            Check(Math.Abs(seamHeightA - seamHeightB) < 0.0000001, "Terrain boundaries share deterministic world-coordinate height evaluation", "Repeated seam evaluation differed.");

            player.ClearAutomatedInput();
            yield return new WaitForSeconds(0.25f);
            Finish();
        }

        private IEnumerator ValidateCourierVerticalSlice(DuneVectorBootstrap bootstrap)
        {
            DuneVectorCourierGame courier = bootstrap.CourierGame;
            Check(courier.State == CourierRunState.Hub,
                "Courier game starts in the safe world hub",
                $"Initial courier state was {courier.State}.");
            Vector3 hubSpawnOffset = bootstrap.Drone.Motor.TransientPosition - courier.HubSpawnPosition;
            float horizontalHubSpawnError = Vector3.ProjectOnPlane(hubSpawnOffset, Vector3.up).magnitude;
            Check(horizontalHubSpawnError < 0.5f,
                "Drone physically starts on the hub platform",
                $"Drone started {horizontalHubSpawnError:0.00} m horizontally from the hub spawn.");
            Check(bootstrap.DroneCamera.FollowingError < 0.05f,
                "Camera starts with the drone at the hub",
                $"Camera follow point remained {bootstrap.DroneCamera.FollowingError:0.00} m from the hub drone while the terminal was open.");
            bool hasEnabledHubCapsule = false;
            CapsuleCollider[] hubCapsules = courier.GetComponentsInChildren<CapsuleCollider>(true);
            for (int i = 0; i < hubCapsules.Length; i++)
            {
                hasEnabledHubCapsule |= hubCapsules[i].enabled;
            }
            Check(!hasEnabledHubCapsule,
                "Hub platforms use fitted circle mesh collision",
                "An enabled capsule collider is still lifting the drone above a hub platform.");
            Check(courier.AvailableContracts.Count >= 5 && courier.AvailableContracts.Count <= 8,
                "Contract terminal offers five to eight modular contracts",
                $"Contract terminal offered {courier.AvailableContracts.Count} contracts.");
            Check(courier.ContractTerminal != null && courier.MessageArchiveTerminal != null && courier.FreeRoamTerminal != null,
                "Courier hub builds contract, message archive, and free roam terminals",
                "One or more physical hub terminals were missing.");
            Check(courier.HubRuneRing != null,
                "Courier hub builds one prefab-authored rune ring",
                "The rune_ringPrefab instance was missing from the runtime hub.");
            if (courier.HubRuneRing != null)
            {
                GameObject runeRingPrefab = Resources.Load<GameObject>("rune_ringPrefab");
                bool usesAuthoredTransform = runeRingPrefab != null &&
                    Vector3.Distance(courier.HubRuneRing.position, runeRingPrefab.transform.position) < 0.001f &&
                    Quaternion.Angle(courier.HubRuneRing.rotation, runeRingPrefab.transform.rotation) < 0.01f &&
                    Vector3.Distance(courier.HubRuneRing.lossyScale, runeRingPrefab.transform.lossyScale) < 0.001f;
                Check(usesAuthoredTransform,
                    "Hub rune ring preserves its prefab-authored transform",
                    "The runtime rune ring position, rotation, or scale differs from rune_ringPrefab.");
                Check(courier.HubRuneRing.IsChildOf(courier.transform),
                    "Hub rune ring follows floating-origin hub shifts",
                    "The rune ring was not parented into the runtime hub hierarchy.");
            }
            if (courier.ContractTerminal != null && courier.MessageArchiveTerminal != null)
            {
                Vector3 contractToArchive = Vector3.ProjectOnPlane(
                    courier.MessageArchiveTerminal.position - courier.ContractTerminal.position,
                    Vector3.up).normalized;
                Vector3 archiveToContract = -contractToArchive;
                bool screensFaceEachOther =
                    Vector3.Dot(-courier.ContractTerminal.forward, contractToArchive) > 0.98f &&
                    Vector3.Dot(-courier.MessageArchiveTerminal.forward, archiveToContract) > 0.98f;
                Check(screensFaceEachOther,
                    "Hub terminal screens face one another",
                    "The contract and archive terminal screen sides were not oriented toward each other.");
            }
            if (courier.FreeRoamTerminal != null)
            {
                Vector3 spawnToFreeRoam = Vector3.ProjectOnPlane(
                    courier.FreeRoamTerminal.position - courier.HubSpawnPosition,
                    Vector3.up).normalized;
                bool terminalIsLeftAndFacesSpawn =
                    Vector3.Dot(Vector3.left, spawnToFreeRoam) > 0.98f &&
                    Vector3.Dot(-courier.FreeRoamTerminal.forward, -spawnToFreeRoam) > 0.98f;
                Check(terminalIsLeftAndFacesSpawn,
                    "Free roam terminal stands left of the player start and faces the player",
                    "The free roam terminal was not positioned left of the hub spawn or its screen did not face the spawn.");
            }
            Check(courier.ArchivedMessageCount >= 0,
                "Message archive resolves completed transmissions safely",
                $"Archive returned an invalid count of {courier.ArchivedMessageCount}.");
            Check(bootstrap.LandmarkDirector != null,
                "Authored procedural landmark director is active",
                "Landmark director was missing.");
            Check(bootstrap.RouteEncounterDirector != null,
                "Route encounter formation director is active",
                "Route encounter director was missing.");
            Check(courier.Progress != null,
                "Courier progression loaded from its persistent data model",
                "Courier progression component was missing.");
            Check(File.Exists(Path.Combine(Application.persistentDataPath, "DuneVectorCourierProgress.dat")),
                "Courier progression persists to a .dat file",
                "DuneVectorCourierProgress.dat was not created.");

            bool accepted = courier.AcceptOffer(0);
            Check(accepted && courier.State == CourierRunState.TeleportingToDesert,
                "Accepting a terminal contract starts the desert teleport sequence",
                $"Accept returned {accepted} and state became {courier.State}.");
            Vector3 pickupBeforeDeploymentRebase = courier.ActiveObjective != null
                ? courier.ActiveObjective.position
                : Vector3.zero;
            Vector3 deploymentRebase = new Vector3(bootstrap.World.ChunkSize, 0f, 0f);
            bootstrap.World.RebaseNow(deploymentRebase);
            Check(courier.ActiveObjective != null &&
                  (courier.ActiveObjective.position - (pickupBeforeDeploymentRebase - deploymentRebase)).sqrMagnitude < 0.001f,
                "Pending pickup cargo follows floating-origin shifts during deployment",
                "Pickup cargo detached from its ring when the world rebased during deployment.");

            float deployTimeout = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deployTimeout && courier.State == CourierRunState.TeleportingToDesert)
            {
                yield return null;
            }
            Check(courier.State == CourierRunState.FindPackage,
                "Teleport deploys the drone into the package-search phase",
                $"Courier state after deployment was {courier.State}.");
            Check(courier.ActiveContract != null && courier.ActiveObjective != null,
                "Accepted contract owns an active package objective",
                "Active contract or package objective was missing after deployment.");
            Vector3 pickupDirection = Vector3.ProjectOnPlane(
                courier.ActiveObjective.position - bootstrap.Drone.Motor.TransientPosition,
                Vector3.up).normalized;
            Check(Vector3.Dot(bootstrap.Drone.Motor.CharacterForward, pickupDirection) > 0.98f,
                "Contract deployment faces the drone toward the pickup",
                "Drone insertion yaw did not face the active pickup objective.");
            Check(Vector3.Dot(bootstrap.DroneCamera.PlanarDirection, pickupDirection) > 0.98f,
                "Contract deployment faces the camera toward the pickup",
                "Camera insertion yaw did not face the active pickup objective.");
            Check(bootstrap.LandmarkDirector.ContractLandmarks.Count >= 2,
                "Contract route pins authored pickup and destination landmarks",
                $"Only {bootstrap.LandmarkDirector.ContractLandmarks.Count} route landmarks were present.");

            courier.RequestReturnToHub(recordAbandonment: false);
            float returnTimeout = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < returnTimeout && courier.State != CourierRunState.Hub)
            {
                yield return null;
            }
            Check(courier.State == CourierRunState.Hub,
                "Pause-menu return flow reconstructs the drone at the hub",
                $"Courier state after return was {courier.State}.");
            Check(bootstrap.Player.InputEnabled,
                "Player control is restored after returning to the hub",
                "Player input remained locked after return.");
            Check(courier.ActiveObjective == null,
                "Returning to the hub clears the abandoned route objective",
                "An objective remained active after returning to the hub.");
        }

        private void SampleTelemetry(DroneCharacterController drone, DroneCameraController camera, DesertWorldStreamer world)
        {
            _report.PeakSpeed = Mathf.Max(_report.PeakSpeed, drone.Speed);
            _report.PeakCameraFollowError = Mathf.Max(_report.PeakCameraFollowError, camera.FollowingError);
            float terrainHeight = world.SampleHeightAtLocal(drone.Motor.TransientPosition.x, drone.Motor.TransientPosition.z);
            _report.PeakJumpClearance = Mathf.Max(_report.PeakJumpClearance, drone.Motor.TransientPosition.y - terrainHeight);
            if (drone.DroneVisualRoot != null)
            {
                float z = drone.DroneVisualRoot.localEulerAngles.z;
                if (z > 180f)
                {
                    z -= 360f;
                }
                _report.PeakVisualBank = Mathf.Max(_report.PeakVisualBank, Mathf.Abs(z));
            }
        }

        private void Check(bool condition, string success, string failure)
        {
            if (condition)
            {
                _report.PassedChecks.Add(success);
            }
            else
            {
                _report.FailedChecks.Add($"{success}: {failure}");
            }
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            {
                return;
            }
            _report.RuntimeErrors.Add($"{type}: {condition}\n{stackTrace}");
        }

        private void Finish()
        {
            Application.logMessageReceived -= OnLogMessage;
            DuneVectorBootstrap bootstrap = DuneVectorBootstrap.Instance;
            if (bootstrap != null && bootstrap.World != null)
            {
                _report.GeneratedChunks = bootstrap.World.GeneratedChunkCount;
                _report.UnloadedChunks = bootstrap.World.UnloadedChunkCount;
                _report.PeakActiveChunks = bootstrap.World.PeakActiveChunkCount;
                _report.Rebases = bootstrap.World.RebaseCount;
            }
            _report.RuntimeSeconds = Time.realtimeSinceStartup - _startedAt;
            _report.RuntimeErrorCount = _report.RuntimeErrors.Count;
            _report.Passed = _report.FailedChecks.Count == 0 && _report.RuntimeErrorCount == 0;

            string logsDirectory = Path.Combine(_projectRoot, "Logs");
            Directory.CreateDirectory(logsDirectory);
            string reportPath = Path.Combine(logsDirectory, "DuneVectorValidation.json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(_report, true));
            EditorPrefs.SetBool("DuneVector.ValidationRequested", false);
            Debug.Log($"DUNE_VECTOR_VALIDATION_COMPLETE: {(_report.Passed ? "PASS" : "FAIL")} - {reportPath}");
            EditorApplication.Exit(_report.Passed ? 0 : 2);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static void CaptureScreenshot(Camera camera, string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
                RenderTexture renderTexture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                RenderTexture previousTarget = camera.targetTexture;
                RenderTexture previousActive = RenderTexture.active;
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.Destroy(image);
                UnityEngine.Object.Destroy(renderTexture);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Dune Vector screenshot capture skipped: {exception.Message}");
            }
        }
    }
}
#endif
