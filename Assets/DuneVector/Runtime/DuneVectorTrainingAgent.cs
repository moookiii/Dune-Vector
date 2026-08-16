using System;
using System.Collections;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace DuneVector
{
    [DefaultExecutionOrder(1900)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorTrainingAgent : Agent
    {
        public const int ObservationCount = 92;
        public const int ActionCount = 11;
        public static readonly int[] ActionBranches = { 7, 7, 7, 7, 2, 2, 2, 2, 3, 2, 2 };

        private DuneVectorBootstrap _bootstrap;
        private PufferTrainingTuning _settings;
        private int _curriculumStage;
        private bool _evaluation;
        private int _episodeSteps;
        private int _hubSteps;
        private int _stepsToDeploy;
        private bool _deployed;
        private bool _terminalOpened;
        private bool _hubStuck;
        private bool _hubTimedOut;
        private bool _stage2TimedOut;
        private bool _postDeploymentStuck;
        private bool _wrongStage1Deployment;
        private int _hubStepsWithoutProgress;
        private float _bestHubDistance;
        private int _pickups;
        private int _deliveries;
        private float _damageTaken;
        private int _deaths;
        private int _shots;
        private float _combatDamage;
        private int _combatDefeats;
        private float _trainingReturn;
        private float _unshapedReturn;
        private float _previousHealth;
        private float _previousHubDistance;
        private float _previousObjectiveDistance;
        private float _bestObjectiveDistance;
        private float _pickupObjectiveMinDistance;
        private int _objectiveStepsWithoutProgress;
        private bool _objectiveDiverged;
        private bool _objectiveNoProgress;
        private float _objectivePotentialReward;
        private Transform _previousObjective;
        private int _previousFreeRoamStreak;
        private int _previousCompletedDeliveries;
        private int _previousContractPickupSequence;
        private int _previousFreeRoamPickupSequence;
        private int _stage2HazardRecoveryStepsRemaining;
        private FreeRoamDeliveryPhase _previousFreeRoamPhase;
        private CourierRunState _previousRunState;
        private int _previousHubTerminalMenuKind;
        private int _previousRingActivations;
        private int _rewardedRingActivations;
        private TraversalRing _previousStage3Ring;
        private float _previousStage3RingDistance;
        private float _stage3RingPotentialReward;
        private float _stage3RingMinDistance;
        private bool _stage3TimedOut;
        private TraversalRing _stage3SelectedRing;
        private bool _stage3SelectedRingActivated;
        private bool _stage3SelectedRingActivationPending;
        private float _stage3DeliveryDistanceAtRing;
        private float _stage3BestDeliveryDistanceAfterRing;
        private float _previousStage3DeliveryDistance;
        private float _stage3DeliveryProgress;
        private int _stage3PostRingStepsWithoutProgress;
        private float _stage3DeliveryPotentialReward;
        private bool _stage3DeliveryProgressRewarded;
        private float _previousTargetHealth;
        private EnemyCombatTarget _previousTarget;
        private readonly float[] _previousActions = new float[ActionCount];
        private readonly int[] _nextPulseStep = new int[ActionCount];
        private readonly Dictionary<TraversalRing, int> _observedRingActivations =
            new Dictionary<TraversalRing, int>();

        public void Bind(DuneVectorBootstrap bootstrap, int curriculumStage, bool evaluation)
        {
            _bootstrap = bootstrap;
            _settings = bootstrap.PufferTraining;
            _curriculumStage = Mathf.Clamp(curriculumStage, 1, 6);
            _evaluation = evaluation;
            MaxStep = 0;
        }

        public override void OnEpisodeBegin()
        {
            if (_bootstrap == null || _bootstrap.CourierGame == null)
            {
                return;
            }

            if (_episodeSteps > 0)
            {
                _bootstrap.CourierGame.Progress?.ResetTrainingEpisodeProgress();
                _bootstrap.CourierGame.RestartAtHub(playReturnEffect: false);
                _bootstrap.DroneHealth?.ReviveAtFullHealth();
                _bootstrap.Player?.SetInputEnabled(true);
            }

            _episodeSteps = 0;
            _hubSteps = 0;
            _stepsToDeploy = 0;
            _deployed = false;
            _terminalOpened = false;
            _hubStuck = false;
            _hubTimedOut = false;
            _stage2TimedOut = false;
            _postDeploymentStuck = false;
            _wrongStage1Deployment = false;
            _hubStepsWithoutProgress = 0;
            _bestHubDistance = float.PositiveInfinity;
            _pickups = 0;
            _deliveries = 0;
            _damageTaken = 0f;
            _deaths = 0;
            _shots = 0;
            _combatDamage = 0f;
            _combatDefeats = 0;
            _trainingReturn = 0f;
            _unshapedReturn = 0f;
            _objectivePotentialReward = 0f;
            _bestObjectiveDistance = float.PositiveInfinity;
            _pickupObjectiveMinDistance = float.PositiveInfinity;
            _objectiveStepsWithoutProgress = 0;
            _objectiveDiverged = false;
            _objectiveNoProgress = false;
            _stage2HazardRecoveryStepsRemaining = 0;
            _previousObjective = null;
            _rewardedRingActivations = 0;
            _previousStage3Ring = null;
            _previousStage3RingDistance = float.NaN;
            _stage3RingPotentialReward = 0f;
            _stage3RingMinDistance = float.PositiveInfinity;
            _stage3TimedOut = false;
            SetStage3SelectedRing(null);
            _stage3SelectedRingActivated = false;
            _stage3SelectedRingActivationPending = false;
            _stage3DeliveryDistanceAtRing = float.NaN;
            _stage3BestDeliveryDistanceAfterRing = float.PositiveInfinity;
            _previousStage3DeliveryDistance = float.NaN;
            _stage3DeliveryProgress = 0f;
            _stage3PostRingStepsWithoutProgress = 0;
            _stage3DeliveryPotentialReward = 0f;
            _stage3DeliveryProgressRewarded = false;
            _observedRingActivations.Clear();
            Array.Clear(_previousActions, 0, _previousActions.Length);
            Array.Clear(_nextPulseStep, 0, _nextPulseStep.Length);
            CaptureBaseline();
            _bootstrap.Player?.SetAutomatedInput(default);
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            int count = 0;
            DuneVectorCourierGame courier = _bootstrap.CourierGame;
            DroneCharacterController drone = _bootstrap.Drone;
            DronePlayer player = _bootstrap.Player;

            int phase = ResolveEpisodePhase(courier);
            AddOneHot(sensor, phase, 6, ref count);
            Add(sensor, courier.IsDeploymentTransition ? 1f : 0f, ref count);

            int terminalKind = 0;
            Vector3 terminalPosition = Vector3.zero;
            float hubDistance = 0f;
            float interactionRadius = 0f;
            bool hubValid;
            if (courier.State == CourierRunState.Hub)
            {
                hubValid = courier.TryGetContractTerminalObservation(
                    out terminalPosition,
                    out hubDistance,
                    out interactionRadius);
                terminalKind = hubValid ? 1 : 0;
            }
            else
            {
                hubValid = courier.State == CourierRunState.Hub &&
                    courier.TryGetHubInteractionObservation(
                        out terminalKind,
                        out terminalPosition,
                        out hubDistance,
                        out interactionRadius);
            }
            Add(sensor, hubValid ? 1f : 0f, ref count);
            AddOneHot(sensor, hubValid ? terminalKind - 1 : -1, 3, ref count);
            Vector3 hubRelative = hubValid
                ? ToCommandLocal(terminalPosition - drone.WorldCenter) / Mathf.Max(1f, _settings.HubDistanceScale)
                : Vector3.zero;
            AddVector(sensor, hubRelative, ref count);
            Add(sensor, hubValid ? NormalizeDistance(hubDistance, _settings.HubDistanceScale) : 0f, ref count);
            bool insideRange = hubValid && hubDistance <= interactionRadius;
            Add(sensor, insideRange ? 1f : 0f, ref count);
            Add(sensor, insideRange && courier.HubTerminalMenuKind == 0 ? 1f : 0f, ref count);
            Add(sensor, courier.IsTerminalOpen ? 1f : 0f, ref count);
            Add(sensor, courier.HubTerminalChoiceCount > 1
                ? courier.HubTerminalSelectedIndex / (float)(courier.HubTerminalChoiceCount - 1)
                : 0f, ref count);
            Add(sensor, Mathf.Clamp01(courier.HubTerminalChoiceCount / 8f), ref count);
            Add(sensor, courier.HubTerminalConfirmValid ? 1f : 0f, ref count);

            bool offerValid = courier.TryGetSelectedHubOffer(out CourierContract offer);
            Add(sensor, offerValid ? 1f : 0f, ref count);
            Add(sensor, offerValid ? Mathf.Clamp01(offer.Difficulty / 20f) : 0f, ref count);
            Add(sensor, offerValid ? NormalizeDistance(offer.RouteDistance, 20000f) : 0f, ref count);
            Add(sensor, offerValid ? Mathf.Clamp01(offer.OfferedReward / 20000f) : 0f, ref count);
            Add(sensor, offerValid ? Mathf.Clamp01(offer.TimeLimit / 1800f) : 0f, ref count);
            Add(sensor, offerValid ? Mathf.Clamp01(offer.StopCount / 4f) : 0f, ref count);
            CourierContractModifier displayed = offerValid ? offer.DisplayModifiers : CourierContractModifier.None;
            Add(sensor, Has(displayed, CourierContractModifier.Express), ref count);
            Add(sensor, Has(displayed, CourierContractModifier.Fragile), ref count);
            Add(sensor, Has(displayed, CourierContractModifier.MultiDrop), ref count);
            Add(sensor, Has(displayed, CourierContractModifier.HighValue), ref count);
            Add(sensor, Has(displayed, CourierContractModifier.Hazardous), ref count);
            Add(sensor, Has(displayed, CourierContractModifier.Unknown), ref count);

            Transform objective = ResolveActiveObjective(courier);
            bool objectiveValid = objective != null;
            float objectiveDistance = objectiveValid
                ? Vector3.Distance(drone.WorldCenter, objective.position)
                : 0f;
            Add(sensor, objectiveValid ? 1f : 0f, ref count);
            Vector3 objectiveDirection = objectiveValid
                ? ToCommandLocal(objective.position - drone.WorldCenter).normalized
                : Vector3.zero;
            AddVector(sensor, objectiveDirection, ref count);
            Add(sensor, objectiveValid
                ? NormalizeDistance(objectiveDistance, _settings.ObjectiveDistanceScale)
                : 0f, ref count);
            FreeRoamDeliveryPhase freePhase = courier.FreeRoamDeliveries != null
                ? courier.FreeRoamDeliveries.Phase
                : FreeRoamDeliveryPhase.Inactive;
            bool pickupPhase = courier.State == CourierRunState.FindPackage || freePhase == FreeRoamDeliveryPhase.Pickup;
            bool deliveryPhase = courier.State == CourierRunState.Delivering || freePhase == FreeRoamDeliveryPhase.Deliver;
            Add(sensor, pickupPhase ? 1f : 0f, ref count);
            Add(sensor, deliveryPhase ? 1f : 0f, ref count);
            Add(sensor, courier.IsCarryingCargo ? 1f : 0f, ref count);
            Add(sensor, Mathf.Clamp01(courier.CargoIntegrity / 100f), ref count);
            float expressLimit = courier.ActiveContract != null ? courier.ActiveContract.TimeLimit : 0f;
            Add(sensor, expressLimit > 0f ? Mathf.Clamp01(courier.ExpressTimeRemaining / expressLimit) : 0f, ref count);
            Add(sensor, courier.FreeRoamDeliveries != null
                ? Mathf.Clamp01(courier.FreeRoamDeliveries.Streak / 20f)
                : 0f, ref count);
            Add(sensor, courier.FreeRoamDeliveries != null
                ? Mathf.Clamp01(courier.FreeRoamDeliveries.RouteRisk / 20f)
                : offerValid ? Mathf.Clamp01(offer.Difficulty / 20f) : 0f, ref count);

            Vector3 velocity = drone.Motor != null ? drone.Motor.Velocity : Vector3.zero;
            AddVector(sensor, ToCommandLocal(velocity) / Mathf.Max(1f, _settings.VelocityScale), ref count);
            Add(sensor, Mathf.Clamp(drone.Speed / Mathf.Max(1f, _settings.VelocityScale), 0f, 2f), ref count);
            Add(sensor, drone.IsStableGrounded ? 1f : 0f, ref count);
            Add(sensor, drone.CurrentMode == DroneTraversalMode.Flight ? 1f : 0f, ref count);
            Add(sensor, drone.FlightTimeNormalized, ref count);
            Add(sensor, _bootstrap.DroneHealth != null ? _bootstrap.DroneHealth.NormalizedHealth : 0f, ref count);
            Add(sensor, player.Stamina != null ? player.Stamina.NormalizedStamina : 0f, ref count);
            Add(sensor, drone.IsRingBoosting ? 1f : 0f, ref count);
            Add(sensor, player.IsHazardControlLocked ? 1f : 0f, ref count);

            TraversalRing nearestRing = FindStage3RouteRing(
                drone.WorldCenter,
                objective,
                out float ringDistance);
            bool ringValid = nearestRing != null;
            Add(sensor, ringValid ? 1f : 0f, ref count);
            AddVector(sensor, ringValid
                ? ToCommandLocal(nearestRing.transform.position - drone.WorldCenter) / Mathf.Max(1f, _settings.RingDistanceScale)
                : Vector3.zero, ref count);
            Add(sensor, ringValid ? NormalizeDistance(ringDistance, _settings.RingDistanceScale) : 0f, ref count);
            AddOneHot(sensor, ringValid ? (int)nearestRing.RingType : -1, 5, ref count);

            EnemyCombatTarget target = _bootstrap.TargetDetector != null
                ? _bootstrap.TargetDetector.SelectedTarget
                : null;
            bool targetValid = target != null && target.IsValid;
            Add(sensor, targetValid ? 1f : 0f, ref count);
            Vector3 targetDelta = targetValid ? target.AimPoint - drone.WorldCenter : Vector3.zero;
            AddVector(sensor, targetValid
                ? ToCommandLocal(targetDelta) / Mathf.Max(1f, _settings.CombatDistanceScale)
                : Vector3.zero, ref count);
            Add(sensor, targetValid
                ? NormalizeDistance(targetDelta.magnitude, _settings.CombatDistanceScale)
                : 0f, ref count);
            AddVector(sensor, targetValid
                ? ToCommandLocal(target.Velocity) / Mathf.Max(1f, _settings.VelocityScale)
                : Vector3.zero, ref count);
            Add(sensor, targetValid && target.IsPriorityTarget ? 1f : 0f, ref count);
            int lockState = _bootstrap.LockOnController != null ? (int)_bootstrap.LockOnController.State : -1;
            AddOneHot(sensor, lockState, 4, ref count);
            Add(sensor, _bootstrap.LockOnController != null ? _bootstrap.LockOnController.AcquisitionProgress : 0f, ref count);
            Add(sensor, _bootstrap.EnergyLauncher != null && _bootstrap.EnergyLauncher.CanFire ? 1f : 0f, ref count);
            Add(sensor, targetValid ? target.NormalizedHealth : 0f, ref count);

            DuneVectorEnvironmentalHazardSystem hazards = _bootstrap.EnvironmentalHazardSystem;
            Add(sensor, hazards != null ? Mathf.Clamp01(hazards.HeatZoneIntensity) : 0f, ref count);
            Add(sensor, hazards != null ? hazards.NormalizedTemperature : 0f, ref count);
            AddCollisionProbes(sensor, drone, ref count);

            if (count != ObservationCount)
            {
                throw new InvalidOperationException($"Dune Vector observation count is {count}, expected {ObservationCount}.");
            }
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            if (_bootstrap == null || _bootstrap.CourierGame == null)
            {
                return;
            }

            ScorePreviousAuthoritativeTick();
            _episodeSteps++;
            if (_bootstrap.CourierGame.State == CourierRunState.Hub)
            {
                _hubSteps++;
            }

            if (ShouldEndEpisode(out bool hubTimeout, out bool stage2Failure))
            {
                _hubTimedOut = hubTimeout && !_hubStuck;
                _stage2TimedOut = stage2Failure &&
                    _episodeSteps >= _settings.Stage2StepBudget;
                _postDeploymentStuck = _deployed && stage2Failure &&
                    (_objectiveNoProgress || (_stage2TimedOut && !_objectiveDiverged));
                if (hubTimeout)
                {
                    AddTrainingReward(-_settings.HubTimeoutPenalty, shaped: true);
                }
                else if (stage2Failure)
                {
                    AddTrainingReward(-(_objectiveNoProgress
                        ? _settings.Stage2NoProgressPenalty
                        : _settings.Stage2DivergencePenalty), shaped: true);
                }
                else if (_curriculumStage == 3 &&
                    (_episodeSteps >= GetStage3StepBudget() ||
                     (_stage3SelectedRingActivated &&
                      _stage3PostRingStepsWithoutProgress >=
                        _settings.Stage3PostRingNoProgressStepBudget)) &&
                    !IsStage3Success())
                {
                    _stage3TimedOut = true;
                    AddTrainingReward(-_settings.Stage3TimeoutPenalty, shaped: true);
                }
                PublishEpisodeMetrics();
                EndEpisode();
                return;
            }

            ActionSegment<int> discrete = actions.DiscreteActions;
            bool hubCurriculum = _curriculumStage == 1 &&
                _bootstrap.CourierGame.State == CourierRunState.Hub;
            bool groundCurriculum = _curriculumStage == 2;
            bool combatCurriculum = _curriculumStage >= 5;
            bool stage2FlightRecovery = groundCurriculum &&
                _bootstrap.Drone.CurrentMode == DroneTraversalMode.Flight;
            bool contractHubCurriculum = _bootstrap.CourierGame.State == CourierRunState.Hub;
            bool stage1ContractInteractValid = !contractHubCurriculum ||
                (_bootstrap.CourierGame.HubTerminalMenuKind == 0 &&
                 _bootstrap.CourierGame.TryGetContractTerminalObservation(
                     out _, out float contractDistance, out float contractRadius) &&
                 contractDistance <= contractRadius);
            bool stage1ContractConfirmValid = !contractHubCurriculum ||
                _bootstrap.CourierGame.HubTerminalConfirmValid;
            Vector2 lookCommand = new Vector2(
                DecodeAxis(discrete[2]), DecodeAxis(discrete[3]));
            bool useStage3RingSteering = _curriculumStage >= 3 &&
                _bootstrap.CourierGame.IsCarryingCargo;
            DroneRawInputFrame command = new DroneRawInputFrame
            {
                Move = Vector2.ClampMagnitude(new Vector2(
                    DecodeAxis(discrete[0]), DecodeAxis(discrete[1])), 1f),
                // Preserve the established Stage 1/2 checkpoint semantics through
                // deployment and pickup. Once Stage 3's genuinely new ring-routing
                // phase begins, interpret the same normalized policy branches as a
                // held controller stick so the agent can turn at the configured rate.
                LookDelta = useStage3RingSteering ? Vector2.zero : lookCommand,
                LookRate = useStage3RingSteering ? lookCommand : Vector2.zero,
                JumpPressed = !hubCurriculum && !groundCurriculum && Pulse(discrete[4] != 0, 4),
                JumpHeld = (!hubCurriculum && !groundCurriculum || stage2FlightRecovery) &&
                    discrete[4] != 0,
                BoostHeld = !hubCurriculum && !groundCurriculum && discrete[5] != 0,
                FirePressed = combatCurriculum && Pulse(discrete[6] != 0, 6),
                InteractPressed = Pulse(discrete[7] != 0 && stage1ContractInteractValid, 7),
                MenuNavigate = PulseSigned(discrete[8] - 1, 8),
                ConfirmPressed = Pulse(discrete[9] != 0 && stage1ContractConfirmValid, 9),
                CancelPressed = !contractHubCurriculum && Pulse(discrete[10] != 0, 10),
            };
            if (command.FirePressed)
            {
                _shots++;
            }
            _bootstrap.Player.SetAutomatedInput(command);
        }

        private static float DecodeAxis(int value)
        {
            return Mathf.Clamp(value, 0, 6) / 3f - 1f;
        }

        private static int EncodeAxis(float value)
        {
            return Mathf.Clamp(Mathf.RoundToInt((Mathf.Clamp(value, -1f, 1f) + 1f) * 3f), 0, 6);
        }

        private bool Pulse(bool active, int index)
        {
            bool rising = active && _previousActions[index] <= 0f;
            bool repeated = active && _episodeSteps >= _nextPulseStep[index];
            _previousActions[index] = active ? 1f : 0f;
            if (!rising && !repeated) return false;
            _nextPulseStep[index] = _episodeSteps + 5;
            return true;
        }

        private float PulseSigned(int value, int index)
        {
            if (Mathf.Abs(value) <= 0.1f)
            {
                _previousActions[index] = value;
                return 0f;
            }
            bool changedDirection = Mathf.Sign(value) != Mathf.Sign(_previousActions[index]);
            bool repeated = _episodeSteps >= _nextPulseStep[index];
            _previousActions[index] = value;
            if (!changedDirection && !repeated) return 0f;
            _nextPulseStep[index] = _episodeSteps + 5;
            return Mathf.Sign(value);
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            ActionSegment<int> actions = actionsOut.DiscreteActions;
            DroneRawInputFrame command = _bootstrap != null && _bootstrap.Player != null
                ? _bootstrap.Player.InputSource.Current
                : default;
            actions[0] = EncodeAxis(command.Move.x);
            actions[1] = EncodeAxis(command.Move.y);
            actions[2] = EncodeAxis(command.LookDelta.x);
            actions[3] = EncodeAxis(command.LookDelta.y);
            actions[4] = command.JumpHeld || command.JumpPressed ? 1 : 0;
            actions[5] = command.BoostHeld ? 1 : 0;
            actions[6] = command.FirePressed ? 1 : 0;
            actions[7] = command.InteractPressed ? 1 : 0;
            actions[8] = command.MenuNavigate < -0.1f ? 0 : command.MenuNavigate > 0.1f ? 2 : 1;
            actions[9] = command.ConfirmPressed ? 1 : 0;
            actions[10] = command.CancelPressed ? 1 : 0;
        }

        private void ScorePreviousAuthoritativeTick()
        {
            DuneVectorCourierGame courier = _bootstrap.CourierGame;
            float health = _bootstrap.DroneHealth != null ? _bootstrap.DroneHealth.CurrentHealth : 0f;
            float healthLoss = Mathf.Max(0f, _previousHealth - health);
            if (healthLoss > 0f)
            {
                _damageTaken += healthLoss;
                AddTrainingReward(-healthLoss * _settings.DamagePenaltyPerHealth, shaped: false);
            }

            bool deploymentStarted = !_deployed &&
                _previousRunState == CourierRunState.Hub &&
                courier.State == CourierRunState.TeleportingToDesert;
            if (!_terminalOpened && _previousHubTerminalMenuKind == 0 &&
                courier.HubTerminalMenuKind == 1)
            {
                _terminalOpened = true;
                AddTrainingReward(_settings.HubTerminalOpenedReward, shaped: true);
            }
            if (deploymentStarted)
            {
                bool validStage1Contract = _curriculumStage != 1 ||
                    _previousHubTerminalMenuKind == 1;
                if (validStage1Contract)
                {
                    _deployed = true;
                    _stepsToDeploy = _hubSteps;
                    AddTrainingReward(_settings.ValidDeploymentReward, shaped: true);
                }
                else
                {
                    _wrongStage1Deployment = true;
                    AddTrainingReward(-_settings.HubTimeoutPenalty, shaped: true);
                }
            }

            if (!_evaluation && courier.State == CourierRunState.Hub && courier.HubTerminalMenuKind == 0 &&
                TryGetTrainingHubObjective(courier, out float hubDistance))
            {
                AddPotentialDifference(_previousHubDistance, hubDistance, _settings.HubPotentialScale);
                _previousHubDistance = hubDistance;
            }

            if (!_deployed && courier.State == CourierRunState.Hub && courier.HubTerminalMenuKind == 0 &&
                TryGetTrainingHubObjective(courier, out float stuckDistance))
            {
                if (stuckDistance <= _bestHubDistance - _settings.HubStuckMinimumProgress)
                {
                    _bestHubDistance = stuckDistance;
                    _hubStepsWithoutProgress = 0;
                }
                else
                {
                    _hubStepsWithoutProgress++;
                }
            }

            FreeRoamDeliveryPhase phase = courier.FreeRoamDeliveries != null
                ? courier.FreeRoamDeliveries.Phase
                : FreeRoamDeliveryPhase.Inactive;
            int contractPickupSequence = courier.PickupSequence;
            int freeRoamPickupSequence = courier.FreeRoamDeliveries != null
                ? courier.FreeRoamDeliveries.PickupSequence
                : 0;
            bool authoritativePickup = contractPickupSequence > _previousContractPickupSequence ||
                freeRoamPickupSequence > _previousFreeRoamPickupSequence;
            bool pickupCompleted = authoritativePickup ||
                (_previousFreeRoamPhase == FreeRoamDeliveryPhase.Pickup &&
                    phase == FreeRoamDeliveryPhase.Deliver) ||
                (_previousRunState == CourierRunState.FindPackage &&
                    courier.State == CourierRunState.Delivering);
            if (pickupCompleted && !float.IsInfinity(_bestObjectiveDistance))
            {
                // The active objective switches to the delivery destination on the
                // pickup tick. Preserve the completed pickup approach before the
                // objective-change reset below so evaluation reports comparable
                // pickup geometry rather than distance to the next destination.
                _pickupObjectiveMinDistance = _bestObjectiveDistance;
            }

            Transform objective = ResolveActiveObjective(courier);
            if (objective != null && _curriculumStage >= 2)
            {
                if (objective != _previousObjective)
                {
                    _previousObjective = objective;
                    _previousObjectiveDistance = float.NaN;
                    _bestObjectiveDistance = float.PositiveInfinity;
                    _objectiveStepsWithoutProgress = 0;
                    _objectiveDiverged = false;
                    _objectiveNoProgress = false;
                    _objectivePotentialReward = 0f;
                }
                float objectiveDistance = Vector3.Distance(_bootstrap.Drone.WorldCenter, objective.position);
                AddCappedObjectivePotential(_previousObjectiveDistance, objectiveDistance);
                ScoreStage2ObjectiveTracking(objective.position, objectiveDistance);
                _previousObjectiveDistance = objectiveDistance;
            }

            ScoreStage3RingProgress();
            ScoreStage3RouteHeading(objective);
            ScoreStage3DeliveryProgress(objective);

            if (pickupCompleted)
            {
                _pickups++;
                AddTrainingReward(_settings.PickupReward, shaped: false);
                _unshapedReturn += 1f;
            }

            int streak = courier.FreeRoamDeliveries != null ? courier.FreeRoamDeliveries.Streak : 0;
            int completed = courier.Progress != null ? courier.Progress.CompletedDeliveries : 0;
            int newDeliveries = Mathf.Max(0, streak - _previousFreeRoamStreak) +
                Mathf.Max(0, completed - _previousCompletedDeliveries);
            if (newDeliveries > 0)
            {
                _deliveries += newDeliveries;
                AddTrainingReward(newDeliveries * _settings.DeliveryReward, shaped: false);
                _unshapedReturn += newDeliveries * 10f;
            }

            int ringActivations = GetTotalRingActivations();
            int newRewardable = ConsumeNewRewardableRingActivations();
            if (newRewardable > 0)
            {
                _rewardedRingActivations += newRewardable;
                float ringReward = _curriculumStage == 3
                    ? _settings.Stage3UsefulRingReward
                    : _settings.UsefulRingReward;
                AddTrainingReward(newRewardable * ringReward, shaped: false);
                _unshapedReturn += newRewardable;
            }

            TrackCombat();
            AddTrainingReward(-_settings.StepPenalty, shaped: true);

            if (_bootstrap.DroneHealth != null && _bootstrap.DroneHealth.IsDead && _previousHealth > 0f)
            {
                _deaths++;
                AddTrainingReward(-_settings.DeathPenalty, shaped: false);
                _unshapedReturn -= 10f;
            }

            _previousHealth = health;
            _previousFreeRoamPhase = phase;
            _previousFreeRoamStreak = streak;
            _previousCompletedDeliveries = completed;
            _previousContractPickupSequence = contractPickupSequence;
            _previousFreeRoamPickupSequence = freeRoamPickupSequence;
            _previousRunState = courier.State;
            _previousHubTerminalMenuKind = courier.HubTerminalMenuKind;
            _previousRingActivations = ringActivations;
        }

        private void TrackCombat()
        {
            EnemyCombatTarget target = _bootstrap.TargetDetector != null
                ? _bootstrap.TargetDetector.SelectedTarget
                : null;
            if (_previousTarget != null)
            {
                float current = _previousTarget.IsValid ? _previousTarget.NormalizedHealth : 0f;
                float damage = Mathf.Max(0f, _previousTargetHealth - current);
                _combatDamage += damage;
                if (_previousTargetHealth > 0f && current <= 0f)
                {
                    _combatDefeats++;
                    _unshapedReturn += 2f;
                }
            }
            _previousTarget = target;
            _previousTargetHealth = target != null && target.IsValid ? target.NormalizedHealth : 0f;
        }

        private bool ShouldEndEpisode(out bool hubTimeout, out bool stage2Failure)
        {
            _hubStuck = !_deployed && _hubStepsWithoutProgress >= _settings.HubStuckStepBudget;
            hubTimeout = !_deployed && (_hubSteps >= _settings.HubStepBudget || _hubStuck);
            stage2Failure = _curriculumStage == 2 && _pickups == 0 &&
                (_objectiveDiverged || _objectiveNoProgress ||
                    _episodeSteps >= _settings.Stage2StepBudget);
            bool stage3Timeout = _curriculumStage == 3 &&
                (_episodeSteps >= GetStage3StepBudget() ||
                 (_stage3SelectedRingActivated &&
                  _stage3PostRingStepsWithoutProgress >=
                    _settings.Stage3PostRingNoProgressStepBudget));
            bool stage4Timeout = _curriculumStage == 4 &&
                _episodeSteps >= _settings.Stage4StepBudget;
            if (hubTimeout || _wrongStage1Deployment || stage2Failure ||
                _episodeSteps >= _settings.EpisodeStepBudget ||
                stage3Timeout || stage4Timeout ||
                (_bootstrap.DroneHealth != null && _bootstrap.DroneHealth.IsDead))
            {
                return true;
            }
            return (_curriculumStage == 1 && _deployed) ||
                (_curriculumStage == 2 && _pickups > 0) ||
                (_curriculumStage == 3 && IsStage3Success()) ||
                (_curriculumStage == 4 && _deliveries > 0);
        }

        private int GetStage3StepBudget()
        {
            int trainingBudget = Mathf.Max(1, _settings.Stage3StepBudget);
            return _evaluation
                ? Mathf.Min(trainingBudget, Mathf.Max(1, _settings.Stage3EvaluationStepBudget))
                : trainingBudget;
        }

        private void PublishEpisodeMetrics()
        {
            StatsRecorder stats = Academy.Instance.StatsRecorder;
            bool endedInHub = _bootstrap.CourierGame.State == CourierRunState.Hub;
            bool verifiedHubStuck = _hubStuck && !_deployed && endedInHub;
            bool verifiedPostDeploymentStuck = _postDeploymentStuck && _deployed && !endedInHub;
            stats.Add("Dune/deployment_success", _deployed ? 1f : 0f);
            stats.Add("Dune/deployment_steps", _deployed ? _stepsToDeploy : _hubSteps);
            stats.Add("Dune/deployment_seconds", (_deployed ? _stepsToDeploy : _hubSteps) * _settings.FixedTickSeconds);
            stats.Add("Dune/ended_in_hub", endedInHub ? 1f : 0f);
            stats.Add("Dune/hub_stuck", verifiedHubStuck ? 1f : 0f);
            stats.Add("Dune/hub_timeout", _hubTimedOut ? 1f : 0f);
            stats.Add("Dune/terminal_open_success", _terminalOpened ? 1f : 0f);
            // Stage 1 deliberately has no pickup objective. Omitting this metric
            // prevents rehearsal episodes from being averaged as pickup failures.
            if (_curriculumStage >= 2)
            {
                stats.Add("Dune/pickup_success", _pickups > 0 ? 1f : 0f);
            }
            stats.Add("Dune/delivery_success", _deliveries > 0 ? 1f : 0f);
            stats.Add("Dune/deliveries", _deliveries);
            stats.Add("Dune/damage_taken", _damageTaken);
            stats.Add("Dune/deaths", _deaths);
            stats.Add("Dune/combat_damage", _combatDamage);
            stats.Add("Dune/combat_defeats", _combatDefeats);
            stats.Add("Dune/shots", _shots);
            stats.Add("Dune/unshaped_return", _unshapedReturn);
            stats.Add("Dune/shaped_training_return", _trainingReturn);
            stats.Add("Dune/curriculum_stage", _curriculumStage);
            stats.Add("Dune/stage2_distance_scale", _curriculumStage == 2
                ? DuneTrainingRuntime.ReadStage2DistanceScale()
                : 0f);
            stats.Add("Dune/rewarded_ring_activations", _rewardedRingActivations);
            stats.Add("Dune/stage3_success", _curriculumStage == 3 &&
                IsStage3Success() ? 1f : 0f);
            stats.Add("Dune/stage3_selected_ring_activated",
                _stage3SelectedRingActivated ? 1f : 0f);
            stats.Add("Dune/stage3_delivery_progress", _stage3DeliveryProgress);
            stats.Add("Dune/stage3_timeout", _stage3TimedOut ? 1f : 0f);
            stats.Add("Dune/stage3_post_ring_no_progress_steps",
                _stage3PostRingStepsWithoutProgress);
            stats.Add("Dune/stage3_ring_min_distance", float.IsInfinity(_stage3RingMinDistance)
                ? 0f
                : _stage3RingMinDistance);
            float reportedObjectiveMinDistance = _pickups > 0 &&
                !float.IsInfinity(_pickupObjectiveMinDistance)
                    ? _pickupObjectiveMinDistance
                    : _bestObjectiveDistance;
            stats.Add("Dune/objective_min_distance", float.IsInfinity(reportedObjectiveMinDistance)
                ? 0f
                : reportedObjectiveMinDistance);
            stats.Add("Dune/stage2_diverged", _objectiveDiverged ? 1f : 0f);
            stats.Add("Dune/stage2_no_progress", _objectiveNoProgress ? 1f : 0f);
            stats.Add("Dune/stage2_timeout", _stage2TimedOut ? 1f : 0f);
            stats.Add("Dune/post_deployment_stuck", verifiedPostDeploymentStuck ? 1f : 0f);
        }

        private void CaptureBaseline()
        {
            DuneVectorCourierGame courier = _bootstrap.CourierGame;
            _previousHealth = _bootstrap.DroneHealth != null ? _bootstrap.DroneHealth.CurrentHealth : 0f;
            _previousRunState = courier.State;
            _previousHubTerminalMenuKind = courier.HubTerminalMenuKind;
            _previousFreeRoamPhase = courier.FreeRoamDeliveries != null
                ? courier.FreeRoamDeliveries.Phase
                : FreeRoamDeliveryPhase.Inactive;
            _previousFreeRoamStreak = courier.FreeRoamDeliveries != null ? courier.FreeRoamDeliveries.Streak : 0;
            _previousCompletedDeliveries = courier.Progress != null ? courier.Progress.CompletedDeliveries : 0;
            _previousContractPickupSequence = courier.PickupSequence;
            _previousFreeRoamPickupSequence = courier.FreeRoamDeliveries != null
                ? courier.FreeRoamDeliveries.PickupSequence
                : 0;
            _previousRingActivations = GetTotalRingActivations();
            _previousHubDistance = float.NaN;
            _previousObjectiveDistance = float.NaN;
            _previousObjective = ResolveActiveObjective(courier);
            _previousTarget = null;
            _previousTargetHealth = 0f;
        }

        private void AddTrainingReward(float reward, bool shaped)
        {
            if (shaped && _evaluation)
            {
                return;
            }
            AddReward(reward);
            _trainingReturn += reward;
        }

        private void AddPotentialDifference(float previous, float current, float scale)
        {
            if (!float.IsNaN(previous) && !float.IsInfinity(previous))
            {
                AddTrainingReward(Mathf.Clamp((previous - current) * scale, -0.01f, 0.01f), shaped: true);
            }
        }

        private void AddCappedObjectivePotential(float previous, float current)
        {
            if (float.IsNaN(previous) || float.IsInfinity(previous)) return;
            float distanceDelta = previous - current;
            float scale = _curriculumStage == 2 && distanceDelta < 0f
                ? _settings.ObjectivePotentialScale * _settings.Stage2DistanceIncreasePenaltyMultiplier
                : _settings.ObjectivePotentialScale;
            float reward = Mathf.Clamp(
                distanceDelta * scale,
                -0.01f,
                0.01f);
            if (reward > 0f)
            {
                reward = Mathf.Min(reward,
                    Mathf.Max(0f, _settings.MaximumObjectivePotentialReward - _objectivePotentialReward));
                _objectivePotentialReward += reward;
            }
            AddTrainingReward(reward, shaped: true);
        }

        private void ScoreStage3RingProgress()
        {
            if (_curriculumStage != 3 || _evaluation || _rewardedRingActivations > 0 ||
                _bootstrap.CourierGame.State == CourierRunState.Hub)
            {
                return;
            }

            Transform delivery = ResolveActiveObjective(_bootstrap.CourierGame);
            TraversalRing ring = FindStage3RouteRing(
                _bootstrap.Drone.WorldCenter,
                delivery,
                out float distance);
            if (ring == null)
            {
                _previousStage3Ring = null;
                _previousStage3RingDistance = float.NaN;
                return;
            }

            _stage3RingMinDistance = Mathf.Min(_stage3RingMinDistance, distance);
            if (ring != _previousStage3Ring)
            {
                _previousStage3Ring = ring;
                _previousStage3RingDistance = distance;
                return;
            }

            float delta = _previousStage3RingDistance - distance;
            float reward = Mathf.Clamp(
                delta * _settings.Stage3RingPotentialScale,
                -0.01f,
                0.01f);
            if (reward > 0f)
            {
                reward = Mathf.Min(
                    reward,
                    Mathf.Max(0f,
                        _settings.MaximumStage3RingPotentialReward -
                        _stage3RingPotentialReward));
                _stage3RingPotentialReward += reward;
            }
            AddTrainingReward(reward, shaped: true);
            _previousStage3RingDistance = distance;
        }

        private void ScoreStage3DeliveryProgress(Transform objective)
        {
            if (_curriculumStage != 3 || !_stage3SelectedRingActivated ||
                objective == null || !_bootstrap.CourierGame.IsCarryingCargo)
            {
                return;
            }

            float distance = Vector3.Distance(_bootstrap.Drone.WorldCenter, objective.position);
            if (float.IsNaN(_stage3DeliveryDistanceAtRing))
            {
                _stage3DeliveryDistanceAtRing = distance;
                _stage3BestDeliveryDistanceAfterRing = distance;
                _previousStage3DeliveryDistance = distance;
                return;
            }

            float signedDelta = _previousStage3DeliveryDistance - distance;
            _previousStage3DeliveryDistance = distance;
            if (signedDelta >= _settings.Stage3MinimumDeliveryProgressPerTick)
            {
                _stage3PostRingStepsWithoutProgress = 0;
            }
            else
            {
                _stage3PostRingStepsWithoutProgress++;
            }

            float previousBest = _stage3BestDeliveryDistanceAfterRing;
            _stage3BestDeliveryDistanceAfterRing = Mathf.Min(previousBest, distance);
            _stage3DeliveryProgress = Mathf.Max(
                0f,
                _stage3DeliveryDistanceAtRing - _stage3BestDeliveryDistanceAfterRing);
            float reward = Mathf.Clamp(
                signedDelta * _settings.Stage3DeliveryPotentialScale,
                -0.01f,
                0.01f);
            if (reward > 0f)
            {
                reward = Mathf.Min(
                    reward,
                    Mathf.Max(0f,
                        _settings.MaximumStage3DeliveryPotentialReward -
                        _stage3DeliveryPotentialReward));
                _stage3DeliveryPotentialReward += reward;
            }
            AddTrainingReward(reward, shaped: true);

            if (!_stage3DeliveryProgressRewarded &&
                _stage3DeliveryProgress >= _settings.Stage3RequiredDeliveryProgress)
            {
                _stage3DeliveryProgressRewarded = true;
                AddTrainingReward(_settings.Stage3DeliveryProgressReward, shaped: true);
            }
        }

        private void ScoreStage3RouteHeading(Transform deliveryObjective)
        {
            if (_curriculumStage != 3 || !_bootstrap.CourierGame.IsCarryingCargo ||
                deliveryObjective == null)
            {
                return;
            }

            Vector3 target = !_stage3SelectedRingActivated && _stage3SelectedRing != null
                ? _stage3SelectedRing.transform.position
                : deliveryObjective.position;
            Vector3 toTarget = Vector3.ProjectOnPlane(
                target - _bootstrap.Drone.WorldCenter,
                Vector3.up);
            Vector3 velocity = _bootstrap.Drone.Motor != null
                ? Vector3.ProjectOnPlane(_bootstrap.Drone.Motor.Velocity, Vector3.up)
                : Vector3.zero;
            if (toTarget.sqrMagnitude <= 0.01f || velocity.sqrMagnitude <= 0.01f)
            {
                return;
            }

            float alignment = Vector3.Dot(toTarget.normalized, velocity.normalized);
            float multiplier = !_stage3SelectedRingActivated &&
                _stage3SelectedRing != null &&
                toTarget.magnitude <= _settings.Stage3NearRingApproachDistance
                    ? _settings.Stage3NearRingHeadingMultiplier
                    : 1f;
            float reward = alignment >= 0f
                ? alignment * _settings.Stage3RouteHeadingAlignmentReward
                : alignment * _settings.Stage3RouteWrongWayPenalty;
            AddTrainingReward(reward * multiplier, shaped: true);
        }

        private void ScoreStage2ObjectiveTracking(Vector3 objectivePosition, float objectiveDistance)
        {
            if (_curriculumStage != 2 || _bootstrap.CourierGame.State == CourierRunState.Hub)
            {
                return;
            }

            Vector3 toObjective = Vector3.ProjectOnPlane(
                objectivePosition - _bootstrap.Drone.WorldCenter,
                Vector3.up);
            Vector3 velocity = _bootstrap.Drone.Motor != null
                ? Vector3.ProjectOnPlane(_bootstrap.Drone.Motor.Velocity, Vector3.up)
                : Vector3.zero;
            if (toObjective.sqrMagnitude > 0.01f && velocity.sqrMagnitude > 0.01f)
            {
                float alignment = Vector3.Dot(toObjective.normalized, velocity.normalized);
                float reward = alignment >= 0f
                    ? alignment * _settings.Stage2HeadingAlignmentReward
                    : alignment * _settings.Stage2WrongWayPenalty;
                AddTrainingReward(reward, shaped: true);

                // A broad heading reward is useful over long routes, but by itself a
                // policy can collect it while passing beside the pickup. Close to the
                // objective, explicitly charge for lateral or wrong-way velocity so
                // the final steering correction is learned before an overshoot.
                if (objectiveDistance <= _settings.Stage2NearObjectiveDistance)
                {
                    float misalignment = 1f - Mathf.Clamp01(alignment);
                    AddTrainingReward(
                        -misalignment * _settings.Stage2NearObjectiveMisalignmentPenalty,
                        shaped: true);
                }
            }

            bool hazardDisruption = _bootstrap.DustDevilSystem != null &&
                (_bootstrap.DustDevilSystem.IsControlDisruptionActive ||
                 _bootstrap.DustDevilSystem.CurrentPlayerSample.Influence >=
                    _settings.Stage2HazardRecoveryInfluenceThreshold);
            if (hazardDisruption)
            {
                _stage2HazardRecoveryStepsRemaining = _settings.Stage2HazardRecoveryGraceSteps;
                _objectiveStepsWithoutProgress = 0;
                _objectiveDiverged = false;
                _objectiveNoProgress = false;
            }
            else if (_stage2HazardRecoveryStepsRemaining > 0)
            {
                _stage2HazardRecoveryStepsRemaining--;
                _objectiveStepsWithoutProgress = 0;
                _objectiveDiverged = false;
                _objectiveNoProgress = false;
            }

            if (objectiveDistance <= _bestObjectiveDistance - _settings.Stage2MinimumProgress)
            {
                _bestObjectiveDistance = objectiveDistance;
                _objectiveStepsWithoutProgress = 0;
                _objectiveNoProgress = false;
            }
            else if (_stage2HazardRecoveryStepsRemaining > 0 || hazardDisruption)
            {
                return;
            }
            else if (objectiveDistance >= _bestObjectiveDistance + _settings.Stage2DivergenceDistance)
            {
                _objectiveStepsWithoutProgress++;
                _objectiveDiverged = _objectiveStepsWithoutProgress >=
                    _settings.Stage2DivergenceStepBudget;
            }
            else
            {
                _objectiveStepsWithoutProgress++;
                _objectiveNoProgress = _objectiveStepsWithoutProgress >=
                    _settings.Stage2NoProgressStepBudget;
            }
        }

        private Transform ResolveActiveObjective(DuneVectorCourierGame courier)
        {
            if (courier.ActiveObjective != null)
            {
                return courier.ActiveObjective;
            }
            return courier.FreeRoamDeliveries != null ? courier.FreeRoamDeliveries.ActiveObjective : null;
        }

        private Vector3 ToCommandLocal(Vector3 worldVector)
        {
            Quaternion rotation = _bootstrap.DroneCamera != null
                ? _bootstrap.DroneCamera.transform.rotation
                : _bootstrap.Drone.transform.rotation;
            return Quaternion.Inverse(rotation) * worldVector;
        }

        private TraversalRing FindStage3RouteRing(
            Vector3 origin,
            Transform objective,
            out float distance)
        {
            if (_curriculumStage == 3)
            {
                if (!_bootstrap.CourierGame.IsCarryingCargo || objective == null)
                {
                    distance = float.PositiveInfinity;
                    return null;
                }
                if (_stage3SelectedRing != null &&
                    _stage3SelectedRing.isActiveAndEnabled &&
                    !_stage3SelectedRingActivated)
                {
                    distance = Vector3.Distance(origin, _stage3SelectedRing.transform.position);
                    return _stage3SelectedRing;
                }
            }

            TraversalRing nearest = null;
            distance = float.PositiveInfinity;
            float bestScore = float.PositiveInfinity;
            float directDistance = objective != null
                ? Vector3.Distance(origin, objective.position)
                : 0f;
            foreach (TraversalRing ring in TraversalRing.ActiveRings)
            {
                if (ring == null || !ring.isActiveAndEnabled || !IsRingObservable(ring)) continue;
                float candidate = Vector3.Distance(origin, ring.transform.position);
                float score = candidate;
                if (_curriculumStage == 3 && objective != null)
                {
                    float detour = candidate +
                        Vector3.Distance(ring.transform.position, objective.position) -
                        directDistance;
                    if (detour > _settings.Stage3MaximumRingDetour) continue;
                    score = candidate + Mathf.Max(0f, detour);
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    nearest = ring;
                    distance = candidate;
                }
            }
            if (_curriculumStage == 3 && nearest != null)
            {
                SetStage3SelectedRing(nearest);
            }
            return nearest;
        }

        private void SetStage3SelectedRing(TraversalRing ring)
        {
            if (_stage3SelectedRing == ring) return;
            if (_stage3SelectedRing != null)
            {
                _stage3SelectedRing.Activated -= HandleStage3SelectedRingActivated;
            }
            _stage3SelectedRing = ring;
            _stage3SelectedRingActivationPending = false;
            if (_stage3SelectedRing != null)
            {
                _stage3SelectedRing.Activated += HandleStage3SelectedRingActivated;
            }
        }

        private void HandleStage3SelectedRingActivated(TraversalRing ring)
        {
            if (ring == _stage3SelectedRing && !_stage3SelectedRingActivated)
            {
                _stage3SelectedRingActivationPending = true;
            }
        }

        private bool IsStage3Success()
        {
            return _pickups > 0 &&
                _stage3SelectedRingActivated &&
                _stage3DeliveryProgress >= _settings.Stage3RequiredDeliveryProgress;
        }

        private int GetTotalRingActivations()
        {
            int total = 0;
            foreach (TraversalRing ring in TraversalRing.ActiveRings)
            {
                if (ring != null) total += ring.ActivationCount;
            }
            return total;
        }

        private bool TryGetTrainingHubObjective(DuneVectorCourierGame courier, out float distance)
        {
            if (courier.State == CourierRunState.Hub)
            {
                return courier.TryGetContractTerminalObservation(out _, out distance, out _);
            }
            return courier.TryGetHubInteractionObservation(out _, out _, out distance, out _);
        }

        private int ConsumeNewRewardableRingActivations()
        {
            int rewardable = 0;
            if (_stage3SelectedRingActivationPending && _stage3SelectedRing != null)
            {
                _stage3SelectedRingActivationPending = false;
                _observedRingActivations[_stage3SelectedRing] =
                    _stage3SelectedRing.ActivationCount;
                MarkStage3SelectedRingActivated();
                rewardable++;
            }
            foreach (TraversalRing ring in TraversalRing.ActiveRings)
            {
                if (ring == null) continue;
                _observedRingActivations.TryGetValue(ring, out int previous);
                int delta = Mathf.Max(0, ring.ActivationCount - previous);
                _observedRingActivations[ring] = ring.ActivationCount;
                if (delta > 0 && IsRingCurrentlyUseful(ring))
                {
                    if (_curriculumStage != 3 || ring == _stage3SelectedRing)
                    {
                        rewardable += delta;
                        if (_curriculumStage == 3)
                        {
                            MarkStage3SelectedRingActivated();
                        }
                    }
                }
            }
            return rewardable;
        }

        private void MarkStage3SelectedRingActivated()
        {
            if (_stage3SelectedRingActivated) return;
            _stage3SelectedRingActivated = true;
            _stage3DeliveryDistanceAtRing = float.NaN;
            _previousStage3DeliveryDistance = float.NaN;
            _stage3PostRingStepsWithoutProgress = 0;
        }

        private void OnDestroy()
        {
            SetStage3SelectedRing(null);
        }

        private bool IsRingCurrentlyUseful(TraversalRing ring)
        {
            if (_curriculumStage == 3)
            {
                return ring.RingType == TraversalRingType.Flight ||
                    ring.RingType == TraversalRingType.UpperFlight;
            }
            return _curriculumStage >= 5 && _bootstrap.VesperKiteDirector != null &&
                _bootstrap.VesperKiteDirector.ActivePilgrimCount > 0;
        }

        private bool IsRingObservable(TraversalRing ring)
        {
            if (_curriculumStage < 3) return false;
            bool flightUtility = ring.RingType == TraversalRingType.Flight ||
                ring.RingType == TraversalRingType.UpperFlight;
            bool missileDefense = _bootstrap.VesperKiteDirector != null &&
                _bootstrap.VesperKiteDirector.ActivePilgrimCount > 0;
            return flightUtility || missileDefense;
        }

        private void AddCollisionProbes(VectorSensor sensor, DroneCharacterController drone, ref int count)
        {
            Vector3 origin = drone.WorldCenter;
            Quaternion frame = _bootstrap.DroneCamera != null
                ? Quaternion.Euler(0f, _bootstrap.DroneCamera.transform.eulerAngles.y, 0f)
                : Quaternion.Euler(0f, drone.transform.eulerAngles.y, 0f);
            float distance = Mathf.Max(1f, _settings.ProbeDistance);
            for (int i = 0; i < 7; i++)
            {
                float angle = -90f + (i * 30f);
                Vector3 direction = frame * Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                bool hit = TryProbeObstacle(origin, direction, distance, drone, out RaycastHit hitInfo);
                Add(sensor, hit ? 1f - Mathf.Clamp01(hitInfo.distance / distance) : 0f, ref count);
            }
            bool ground = TryProbeObstacle(origin, Vector3.down, distance, drone, out RaycastHit groundHit);
            Add(sensor, ground ? 1f - Mathf.Clamp01(groundHit.distance / distance) : 0f, ref count);
        }

        private bool TryProbeObstacle(
            Vector3 origin,
            Vector3 direction,
            float distance,
            DroneCharacterController drone,
            out RaycastHit nearest)
        {
            nearest = default;
            float nearestDistance = float.PositiveInfinity;
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                direction,
                distance,
                -1,
                QueryTriggerInteraction.Ignore);
            Transform droneRoot = drone != null ? drone.transform : null;
            Transform playerRoot = _bootstrap.Player != null ? _bootstrap.Player.transform : null;
            for (int i = 0; i < hits.Length; i++)
            {
                Transform candidate = hits[i].collider != null
                    ? hits[i].collider.transform
                    : null;
                if (candidate == null ||
                    (droneRoot != null && (candidate == droneRoot || candidate.IsChildOf(droneRoot))) ||
                    (playerRoot != null && (candidate == playerRoot || candidate.IsChildOf(playerRoot))))
                {
                    continue;
                }
                if (hits[i].distance < nearestDistance)
                {
                    nearestDistance = hits[i].distance;
                    nearest = hits[i];
                }
            }
            return !float.IsInfinity(nearestDistance);
        }

        private static int ResolveEpisodePhase(DuneVectorCourierGame courier)
        {
            if (DuneVectorBootstrap.Instance != null && DuneVectorBootstrap.Instance.DroneHealth != null &&
                DuneVectorBootstrap.Instance.DroneHealth.IsDead) return 5;
            if (courier.State == CourierRunState.Hub) return courier.IsTerminalOpen ? 1 : 0;
            if (courier.State == CourierRunState.TeleportingToDesert ||
                courier.State == CourierRunState.ReturnToBase || courier.State == CourierRunState.TeleportOut) return 2;
            if (courier.State == CourierRunState.FreeRoam || courier.State == CourierRunState.FindPackage ||
                courier.State == CourierRunState.Delivering) return 3;
            return 4;
        }

        private static float NormalizeDistance(float value, float scale) => Mathf.Clamp(value / Mathf.Max(1f, scale), 0f, 2f);
        private static float Has(CourierContractModifier value, CourierContractModifier flag) => value.HasFlag(flag) ? 1f : 0f;
        private static void Add(VectorSensor sensor, float value, ref int count) { sensor.AddObservation(value); count++; }
        private static void AddVector(VectorSensor sensor, Vector3 value, ref int count)
        {
            sensor.AddObservation(value); count += 3;
        }
        private static void AddOneHot(VectorSensor sensor, int selected, int size, ref int count)
        {
            for (int i = 0; i < size; i++) Add(sensor, i == selected ? 1f : 0f, ref count);
        }
    }

    public static class DuneTrainingRuntime
    {
        public static bool Enabled => HasArgument("--dune-training");
        public static bool Evaluation => HasArgument("--dune-evaluation");
        public static bool VisualEvaluation => Evaluation && HasArgument("--dune-visual-evaluation");
        public static bool ControlledGroundStage => Enabled && ReadCurriculumStage() == 2;
        public static bool ControlledPreHazardStage => Enabled && ReadCurriculumStage() >= 2 &&
            ReadCurriculumStage() <= 4;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureHeadlessRuntime()
        {
            if (!Enabled) return;
            Time.fixedDeltaTime = 0.05f;
            Time.maximumDeltaTime = 0.05f;
            Time.captureDeltaTime = VisualEvaluation ? 0f : 0.05f;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = VisualEvaluation ? 60 : -1;
            Application.runInBackground = true;
            AudioListener.pause = !VisualEvaluation;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateTrainingDriver()
        {
            if (!Enabled) return;
            GameObject driver = new GameObject("Dune Vector Headless Training Driver");
            UnityEngine.Object.DontDestroyOnLoad(driver);
            driver.AddComponent<DuneTrainingDriver>();
        }

        private static bool HasArgument(string expected)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static int ReadCurriculumStage()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(arguments[i], "--dune-curriculum-stage", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(arguments[i + 1], out int stage)) return Mathf.Clamp(stage, 1, 6);
            }
            return 6;
        }

        public static int ReadWorldSeed(int fallback)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(arguments[i], "--dune-world-seed", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(arguments[i + 1], out int seed)) return seed;
            }
            return fallback;
        }

        public static float ReadStage2DistanceScale()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(
                        arguments[i],
                        "--dune-stage2-distance-scale",
                        StringComparison.OrdinalIgnoreCase) &&
                    float.TryParse(
                        arguments[i + 1],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float scale))
                {
                    return Mathf.Clamp01(scale);
                }
            }
            return Evaluation ? 1f : 0f;
        }
    }

    [DefaultExecutionOrder(2000)]
    public sealed class DuneTrainingDriver : MonoBehaviour
    {
        private IEnumerator Start()
        {
            while (DuneVectorBootstrap.Instance == null ||
                   DuneVectorBootstrap.Instance.CourierGame == null ||
                   DuneVectorBootstrap.Instance.Player == null)
            {
                yield return null;
            }

            DuneVectorBootstrap bootstrap = DuneVectorBootstrap.Instance;
            PufferTrainingTuning settings = bootstrap.PufferTraining;
            Time.fixedDeltaTime = settings.FixedTickSeconds;
            Time.maximumDeltaTime = settings.FixedTickSeconds;
            Time.captureDeltaTime = DuneTrainingRuntime.VisualEvaluation
                ? 0f
                : settings.FixedTickSeconds;

            BehaviorParameters behavior = bootstrap.gameObject.AddComponent<BehaviorParameters>();
            behavior.BehaviorName = "DuneVector";
            behavior.BehaviorType = BehaviorType.Default;
            behavior.BrainParameters.VectorObservationSize = DuneVectorTrainingAgent.ObservationCount;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(
                DuneVectorTrainingAgent.ActionBranches);

            DuneVectorTrainingAgent agent = bootstrap.gameObject.AddComponent<DuneVectorTrainingAgent>();
            agent.Bind(bootstrap, DuneTrainingRuntime.ReadCurriculumStage(), DuneTrainingRuntime.Evaluation);
            bootstrap.Player.SetHumanGameplayInputSuppressed(true);
            DecisionRequester requester = bootstrap.gameObject.AddComponent<DecisionRequester>();
            requester.DecisionPeriod = 1;
            requester.TakeActionsBetweenDecisions = true;

            if (!DuneTrainingRuntime.VisualEvaluation)
            {
                DisablePresentation(bootstrap);
            }
        }

        private static void DisablePresentation(DuneVectorBootstrap bootstrap)
        {
            foreach (DuneVectorSpatialInstancing instancing in UnityEngine.Object.FindObjectsByType<DuneVectorSpatialInstancing>())
                instancing.enabled = false;
            foreach (DuneVectorProceduralBuildingDirector buildings in UnityEngine.Object.FindObjectsByType<DuneVectorProceduralBuildingDirector>())
                buildings.enabled = false;
            foreach (Camera camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) camera.enabled = false;
            foreach (AudioListener listener in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) listener.enabled = false;
            foreach (AudioSource source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) source.enabled = false;
            foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = false;
            foreach (ParticleSystem particles in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
                if (particleRenderer != null) particleRenderer.forceRenderingOff = true;
            }
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) renderer.forceRenderingOff = true;
            foreach (Behaviour behaviour in UnityEngine.Object.FindObjectsByType<Behaviour>(FindObjectsSortMode.None))
            {
                if (behaviour == null || behaviour == bootstrap || behaviour is DuneVectorTrainingAgent ||
                    behaviour is DecisionRequester || behaviour is DuneTrainingDriver ||
                    behaviour is DronePlayer || behaviour is DroneCameraController ||
                    behaviour is DuneVectorCourierGame) continue;
                string fullName = behaviour.GetType().FullName ?? string.Empty;
                if (fullName.StartsWith("FMODUnity.", StringComparison.Ordinal) ||
                    fullName.EndsWith("HUD", StringComparison.Ordinal) ||
                    fullName.Contains("Overlay", StringComparison.Ordinal) ||
                    fullName.Contains("Swoosh", StringComparison.Ordinal) ||
                    fullName.Contains("MusicReactive", StringComparison.Ordinal))
                {
                    behaviour.enabled = false;
                }
            }
        }
    }
}
