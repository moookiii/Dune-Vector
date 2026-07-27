using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DuneVector
{
    public enum UpgradePurchaseFailure
    {
        None,
        NotInitialized,
        DefinitionMissing,
        MaximumTierReached,
        CannotAfford,
        CurrencySaveFailed,
        UpgradeSaveFailed,
    }

    public readonly struct UpgradePurchaseResult
    {
        public bool Succeeded => Failure == UpgradePurchaseFailure.None;
        public UpgradePurchaseFailure Failure { get; }
        public DroneUpgradeId UpgradeId { get; }
        public int PurchasedTier { get; }
        public int GoldCost { get; }

        public UpgradePurchaseResult(
            UpgradePurchaseFailure failure,
            DroneUpgradeId upgradeId,
            int purchasedTier,
            int goldCost)
        {
            Failure = failure;
            UpgradeId = upgradeId;
            PurchasedTier = purchasedTier;
            GoldCost = goldCost;
        }
    }

    internal sealed class DroneUpgradeTierState
    {
        private readonly Dictionary<DroneUpgradeId, int> _purchasedTiers = new Dictionary<DroneUpgradeId, int>();

        public bool HubRgbFloorUnlocked { get; set; }
        public bool HubRgbTerminalsEnabled { get; set; }
        public bool AtlasGlyphBrushedMetalEnabled { get; set; }

        public void Initialize(IReadOnlyList<DroneUpgradeDefinition> definitions)
        {
            _purchasedTiers.Clear();
            HubRgbFloorUnlocked = false;
            HubRgbTerminalsEnabled = false;
            AtlasGlyphBrushedMetalEnabled = false;
            for (int index = 0; index < definitions.Count; index++)
            {
                DroneUpgradeDefinition definition = definitions[index];
                if (definition != null && !_purchasedTiers.ContainsKey(definition.Id))
                {
                    _purchasedTiers.Add(definition.Id, 0);
                }
            }
        }

        public bool Contains(DroneUpgradeId id)
        {
            return _purchasedTiers.ContainsKey(id);
        }

        public int Get(DroneUpgradeId id)
        {
            return _purchasedTiers.TryGetValue(id, out int tier)
                ? Mathf.Clamp(tier, 0, DronePermanentUpgradeSystem.MaximumPurchasableTier)
                : 0;
        }

        public void Set(DroneUpgradeId id, int purchasedTier)
        {
            if (_purchasedTiers.ContainsKey(id))
            {
                _purchasedTiers[id] = Mathf.Clamp(
                    purchasedTier,
                    0,
                    DronePermanentUpgradeSystem.MaximumPurchasableTier);
            }
        }
    }

    internal sealed class DroneUpgradeSaveRepository
    {
        private const string UpgradeSaveFileName = "DuneVectorUpgrades.dat";

        [Serializable]
        private sealed class UpgradeTierRecord
        {
            public DroneUpgradeId Id;
            public int PurchasedTier;
        }

        [Serializable]
        private sealed class UpgradeSaveData
        {
            public int Version = 4;
            public bool HubRgbFloorUnlocked;
            public bool HubRgbTerminalsEnabled;
            public bool AtlasGlyphBrushedMetalEnabled;
            public List<UpgradeTierRecord> Tiers = new List<UpgradeTierRecord>();
        }

        private readonly string _savePath;
        private readonly UnityEngine.Object _logContext;

        public DroneUpgradeSaveRepository(UnityEngine.Object logContext)
        {
            _savePath = Path.Combine(Application.persistentDataPath, UpgradeSaveFileName);
            _logContext = logContext;
        }

        public void LoadInto(DroneUpgradeTierState state)
        {
            if (!File.Exists(_savePath))
            {
                return;
            }

            try
            {
                UpgradeSaveData saveData = JsonUtility.FromJson<UpgradeSaveData>(File.ReadAllText(_savePath));
                if (saveData?.Tiers == null)
                {
                    return;
                }

                state.HubRgbFloorUnlocked = saveData.HubRgbFloorUnlocked;
                state.HubRgbTerminalsEnabled = saveData.HubRgbFloorUnlocked
                    && (saveData.Version < 3 || saveData.HubRgbTerminalsEnabled);
                state.AtlasGlyphBrushedMetalEnabled = saveData.Version >= 4 && saveData.AtlasGlyphBrushedMetalEnabled;
                for (int index = 0; index < saveData.Tiers.Count; index++)
                {
                    UpgradeTierRecord record = saveData.Tiers[index];
                    if (record != null && state.Contains(record.Id))
                    {
                        state.Set(record.Id, record.PurchasedTier);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not load permanent upgrades from '{_savePath}': {exception.Message}", _logContext);
            }
        }

        public bool Save(DroneUpgradeTierState state, IReadOnlyList<DroneUpgradeDefinition> definitions)
        {
            try
            {
                UpgradeSaveData saveData = new UpgradeSaveData
                {
                    HubRgbFloorUnlocked = state.HubRgbFloorUnlocked,
                    HubRgbTerminalsEnabled = state.HubRgbTerminalsEnabled,
                    AtlasGlyphBrushedMetalEnabled = state.AtlasGlyphBrushedMetalEnabled,
                };
                for (int index = 0; index < definitions.Count; index++)
                {
                    DroneUpgradeDefinition definition = definitions[index];
                    if (definition == null)
                    {
                        continue;
                    }
                    saveData.Tiers.Add(new UpgradeTierRecord
                    {
                        Id = definition.Id,
                        PurchasedTier = state.Get(definition.Id),
                    });
                }
                File.WriteAllText(_savePath, JsonUtility.ToJson(saveData));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not save permanent upgrades to '{_savePath}': {exception.Message}", _logContext);
                return false;
            }
        }
    }

    internal sealed class DroneUpgradeStatApplicator
    {
        private readonly DroneCharacterController _drone;
        private readonly DroneHealth _health;
        private readonly DroneStaminaSystem _stamina;
        private readonly DroneBoostSpeedModifier _boostSpeed;

        public DroneUpgradeStatApplicator(
            DroneCharacterController drone,
            DroneHealth health,
            DroneStaminaSystem stamina,
            DroneBoostSpeedModifier boostSpeed)
        {
            _drone = drone;
            _health = health;
            _stamina = stamina;
            _boostSpeed = boostSpeed;
        }

        public void Apply(DroneUpgradeId id, float value)
        {
            switch (id)
            {
                case DroneUpgradeId.MaximumHealth:
                    _health?.SetMaximumHealth(value, true);
                    break;
                case DroneUpgradeId.MaximumStamina:
                    _stamina?.SetMaximumStamina(value, true);
                    break;
                case DroneUpgradeId.BoostMaximumSpeed:
                    _boostSpeed?.SetBoostMaximumSpeed(value);
                    break;
                case DroneUpgradeId.GroundMaximumSpeed:
                    if (_drone != null)
                    {
                        _drone.MaxGroundSpeed = Mathf.Max(0f, value);
                    }
                    break;
                case DroneUpgradeId.GroundAcceleration:
                    if (_drone != null)
                    {
                        _drone.GroundMovementSharpness = Mathf.Max(0f, value);
                    }
                    break;
                case DroneUpgradeId.GroundHandling:
                    if (_drone != null)
                    {
                        _drone.RotationSharpness = Mathf.Max(0f, value);
                    }
                    break;
                case DroneUpgradeId.FlightMaximumSpeed:
                    if (_drone != null)
                    {
                        _drone.MaximumFlightSpeed = Mathf.Max(_drone.FlightSpeed, value);
                    }
                    break;
                case DroneUpgradeId.FlightAcceleration:
                    if (_drone != null)
                    {
                        _drone.FlightAcceleration = Mathf.Max(0f, value);
                    }
                    break;
                case DroneUpgradeId.FlightHandling:
                    if (_drone != null)
                    {
                        _drone.FlightSteeringSharpness = Mathf.Max(0f, value);
                    }
                    break;
            }
        }
    }

    internal sealed class DroneUpgradePurchaseValidator
    {
        public UpgradePurchaseFailure Validate(
            bool isInitialized,
            DroneGoldWallet wallet,
            DroneUpgradeTierState tierState,
            DroneUpgradeDefinition definition,
            DroneUpgradeId id,
            int goldCost)
        {
            if (!isInitialized || wallet == null)
            {
                return UpgradePurchaseFailure.NotInitialized;
            }
            if (definition == null || tierState == null || !tierState.Contains(id))
            {
                return UpgradePurchaseFailure.DefinitionMissing;
            }
            if (tierState.Get(id) >= DronePermanentUpgradeSystem.MaximumPurchasableTier)
            {
                return UpgradePurchaseFailure.MaximumTierReached;
            }
            return wallet.Gold < goldCost
                ? UpgradePurchaseFailure.CannotAfford
                : UpgradePurchaseFailure.None;
        }
    }

    [DisallowMultipleComponent]
    public sealed class DronePermanentUpgradeSystem : MonoBehaviour
    {
        public const int MaximumPurchasableTier = 15;

        public bool IsInitialized { get; private set; }
        public DroneGoldWallet Wallet { get; private set; }
        public IReadOnlyList<DroneUpgradeDefinition> Definitions =>
            _tuning != null && _tuning.Definitions != null
                ? _tuning.Definitions
                : Array.Empty<DroneUpgradeDefinition>();

        public event Action<DroneUpgradeId, int, int> UpgradePurchased;
        public event Action<int> HubRgbTerminalsUnlocked;
        public event Action<bool> HubRgbTerminalsEnabledChanged;
        public event Action<bool> AtlasGlyphBrushedMetalEnabledChanged;

        // The stored field retains its original name so existing DuneVectorUpgrades.dat files keep the unlock.
        public bool AreHubRgbTerminalsUnlocked => _tierState.HubRgbFloorUnlocked;
        public bool AreHubRgbTerminalsEnabled =>
            AreHubRgbTerminalsUnlocked && _tierState.HubRgbTerminalsEnabled;
        public HubRgbTerminalUnlockTuning HubRgbTerminalTuning => _tuning?.HubRgbTerminals;
        public AtlasGlyphMaterialUnlockTuning AtlasGlyphMaterialTuning => _tuning?.AtlasGlyphMaterial;
        public bool IsAtlasGlyphMaterialAvailable =>
            _desertAtlas != null && _desertAtlas.IsComplete && AtlasGlyphMaterialTuning?.GlyphMaterial != null;
        public bool IsAtlasGlyphBrushedMetalEnabled =>
            IsAtlasGlyphMaterialAvailable && _tierState.AtlasGlyphBrushedMetalEnabled;

        private readonly DroneUpgradeTierState _tierState = new DroneUpgradeTierState();
        private readonly DroneUpgradePurchaseValidator _purchaseValidator = new DroneUpgradePurchaseValidator();
        private readonly Dictionary<DroneUpgradeId, float> _tierZeroValues = new Dictionary<DroneUpgradeId, float>();
        private DronePermanentUpgradeTuning _tuning;
        private EnergyLauncherTuning _energyLauncherTuning;
        private DroneUpgradeSaveRepository _saveRepository;
        private DroneUpgradeStatApplicator _statApplicator;
        private DuneVectorDesertAtlas _desertAtlas;
        private DuneVectorMaterials _materials;

        public void Initialize(
            DuneVectorRuntimeSettings runtimeSettings,
            DroneGoldWallet wallet,
            DroneCharacterController drone,
            DroneHealth health,
            DroneStaminaSystem stamina,
            DroneBoostSpeedModifier boostSpeed)
        {
            if (IsInitialized || runtimeSettings == null)
            {
                return;
            }

            runtimeSettings.EnsureInitialized();
            _tuning = runtimeSettings.PermanentUpgrades;
            _energyLauncherTuning = runtimeSettings.EnergyLauncher;
            Wallet = wallet;
            _saveRepository = new DroneUpgradeSaveRepository(this);
            _statApplicator = new DroneUpgradeStatApplicator(drone, health, stamina, boostSpeed);

            CaptureTierZeroValues(runtimeSettings);
            _tierState.Initialize(Definitions);
            _saveRepository.LoadInto(_tierState);
            IsInitialized = true;
            ApplyAllStats();
            _saveRepository.Save(_tierState, Definitions);
        }

        public DroneUpgradeDefinition GetDefinition(DroneUpgradeId id)
        {
            return _tuning?.Find(id);
        }

        public int GetPurchasedTier(DroneUpgradeId id)
        {
            return _tierState.Get(id);
        }

        public int GetRemainingTierCapacity(DroneUpgradeId id)
        {
            return MaximumPurchasableTier - GetPurchasedTier(id);
        }

        public float GetTierZeroValue(DroneUpgradeId id)
        {
            return _tierZeroValues.TryGetValue(id, out float value) ? value : 0f;
        }

        public float GetCurrentValue(DroneUpgradeId id)
        {
            return GetValueAtTier(id, GetPurchasedTier(id));
        }

        public float GetNextValue(DroneUpgradeId id)
        {
            int tier = GetPurchasedTier(id);
            return GetValueAtTier(id, Mathf.Min(MaximumPurchasableTier, tier + 1));
        }

        public float GetTier15Value(DroneUpgradeId id)
        {
            return GetValueAtTier(id, MaximumPurchasableTier);
        }

        public float GetCurrentEnergyProjectileSpeed()
        {
            return GetEnergyProjectileSpeedAtTier(GetPurchasedTier(DroneUpgradeId.EnergyShotCooldown));
        }

        public float GetNextEnergyProjectileSpeed()
        {
            int tier = GetPurchasedTier(DroneUpgradeId.EnergyShotCooldown);
            return GetEnergyProjectileSpeedAtTier(Mathf.Min(MaximumPurchasableTier, tier + 1));
        }

        public float GetEnergyProjectileSpeedAtTier(int tier)
        {
            if (_energyLauncherTuning == null)
            {
                return 0f;
            }

            DroneUpgradeDefinition definition = GetDefinition(DroneUpgradeId.EnergyShotCooldown);
            float progress = definition != null
                ? definition.EvaluateProgress(tier, MaximumPurchasableTier)
                : 0f;
            float maximumMultiplier = Mathf.Max(
                1f,
                _energyLauncherTuning.ProjectileSpeedAtMaximumFireRateTierMultiplier);
            return _energyLauncherTuning.ProjectileSpeed * Mathf.Lerp(1f, maximumMultiplier, progress);
        }

        public float GetValueAtTier(DroneUpgradeId id, int tier)
        {
            float tierZeroValue = GetTierZeroValue(id);
            DroneUpgradeDefinition definition = GetDefinition(id);
            return definition != null
                ? definition.Evaluate(tierZeroValue, tier, MaximumPurchasableTier)
                : tierZeroValue;
        }

        public int GetNextGoldCost(DroneUpgradeId id)
        {
            int tier = GetPurchasedTier(id);
            DroneUpgradeDefinition definition = GetDefinition(id);
            if (definition == null || tier >= MaximumPurchasableTier)
            {
                return 0;
            }

            return definition.GetGoldCost(
                tier + 1,
                MaximumPurchasableTier,
                _tuning != null ? _tuning.GoldCostRounding : 1);
        }

        public bool CanAffordNextTier(DroneUpgradeId id)
        {
            int cost = GetNextGoldCost(id);
            return Wallet != null && cost > 0 && Wallet.Gold >= cost;
        }

        public int GetHubRgbTerminalGoldCost()
        {
            return Mathf.Max(1, HubRgbTerminalTuning?.GoldCost ?? 1);
        }

        public bool CanUnlockHubRgbTerminals()
        {
            return IsInitialized
                && !AreHubRgbTerminalsUnlocked
                && Wallet != null
                && Wallet.Gold >= GetHubRgbTerminalGoldCost();
        }

        public UpgradePurchaseFailure TryUnlockHubRgbTerminals(out int goldCost)
        {
            goldCost = GetHubRgbTerminalGoldCost();
            if (!IsInitialized || Wallet == null || _saveRepository == null)
            {
                return UpgradePurchaseFailure.NotInitialized;
            }
            if (AreHubRgbTerminalsUnlocked)
            {
                return UpgradePurchaseFailure.MaximumTierReached;
            }
            if (Wallet.Gold < goldCost)
            {
                return UpgradePurchaseFailure.CannotAfford;
            }
            if (!Wallet.TrySpendGold(goldCost))
            {
                return UpgradePurchaseFailure.CurrencySaveFailed;
            }

            _tierState.HubRgbFloorUnlocked = true;
            _tierState.HubRgbTerminalsEnabled = true;
            if (!_saveRepository.Save(_tierState, Definitions))
            {
                _tierState.HubRgbFloorUnlocked = false;
                _tierState.HubRgbTerminalsEnabled = false;
                Wallet.AddGold(goldCost);
                return UpgradePurchaseFailure.UpgradeSaveFailed;
            }

            HubRgbTerminalsUnlocked?.Invoke(goldCost);
            return UpgradePurchaseFailure.None;
        }

        public UpgradePurchaseFailure TrySetHubRgbTerminalsEnabled(bool enabled)
        {
            if (!IsInitialized || _saveRepository == null)
            {
                return UpgradePurchaseFailure.NotInitialized;
            }
            if (!AreHubRgbTerminalsUnlocked)
            {
                return UpgradePurchaseFailure.DefinitionMissing;
            }
            if (_tierState.HubRgbTerminalsEnabled == enabled)
            {
                return UpgradePurchaseFailure.None;
            }

            bool previousEnabled = _tierState.HubRgbTerminalsEnabled;
            _tierState.HubRgbTerminalsEnabled = enabled;
            if (!_saveRepository.Save(_tierState, Definitions))
            {
                _tierState.HubRgbTerminalsEnabled = previousEnabled;
                return UpgradePurchaseFailure.UpgradeSaveFailed;
            }

            HubRgbTerminalsEnabledChanged?.Invoke(enabled);
            return UpgradePurchaseFailure.None;
        }

        public void BindAtlasGlyphMaterial(DuneVectorDesertAtlas desertAtlas, DuneVectorMaterials materials)
        {
            _desertAtlas = desertAtlas;
            _materials = materials;
            ApplyAtlasGlyphMaterial();
        }

        public UpgradePurchaseFailure TrySetAtlasGlyphBrushedMetalEnabled(bool enabled)
        {
            if (!IsInitialized || _saveRepository == null)
            {
                return UpgradePurchaseFailure.NotInitialized;
            }
            if (!IsAtlasGlyphMaterialAvailable)
            {
                return UpgradePurchaseFailure.DefinitionMissing;
            }
            if (_tierState.AtlasGlyphBrushedMetalEnabled == enabled)
            {
                return UpgradePurchaseFailure.None;
            }

            bool previousEnabled = _tierState.AtlasGlyphBrushedMetalEnabled;
            _tierState.AtlasGlyphBrushedMetalEnabled = enabled;
            if (!_saveRepository.Save(_tierState, Definitions))
            {
                _tierState.AtlasGlyphBrushedMetalEnabled = previousEnabled;
                return UpgradePurchaseFailure.UpgradeSaveFailed;
            }

            ApplyAtlasGlyphMaterial();
            AtlasGlyphBrushedMetalEnabledChanged?.Invoke(enabled);
            return UpgradePurchaseFailure.None;
        }

        private void ApplyAtlasGlyphMaterial()
        {
            AtlasGlyphMaterialUnlockTuning tuning = AtlasGlyphMaterialTuning;
            if (_materials == null || tuning == null)
            {
                return;
            }

            _materials.SetGeoglyphOverlayMaterial(
                tuning.GlyphMaterial,
                tuning.GlyphTextureTiling,
                tuning.GlyphTextureOffset,
                tuning.GlyphEmissionColor,
                IsAtlasGlyphBrushedMetalEnabled);
        }

        public UpgradePurchaseResult TryPurchase(DroneUpgradeId id)
        {
            DroneUpgradeDefinition definition = GetDefinition(id);
            int previousTier = GetPurchasedTier(id);
            int goldCost = GetNextGoldCost(id);
            UpgradePurchaseFailure validation = _purchaseValidator.Validate(
                IsInitialized,
                Wallet,
                _tierState,
                definition,
                id,
                goldCost);
            if (validation != UpgradePurchaseFailure.None)
            {
                return new UpgradePurchaseResult(validation, id, previousTier, goldCost);
            }
            if (!Wallet.TrySpendGold(goldCost))
            {
                return new UpgradePurchaseResult(UpgradePurchaseFailure.CurrencySaveFailed, id, previousTier, goldCost);
            }

            int purchasedTier = previousTier + 1;
            _tierState.Set(id, purchasedTier);
            if (!_saveRepository.Save(_tierState, Definitions))
            {
                _tierState.Set(id, previousTier);
                Wallet.AddGold(goldCost);
                return new UpgradePurchaseResult(UpgradePurchaseFailure.UpgradeSaveFailed, id, previousTier, goldCost);
            }

            ApplyStat(id);
            UpgradePurchased?.Invoke(id, purchasedTier, goldCost);
            return new UpgradePurchaseResult(UpgradePurchaseFailure.None, id, purchasedTier, goldCost);
        }

        private void CaptureTierZeroValues(DuneVectorRuntimeSettings settings)
        {
            _tierZeroValues.Clear();
            _tierZeroValues[DroneUpgradeId.MaximumHealth] = settings.HealthSettings.MaximumHealth;
            _tierZeroValues[DroneUpgradeId.MaximumStamina] = settings.PlayerTuning.StaminaBoost.MaxStamina;
            _tierZeroValues[DroneUpgradeId.BoostMaximumSpeed] = settings.PlayerTuning.StaminaBoost.BoostMaximumSpeed;
            _tierZeroValues[DroneUpgradeId.EnergyShotDamage] = settings.EnergyLauncher.Damage;
            _tierZeroValues[DroneUpgradeId.EnergyShotCooldown] = settings.EnergyLauncher.FireCooldown;
            _tierZeroValues[DroneUpgradeId.LockOnSpeed] = settings.EnergyLauncher.AcquisitionTime;
            _tierZeroValues[DroneUpgradeId.GroundMaximumSpeed] = settings.PlayerTuning.MaxGroundSpeed;
            _tierZeroValues[DroneUpgradeId.GroundAcceleration] = settings.PlayerTuning.GroundMovementSharpness;
            _tierZeroValues[DroneUpgradeId.GroundHandling] = settings.PlayerTuning.GroundSteeringSharpness;
            _tierZeroValues[DroneUpgradeId.FlightMaximumSpeed] = settings.PlayerTuning.MaximumFlightSpeed;
            _tierZeroValues[DroneUpgradeId.FlightAcceleration] = settings.PlayerTuning.FlightAcceleration;
            _tierZeroValues[DroneUpgradeId.FlightHandling] = settings.PlayerTuning.FlightSteeringSharpness;
        }

        private void ApplyAllStats()
        {
            IReadOnlyList<DroneUpgradeDefinition> definitions = Definitions;
            for (int index = 0; index < definitions.Count; index++)
            {
                if (definitions[index] != null)
                {
                    ApplyStat(definitions[index].Id);
                }
            }
        }

        private void ApplyStat(DroneUpgradeId id)
        {
            _statApplicator?.Apply(id, GetCurrentValue(id));
        }
    }
}
