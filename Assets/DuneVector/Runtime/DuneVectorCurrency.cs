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
            if (_health == null || _health.IsDead)
            {
                return false;
            }

            return _health.CurrentHealth >= _health.MaximumHealth || _health.RestoreHealth(_amount);
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
        private GUIStyle _goldLabelStyle;
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
            // This overlay only draws; it owns no controls and mutates no state. Running the
            // layout pass would repeat every measurement for nothing, so only Repaint does work.
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }
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
            Color accent = _settings.GoldHudTextColor;
            Color border = accent;
            border.a *= 0.5f;

            DuneVectorHudChrome.DrawSoftShadow(
                panel,
                _settings.GoldHudShadowColor,
                _settings.GoldHudShadowOffset,
                5f);
            DuneVectorHudChrome.DrawGlassPanel(panel, _settings.GoldHudPanelColor, border, 1f, 1f);
            DuneVectorHudChrome.DrawAccentRail(panel, accent, 4f, 28f);
            Color bracket = accent;
            bracket.a *= 0.6f;
            DuneVectorHudChrome.DrawCornerBrackets(panel, bracket, 11f, 1f);

            float padding = 14f;
            Rect content = new Rect(
                panel.x + padding,
                panel.y,
                panel.width - (padding * 1.6f),
                panel.height);
            Vector2 textShadow = new Vector2(1f, 1f);
            Color textShadowColor = new Color(0f, 0f, 0f, 0.6f);
            DuneVectorHudChrome.DrawLabel(content, "GOLD", _goldLabelStyle, Color.white, textShadowColor, textShadow);
            DuneVectorHudChrome.DrawLabel(
                content,
                $"{_wallet.Gold:N0}",
                _goldStyle,
                Color.white,
                textShadowColor,
                textShadow);

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
                Color feedbackShadow = new Color(0f, 0f, 0f, 0.6f * Mathf.Clamp01(remaining / duration));
                DuneVectorHudChrome.DrawLabel(
                    feedback,
                    $"+{_lastReward:N0} GOLD",
                    _feedbackStyle,
                    Color.white,
                    feedbackShadow,
                    new Vector2(2f, 2f));
            }
            GUI.color = oldColor;
        }

        private void EnsureStyles()
        {
            _goldStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                wordWrap = false,
            };
            _goldStyle.fontSize = _settings.GoldHudFontSize;
            _goldStyle.normal.textColor = _settings.GoldHudTextColor;

            _goldLabelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                wordWrap = false,
            };
            _goldLabelStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(_settings.GoldHudFontSize * 0.66f));
            Color labelColor = _settings.GoldHudTextColor;
            labelColor.r = Mathf.Lerp(labelColor.r, 0.75f, 0.35f);
            labelColor.g = Mathf.Lerp(labelColor.g, 0.75f, 0.35f);
            labelColor.b = Mathf.Lerp(labelColor.b, 0.75f, 0.35f);
            labelColor.a *= 0.8f;
            _goldLabelStyle.normal.textColor = labelColor;

            _feedbackStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            _feedbackStyle.fontSize = _settings.GoldPickupFeedbackFontSize;
        }
    }
}
