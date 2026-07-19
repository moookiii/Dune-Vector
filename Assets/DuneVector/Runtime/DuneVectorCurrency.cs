using System;
using System.IO;
using UnityEngine;

namespace DuneVector
{
    public interface ITraversalRingReward
    {
        void BindTargets(DroneHealth health, DroneGoldWallet wallet);
        bool TryReward();
    }

    [DisallowMultipleComponent]
    public sealed class HealthRingReward : MonoBehaviour, ITraversalRingReward
    {
        private DroneHealth _health;
        private float _amount;

        public void Initialize(DroneHealth health, float amount)
        {
            _health = health;
            _amount = Mathf.Max(0f, amount);
        }

        public void BindTargets(DroneHealth health, DroneGoldWallet wallet)
        {
            _health = health;
        }

        public bool TryReward()
        {
            return _health != null && _health.RestoreHealth(_amount);
        }
    }

    [DisallowMultipleComponent]
    public sealed class CoinRingReward : MonoBehaviour, ITraversalRingReward
    {
        private DroneGoldWallet _wallet;
        private int _amount;

        public void Initialize(DroneGoldWallet wallet, int amount)
        {
            _wallet = wallet;
            _amount = Mathf.Max(1, amount);
        }

        public void BindTargets(DroneHealth health, DroneGoldWallet wallet)
        {
            _wallet = wallet;
        }

        public bool TryReward()
        {
            return _wallet != null && _wallet.AddGold(_amount);
        }
    }

    [DisallowMultipleComponent]
    public sealed class DroneGoldWallet : MonoBehaviour
    {
        private const string GoldSaveFileName = "DuneVectorGold.dat";

        [Serializable]
        private sealed class GoldSaveData
        {
            public int Version = 1;
            public int Gold;
        }

        public int Gold { get; private set; }
        public event Action<int, int> GoldChanged;

        private bool _initialized;
        private string _savePath;

        public void Initialize(int startingGold)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _savePath = Path.Combine(Application.persistentDataPath, GoldSaveFileName);
            Gold = LoadGold(Mathf.Max(0, startingGold));
            SaveGold();
        }

        public bool AddGold(int amount)
        {
            if (amount <= 0)
            {
                return false;
            }

            int previousGold = Gold;
            Gold = Gold > int.MaxValue - amount ? int.MaxValue : Gold + amount;
            int gained = Gold - previousGold;
            if (gained <= 0)
            {
                return false;
            }

            if (!SaveGold())
            {
                Gold = previousGold;
                return false;
            }
            GoldChanged?.Invoke(Gold, gained);
            return true;
        }

        public bool TrySpendGold(int amount)
        {
            if (amount <= 0 || Gold < amount)
            {
                return false;
            }

            int previousGold = Gold;
            Gold -= amount;
            if (!SaveGold())
            {
                Gold = previousGold;
                return false;
            }

            GoldChanged?.Invoke(Gold, -amount);
            return true;
        }

        private int LoadGold(int fallbackGold)
        {
            if (!File.Exists(_savePath))
            {
                return fallbackGold;
            }

            try
            {
                GoldSaveData stored = JsonUtility.FromJson<GoldSaveData>(File.ReadAllText(_savePath));
                return stored != null ? Mathf.Max(0, stored.Gold) : fallbackGold;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not load saved gold from '{_savePath}': {exception.Message}", this);
                return fallbackGold;
            }
        }

        private bool SaveGold()
        {
            try
            {
                GoldSaveData stored = new GoldSaveData { Gold = Gold };
                File.WriteAllText(_savePath, JsonUtility.ToJson(stored));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not save gold to '{_savePath}': {exception.Message}", this);
                return false;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class EnemyGoldReward : MonoBehaviour
    {
        private EnemyHealth _health;
        private DroneGoldWallet _wallet;
        private int _amount;
        private bool _awarded;

        public void Initialize(EnemyHealth health, DroneGoldWallet wallet, int amount)
        {
            if (_health != null)
            {
                _health.Died -= HandleEnemyDied;
            }

            _health = health;
            _wallet = wallet;
            _amount = Mathf.Max(0, amount);
            _awarded = false;
            if (_health != null)
            {
                _health.Died += HandleEnemyDied;
            }
        }

        public void BindWallet(DroneGoldWallet wallet)
        {
            _wallet = wallet;
        }

        private void HandleEnemyDied()
        {
            if (_awarded)
            {
                return;
            }

            _awarded = true;
            _wallet?.AddGold(_amount);
        }

        private void OnDestroy()
        {
            if (_health != null)
            {
                _health.Died -= HandleEnemyDied;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorGoldHUD : MonoBehaviour
    {
        private DroneGoldWallet _wallet;
        private RingTuning _settings;
        private GUIStyle _goldStyle;
        private GUIStyle _feedbackStyle;
        private int _lastReward;
        private float _feedbackUntil;

        public void Initialize(DroneGoldWallet wallet, RingTuning settings)
        {
            if (_wallet != null)
            {
                _wallet.GoldChanged -= HandleGoldChanged;
            }

            _wallet = wallet;
            _settings = settings;
            if (_wallet != null)
            {
                _wallet.GoldChanged += HandleGoldChanged;
            }
        }

        private void OnDestroy()
        {
            if (_wallet != null)
            {
                _wallet.GoldChanged -= HandleGoldChanged;
            }
        }

        private void HandleGoldChanged(int total, int gained)
        {
            if (gained <= 0)
            {
                return;
            }
            _lastReward = gained;
            _feedbackUntil = Time.unscaledTime + Mathf.Max(0.1f, _settings.GoldPickupFeedbackDuration);
        }

        private void OnGUI()
        {
            if (_wallet == null || _settings == null)
            {
                return;
            }

            EnsureStyles();
            Rect panel = new Rect(
                Screen.width - _settings.GoldHudRightMargin - _settings.GoldHudWidth,
                _settings.GoldHudTopMargin,
                _settings.GoldHudWidth,
                _settings.GoldHudHeight);
            Color oldColor = GUI.color;
            GUI.color = _settings.GoldHudPanelColor;
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(panel, $"GOLD  {_wallet.Gold:N0}", _goldStyle);

            float remaining = _feedbackUntil - Time.unscaledTime;
            if (remaining > 0f)
            {
                float duration = Mathf.Max(0.1f, _settings.GoldPickupFeedbackDuration);
                Color feedbackColor = _settings.GoldPickupFeedbackColor;
                feedbackColor.a *= Mathf.Clamp01(remaining / duration);
                _feedbackStyle.normal.textColor = feedbackColor;
                Rect feedback = new Rect(
                    0f,
                    _settings.GoldPickupFeedbackTop,
                    Screen.width,
                    _settings.GoldPickupFeedbackHeight);
                GUI.Label(feedback, $"+{_lastReward:N0} GOLD", _feedbackStyle);
            }
            GUI.color = oldColor;
        }

        private void EnsureStyles()
        {
            _goldStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            _goldStyle.fontSize = _settings.GoldHudFontSize;
            _goldStyle.normal.textColor = _settings.GoldHudTextColor;

            _feedbackStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            _feedbackStyle.fontSize = _settings.GoldPickupFeedbackFontSize;
        }
    }
}
