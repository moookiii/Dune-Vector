using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DuneVector
{
    public sealed class DroneTrailOption
    {
        public string ObjectName { get; }
        public string DisplayName { get; }
        public bool IsModular { get; }

        internal GameObject TrailObject { get; }

        internal DroneTrailOption(GameObject trailObject)
        {
            TrailObject = trailObject;
            ObjectName = trailObject.name;
            DisplayName = CreateDisplayName(ObjectName);
            IsModular = ObjectName.IndexOf("Modular", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string CreateDisplayName(string objectName)
        {
            string compactName = (objectName ?? string.Empty)
                .Replace("Missile", string.Empty)
                .Replace("Ring2", "Rings");
            if (string.IsNullOrWhiteSpace(compactName))
            {
                return "Trail";
            }

            StringBuilder displayName = new StringBuilder(compactName.Length + 8);
            for (int i = 0; i < compactName.Length; i++)
            {
                char current = compactName[i];
                if (i > 0 && char.IsUpper(current) && char.IsLower(compactName[i - 1]))
                {
                    displayName.Append(' ');
                }
                displayName.Append(current);
            }
            return displayName.ToString().Trim();
        }
    }

    [DisallowMultipleComponent]
    public sealed class DroneTrailCosmeticSystem : MonoBehaviour
    {
        private const string SaveFileName = "DuneVectorDroneTrails.dat";

        [Serializable]
        private sealed class SaveData
        {
            public int Version = 2;
            public int ContractUnlocksGranted;
            public string EquippedTrailObjectName;
            public List<string> UnlockedTrailObjectNames = new List<string>();
            public List<string> ContractTrailUnlockOrder = new List<string>();
        }

        private readonly List<DroneTrailOption> _options = new List<DroneTrailOption>();
        private readonly HashSet<string> _unlocked = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _contractTrailUnlockOrder = new List<string>();
        private DroneTrailCosmeticTuning _tuning;
        private DroneGoldWallet _wallet;
        private string _savePath;
        private string _equippedTrailObjectName;
        private int _contractUnlocksGranted;

        public IReadOnlyList<DroneTrailOption> Options => _options;
        public int ModularTrailGoldCost => Mathf.Max(1, _tuning?.ModularTrailGoldCost ?? 1);
        public string ContractTrailDescription => _tuning?.ContractTrailDescription ?? string.Empty;
        public string ModularTrailDescription => _tuning?.ModularTrailDescription ?? string.Empty;
        public DroneTrailUnlockShowcaseTuning UnlockShowcaseTuning => _tuning?.UnlockShowcase;

        public void Initialize(Transform droneVisualRoot, DroneTrailCosmeticTuning tuning, DroneGoldWallet wallet)
        {
            _tuning = tuning ?? new DroneTrailCosmeticTuning();
            _wallet = wallet;
            _savePath = Path.Combine(Application.persistentDataPath, SaveFileName);
            DiscoverOptions(droneVisualRoot);
            Load();

            DroneTrailOption defaultOption = FindOption(_tuning.DefaultTrailObjectName) ??
                (_options.Count > 0 ? _options[0] : null);
            if (defaultOption != null)
            {
                _unlocked.Add(defaultOption.ObjectName);
                if (FindOption(_equippedTrailObjectName) == null || !_unlocked.Contains(_equippedTrailObjectName))
                {
                    _equippedTrailObjectName = defaultOption.ObjectName;
                }
            }

            EnsureContractUnlockOrder(defaultOption);
            ApplyEquippedTrail();
            Save();
        }

        public bool IsUnlocked(DroneTrailOption option)
        {
            return option != null && _unlocked.Contains(option.ObjectName);
        }

        public bool IsEquipped(DroneTrailOption option)
        {
            return option != null && string.Equals(
                option.ObjectName,
                _equippedTrailObjectName,
                StringComparison.Ordinal);
        }

        public bool CanAffordModularTrail(DroneTrailOption option)
        {
            return option != null && option.IsModular && !IsUnlocked(option) &&
                _wallet != null && _wallet.Gold >= ModularTrailGoldCost;
        }

        public UpgradePurchaseFailure TryPurchaseModularTrail(DroneTrailOption option)
        {
            if (option == null || !option.IsModular || _wallet == null)
            {
                return UpgradePurchaseFailure.DefinitionMissing;
            }
            if (IsUnlocked(option))
            {
                return UpgradePurchaseFailure.MaximumTierReached;
            }
            int cost = ModularTrailGoldCost;
            if (_wallet.Gold < cost)
            {
                return UpgradePurchaseFailure.CannotAfford;
            }
            if (!_wallet.TrySpendGold(cost))
            {
                return UpgradePurchaseFailure.CurrencySaveFailed;
            }

            _unlocked.Add(option.ObjectName);
            string previousEquipped = _equippedTrailObjectName;
            _equippedTrailObjectName = option.ObjectName;
            if (!Save())
            {
                _unlocked.Remove(option.ObjectName);
                _equippedTrailObjectName = previousEquipped;
                _wallet.AddGold(cost);
                return UpgradePurchaseFailure.UpgradeSaveFailed;
            }

            ApplyEquippedTrail();
            return UpgradePurchaseFailure.None;
        }

        public UpgradePurchaseFailure TryEquip(DroneTrailOption option)
        {
            if (option == null || !IsUnlocked(option))
            {
                return UpgradePurchaseFailure.DefinitionMissing;
            }
            if (IsEquipped(option))
            {
                return UpgradePurchaseFailure.None;
            }

            string previousEquipped = _equippedTrailObjectName;
            _equippedTrailObjectName = option.ObjectName;
            if (!Save())
            {
                _equippedTrailObjectName = previousEquipped;
                return UpgradePurchaseFailure.UpgradeSaveFailed;
            }

            ApplyEquippedTrail();
            return UpgradePurchaseFailure.None;
        }

        /// <summary>
        /// Grants every contract trail the completed-contract count has earned and returns the
        /// newest one, or null when nothing was unlocked. The option itself is returned so the
        /// unlock showcase can clone the authored effect for its render view.
        /// </summary>
        public DroneTrailOption SynchronizeContractUnlocks(int completedContracts)
        {
            int targetCount = Mathf.Max(0, completedContracts);
            DroneTrailOption newestUnlock = null;
            bool changed = false;
            while (_contractUnlocksGranted < targetCount)
            {
                DroneTrailOption next = FindContractTrailForGrant(_contractUnlocksGranted);
                if (next != null)
                {
                    _unlocked.Add(next.ObjectName);
                    newestUnlock = next;
                }
                _contractUnlocksGranted++;
                changed = true;
            }

            if (changed)
            {
                Save();
            }
            return newestUnlock;
        }

        private void DiscoverOptions(Transform droneVisualRoot)
        {
            _options.Clear();
            if (droneVisualRoot == null)
            {
                return;
            }

            Transform bestParent = null;
            int bestCount = 0;
            Transform[] transforms = droneVisualRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                int count = 0;
                for (int childIndex = 0; childIndex < candidate.childCount; childIndex++)
                {
                    if (IsTrailRootName(candidate.GetChild(childIndex).name))
                    {
                        count++;
                    }
                }
                if (count > bestCount)
                {
                    bestCount = count;
                    bestParent = candidate;
                }
            }

            if (bestParent == null)
            {
                Debug.LogError("Drone trail cosmetic roots were not found beneath the drone visual.", this);
                return;
            }
            for (int childIndex = 0; childIndex < bestParent.childCount; childIndex++)
            {
                GameObject child = bestParent.GetChild(childIndex).gameObject;
                if (IsTrailRootName(child.name))
                {
                    _options.Add(new DroneTrailOption(child));
                }
            }
        }

        private static bool IsTrailRootName(string objectName)
        {
            return !string.IsNullOrEmpty(objectName) &&
                objectName.IndexOf("Missile", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private DroneTrailOption FindOption(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return null;
            }
            for (int i = 0; i < _options.Count; i++)
            {
                if (string.Equals(_options[i].ObjectName, objectName, StringComparison.Ordinal))
                {
                    return _options[i];
                }
            }
            return null;
        }

        private DroneTrailOption FindContractTrailForGrant(int grantIndex)
        {
            if (grantIndex < 0 || grantIndex >= _contractTrailUnlockOrder.Count)
            {
                return null;
            }
            return FindOption(_contractTrailUnlockOrder[grantIndex]);
        }

        private void EnsureContractUnlockOrder(DroneTrailOption defaultOption)
        {
            HashSet<string> validNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _options.Count; i++)
            {
                DroneTrailOption option = _options[i];
                if (!option.IsModular && option != defaultOption)
                {
                    validNames.Add(option.ObjectName);
                }
            }

            for (int i = _contractTrailUnlockOrder.Count - 1; i >= 0; i--)
            {
                if (!validNames.Remove(_contractTrailUnlockOrder[i]))
                {
                    _contractTrailUnlockOrder.RemoveAt(i);
                }
            }

            List<string> remaining = new List<string>(validNames);
            System.Random random = new System.Random(Guid.NewGuid().GetHashCode());
            for (int i = remaining.Count - 1; i > 0; i--)
            {
                int swapIndex = random.Next(i + 1);
                (remaining[i], remaining[swapIndex]) = (remaining[swapIndex], remaining[i]);
            }
            _contractTrailUnlockOrder.AddRange(remaining);
        }

        private void ApplyEquippedTrail()
        {
            for (int i = 0; i < _options.Count; i++)
            {
                DroneTrailOption option = _options[i];
                option.TrailObject.SetActive(IsEquipped(option));
            }
        }

        private void Load()
        {
            if (DuneTrainingRuntime.Enabled || !File.Exists(_savePath))
            {
                return;
            }
            try
            {
                SaveData data = JsonUtility.FromJson<SaveData>(File.ReadAllText(_savePath));
                if (data == null)
                {
                    return;
                }
                _contractUnlocksGranted = Mathf.Max(0, data.ContractUnlocksGranted);
                _equippedTrailObjectName = data.EquippedTrailObjectName;
                if (data.ContractTrailUnlockOrder != null)
                {
                    _contractTrailUnlockOrder.AddRange(data.ContractTrailUnlockOrder);
                }
                if (data.UnlockedTrailObjectNames != null)
                {
                    for (int i = 0; i < data.UnlockedTrailObjectNames.Count; i++)
                    {
                        string objectName = data.UnlockedTrailObjectNames[i];
                        if (FindOption(objectName) != null)
                        {
                            _unlocked.Add(objectName);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not load drone trails from '{_savePath}': {exception.Message}", this);
            }
        }

        private bool Save()
        {
            if (DuneTrainingRuntime.Enabled)
            {
                return true;
            }
            try
            {
                SaveData data = new SaveData
                {
                    ContractUnlocksGranted = _contractUnlocksGranted,
                    EquippedTrailObjectName = _equippedTrailObjectName,
                    UnlockedTrailObjectNames = new List<string>(_unlocked),
                    ContractTrailUnlockOrder = new List<string>(_contractTrailUnlockOrder),
                };
                File.WriteAllText(_savePath, JsonUtility.ToJson(data));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not save drone trails to '{_savePath}': {exception.Message}", this);
                return false;
            }
        }
    }
}
