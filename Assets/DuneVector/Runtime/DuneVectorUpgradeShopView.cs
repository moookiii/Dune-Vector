using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuneVector
{
    public sealed class DuneVectorUpgradeShopView : IDisposable
    {
        private readonly DronePermanentUpgradeSystem _upgrades;
        private readonly DroneGoldWallet _wallet;
        private readonly UpgradeShopVisualTuning _visuals;
        private readonly Dictionary<DroneUpgradeId, float> _displayedTiers = new Dictionary<DroneUpgradeId, float>();
        private readonly Dictionary<DroneUpgradeId, Texture2D> _icons = new Dictionary<DroneUpgradeId, Texture2D>();

        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _goldStyle;
        private GUIStyle _goldDeltaStyle;
        private GUIStyle _groupStyle;
        private GUIStyle _nameStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _valueRightStyle;
        private GUIStyle _tierStyle;
        private GUIStyle _tierRightStyle;
        private GUIStyle _costStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _disabledButtonStyle;
        private GUIStyle _maximumButtonStyle;
        private GUIStyle _backButtonStyle;

        private Texture2D _buttonTexture;
        private Texture2D _buttonHoverTexture;
        private Texture2D _disabledButtonTexture;
        private Texture2D _maximumButtonTexture;
        private Texture2D _backButtonTexture;
        private Texture2D _backButtonHoverTexture;
        private Texture2D _hubRgbTerminalIcon;
        private Vector2 _scrollPosition;
        private float _styledScale = -1f;
        private float _displayedGold;
        private bool _hasDisplayedGold;
        private DroneUpgradeId _recentUpgrade;
        private float _recentPurchaseUntil;
        private int _lastGoldCost;
        private float _goldDeductionUntil;
        private float _hubRgbTerminalRecentUntil;
        private string _statusMessage;
        private float _statusUntil;

        public DuneVectorUpgradeShopView(
            DronePermanentUpgradeSystem upgrades,
            DroneGoldWallet wallet,
            UpgradeShopVisualTuning visuals)
        {
            _upgrades = upgrades;
            _wallet = wallet;
            _visuals = visuals;
        }

        public void Open()
        {
            if (_wallet != null)
            {
                _displayedGold = _wallet.Gold;
                _hasDisplayedGold = true;
            }
        }

        public bool Draw()
        {
            if (_upgrades == null || _wallet == null || _visuals == null)
            {
                return true;
            }

            float scale = CalculateScale();
            EnsureStyles(scale);
            UpdateAnimations();

            float margin = _visuals.ScreenMargin * scale;
            float panelWidth = Mathf.Min(_visuals.PanelWidth * scale, Screen.width - (margin * 2f));
            float panelHeight = Mathf.Min(_visuals.PanelHeight * scale, Screen.height - (margin * 2f));
            Rect panel = new Rect(
                (Screen.width - panelWidth) * 0.5f,
                (Screen.height - panelHeight) * 0.5f,
                panelWidth,
                panelHeight);

            float shadowOffset = _visuals.ShadowOffset * scale;
            DrawSolidRect(new Rect(panel.x + shadowOffset, panel.y + shadowOffset, panel.width, panel.height), _visuals.ShadowColor);
            DrawSolidRect(panel, _visuals.PanelColor);
            DrawBorder(panel, _visuals.PanelBorderColor, Mathf.Max(1f, _visuals.BorderThickness * scale));
            DrawSolidRect(new Rect(panel.x, panel.y, panel.width, _visuals.AccentBarHeight * scale), _visuals.CoreColor);

            float padding = _visuals.PanelPadding * scale;
            float headerHeight = _visuals.HeaderHeight * scale;
            Rect header = new Rect(panel.x, panel.y, panel.width, headerHeight);
            DrawSolidRect(header, _visuals.HeaderColor);
            float borderThickness = Mathf.Max(1f, _visuals.BorderThickness * scale);
            DrawSolidRect(new Rect(panel.x + padding, header.yMax - borderThickness, panel.width - (padding * 2f), borderThickness), _visuals.DividerColor);

            Rect backButton = new Rect(
                panel.x + padding,
                panel.y + ((headerHeight - (_visuals.BackButtonHeight * scale)) * 0.5f),
                _visuals.BackButtonWidth * scale,
                _visuals.BackButtonHeight * scale);
            bool backRequested = GUI.Button(backButton, "‹  PAUSE", _backButtonStyle);

            float titleX = backButton.xMax + (_visuals.ColumnGap * scale);
            float goldWidth = _visuals.GoldPanelWidth * scale;
            float titleBlockHeight = _titleStyle.lineHeight + _subtitleStyle.lineHeight;
            Rect titleRect = new Rect(titleX, panel.y + ((headerHeight - titleBlockHeight) * 0.5f), panel.xMax - padding - goldWidth - titleX, _titleStyle.lineHeight);
            GUI.Label(titleRect, "DRONE UPGRADE SHOP", _titleStyle);
            GUI.Label(
                new Rect(titleRect.x, titleRect.yMax, titleRect.width, _subtitleStyle.lineHeight),
                "PERMANENT SYSTEM CALIBRATION  /  15 UPGRADE TIERS PER STAT",
                _subtitleStyle);

            Rect goldPanel = new Rect(
                panel.xMax - padding - goldWidth,
                panel.y + ((headerHeight - (_visuals.GoldPanelHeight * scale)) * 0.5f),
                goldWidth,
                _visuals.GoldPanelHeight * scale);
            DrawSolidRect(goldPanel, Color.Lerp(_visuals.PanelColor, _visuals.GoldColor, _visuals.GoldPanelTintAmount));
            DrawBorder(goldPanel, WithAlpha(_visuals.GoldColor, _visuals.GoldPanelBorderOpacity), borderThickness);
            GUI.Label(goldPanel, $"GOLD  {Mathf.RoundToInt(_displayedGold):N0}", _goldStyle);
            DrawGoldDeduction(goldPanel, scale);

            Rect scrollViewport = new Rect(
                panel.x + padding,
                header.yMax + (_visuals.RowGap * scale),
                panel.width - (padding * 2f),
                panel.yMax - padding - header.yMax - (_visuals.RowGap * scale));
            float contentHeight = CalculateContentHeight(scale);
            float scrollbarClearance = (_visuals.ScrollbarWidth + _visuals.ScrollbarContentGap) * scale;
            Rect scrollContent = new Rect(
                0f,
                0f,
                Mathf.Max(1f, scrollViewport.width - scrollbarClearance),
                contentHeight);
            _scrollPosition = GUI.BeginScrollView(scrollViewport, _scrollPosition, scrollContent, false, true);
            DrawGroups(scrollContent.width, scale);
            GUI.EndScrollView();

            if (!string.IsNullOrEmpty(_statusMessage) && Time.unscaledTime < _statusUntil)
            {
                GUI.Label(
                    new Rect(panel.x + padding, panel.yMax - padding, panel.width - (padding * 2f), _subtitleStyle.lineHeight),
                    _statusMessage,
                    _subtitleStyle);
            }

            return backRequested;
        }

        private void DrawGroups(float width, float scale)
        {
            float y = 0f;
            DroneUpgradeGroup[] groups =
            {
                DroneUpgradeGroup.Core,
                DroneUpgradeGroup.Ground,
                DroneUpgradeGroup.Flight,
                DroneUpgradeGroup.Cosmetic,
            };

            for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                DroneUpgradeGroup group = groups[groupIndex];
                if (groupIndex > 0)
                {
                    y += _visuals.GroupGap * scale;
                }

                Color groupColor = GetGroupColor(group);
                float groupHeaderHeight = _visuals.GroupHeaderHeight * scale;
                Rect groupHeader = new Rect(0f, y, width, groupHeaderHeight);
                DrawSolidRect(groupHeader, _visuals.RowColor);
                DrawSolidRect(
                    new Rect(
                        groupHeader.x,
                        groupHeader.yMax - Mathf.Max(1f, _visuals.BorderThickness * scale),
                        groupHeader.width,
                        Mathf.Max(1f, _visuals.BorderThickness * scale)),
                    WithAlpha(groupColor, _visuals.GroupLineOpacity));
                DrawSolidRect(new Rect(groupHeader.x, groupHeader.y, _visuals.GroupAccentWidth * scale, groupHeader.height), groupColor);
                _groupStyle.normal.textColor = groupColor;
                GUI.Label(new Rect(groupHeader.x + (_visuals.GroupLabelIndent * scale), groupHeader.y, groupHeader.width, groupHeader.height), group.ToString().ToUpperInvariant(), _groupStyle);
                y += groupHeaderHeight;

                if (group == DroneUpgradeGroup.Cosmetic)
                {
                    DrawHubRgbTerminalRow(new Rect(0f, y, width, _visuals.RowHeight * scale), groupColor, scale);
                    y += (_visuals.RowHeight + _visuals.RowGap) * scale;
                    if (_upgrades.IsAtlasGlyphMaterialAvailable)
                    {
                        DrawAtlasGlyphMaterialRow(
                            new Rect(0f, y, width, _visuals.RowHeight * scale),
                            groupColor,
                            scale);
                        y += (_visuals.RowHeight + _visuals.RowGap) * scale;
                    }
                    continue;
                }

                IReadOnlyList<DroneUpgradeDefinition> definitions = _upgrades.Definitions;
                for (int index = 0; index < definitions.Count; index++)
                {
                    DroneUpgradeDefinition definition = definitions[index];
                    if (definition == null || definition.Group != group)
                    {
                        continue;
                    }

                    DrawUpgradeRow(new Rect(0f, y, width, _visuals.RowHeight * scale), definition, groupColor, scale);
                    y += (_visuals.RowHeight + _visuals.RowGap) * scale;
                }
            }
        }

        private void DrawHubRgbTerminalRow(Rect row, Color groupColor, float scale)
        {
            HubRgbTerminalUnlockTuning tuning = _upgrades.HubRgbTerminalTuning;
            if (tuning == null)
            {
                return;
            }

            bool unlocked = _upgrades.AreHubRgbTerminalsUnlocked;
            bool enabled = _upgrades.AreHubRgbTerminalsEnabled;
            bool recent = Time.unscaledTime < _hubRgbTerminalRecentUntil;
            bool hovered = row.Contains(Event.current.mousePosition);
            Color rowColor = recent ? _visuals.RecentPurchaseColor : hovered ? _visuals.RowHoverColor : _visuals.RowColor;
            DrawSolidRect(row, rowColor);
            DrawBorder(
                row,
                WithAlpha(groupColor, recent ? _visuals.RecentBorderOpacity : _visuals.RowBorderOpacity),
                Mathf.Max(1f, _visuals.BorderThickness * scale));
            DrawSolidRect(new Rect(row.x, row.y, _visuals.RowAccentWidth * scale, row.height), groupColor);

            float columnGap = _visuals.ColumnGap * scale;
            float iconSize = _visuals.IconSize * scale;
            Rect iconPanel = new Rect(row.x + columnGap, row.y + ((row.height - iconSize) * 0.5f), iconSize, iconSize);
            DrawSolidRect(iconPanel, _visuals.RowColor);
            DrawBorder(iconPanel, WithAlpha(groupColor, _visuals.IconBorderOpacity), Mathf.Max(1f, _visuals.BorderThickness * scale));
            GUI.DrawTexture(iconPanel, GetHubRgbTerminalIcon(tuning), ScaleMode.ScaleToFit, true);

            float detailsX = iconPanel.xMax + columnGap;
            float detailsWidth = _visuals.DetailsColumnWidth * scale;
            float rowPadding = _visuals.RowVerticalPadding * scale;
            Rect nameRect = new Rect(detailsX, row.y + rowPadding, detailsWidth, _nameStyle.lineHeight);
            GUI.Label(nameRect, (tuning.DisplayName ?? string.Empty).ToUpperInvariant(), _nameStyle);
            GUI.Label(
                new Rect(detailsX, nameRect.yMax + (_visuals.ValueLineGap * scale), detailsWidth, _valueStyle.lineHeight),
                (tuning.Description ?? string.Empty).ToUpperInvariant(),
                _valueStyle);

            float actionWidth = _visuals.ActionColumnWidth * scale;
            float statusX = detailsX + detailsWidth + columnGap;
            float statusWidth = Mathf.Max(
                _visuals.MinimumTierIndicatorWidth * scale,
                row.xMax - statusX - actionWidth - (columnGap * 2f));
            _valueRightStyle.normal.textColor = unlocked ? _visuals.MaximumColor : groupColor;
            GUI.Label(
                new Rect(statusX, row.y + ((row.height - _valueRightStyle.lineHeight) * 0.5f), statusWidth, _valueRightStyle.lineHeight),
                unlocked
                    ? enabled ? "RGB TERMINALS ACTIVE" : "RGB TERMINALS DISABLED"
                    : "ONE-TIME COSMETIC UNLOCK",
                _valueRightStyle);

            float actionX = row.xMax - actionWidth - columnGap;
            Rect action = new Rect(actionX, row.y, actionWidth, row.height);
            if (unlocked)
            {
                _costStyle.normal.textColor = enabled ? _visuals.MaximumColor : _visuals.MutedTextColor;
                GUI.Label(
                    new Rect(action.x, action.y + rowPadding, action.width, _costStyle.lineHeight),
                    enabled ? "ACTIVE" : "DISABLED",
                    _costStyle);
                bool toggleRequested = GUI.Button(
                    new Rect(action.x + ((action.width - (_visuals.PurchaseButtonWidth * scale)) * 0.5f), action.yMax - (_visuals.PurchaseButtonHeight * scale) - rowPadding, _visuals.PurchaseButtonWidth * scale, _visuals.PurchaseButtonHeight * scale),
                    enabled ? "DISABLE" : "ENABLE",
                    _buttonStyle);
                if (toggleRequested)
                {
                    HandleHubRgbTerminalToggle(!enabled);
                }
                return;
            }

            int cost = _upgrades.GetHubRgbTerminalGoldCost();
            bool affordable = _upgrades.CanUnlockHubRgbTerminals();
            _costStyle.normal.textColor = affordable ? _visuals.GoldColor : _visuals.MutedTextColor;
            GUI.Label(new Rect(action.x, action.y + rowPadding, action.width, _costStyle.lineHeight), $"COST  {cost:N0} GOLD", _costStyle);
            Rect purchaseButton = new Rect(
                action.x + ((action.width - (_visuals.PurchaseButtonWidth * scale)) * 0.5f),
                action.yMax - (_visuals.PurchaseButtonHeight * scale) - rowPadding,
                _visuals.PurchaseButtonWidth * scale,
                _visuals.PurchaseButtonHeight * scale);
            bool purchaseRequested = GUI.Button(
                purchaseButton,
                affordable ? "UNLOCK" : "INSUFFICIENT GOLD",
                affordable ? _buttonStyle : _disabledButtonStyle);
            if (purchaseRequested && affordable)
            {
                HandleHubRgbTerminalPurchase();
            }
        }

        private void HandleHubRgbTerminalPurchase()
        {
            UpgradePurchaseFailure failure = _upgrades.TryUnlockHubRgbTerminals(out int goldCost);
            if (failure == UpgradePurchaseFailure.None)
            {
                _hubRgbTerminalRecentUntil = Time.unscaledTime + _visuals.RecentPurchaseDuration;
                _lastGoldCost = goldCost;
                _goldDeductionUntil = Time.unscaledTime + _visuals.GoldDeductionDuration;
                _statusMessage = "RGB HUB TERMINALS INSTALLED";
                _statusUntil = Time.unscaledTime + _visuals.RecentPurchaseDuration;
                return;
            }

            _statusMessage = failure switch
            {
                UpgradePurchaseFailure.CannotAfford => "INSUFFICIENT GOLD",
                UpgradePurchaseFailure.CurrencySaveFailed => "CURRENCY TRANSACTION COULD NOT BE SAVED",
                UpgradePurchaseFailure.UpgradeSaveFailed => "UPGRADE SAVE FAILED  /  GOLD REFUNDED",
                UpgradePurchaseFailure.MaximumTierReached => "RGB HUB TERMINALS ALREADY INSTALLED",
                _ => "COSMETIC UNLOCK UNAVAILABLE",
            };
            _statusUntil = Time.unscaledTime + _visuals.RecentPurchaseDuration;
        }

        private void HandleHubRgbTerminalToggle(bool enabled)
        {
            UpgradePurchaseFailure failure = _upgrades.TrySetHubRgbTerminalsEnabled(enabled);
            if (failure == UpgradePurchaseFailure.None)
            {
                _hubRgbTerminalRecentUntil = Time.unscaledTime + _visuals.RecentPurchaseDuration;
                _statusMessage = enabled
                    ? "RGB HUB TERMINALS ENABLED"
                    : "RGB HUB TERMINALS DISABLED";
            }
            else
            {
                _statusMessage = failure == UpgradePurchaseFailure.UpgradeSaveFailed
                    ? "RGB TERMINAL SETTING COULD NOT BE SAVED"
                    : "RGB TERMINAL SETTING UNAVAILABLE";
            }
            _statusUntil = Time.unscaledTime + _visuals.RecentPurchaseDuration;
        }

        private void DrawAtlasGlyphMaterialRow(Rect row, Color groupColor, float scale)
        {
            AtlasGlyphMaterialUnlockTuning tuning = _upgrades.AtlasGlyphMaterialTuning;
            if (tuning?.GlyphMaterial == null)
            {
                return;
            }

            bool enabled = _upgrades.IsAtlasGlyphBrushedMetalEnabled;
            bool hovered = row.Contains(Event.current.mousePosition);
            DrawSolidRect(row, hovered ? _visuals.RowHoverColor : _visuals.RowColor);
            DrawBorder(
                row,
                WithAlpha(groupColor, _visuals.RowBorderOpacity),
                Mathf.Max(1f, _visuals.BorderThickness * scale));
            DrawSolidRect(new Rect(row.x, row.y, _visuals.RowAccentWidth * scale, row.height), groupColor);

            float columnGap = _visuals.ColumnGap * scale;
            float iconSize = _visuals.IconSize * scale;
            Rect iconPanel = new Rect(row.x + columnGap, row.y + ((row.height - iconSize) * 0.5f), iconSize, iconSize);
            DrawSolidRect(iconPanel, GetMaterialDisplayColor(tuning.GlyphMaterial));
            DrawBorder(iconPanel, WithAlpha(groupColor, _visuals.IconBorderOpacity), Mathf.Max(1f, _visuals.BorderThickness * scale));

            float detailsX = iconPanel.xMax + columnGap;
            float detailsWidth = _visuals.DetailsColumnWidth * scale;
            float rowPadding = _visuals.RowVerticalPadding * scale;
            Rect nameRect = new Rect(detailsX, row.y + rowPadding, detailsWidth, _nameStyle.lineHeight);
            GUI.Label(nameRect, (tuning.DisplayName ?? string.Empty).ToUpperInvariant(), _nameStyle);
            GUI.Label(
                new Rect(detailsX, nameRect.yMax + (_visuals.ValueLineGap * scale), detailsWidth, _valueStyle.lineHeight),
                (tuning.Description ?? string.Empty).ToUpperInvariant(),
                _valueStyle);

            float actionWidth = _visuals.ActionColumnWidth * scale;
            float statusX = detailsX + detailsWidth + columnGap;
            float statusWidth = Mathf.Max(
                _visuals.MinimumTierIndicatorWidth * scale,
                row.xMax - statusX - actionWidth - (columnGap * 2f));
            _valueRightStyle.normal.textColor = enabled ? _visuals.MaximumColor : _visuals.MutedTextColor;
            GUI.Label(
                new Rect(statusX, row.y + ((row.height - _valueRightStyle.lineHeight) * 0.5f), statusWidth, _valueRightStyle.lineHeight),
                enabled ? "BRUSHED METAL ACTIVE" : "BRUSHED METAL DISABLED",
                _valueRightStyle);

            float actionX = row.xMax - actionWidth - columnGap;
            Rect action = new Rect(actionX, row.y, actionWidth, row.height);
            _costStyle.normal.textColor = enabled ? _visuals.MaximumColor : _visuals.MutedTextColor;
            GUI.Label(
                new Rect(action.x, action.y + rowPadding, action.width, _costStyle.lineHeight),
                enabled ? "ACTIVE" : "DISABLED",
                _costStyle);
            bool toggleRequested = GUI.Button(
                new Rect(action.x + ((action.width - (_visuals.PurchaseButtonWidth * scale)) * 0.5f), action.yMax - (_visuals.PurchaseButtonHeight * scale) - rowPadding, _visuals.PurchaseButtonWidth * scale, _visuals.PurchaseButtonHeight * scale),
                enabled ? "DISABLE" : "ENABLE",
                _buttonStyle);
            if (toggleRequested)
            {
                HandleAtlasGlyphMaterialToggle(!enabled);
            }
        }

        private void HandleAtlasGlyphMaterialToggle(bool enabled)
        {
            UpgradePurchaseFailure failure = _upgrades.TrySetAtlasGlyphBrushedMetalEnabled(enabled);
            _statusMessage = failure == UpgradePurchaseFailure.None
                ? enabled ? "BRUSHED METAL GLYPHS ENABLED" : "BRUSHED METAL GLYPHS DISABLED"
                : failure == UpgradePurchaseFailure.UpgradeSaveFailed
                    ? "GLYPH MATERIAL SETTING COULD NOT BE SAVED"
                    : "GLYPH MATERIAL SETTING UNAVAILABLE";
            _statusUntil = Time.unscaledTime + _visuals.RecentPurchaseDuration;
        }

        private void DrawUpgradeRow(Rect row, DroneUpgradeDefinition definition, Color groupColor, float scale)
        {
            bool recent = definition.Id == _recentUpgrade && Time.unscaledTime < _recentPurchaseUntil;
            bool hovered = row.Contains(Event.current.mousePosition);
            Color rowColor = recent ? _visuals.RecentPurchaseColor : hovered ? _visuals.RowHoverColor : _visuals.RowColor;
            DrawSolidRect(row, rowColor);
            DrawBorder(
                row,
                WithAlpha(groupColor, recent ? _visuals.RecentBorderOpacity : _visuals.RowBorderOpacity),
                Mathf.Max(1f, _visuals.BorderThickness * scale));
            DrawSolidRect(new Rect(row.x, row.y, _visuals.RowAccentWidth * scale, row.height), groupColor);

            float columnGap = _visuals.ColumnGap * scale;
            float iconSize = _visuals.IconSize * scale;
            Rect iconPanel = new Rect(row.x + columnGap, row.y + ((row.height - iconSize) * 0.5f), iconSize, iconSize);
            DrawSolidRect(iconPanel, Color.Lerp(_visuals.RowColor, groupColor, _visuals.IconPanelTintAmount));
            DrawBorder(iconPanel, WithAlpha(groupColor, _visuals.IconBorderOpacity), Mathf.Max(1f, _visuals.BorderThickness * scale));
            GUI.DrawTexture(iconPanel, GetIcon(definition.Id, groupColor), ScaleMode.ScaleToFit, true);

            float detailsX = iconPanel.xMax + columnGap;
            float detailsWidth = _visuals.DetailsColumnWidth * scale;
            float rowPadding = _visuals.RowVerticalPadding * scale;
            Rect nameRect = new Rect(detailsX, row.y + rowPadding, detailsWidth, _nameStyle.lineHeight);
            GUI.Label(nameRect, definition.DisplayName.ToUpperInvariant(), _nameStyle);

            int currentTier = _upgrades.GetPurchasedTier(definition.Id);
            int maximumTier = _upgrades.GetMaximumTier(definition.Id);
            bool maximum = currentTier >= maximumTier;
            float currentValue = _upgrades.GetCurrentValue(definition.Id);
            float nextValue = _upgrades.GetNextValue(definition.Id);
            string currentLabel = $"CURRENT  {FormatUpgradeValue(definition, currentValue, false)}";
            string nextLabel = maximum ? "CAPACITY COMPLETE" : $"NEXT  {FormatUpgradeValue(definition, nextValue, true)}";
            Color previousValueColor = _valueStyle.normal.textColor;
            if (recent)
            {
                float pulse = (Mathf.Sin(Time.unscaledTime * _visuals.ValuePulseSpeed) + 1f) * 0.5f;
                _valueStyle.normal.textColor = Color.Lerp(
                    _visuals.PrimaryTextColor,
                    groupColor,
                    pulse * _visuals.ValuePulseAmount);
            }
            GUI.Label(new Rect(detailsX, nameRect.yMax + (_visuals.ValueLineGap * scale), detailsWidth, _valueStyle.lineHeight), currentLabel, _valueStyle);
            _valueStyle.normal.textColor = previousValueColor;
            _valueRightStyle.normal.textColor = maximum ? _visuals.MaximumColor : groupColor;
            GUI.Label(new Rect(detailsX, row.yMax - _valueRightStyle.lineHeight - rowPadding, detailsWidth, _valueRightStyle.lineHeight), nextLabel, _valueRightStyle);

            float actionWidth = _visuals.ActionColumnWidth * scale;
            float progressX = detailsX + detailsWidth + columnGap;
            float progressWidth = Mathf.Max(_visuals.MinimumTierIndicatorWidth * scale, row.xMax - progressX - actionWidth - (columnGap * 2f));
            DrawTierIndicator(
                new Rect(progressX, row.y + (_visuals.TierIndicatorTop * scale), progressWidth, _visuals.TierSegmentHeight * scale),
                definition.Id,
                currentTier,
                maximumTier,
                groupColor,
                scale);

            int remaining = _upgrades.GetRemainingTierCapacity(definition.Id);
            GUI.Label(
                new Rect(progressX, row.y + (_visuals.TierLabelTop * scale), progressWidth * 0.5f, _tierStyle.lineHeight),
                $"TIER {currentTier} / {maximumTier}",
                _tierStyle);
            GUI.Label(
                new Rect(progressX + (progressWidth * 0.5f), row.y + (_visuals.TierLabelTop * scale), progressWidth * 0.5f, _tierRightStyle.lineHeight),
                maximum ? "NO CAPACITY REMAINING" : $"{remaining} TIERS REMAIN",
                _tierRightStyle);

            float actionX = row.xMax - actionWidth - columnGap;
            Rect action = new Rect(actionX, row.y, actionWidth, row.height);
            if (maximum)
            {
                _costStyle.normal.textColor = _visuals.MaximumColor;
                GUI.Label(new Rect(action.x, action.y + rowPadding, action.width, _costStyle.lineHeight), "SYSTEM OPTIMIZED", _costStyle);
                GUI.Button(
                    new Rect(action.x + ((action.width - (_visuals.PurchaseButtonWidth * scale)) * 0.5f), action.yMax - (_visuals.PurchaseButtonHeight * scale) - rowPadding, _visuals.PurchaseButtonWidth * scale, _visuals.PurchaseButtonHeight * scale),
                    "MAX",
                    _maximumButtonStyle);
                return;
            }

            int cost = _upgrades.GetNextGoldCost(definition.Id);
            bool affordable = _upgrades.CanAffordNextTier(definition.Id);
            _costStyle.normal.textColor = affordable ? _visuals.GoldColor : _visuals.MutedTextColor;
            GUI.Label(new Rect(action.x, action.y + rowPadding, action.width, _costStyle.lineHeight), $"COST  {cost:N0} GOLD", _costStyle);
            Rect purchaseButton = new Rect(
                action.x + ((action.width - (_visuals.PurchaseButtonWidth * scale)) * 0.5f),
                action.yMax - (_visuals.PurchaseButtonHeight * scale) - rowPadding,
                _visuals.PurchaseButtonWidth * scale,
                _visuals.PurchaseButtonHeight * scale);
            bool purchaseRequested = GUI.Button(
                purchaseButton,
                affordable ? "PURCHASE TIER" : "INSUFFICIENT GOLD",
                affordable ? _buttonStyle : _disabledButtonStyle);
            if (purchaseRequested && affordable)
            {
                HandlePurchase(definition.Id);
            }
        }

        private void DrawTierIndicator(
            Rect area,
            DroneUpgradeId id,
            int currentTier,
            int maximumTier,
            Color groupColor,
            float scale)
        {
            if (!_displayedTiers.TryGetValue(id, out float displayedTier))
            {
                displayedTier = currentTier;
                _displayedTiers[id] = displayedTier;
            }

            float gap = _visuals.TierSegmentGap * scale;
            int segmentCount = Mathf.Max(1, maximumTier);
            float segmentWidth = Mathf.Max(1f, (area.width - (gap * (segmentCount - 1))) / segmentCount);
            for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                Rect segment = new Rect(area.x + (segmentIndex * (segmentWidth + gap)), area.y, segmentWidth, area.height);
                DrawSolidRect(segment, _visuals.TierEmptyColor);
                float fill = Mathf.Clamp01(displayedTier - segmentIndex);
                if (fill > 0f)
                {
                    DrawSolidRect(new Rect(segment.x, segment.y, segment.width * fill, segment.height), groupColor);
                }
            }
        }

        private void HandlePurchase(DroneUpgradeId id)
        {
            UpgradePurchaseResult result = _upgrades.TryPurchase(id);
            if (result.Succeeded)
            {
                _recentUpgrade = id;
                _recentPurchaseUntil = Time.unscaledTime + _visuals.RecentPurchaseDuration;
                _lastGoldCost = result.GoldCost;
                _goldDeductionUntil = Time.unscaledTime + _visuals.GoldDeductionDuration;
                _statusMessage = "UPGRADE TIER INSTALLED";
                _statusUntil = Time.unscaledTime + _visuals.RecentPurchaseDuration;
                return;
            }

            _statusMessage = result.Failure switch
            {
                UpgradePurchaseFailure.CannotAfford => "INSUFFICIENT GOLD",
                UpgradePurchaseFailure.MaximumTierReached => "MAXIMUM UPGRADE TIER REACHED",
                UpgradePurchaseFailure.CurrencySaveFailed => "CURRENCY TRANSACTION COULD NOT BE SAVED",
                UpgradePurchaseFailure.UpgradeSaveFailed => "UPGRADE SAVE FAILED  /  GOLD REFUNDED",
                _ => "UPGRADE UNAVAILABLE",
            };
            _statusUntil = Time.unscaledTime + _visuals.RecentPurchaseDuration;
        }

        private void DrawGoldDeduction(Rect goldPanel, float scale)
        {
            float remaining = _goldDeductionUntil - Time.unscaledTime;
            if (remaining <= 0f || _lastGoldCost <= 0)
            {
                return;
            }

            float duration = Mathf.Max(0.01f, _visuals.GoldDeductionDuration);
            Color color = _visuals.GoldColor;
            color.a *= Mathf.Clamp01(remaining / duration);
            _goldDeltaStyle.normal.textColor = color;
            float rise = (1f - Mathf.Clamp01(remaining / duration)) * (_visuals.GoldDeductionRise * scale);
            GUI.Label(
                new Rect(goldPanel.x, goldPanel.yMax - rise, goldPanel.width, _goldDeltaStyle.lineHeight),
                $"-{_lastGoldCost:N0}",
                _goldDeltaStyle);
        }

        private void UpdateAnimations()
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (!_hasDisplayedGold)
            {
                _displayedGold = _wallet.Gold;
                _hasDisplayedGold = true;
            }

            float goldDifference = Mathf.Max(0f, _displayedGold - _wallet.Gold);
            float goldAnimationSpeed = Mathf.Max(1f, _visuals.GoldCountAnimationSpeed)
                + (goldDifference * Mathf.Max(0f, _visuals.GoldCountDistanceSpeedMultiplier));
            _displayedGold = Mathf.MoveTowards(
                _displayedGold,
                _wallet.Gold,
                goldAnimationSpeed * Time.unscaledDeltaTime);

            float tierSpeed = 1f / Mathf.Max(0.01f, _visuals.TierAnimationDuration);
            IReadOnlyList<DroneUpgradeDefinition> definitions = _upgrades.Definitions;
            for (int index = 0; index < definitions.Count; index++)
            {
                DroneUpgradeDefinition definition = definitions[index];
                if (definition == null || !_displayedTiers.TryGetValue(definition.Id, out float displayed))
                {
                    continue;
                }

                _displayedTiers[definition.Id] = Mathf.MoveTowards(
                    displayed,
                    _upgrades.GetPurchasedTier(definition.Id),
                    tierSpeed * Time.unscaledDeltaTime);
            }
        }

        private float CalculateContentHeight(float scale)
        {
            int definitionCount = 0;
            IReadOnlyList<DroneUpgradeDefinition> definitions = _upgrades.Definitions;
            for (int index = 0; index < definitions.Count; index++)
            {
                if (definitions[index] != null)
                {
                    definitionCount++;
                }
            }

            int cosmeticRowCount = 1 + (_upgrades.IsAtlasGlyphMaterialAvailable ? 1 : 0);
            return ((4f * _visuals.GroupHeaderHeight)
                + ((definitionCount + cosmeticRowCount) * (_visuals.RowHeight + _visuals.RowGap))
                + (3f * _visuals.GroupGap)
                + _visuals.RowGap) * scale;
        }

        private float CalculateScale()
        {
            float widthScale = Screen.width / Mathf.Max(1f, _visuals.ReferenceWidth);
            float heightScale = Screen.height / Mathf.Max(1f, _visuals.ReferenceHeight);
            return Mathf.Clamp(Mathf.Min(widthScale, heightScale), _visuals.MinimumScale, _visuals.MaximumScale);
        }

        private void EnsureStyles(float scale)
        {
            EnsureTextures();
            if (Mathf.Abs(scale - _styledScale) < 0.001f)
            {
                return;
            }
            _styledScale = scale;

            _titleStyle = CreateLabelStyle(_visuals.TitleFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, _visuals.PrimaryTextColor, scale);
            _subtitleStyle = CreateLabelStyle(_visuals.SubtitleFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, _visuals.SecondaryTextColor, scale);
            _goldStyle = CreateLabelStyle(_visuals.GoldFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, _visuals.GoldColor, scale);
            _goldDeltaStyle = CreateLabelStyle(_visuals.CostFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, _visuals.GoldColor, scale);
            _groupStyle = CreateLabelStyle(_visuals.GroupFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, _visuals.CoreColor, scale);
            _nameStyle = CreateLabelStyle(_visuals.UpgradeNameFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, _visuals.PrimaryTextColor, scale);
            _valueStyle = CreateLabelStyle(_visuals.ValueFontSize, FontStyle.Normal, TextAnchor.MiddleLeft, _visuals.SecondaryTextColor, scale);
            _valueRightStyle = CreateLabelStyle(_visuals.ValueFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, _visuals.CoreColor, scale);
            _tierStyle = CreateLabelStyle(_visuals.TierFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, _visuals.PrimaryTextColor, scale);
            _tierRightStyle = CreateLabelStyle(_visuals.TierFontSize, FontStyle.Normal, TextAnchor.MiddleRight, _visuals.SecondaryTextColor, scale);
            _costStyle = CreateLabelStyle(_visuals.CostFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, _visuals.GoldColor, scale);
            _buttonStyle = CreateButtonStyle(_buttonTexture, _buttonHoverTexture, _visuals.PrimaryTextColor, scale);
            _disabledButtonStyle = CreateButtonStyle(_disabledButtonTexture, _disabledButtonTexture, _visuals.MutedTextColor, scale);
            _maximumButtonStyle = CreateButtonStyle(_maximumButtonTexture, _maximumButtonTexture, _visuals.MaximumColor, scale);
            _backButtonStyle = CreateButtonStyle(_backButtonTexture, _backButtonHoverTexture, _visuals.PrimaryTextColor, scale);
        }

        private GUIStyle CreateLabelStyle(
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Color color,
            float scale)
        {
            return new GUIStyle(GUI.skin.label)
            {
                alignment = alignment,
                fontSize = Mathf.Max(8, Mathf.RoundToInt(fontSize * scale)),
                fontStyle = fontStyle,
                clipping = TextClipping.Clip,
                wordWrap = false,
                padding = new RectOffset(),
                margin = new RectOffset(),
                normal = { textColor = color },
            };
        }

        private GUIStyle CreateButtonStyle(Texture2D normal, Texture2D hover, Color textColor, float scale)
        {
            GUIStyle style = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Max(8, Mathf.RoundToInt(_visuals.ButtonFontSize * scale)),
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                border = new RectOffset(),
                padding = new RectOffset(),
                margin = new RectOffset(),
            };
            style.normal.background = normal;
            style.normal.textColor = textColor;
            style.hover.background = hover;
            style.hover.textColor = textColor;
            style.active.background = hover;
            style.active.textColor = textColor;
            style.focused.background = hover;
            style.focused.textColor = textColor;
            style.onNormal = style.normal;
            return style;
        }

        private void EnsureTextures()
        {
            if (_buttonTexture != null)
            {
                return;
            }

            _buttonTexture = CreateSolidTexture("Upgrade Shop Purchase", _visuals.AffordableButtonColor);
            _buttonHoverTexture = CreateSolidTexture("Upgrade Shop Purchase Hover", _visuals.AffordableButtonHoverColor);
            _disabledButtonTexture = CreateSolidTexture("Upgrade Shop Unavailable", _visuals.UnaffordableButtonColor);
            _maximumButtonTexture = CreateSolidTexture(
                "Upgrade Shop Maximum",
                Color.Lerp(_visuals.UnaffordableButtonColor, _visuals.MaximumColor, _visuals.MaximumButtonTintAmount));
            _backButtonTexture = CreateSolidTexture("Upgrade Shop Back", _visuals.RowColor);
            _backButtonHoverTexture = CreateSolidTexture("Upgrade Shop Back Hover", _visuals.RowHoverColor);
        }

        private Texture2D GetIcon(DroneUpgradeId id, Color color)
        {
            if (_icons.TryGetValue(id, out Texture2D texture) && texture != null)
            {
                return texture;
            }

            texture = DuneVectorUpgradeIconFactory.Create(
                id,
                Mathf.Max(16, _visuals.IconTextureSize),
                Mathf.Max(1, _visuals.IconLineThickness),
                color);
            _icons[id] = texture;
            return texture;
        }

        private Texture2D GetHubRgbTerminalIcon(HubRgbTerminalUnlockTuning tuning)
        {
            if (_hubRgbTerminalIcon != null)
            {
                return _hubRgbTerminalIcon;
            }

            _hubRgbTerminalIcon = DuneVectorUpgradeIconFactory.CreateRgbHubTerminals(
                Mathf.Max(16, _visuals.IconTextureSize),
                Mathf.Max(1, _visuals.IconLineThickness),
                NormalizeGuiColor(tuning.Red),
                NormalizeGuiColor(tuning.Green),
                NormalizeGuiColor(tuning.Blue),
                _visuals.PrimaryTextColor);
            return _hubRgbTerminalIcon;
        }

        private Color GetGroupColor(DroneUpgradeGroup group)
        {
            return group switch
            {
                DroneUpgradeGroup.Ground => _visuals.GroundColor,
                DroneUpgradeGroup.Flight => _visuals.FlightColor,
                DroneUpgradeGroup.Cosmetic => _visuals.CosmeticColor,
                _ => _visuals.CoreColor,
            };
        }

        private static Color NormalizeGuiColor(Color color)
        {
            float maximum = Mathf.Max(1f, color.r, color.g, color.b);
            return new Color(color.r / maximum, color.g / maximum, color.b / maximum, 1f);
        }

        private static Color GetMaterialDisplayColor(Material material)
        {
            Color color = material != null && material.HasProperty("_Color")
                ? material.GetColor("_Color")
                : Color.gray;
            return NormalizeGuiColor(color);
        }

        private static string FormatValue(DroneUpgradeDefinition definition, float value)
        {
            return definition.ValueFormat switch
            {
                DroneUpgradeValueFormat.WholeNumber => Mathf.RoundToInt(value).ToString("N0"),
                DroneUpgradeValueFormat.TwoDecimalSeconds => $"{value:0.00}s",
                _ => value.ToString("0.0"),
            };
        }

        private string FormatUpgradeValue(DroneUpgradeDefinition definition, float value, bool nextTier)
        {
            string formattedValue = FormatValue(definition, value);
            if (definition.Id != DroneUpgradeId.EnergyShotCooldown)
            {
                return formattedValue;
            }

            float projectileSpeed = nextTier
                ? _upgrades.GetNextEnergyProjectileSpeed()
                : _upgrades.GetCurrentEnergyProjectileSpeed();
            return $"{formattedValue} / {projectileSpeed:0} M/S";
        }

        private static Texture2D CreateSolidTexture(string textureName, Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = textureName,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            return texture;
        }

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static void DrawBorder(Rect rect, Color color, float thickness)
        {
            DrawSolidRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawSolidRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        public void Dispose()
        {
            foreach (Texture2D icon in _icons.Values)
            {
                DestroyTexture(icon);
            }
            _icons.Clear();
            DestroyTexture(_buttonTexture);
            DestroyTexture(_buttonHoverTexture);
            DestroyTexture(_disabledButtonTexture);
            DestroyTexture(_maximumButtonTexture);
            DestroyTexture(_backButtonTexture);
            DestroyTexture(_backButtonHoverTexture);
            DestroyTexture(_hubRgbTerminalIcon);
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
        }
    }

    internal static class DuneVectorUpgradeIconFactory
    {
        public static Texture2D CreateRgbHubTerminals(
            int size,
            int thickness,
            Color panelColor,
            Color leftAntennaColor,
            Color rightAntennaColor,
            Color frameColor)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Upgrade Icon - RGB Hub Terminals",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels(new Color[size * size]);

            IconCanvas frame = new IconCanvas(texture, size, thickness, frameColor);
            frame.Polyline((0.2f, 0.22f), (0.8f, 0.22f), (0.8f, 0.62f), (0.2f, 0.62f), (0.2f, 0.22f));
            frame.Line(0.32f, 0.62f, 0.22f, 0.88f);
            frame.Line(0.68f, 0.62f, 0.78f, 0.88f);

            IconCanvas panel = new IconCanvas(texture, size, thickness, panelColor);
            IconCanvas leftAntenna = new IconCanvas(texture, size, thickness, leftAntennaColor);
            IconCanvas rightAntenna = new IconCanvas(texture, size, thickness, rightAntennaColor);
            panel.Circle(0.5f, 0.42f, 0.11f);
            leftAntenna.Circle(0.22f, 0.88f, 0.07f);
            rightAntenna.Circle(0.78f, 0.88f, 0.07f);

            texture.Apply(false, true);
            return texture;
        }

        public static Texture2D Create(DroneUpgradeId id, int size, int thickness, Color color)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = $"Upgrade Icon - {id}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            Color[] pixels = new Color[size * size];
            texture.SetPixels(pixels);

            IconCanvas canvas = new IconCanvas(texture, size, thickness, color);
            switch (id)
            {
                case DroneUpgradeId.MaximumHealth:
                    canvas.Polyline((0.5f, 0.12f), (0.78f, 0.24f), (0.74f, 0.62f), (0.5f, 0.86f), (0.26f, 0.62f), (0.22f, 0.24f), (0.5f, 0.12f));
                    canvas.Plus(0.5f, 0.47f, 0.13f);
                    break;
                case DroneUpgradeId.MaximumStamina:
                    canvas.Polyline((0.57f, 0.1f), (0.29f, 0.53f), (0.49f, 0.53f), (0.4f, 0.9f), (0.72f, 0.43f), (0.52f, 0.43f), (0.57f, 0.1f));
                    break;
                case DroneUpgradeId.BoostMaximumSpeed:
                    canvas.Polyline((0.17f, 0.29f), (0.42f, 0.5f), (0.17f, 0.71f));
                    canvas.Polyline((0.42f, 0.29f), (0.67f, 0.5f), (0.42f, 0.71f));
                    canvas.Line(0.7f, 0.5f, 0.88f, 0.5f);
                    break;
                case DroneUpgradeId.EnergyShotDamage:
                    canvas.Circle(0.48f, 0.5f, 0.16f);
                    canvas.Line(0.1f, 0.5f, 0.3f, 0.5f);
                    canvas.Line(0.66f, 0.5f, 0.9f, 0.5f);
                    canvas.Line(0.48f, 0.14f, 0.48f, 0.3f);
                    canvas.Line(0.48f, 0.7f, 0.48f, 0.86f);
                    canvas.Line(0.24f, 0.25f, 0.34f, 0.36f);
                    canvas.Line(0.62f, 0.64f, 0.74f, 0.76f);
                    break;
                case DroneUpgradeId.EnergyShotCooldown:
                    canvas.Circle(0.5f, 0.52f, 0.3f);
                    canvas.Line(0.5f, 0.52f, 0.5f, 0.31f);
                    canvas.Line(0.5f, 0.52f, 0.67f, 0.62f);
                    canvas.Polyline((0.67f, 0.13f), (0.84f, 0.2f), (0.75f, 0.35f));
                    break;
                case DroneUpgradeId.LockOnSpeed:
                    canvas.CornerBrackets(0.2f, 0.2f, 0.8f, 0.8f, 0.18f);
                    canvas.Circle(0.5f, 0.5f, 0.1f);
                    canvas.Line(0.5f, 0.1f, 0.5f, 0.28f);
                    break;
                case DroneUpgradeId.GroundMaximumSpeed:
                    canvas.Line(0.1f, 0.35f, 0.46f, 0.35f);
                    canvas.Line(0.18f, 0.5f, 0.52f, 0.5f);
                    canvas.Line(0.27f, 0.65f, 0.58f, 0.65f);
                    canvas.Circle(0.7f, 0.58f, 0.18f);
                    break;
                case DroneUpgradeId.GroundAcceleration:
                    canvas.Polyline((0.16f, 0.74f), (0.38f, 0.52f), (0.5f, 0.64f), (0.8f, 0.28f));
                    canvas.Polyline((0.62f, 0.29f), (0.8f, 0.28f), (0.78f, 0.46f));
                    canvas.Line(0.14f, 0.82f, 0.86f, 0.82f);
                    break;
                case DroneUpgradeId.GroundHandling:
                    canvas.Circle(0.5f, 0.52f, 0.28f);
                    canvas.Line(0.5f, 0.52f, 0.28f, 0.36f);
                    canvas.Line(0.5f, 0.52f, 0.72f, 0.36f);
                    canvas.Line(0.5f, 0.52f, 0.5f, 0.8f);
                    canvas.Polyline((0.18f, 0.19f), (0.1f, 0.3f), (0.24f, 0.32f));
                    canvas.Polyline((0.82f, 0.19f), (0.9f, 0.3f), (0.76f, 0.32f));
                    break;
                case DroneUpgradeId.FlightMaximumSpeed:
                    canvas.Polyline((0.1f, 0.55f), (0.42f, 0.46f), (0.5f, 0.18f), (0.58f, 0.46f), (0.9f, 0.55f), (0.57f, 0.62f), (0.5f, 0.82f), (0.43f, 0.62f), (0.1f, 0.55f));
                    canvas.Line(0.5f, 0.25f, 0.5f, 0.73f);
                    break;
                case DroneUpgradeId.FlightAcceleration:
                    canvas.Polyline((0.18f, 0.34f), (0.5f, 0.18f), (0.82f, 0.34f), (0.63f, 0.55f), (0.37f, 0.55f), (0.18f, 0.34f));
                    canvas.Polyline((0.38f, 0.61f), (0.5f, 0.87f), (0.62f, 0.61f));
                    canvas.Line(0.5f, 0.5f, 0.5f, 0.78f);
                    break;
                case DroneUpgradeId.FlightHandling:
                    canvas.Polyline((0.1f, 0.5f), (0.4f, 0.39f), (0.5f, 0.22f), (0.6f, 0.39f), (0.9f, 0.5f), (0.6f, 0.57f), (0.5f, 0.78f), (0.4f, 0.57f), (0.1f, 0.5f));
                    canvas.Polyline((0.18f, 0.72f), (0.31f, 0.82f), (0.38f, 0.68f));
                    canvas.Polyline((0.82f, 0.72f), (0.69f, 0.82f), (0.62f, 0.68f));
                    break;
            }

            texture.Apply(false, true);
            return texture;
        }

        private sealed class IconCanvas
        {
            private readonly Texture2D _texture;
            private readonly int _size;
            private readonly int _thickness;
            private readonly Color _color;

            public IconCanvas(Texture2D texture, int size, int thickness, Color color)
            {
                _texture = texture;
                _size = size;
                _thickness = thickness;
                _color = color;
            }

            public void Line(float x0, float y0, float x1, float y1)
            {
                int startX = ToPixel(x0);
                int startY = ToPixel(y0);
                int endX = ToPixel(x1);
                int endY = ToPixel(y1);
                int dx = Mathf.Abs(endX - startX);
                int sx = startX < endX ? 1 : -1;
                int dy = -Mathf.Abs(endY - startY);
                int sy = startY < endY ? 1 : -1;
                int error = dx + dy;
                while (true)
                {
                    Stamp(startX, startY);
                    if (startX == endX && startY == endY)
                    {
                        break;
                    }
                    int doubledError = error * 2;
                    if (doubledError >= dy)
                    {
                        error += dy;
                        startX += sx;
                    }
                    if (doubledError <= dx)
                    {
                        error += dx;
                        startY += sy;
                    }
                }
            }

            public void Polyline(params (float x, float y)[] points)
            {
                for (int index = 1; index < points.Length; index++)
                {
                    Line(points[index - 1].x, points[index - 1].y, points[index].x, points[index].y);
                }
            }

            public void Circle(float centerX, float centerY, float radius)
            {
                int steps = Mathf.Max(20, Mathf.RoundToInt(radius * _size * 4f));
                float previousX = centerX + radius;
                float previousY = centerY;
                for (int index = 1; index <= steps; index++)
                {
                    float angle = index * Mathf.PI * 2f / steps;
                    float x = centerX + (Mathf.Cos(angle) * radius);
                    float y = centerY + (Mathf.Sin(angle) * radius);
                    Line(previousX, previousY, x, y);
                    previousX = x;
                    previousY = y;
                }
            }

            public void Plus(float centerX, float centerY, float extent)
            {
                Line(centerX - extent, centerY, centerX + extent, centerY);
                Line(centerX, centerY - extent, centerX, centerY + extent);
            }

            public void CornerBrackets(float xMin, float yMin, float xMax, float yMax, float length)
            {
                Line(xMin, yMin, xMin + length, yMin);
                Line(xMin, yMin, xMin, yMin + length);
                Line(xMax, yMin, xMax - length, yMin);
                Line(xMax, yMin, xMax, yMin + length);
                Line(xMin, yMax, xMin + length, yMax);
                Line(xMin, yMax, xMin, yMax - length);
                Line(xMax, yMax, xMax - length, yMax);
                Line(xMax, yMax, xMax, yMax - length);
            }

            private int ToPixel(float normalized)
            {
                return Mathf.Clamp(Mathf.RoundToInt(normalized * (_size - 1)), 0, _size - 1);
            }

            private void Stamp(int x, int y)
            {
                int minimumOffset = -(_thickness / 2);
                int maximumOffset = (_thickness - 1) / 2;
                for (int offsetY = minimumOffset; offsetY <= maximumOffset; offsetY++)
                {
                    for (int offsetX = minimumOffset; offsetX <= maximumOffset; offsetX++)
                    {
                        int pixelX = x + offsetX;
                        int pixelY = y + offsetY;
                        if (pixelX >= 0 && pixelX < _size && pixelY >= 0 && pixelY < _size)
                        {
                            _texture.SetPixel(pixelX, pixelY, _color);
                        }
                    }
                }
            }
        }
    }
}
