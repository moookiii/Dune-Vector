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

        private DuneVectorBootstrap _bootstrap;
        private PufferTrainingTuning _settings;
        private int _curriculumStage;
        private bool _evaluation;
        private int _episodeSteps;
        private int _hubSteps;
        private int _stepsToDeploy;
        private bool _deployed;
        private bool _hubStuck;
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
        private float _objectivePotentialReward;
        private Transform _previousObjective;
        private int _previousFreeRoamStreak;
        private int _previousCompletedDeliveries;
        private FreeRoamDeliveryPhase _previousFreeRoamPhase;
        private CourierRunState _previousRunState;
        private int _previousRingActivations;
        private float _previousTargetHealth;
        private EnemyCombatTarget _previousTarget;
        private readonly float[] _previousActions = new float[ActionCount];
        private readonly int[] _nextPulseStep = new int[ActionCount];

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
                _bootstrap.CourierGame.RestartAtHub(playReturnEffect: false);
                _bootstrap.DroneHealth?.ReviveAtFullHealth();
                _bootstrap.Player?.SetInputEnabled(true);
            }

            _episodeSteps = 0;
            _hubSteps = 0;
            _stepsToDeploy = 0;
            _deployed = false;
            _hubStuck = false;
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
            _previousObjective = null;
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
            bool hubValid = courier.State == CourierRunState.Hub &&
                courier.TryGetHubInteractionObservation(
                    out terminalKind,
                    out terminalPosition,
                    out hubDistance,
                    out interactionRadius);
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
            AddVector(sensor, objectiveValid
                ? ToCommandLocal(objective.position - drone.WorldCenter) / Mathf.Max(1f, _settings.ObjectiveDistanceScale)
                : Vector3.zero, ref count);
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

            TraversalRing nearestRing = FindNearestUsefulRing(drone.WorldCenter, out float ringDistance);
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

            if (ShouldEndEpisode(out bool hubTimeout))
            {
                if (hubTimeout)
                {
                    AddTrainingReward(-_settings.HubTimeoutPenalty, shaped: true);
                }
                PublishEpisodeMetrics();
                EndEpisode();
                return;
            }

            ActionSegment<float> continuous = actions.ContinuousActions;
            bool hubCurriculum = _curriculumStage == 1 &&
                _bootstrap.CourierGame.State == CourierRunState.Hub;
            bool groundCurriculum = _curriculumStage == 2;
            bool stage2FlightRecovery = groundCurriculum &&
                _bootstrap.Drone.CurrentMode == DroneTraversalMode.Flight;
            DroneRawInputFrame command = new DroneRawInputFrame
            {
                Move = Vector2.ClampMagnitude(new Vector2(continuous[0], continuous[1]), 1f),
                LookDelta = new Vector2(continuous[2], continuous[3]),
                JumpPressed = !hubCurriculum && !groundCurriculum && Pulse(continuous[4], 4),
                JumpHeld = (!hubCurriculum && !groundCurriculum || stage2FlightRecovery) &&
                    continuous[4] > 0f,
                BoostHeld = !hubCurriculum && !groundCurriculum && continuous[5] > 0f,
                FirePressed = !hubCurriculum && !groundCurriculum && Pulse(continuous[6], 6),
                InteractPressed = Pulse(continuous[7], 7),
                MenuNavigate = PulseSigned(continuous[8], 8),
                ConfirmPressed = Pulse(continuous[9], 9),
                CancelPressed = Pulse(continuous[10], 10),
            };
            if (command.FirePressed)
            {
                _shots++;
            }
            _bootstrap.Player.SetAutomatedInput(command);
        }

        private bool Pulse(float value, int index)
        {
            bool active = value > 0f;
            bool rising = active && _previousActions[index] <= 0f;
            bool repeated = active && _episodeSteps >= _nextPulseStep[index];
            _previousActions[index] = value;
            if (!rising && !repeated) return false;
            _nextPulseStep[index] = _episodeSteps + 5;
            return true;
        }

        private float PulseSigned(float value, int index)
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
            ActionSegment<float> actions = actionsOut.ContinuousActions;
            DroneRawInputFrame command = _bootstrap != null && _bootstrap.Player != null
                ? _bootstrap.Player.InputSource.Current
                : default;
            actions[0] = command.Move.x;
            actions[1] = command.Move.y;
            actions[2] = Mathf.Clamp(command.LookDelta.x, -1f, 1f);
            actions[3] = Mathf.Clamp(command.LookDelta.y, -1f, 1f);
            actions[4] = command.JumpHeld || command.JumpPressed ? 1f : 0f;
            actions[5] = command.BoostHeld ? 1f : 0f;
            actions[6] = command.FirePressed ? 1f : 0f;
            actions[7] = command.InteractPressed ? 1f : 0f;
            actions[8] = command.MenuNavigate;
            actions[9] = command.ConfirmPressed ? 1f : 0f;
            actions[10] = command.CancelPressed ? 1f : 0f;
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
            if (deploymentStarted)
            {
                _deployed = true;
                _stepsToDeploy = _hubSteps;
                AddTrainingReward(_settings.ValidDeploymentReward, shaped: true);
            }

            if (!_evaluation && courier.State == CourierRunState.Hub && courier.HubTerminalMenuKind == 0 &&
                courier.TryGetHubInteractionObservation(out _, out _, out float hubDistance, out _))
            {
                AddPotentialDifference(_previousHubDistance, hubDistance, _settings.HubPotentialScale);
                _previousHubDistance = hubDistance;
            }

            if (!_deployed && courier.State == CourierRunState.Hub && courier.HubTerminalMenuKind == 0 &&
                courier.TryGetHubInteractionObservation(out _, out _, out float stuckDistance, out _))
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

            Transform objective = ResolveActiveObjective(courier);
            if (!_evaluation && objective != null && _curriculumStage >= 2)
            {
                if (objective != _previousObjective)
                {
                    _previousObjective = objective;
                    _previousObjectiveDistance = float.NaN;
                    _objectivePotentialReward = 0f;
                }
                float objectiveDistance = Vector3.Distance(_bootstrap.Drone.WorldCenter, objective.position);
                AddCappedObjectivePotential(_previousObjectiveDistance, objectiveDistance);
                _previousObjectiveDistance = objectiveDistance;
            }

            FreeRoamDeliveryPhase phase = courier.FreeRoamDeliveries != null
                ? courier.FreeRoamDeliveries.Phase
                : FreeRoamDeliveryPhase.Inactive;
            if ((_previousFreeRoamPhase == FreeRoamDeliveryPhase.Pickup && phase == FreeRoamDeliveryPhase.Deliver) ||
                (_previousRunState == CourierRunState.FindPackage && courier.State == CourierRunState.Delivering))
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
            if (_curriculumStage >= 3 && ringActivations > _previousRingActivations)
            {
                AddTrainingReward((ringActivations - _previousRingActivations) * _settings.UsefulRingReward, shaped: false);
                _unshapedReturn += ringActivations - _previousRingActivations;
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
            _previousRunState = courier.State;
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

        private bool ShouldEndEpisode(out bool hubTimeout)
        {
            _hubStuck = !_deployed && _hubStepsWithoutProgress >= _settings.HubStuckStepBudget;
            hubTimeout = !_deployed && (_hubSteps >= _settings.HubStepBudget || _hubStuck);
            if (hubTimeout || _episodeSteps >= _settings.EpisodeStepBudget ||
                (_bootstrap.DroneHealth != null && _bootstrap.DroneHealth.IsDead))
            {
                return true;
            }
            return (_curriculumStage == 1 && _deployed) ||
                (_curriculumStage == 2 && _pickups > 0);
        }

        private void PublishEpisodeMetrics()
        {
            StatsRecorder stats = Academy.Instance.StatsRecorder;
            stats.Add("Dune/deployment_success", _deployed ? 1f : 0f);
            stats.Add("Dune/deployment_steps", _deployed ? _stepsToDeploy : _hubSteps);
            stats.Add("Dune/deployment_seconds", (_deployed ? _stepsToDeploy : _hubSteps) * _settings.FixedTickSeconds);
            stats.Add("Dune/hub_stuck", _hubStuck ? 1f : 0f);
            stats.Add("Dune/pickup_success", _pickups > 0 ? 1f : 0f);
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
        }

        private void CaptureBaseline()
        {
            DuneVectorCourierGame courier = _bootstrap.CourierGame;
            _previousHealth = _bootstrap.DroneHealth != null ? _bootstrap.DroneHealth.CurrentHealth : 0f;
            _previousRunState = courier.State;
            _previousFreeRoamPhase = courier.FreeRoamDeliveries != null
                ? courier.FreeRoamDeliveries.Phase
                : FreeRoamDeliveryPhase.Inactive;
            _previousFreeRoamStreak = courier.FreeRoamDeliveries != null ? courier.FreeRoamDeliveries.Streak : 0;
            _previousCompletedDeliveries = courier.Progress != null ? courier.Progress.CompletedDeliveries : 0;
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
            float reward = Mathf.Clamp(
                (previous - current) * _settings.ObjectivePotentialScale,
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

        private TraversalRing FindNearestUsefulRing(Vector3 origin, out float distance)
        {
            TraversalRing nearest = null;
            distance = float.PositiveInfinity;
            foreach (TraversalRing ring in TraversalRing.ActiveRings)
            {
                if (ring == null || !ring.isActiveAndEnabled) continue;
                float candidate = Vector3.Distance(origin, ring.transform.position);
                if (candidate < distance)
                {
                    nearest = ring;
                    distance = candidate;
                }
            }
            return nearest;
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
                bool hit = Physics.Raycast(origin, direction, out RaycastHit hitInfo, distance, -1, QueryTriggerInteraction.Ignore);
                Add(sensor, hit ? 1f - Mathf.Clamp01(hitInfo.distance / distance) : 0f, ref count);
            }
            bool ground = Physics.Raycast(origin, Vector3.down, out RaycastHit groundHit, distance, -1, QueryTriggerInteraction.Ignore);
            Add(sensor, ground ? 1f - Mathf.Clamp01(groundHit.distance / distance) : 0f, ref count);
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
            Time.captureDeltaTime = DuneTrainingRuntime.VisualEvaluation ? 0f : settings.FixedTickSeconds;

            BehaviorParameters behavior = bootstrap.gameObject.AddComponent<BehaviorParameters>();
            behavior.BehaviorName = "DuneVector";
            behavior.BehaviorType = BehaviorType.Default;
            behavior.BrainParameters.VectorObservationSize = DuneVectorTrainingAgent.ObservationCount;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(DuneVectorTrainingAgent.ActionCount);

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
