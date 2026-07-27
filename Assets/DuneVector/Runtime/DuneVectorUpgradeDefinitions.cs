using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuneVector
{
    public enum DroneUpgradeId
    {
        MaximumHealth,
        MaximumStamina,
        BoostMaximumSpeed,
        EnergyShotDamage,
        EnergyShotCooldown,
        LockOnSpeed,
        GroundMaximumSpeed,
        GroundAcceleration,
        GroundHandling,
        FlightMaximumSpeed,
        FlightAcceleration,
        FlightHandling,
    }

    public enum DroneUpgradeGroup
    {
        Core,
        Ground,
        Flight,
        Cosmetic,
    }

    public enum DroneUpgradeValueFormat
    {
        WholeNumber,
        OneDecimal,
        TwoDecimalSeconds,
    }

    [Serializable]
    public sealed class DroneUpgradeDefinition
    {
        public DroneUpgradeId Id;
        public DroneUpgradeGroup Group;
        public string DisplayName;
        public DroneUpgradeValueFormat ValueFormat;

        [Header("Progression Curve")]
        [Tooltip("Tier 15 is the configured gameplay value multiplied by this value. Values below one create time reductions.")]
        [Min(0.01f)] public float Tier15Multiplier = 1.5f;
        [Tooltip("Shapes normalized tier progress. Below one gives more benefit early; above one reserves more benefit for late tiers.")]
        [Min(0.1f)] public float ProgressionExponent = 1f;

        [Header("Gold Cost Curve")]
        [Min(1)] public int BaseGoldCost = 100;
        [Tooltip("Each successive tier costs the previous tier cost multiplied by this value before rounding.")]
        [Min(1f)] public float GoldCostGrowth = 1.2f;

        public float Evaluate(float tierZeroValue, int purchasedTier, int maximumTier)
        {
            float curvedTier = EvaluateProgress(purchasedTier, maximumTier);
            if (curvedTier <= 0f)
            {
                return tierZeroValue;
            }

            float tier15Value = tierZeroValue * Mathf.Max(0.01f, Tier15Multiplier);
            float evaluated = Mathf.Lerp(tierZeroValue, tier15Value, curvedTier);
            return Mathf.Clamp(evaluated, Mathf.Min(tierZeroValue, tier15Value), Mathf.Max(tierZeroValue, tier15Value));
        }

        public float EvaluateProgress(int purchasedTier, int maximumTier)
        {
            if (maximumTier <= 0)
            {
                return 0f;
            }

            float normalizedTier = Mathf.Clamp(purchasedTier, 0, maximumTier) / (float)maximumTier;
            return Mathf.Pow(normalizedTier, Mathf.Max(0.1f, ProgressionExponent));
        }

        public int GetGoldCost(int targetTier, int maximumTier, int roundingIncrement)
        {
            int clampedTier = Mathf.Clamp(targetTier, 1, maximumTier);
            double rawCost = Math.Max(1, BaseGoldCost) * Math.Pow(Math.Max(1f, GoldCostGrowth), clampedTier - 1);
            int increment = Mathf.Max(1, roundingIncrement);
            double rounded = Math.Ceiling(rawCost / increment) * increment;
            return rounded >= int.MaxValue ? int.MaxValue : Mathf.Max(1, (int)rounded);
        }
    }

    [Serializable]
    public sealed class UpgradeShopVisualTuning
    {
        [Header("Responsive Page Layout")]
        [Min(320f)] public float ReferenceWidth = 1920f;
        [Min(240f)] public float ReferenceHeight = 1080f;
        [Range(0.5f, 2f)] public float MinimumScale = 0.68f;
        [Range(0.5f, 2f)] public float MaximumScale = 1.15f;
        [Min(640f)] public float PanelWidth = 1500f;
        [Min(420f)] public float PanelHeight = 940f;
        [Min(8f)] public float ScreenMargin = 24f;
        [Min(12f)] public float PanelPadding = 30f;
        [Min(1f)] public float AccentBarHeight = 5f;
        [Min(0f)] public float ShadowOffset = 10f;
        [Min(56f)] public float HeaderHeight = 88f;
        [Min(32f)] public float BackButtonWidth = 118f;
        [Min(24f)] public float BackButtonHeight = 38f;
        [Min(120f)] public float GoldPanelWidth = 224f;
        [Min(36f)] public float GoldPanelHeight = 54f;
        [Min(1f)] public float BorderThickness = 1f;
        [Min(8f)] public float ScrollbarWidth = 18f;
        [Min(0f)] public float ScrollbarContentGap = 16f;

        [Header("Upgrade Rows")]
        [Min(16f)] public float GroupHeaderHeight = 34f;
        [Min(52f)] public float RowHeight = 82f;
        [Min(0f)] public float RowGap = 8f;
        [Min(0f)] public float GroupGap = 18f;
        [Min(24f)] public float IconSize = 46f;
        [Min(80f)] public float DetailsColumnWidth = 292f;
        [Min(96f)] public float ActionColumnWidth = 180f;
        [Min(4f)] public float ColumnGap = 18f;
        [Min(0f)] public float RowVerticalPadding = 9f;
        [Min(0f)] public float ValueLineGap = 8f;
        [Min(0f)] public float TierIndicatorTop = 24f;
        [Min(0f)] public float TierLabelTop = 45f;
        [Min(40f)] public float MinimumTierIndicatorWidth = 120f;
        [Min(1f)] public float GroupAccentWidth = 4f;
        [Min(1f)] public float RowAccentWidth = 3f;
        [Min(0f)] public float GroupLabelIndent = 14f;
        [Min(2f)] public float TierSegmentHeight = 10f;
        [Min(1f)] public float TierSegmentGap = 3f;
        [Min(60f)] public float PurchaseButtonWidth = 150f;
        [Min(24f)] public float PurchaseButtonHeight = 36f;
        [Min(16)] public int IconTextureSize = 48;
        [Range(1, 6)] public int IconLineThickness = 2;

        [Header("Typography")]
        [Min(12)] public int TitleFontSize = 30;
        [Min(9)] public int SubtitleFontSize = 12;
        [Min(10)] public int GroupFontSize = 13;
        [Min(10)] public int UpgradeNameFontSize = 15;
        [Min(9)] public int ValueFontSize = 12;
        [Min(9)] public int TierFontSize = 11;
        [Min(9)] public int CostFontSize = 12;
        [Min(10)] public int GoldFontSize = 19;
        [Min(10)] public int ButtonFontSize = 12;

        [Header("Motion and Feedback")]
        [Min(0.01f)] public float TierAnimationDuration = 0.28f;
        [Min(0.01f)] public float RecentPurchaseDuration = 0.9f;
        [Min(0.01f)] public float GoldDeductionDuration = 0.8f;
        [Tooltip("Additional displayed gold counted per second for every gold of difference from the wallet balance.")]
        [Min(0f)] public float GoldCountDistanceSpeedMultiplier = 6f;
        [Min(1f)] public float GoldCountAnimationSpeed = 800f;
        [Min(0f)] public float ValuePulseAmount = 0.12f;
        [Min(0f)] public float ValuePulseSpeed = 8f;
        [Min(0f)] public float GoldDeductionRise = 12f;

        [Header("Tint and Border Accents")]
        [Range(0f, 1f)] public float GroupLineOpacity = 0.25f;
        [Range(0f, 1f)] public float RowBorderOpacity = 0.28f;
        [Range(0f, 1f)] public float RecentBorderOpacity = 0.9f;
        [Range(0f, 1f)] public float IconPanelTintAmount = 0.12f;
        [Range(0f, 1f)] public float IconBorderOpacity = 0.52f;
        [Range(0f, 1f)] public float GoldPanelTintAmount = 0.12f;
        [Range(0f, 1f)] public float GoldPanelBorderOpacity = 0.6f;
        [Range(0f, 1f)] public float MaximumButtonTintAmount = 0.12f;

        [Header("Sci-Fi Desert Palette")]
        [ColorUsage(false)] public Color ShadowColor = new Color(0f, 0f, 0f, 0.52f);
        [ColorUsage(false)] public Color PanelColor = new Color(0.025f, 0.045f, 0.065f, 0.97f);
        [ColorUsage(false)] public Color PanelBorderColor = new Color(0.32f, 0.48f, 0.58f, 0.8f);
        [ColorUsage(false)] public Color HeaderColor = new Color(0.035f, 0.075f, 0.1f, 0.98f);
        [ColorUsage(false)] public Color RowColor = new Color(0.045f, 0.075f, 0.095f, 0.92f);
        [ColorUsage(false)] public Color RowHoverColor = new Color(0.07f, 0.12f, 0.15f, 0.96f);
        [ColorUsage(false)] public Color RecentPurchaseColor = new Color(0.08f, 0.22f, 0.2f, 0.98f);
        [ColorUsage(false)] public Color PrimaryTextColor = new Color(0.9f, 0.97f, 1f, 1f);
        [ColorUsage(false)] public Color SecondaryTextColor = new Color(0.52f, 0.68f, 0.74f, 1f);
        [ColorUsage(false)] public Color MutedTextColor = new Color(0.34f, 0.44f, 0.48f, 1f);
        [ColorUsage(false)] public Color GoldColor = new Color(1f, 0.72f, 0.2f, 1f);
        [ColorUsage(false)] public Color CoreColor = new Color(1f, 0.52f, 0.16f, 1f);
        [ColorUsage(false)] public Color GroundColor = new Color(0.32f, 0.9f, 0.66f, 1f);
        [ColorUsage(false)] public Color FlightColor = new Color(0.18f, 0.76f, 1f, 1f);
        [ColorUsage(false)] public Color CosmeticColor = new Color(0.82f, 0.3f, 1f, 1f);
        [ColorUsage(false)] public Color TierEmptyColor = new Color(0.1f, 0.17f, 0.2f, 1f);
        [ColorUsage(false)] public Color AffordableButtonColor = new Color(0.08f, 0.38f, 0.42f, 1f);
        [ColorUsage(false)] public Color AffordableButtonHoverColor = new Color(0.11f, 0.58f, 0.62f, 1f);
        [ColorUsage(false)] public Color UnaffordableButtonColor = new Color(0.12f, 0.15f, 0.16f, 1f);
        [ColorUsage(false)] public Color MaximumColor = new Color(0.26f, 0.9f, 0.58f, 1f);
        [ColorUsage(false)] public Color DividerColor = new Color(0.2f, 0.34f, 0.4f, 0.75f);
    }

    [Serializable]
    public sealed class HubRgbTerminalUnlockTuning
    {
        public string DisplayName = "RGB Hub Terminals";
        [TextArea] public string Description = "Cycles every hub terminal through RGB from a different starting color.";
        [Min(1)] public int GoldCost = 5000;
        [Min(0.01f)] public float ColorCycleSpeed = 0.6f;
        [Tooltip("Cycle phase added between successive hub terminals so each starts on a different color.")]
        [Min(0f)] public float StartingPhaseOffset = 0.75f;
        [Tooltip("Additional cycle phase applied to both antennae so they start on a different color than their terminal panel.")]
        [Min(0f)] public float AntennaStartingPhaseOffset = 1f;
        [ColorUsage(false, true)] public Color Red = new Color(3.2f, 0.04f, 0.02f, 1f);
        [ColorUsage(false, true)] public Color Green = new Color(0.02f, 3.2f, 0.08f, 1f);
        [ColorUsage(false, true)] public Color Blue = new Color(0.02f, 0.12f, 3.2f, 1f);
        [Range(0f, 1f)] public float BaseColorIntensity = 0.28f;
        [Min(0f)] public float EmissionIntensity = 1f;
    }

    [Serializable]
    public sealed class AtlasGlyphMaterialUnlockTuning
    {
        public string DisplayName = "Brushed Metal Glyphs";
        [TextArea] public string Description = "Recasts every discovered Atlas glyph with the BrushedMetal material finish.";
        public Material GlyphMaterial;
        public Vector2 GlyphTextureTiling = Vector2.one;
        public Vector2 GlyphTextureOffset = Vector2.zero;
        [ColorUsage(false, true)] public Color GlyphEmissionColor = Color.black;
    }

    [Serializable]
    public sealed class DronePermanentUpgradeTuning
    {
        [Tooltip("Gold prices are rounded upward to this increment.")]
        [Min(1)] public int GoldCostRounding = 5;

        public HubRgbTerminalUnlockTuning HubRgbTerminals = new HubRgbTerminalUnlockTuning();
        public AtlasGlyphMaterialUnlockTuning AtlasGlyphMaterial = new AtlasGlyphMaterialUnlockTuning();
        public List<DroneUpgradeDefinition> Definitions = CreateDefaultDefinitions();
        public UpgradeShopVisualTuning ShopVisuals = new UpgradeShopVisualTuning();

        public void EnsureInitialized()
        {
            Definitions ??= CreateDefaultDefinitions();
            if (Definitions.Count == 0)
            {
                Definitions = CreateDefaultDefinitions();
            }
            HubRgbTerminals ??= new HubRgbTerminalUnlockTuning();
            AtlasGlyphMaterial ??= new AtlasGlyphMaterialUnlockTuning();
            ShopVisuals ??= new UpgradeShopVisualTuning();
        }

        public DroneUpgradeDefinition Find(DroneUpgradeId id)
        {
            if (Definitions == null)
            {
                return null;
            }

            for (int index = 0; index < Definitions.Count; index++)
            {
                DroneUpgradeDefinition definition = Definitions[index];
                if (definition != null && definition.Id == id)
                {
                    return definition;
                }
            }
            return null;
        }

        private static List<DroneUpgradeDefinition> CreateDefaultDefinitions()
        {
            return new List<DroneUpgradeDefinition>
            {
                Create(DroneUpgradeId.MaximumHealth, DroneUpgradeGroup.Core, "Maximum Health", DroneUpgradeValueFormat.WholeNumber, 2f, 0.82f, 90, 1.19f),
                Create(DroneUpgradeId.MaximumStamina, DroneUpgradeGroup.Core, "Maximum Stamina", DroneUpgradeValueFormat.WholeNumber, 1.75f, 0.78f, 75, 1.18f),
                Create(DroneUpgradeId.BoostMaximumSpeed, DroneUpgradeGroup.Core, "Boost Speed", DroneUpgradeValueFormat.WholeNumber, 1.3f, 1.12f, 120, 1.2f),
                Create(DroneUpgradeId.EnergyShotDamage, DroneUpgradeGroup.Core, "Energy Shot Damage", DroneUpgradeValueFormat.OneDecimal, 2f, 0.95f, 120, 1.21f),
                Create(DroneUpgradeId.EnergyShotCooldown, DroneUpgradeGroup.Core, "Fire Rate + Beam Speed", DroneUpgradeValueFormat.TwoDecimalSeconds, 0.58f, 0.72f, 150, 1.21f),
                Create(DroneUpgradeId.LockOnSpeed, DroneUpgradeGroup.Core, "Lock-On Speed", DroneUpgradeValueFormat.TwoDecimalSeconds, 0.45f, 0.7f, 145, 1.2f),
                Create(DroneUpgradeId.GroundMaximumSpeed, DroneUpgradeGroup.Ground, "Maximum Speed", DroneUpgradeValueFormat.OneDecimal, 1.5f, 1.08f, 90, 1.19f),
                Create(DroneUpgradeId.GroundAcceleration, DroneUpgradeGroup.Ground, "Acceleration", DroneUpgradeValueFormat.OneDecimal, 1.7f, 0.88f, 75, 1.18f),
                Create(DroneUpgradeId.GroundHandling, DroneUpgradeGroup.Ground, "Handling", DroneUpgradeValueFormat.OneDecimal, 1.55f, 0.8f, 80, 1.18f),
                Create(DroneUpgradeId.FlightMaximumSpeed, DroneUpgradeGroup.Flight, "Maximum Speed", DroneUpgradeValueFormat.OneDecimal, 1.35f, 1.15f, 140, 1.21f),
                Create(DroneUpgradeId.FlightAcceleration, DroneUpgradeGroup.Flight, "Acceleration", DroneUpgradeValueFormat.OneDecimal, 1.65f, 0.9f, 110, 1.2f),
                Create(DroneUpgradeId.FlightHandling, DroneUpgradeGroup.Flight, "Handling", DroneUpgradeValueFormat.OneDecimal, 1.45f, 0.82f, 115, 1.2f),
            };
        }

        private static DroneUpgradeDefinition Create(
            DroneUpgradeId id,
            DroneUpgradeGroup group,
            string displayName,
            DroneUpgradeValueFormat valueFormat,
            float tier15Multiplier,
            float progressionExponent,
            int baseGoldCost,
            float goldCostGrowth)
        {
            return new DroneUpgradeDefinition
            {
                Id = id,
                Group = group,
                DisplayName = displayName,
                ValueFormat = valueFormat,
                Tier15Multiplier = tier15Multiplier,
                ProgressionExponent = progressionExponent,
                BaseGoldCost = baseGoldCost,
                GoldCostGrowth = goldCostGrowth,
            };
        }
    }
}
