using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace DuneVector
{
    internal sealed class DuneVectorToolkitCompendiumView : IDisposable
    {
        private sealed class CardReferences
        {
            public string SubjectId;
            public Button Root;
            public Image Thumbnail;
            public VisualElement LockedVisual;
            public Label Title;
            public Label Metadata;
            public VisualElement SelectionMarker;
        }

        private static readonly PhotographableSubjectCategory[] Tabs =
        {
            PhotographableSubjectCategory.Glyph,
            PhotographableSubjectCategory.Landmark,
            PhotographableSubjectCategory.Enemy,
            PhotographableSubjectCategory.Misc,
        };

        private readonly DuneVectorPhotographStorage _storage;
        private readonly PhotographyTuning _settings;
        private readonly List<DuneVectorCompendiumEntry> _entries = new List<DuneVectorCompendiumEntry>();
        private readonly List<int> _rowStarts = new List<int>();
        private readonly List<DuneVectorCompendiumEntry> _visibleEntries =
            new List<DuneVectorCompendiumEntry>();
        private readonly Button[] _tabs = new Button[Tabs.Length];
        private readonly Image[] _tabImages = new Image[Tabs.Length];
        private readonly Label[] _tabLabels = new Label[Tabs.Length];
        private readonly VisualElement[] _tabUnderlines = new VisualElement[Tabs.Length];
        private readonly Texture2D[] _tabIcons = new Texture2D[Tabs.Length];
        private readonly GameObject _host;
        private readonly PanelSettings _panelSettings;
        private readonly UIDocument _document;
        private readonly VisualElement _root;
        private readonly VisualElement _window;
        private readonly Label _progress;
        private readonly ListView _grid;
        private readonly Scroller _verticalScroller;
        private readonly VisualElement _detail;
        private readonly Image _detailImage;
        private readonly VisualElement _detailLocked;
        private readonly Label _detailTitle;
        private readonly Label _detailMetadata;
        private readonly Label _detailDescription;
        private readonly Label _detailLocationLabel;
        private readonly Label _detailLocation;
        private readonly Label _detailNotesLabel;
        private readonly Label _detailNotes;
        private int _selectedTab;
        private int _columnCount;
        private string _selectedSubjectId;
        private bool _visible;

        public DuneVectorToolkitCompendiumView(
            DuneVectorPhotographStorage storage,
            PhotographyTuning settings,
            DesertAtlasTuning atlas)
        {
            _storage = storage;
            _settings = settings;
            PopulateEntries(atlas);

            _host = new GameObject("Dune Vector Compendium UI")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.name = "Dune Vector Compendium Runtime Panel";
            _panelSettings.hideFlags = HideFlags.HideAndDontSave;
            _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _panelSettings.referenceResolution = new Vector2Int(
                Mathf.RoundToInt(_settings.GalleryReferenceWidth),
                Mathf.RoundToInt(_settings.GalleryReferenceHeight));
            _panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _panelSettings.match = 0.5f;
            _panelSettings.sortingOrder = 2000;
            _panelSettings.themeStyleSheet =
                Resources.Load<ThemeStyleSheet>("UI/DuneVectorCompendiumTheme");
            _document = _host.AddComponent<UIDocument>();
            _document.panelSettings = _panelSettings;
            _document.sortingOrder = 2000;
            _root = _document.rootVisualElement;
            _root.name = "dune-vector-compendium";
            _root.AddToClassList("compendium-root");
            StyleSheet styleSheet = Resources.Load<StyleSheet>("UI/DuneVectorCompendium");
            if (styleSheet != null)
            {
                _root.styleSheets.Add(styleSheet);
            }

            _window = new VisualElement { name = "compendium-window" };
            _window.AddToClassList("compendium-window");
            _root.Add(_window);

            VisualElement header = BuildHeader(out _progress);
            _window.Add(header);
            _window.Add(BuildTabs());

            VisualElement content = new VisualElement { name = "compendium-content" };
            content.AddToClassList("compendium-content");
            _window.Add(content);

            _grid = new ListView
            {
                name = "compendium-grid",
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                makeItem = MakeGridRow,
                bindItem = BindGridRow,
            };
            _grid.AddToClassList("compendium-grid");
            _verticalScroller = _grid.Q<Scroller>(className: "unity-scroller--vertical");
            if (_verticalScroller != null)
            {
                _verticalScroller.style.width = _settings.CompendiumScrollbarWidth;
            }
            content.Add(_grid);

            _detail = BuildDetail(
                out _detailImage,
                out _detailLocked,
                out _detailTitle,
                out _detailMetadata,
                out _detailDescription,
                out _detailLocationLabel,
                out _detailLocation,
                out _detailNotesLabel,
                out _detailNotes);
            content.Add(_detail);
            ApplyTheme();
            _root.RegisterCallback<GeometryChangedEvent>(_ => UpdateResponsiveLayout());
            Hide();
        }

        public bool TryResolve(string subjectId, out string displayName)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].SubjectId, subjectId, StringComparison.Ordinal))
                {
                    displayName = _entries[i].DisplayName;
                    return true;
                }
            }
            displayName = string.Empty;
            return false;
        }

        public void Show()
        {
            if (_visible)
            {
                return;
            }
            _root.style.display = DisplayStyle.Flex;
            _visible = true;
            UpdateResponsiveLayout();
            Refresh();
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
            _visible = false;
        }

        private void PopulateEntries(DesertAtlasTuning atlas)
        {
            _tabIcons[0] = _settings.CompendiumGlyphTabIcon;
            _tabIcons[1] = _settings.CompendiumLandmarkTabIcon;
            _tabIcons[2] = _settings.CompendiumEnemyTabIcon;
            _tabIcons[3] = _settings.CompendiumMiscTabIcon;
            if (atlas?.Sites != null)
            {
                for (int i = 0; i < atlas.Sites.Count; i++)
                {
                    DesertAtlasSiteDefinition site = atlas.Sites[i];
                    if (site == null || string.IsNullOrWhiteSpace(site.PersistentId))
                    {
                        continue;
                    }
                    _entries.Add(new DuneVectorCompendiumEntry(
                        site.PersistentId,
                        site.DisplayName,
                        PhotographableSubjectCategory.Glyph,
                        site.Description,
                        string.Empty,
                        string.Empty));
                }
            }
            if (_settings.CompendiumEntries == null)
            {
                return;
            }
            for (int i = 0; i < _settings.CompendiumEntries.Count; i++)
            {
                CompendiumEntryDefinition definition = _settings.CompendiumEntries[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.SubjectId))
                {
                    continue;
                }
                _entries.Add(new DuneVectorCompendiumEntry(
                    definition.SubjectId,
                    definition.DisplayName,
                    definition.Category,
                    definition.Description,
                    definition.DiscoveryLocation,
                    definition.FieldNotes));
            }
        }

        private VisualElement BuildHeader(out Label progress)
        {
            VisualElement header = new VisualElement { name = "compendium-header" };
            header.AddToClassList("compendium-header");
            VisualElement titles = new VisualElement();
            titles.AddToClassList("compendium-header-titles");
            Label title = new Label(_settings.CompendiumTitle);
            title.AddToClassList("compendium-title");
            titles.Add(title);
            Label subtitle = new Label(_settings.CompendiumSubtitle);
            subtitle.AddToClassList("compendium-subtitle");
            titles.Add(subtitle);
            header.Add(titles);

            progress = new Label();
            progress.AddToClassList("compendium-progress");
            header.Add(progress);
            Button close = new Button(DuneVectorPhotographySystem.RequestCloseCompendium)
            {
                text = _settings.CompendiumCloseLabel,
            };
            close.AddToClassList("compendium-close");
            header.Add(close);
            return header;
        }

        private VisualElement BuildTabs()
        {
            VisualElement navigation = new VisualElement { name = "compendium-tabs" };
            navigation.AddToClassList("compendium-tabs");
            for (int i = 0; i < Tabs.Length; i++)
            {
                int index = i;
                Button tab = new Button(() => SelectTab(index));
                tab.AddToClassList("compendium-tab");
                Image icon = new Image
                {
                    image = _tabIcons[i],
                    scaleMode = ScaleMode.ScaleToFit,
                    tintColor = _settings.CompendiumIconColor,
                    pickingMode = PickingMode.Ignore,
                };
                icon.AddToClassList("compendium-tab-icon");
                tab.Add(icon);
                Label label = new Label();
                label.AddToClassList("compendium-tab-label");
                tab.Add(label);
                VisualElement underline = new VisualElement
                {
                    pickingMode = PickingMode.Ignore,
                };
                underline.AddToClassList("compendium-tab-underline");
                tab.Add(underline);
                _tabs[i] = tab;
                _tabImages[i] = icon;
                _tabLabels[i] = label;
                _tabUnderlines[i] = underline;
                tab.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    if (!tab.ClassListContains("is-selected"))
                    {
                        tab.EnableInClassList("is-hovered", true);
                        tab.style.backgroundColor = _settings.CompendiumRaisedSurfaceColor;
                    }
                });
                tab.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    tab.EnableInClassList("is-hovered", false);
                    if (!tab.ClassListContains("is-selected"))
                    {
                        tab.style.backgroundColor = _settings.CompendiumTabColor;
                    }
                });
                navigation.Add(tab);
            }
            return navigation;
        }

        private VisualElement BuildDetail(
            out Image hero,
            out VisualElement locked,
            out Label title,
            out Label metadata,
            out Label description,
            out Label locationLabel,
            out Label location,
            out Label notesLabel,
            out Label notes)
        {
            VisualElement detail = new VisualElement { name = "compendium-detail" };
            detail.AddToClassList("compendium-detail");
            VisualElement heroContainer = new VisualElement();
            heroContainer.AddToClassList("compendium-detail-hero");
            hero = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            hero.AddToClassList("compendium-detail-image");
            heroContainer.Add(hero);
            locked = BuildLockedVisual();
            locked.AddToClassList("compendium-detail-locked");
            heroContainer.Add(locked);
            detail.Add(heroContainer);

            title = new Label();
            title.AddToClassList("compendium-detail-title");
            detail.Add(title);
            metadata = new Label();
            metadata.AddToClassList("compendium-detail-metadata");
            detail.Add(metadata);
            VisualElement rule = new VisualElement();
            rule.AddToClassList("compendium-detail-rule");
            detail.Add(rule);
            description = new Label();
            description.AddToClassList("compendium-detail-description");
            detail.Add(description);
            locationLabel = new Label(_settings.CompendiumDiscoveryLocationLabel);
            locationLabel.AddToClassList("compendium-section-label");
            detail.Add(locationLabel);
            location = new Label();
            location.AddToClassList("compendium-detail-copy");
            detail.Add(location);
            notesLabel = new Label(_settings.CompendiumFieldNotesLabel);
            notesLabel.AddToClassList("compendium-section-label");
            detail.Add(notesLabel);
            notes = new Label();
            notes.AddToClassList("compendium-detail-copy");
            detail.Add(notes);
            return detail;
        }

        private VisualElement MakeGridRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("compendium-grid-row");
            List<CardReferences> cards = new List<CardReferences>();
            int maximumColumns = Mathf.Max(
                _settings.CompendiumCompactColumns,
                _settings.CompendiumWideColumns);
            for (int i = 0; i < maximumColumns; i++)
            {
                CardReferences references = CreateCard();
                cards.Add(references);
                row.Add(references.Root);
            }
            row.userData = cards;
            return row;
        }

        private CardReferences CreateCard()
        {
            CardReferences references = new CardReferences();
            Button card = new Button(() => SelectEntry(references.SubjectId));
            card.AddToClassList("compendium-card");
            Image thumbnail = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            thumbnail.AddToClassList("compendium-card-image");
            card.Add(thumbnail);
            VisualElement locked = BuildLockedVisual();
            card.Add(locked);
            VisualElement overlay = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
            };
            overlay.AddToClassList("compendium-card-overlay");
            Label title = new Label();
            title.AddToClassList("compendium-card-title");
            overlay.Add(title);
            Label metadata = new Label();
            metadata.AddToClassList("compendium-card-metadata");
            overlay.Add(metadata);
            card.Add(overlay);
            VisualElement marker = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
            };
            marker.AddToClassList("compendium-card-selection-marker");
            Image selectionIcon = new Image
            {
                image = _settings.CompendiumSelectedCardIcon,
                scaleMode = ScaleMode.ScaleToFit,
                tintColor = Color.white,
                pickingMode = PickingMode.Ignore,
            };
            selectionIcon.AddToClassList("compendium-card-selection-icon");
            marker.Add(selectionIcon);
            card.Add(marker);
            references.Root = card;
            references.Thumbnail = thumbnail;
            references.LockedVisual = locked;
            references.Title = title;
            references.Metadata = metadata;
            references.SelectionMarker = marker;
            card.RegisterCallback<PointerEnterEvent>(_ =>
            {
                card.EnableInClassList("is-hovered", true);
                if (!card.ClassListContains("is-selected"))
                {
                    SetBorder(
                        card,
                        _settings.CompendiumHoverBorderColor,
                        _settings.CompendiumCardBorderThickness,
                        _settings.CompendiumCardCornerRadius);
                }
            });
            card.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                card.EnableInClassList("is-hovered", false);
                bool selected = card.ClassListContains("is-selected");
                SetBorder(
                    card,
                    selected
                        ? _settings.CompendiumActiveAccentColor
                        : _settings.CompendiumCardBorderColor,
                    _settings.CompendiumCardBorderThickness,
                    _settings.CompendiumCardCornerRadius);
            });
            return references;
        }

        private VisualElement BuildLockedVisual()
        {
            VisualElement locked = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
            };
            locked.AddToClassList("compendium-locked");
            for (int i = 0; i < 4; i++)
            {
                VisualElement contour = new VisualElement();
                contour.AddToClassList("compendium-contour");
                contour.style.width = Length.Percent(34f + (i * 13f));
                contour.style.opacity = 0.16f + (i * 0.06f);
                locked.Add(contour);
            }
            Label symbol = new Label("◇");
            symbol.AddToClassList("compendium-locked-symbol");
            locked.Add(symbol);
            return locked;
        }

        private void BindGridRow(VisualElement element, int rowIndex)
        {
            List<CardReferences> cards = (List<CardReferences>)element.userData;
            int start = _rowStarts[rowIndex];
            for (int column = 0; column < cards.Count; column++)
            {
                CardReferences card = cards[column];
                int entryIndex = start + column;
                bool populated = column < _columnCount && entryIndex < _visibleEntries.Count;
                card.Root.style.display = populated ? DisplayStyle.Flex : DisplayStyle.None;
                if (!populated)
                {
                    card.SubjectId = null;
                    continue;
                }
                DuneVectorCompendiumEntry entry = _visibleEntries[entryIndex];
                bool documented = _storage.IsDocumented(entry.SubjectId);
                bool selected = string.Equals(
                    entry.SubjectId,
                    _selectedSubjectId,
                    StringComparison.Ordinal);
                card.SubjectId = entry.SubjectId;
                card.Root.style.marginRight =
                    column < _columnCount - 1 ? _settings.CompendiumCardGap : 0f;
                card.Thumbnail.image = documented
                    ? _storage.GetCanonicalTexture(entry.SubjectId)
                    : null;
                card.Thumbnail.style.display = documented ? DisplayStyle.Flex : DisplayStyle.None;
                card.LockedVisual.style.display = documented ? DisplayStyle.None : DisplayStyle.Flex;
                card.Title.text = documented
                    ? ToTitleCase(entry.DisplayName)
                    : _settings.CompendiumUnknownLabel;
                card.Metadata.text = GetCategoryLabel(entry.Category);
                card.Root.EnableInClassList("is-documented", documented);
                card.Root.EnableInClassList("is-locked", !documented);
                card.Root.EnableInClassList("is-selected", selected);
                card.SelectionMarker.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
                ApplyCardState(card, selected);
            }
        }

        private void SelectTab(int index)
        {
            if (_selectedTab == index)
            {
                return;
            }
            _selectedTab = index;
            _selectedSubjectId = null;
            Refresh();
        }

        private void SelectEntry(string subjectId)
        {
            if (string.IsNullOrEmpty(subjectId))
            {
                return;
            }
            _selectedSubjectId = subjectId;
            _grid.RefreshItems();
            RefreshDetails();
        }

        private void Refresh()
        {
            RefreshCounts();
            RefreshTabs();
            _visibleEntries.Clear();
            PhotographableSubjectCategory category = Tabs[_selectedTab];
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Category == category)
                {
                    _visibleEntries.Add(_entries[i]);
                }
            }
            if (string.IsNullOrEmpty(_selectedSubjectId) && _visibleEntries.Count > 0)
            {
                _selectedSubjectId = _visibleEntries[0].SubjectId;
            }
            RebuildRows();
            RefreshDetails();
        }

        private void RefreshCounts()
        {
            int documented = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_storage.IsDocumented(_entries[i].SubjectId))
                {
                    documented++;
                }
            }
            _progress.text = string.Format(
                CultureInfo.InvariantCulture,
                _settings.CompendiumDiscoveryCountFormat,
                documented,
                _entries.Count);
        }

        private void RefreshTabs()
        {
            for (int tabIndex = 0; tabIndex < Tabs.Length; tabIndex++)
            {
                int total = 0;
                int documented = 0;
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Category != Tabs[tabIndex])
                    {
                        continue;
                    }
                    total++;
                    if (_storage.IsDocumented(_entries[i].SubjectId))
                    {
                        documented++;
                    }
                }
                _tabLabels[tabIndex].text = string.Format(
                    CultureInfo.InvariantCulture,
                    _settings.CompendiumTabCountFormat,
                    GetTabLabel(tabIndex),
                    documented,
                    total);
                bool selected = tabIndex == _selectedTab;
                _tabs[tabIndex].EnableInClassList("is-selected", selected);
                _tabs[tabIndex].style.backgroundColor = selected
                    ? _settings.CompendiumSelectedTabColor
                    : _settings.CompendiumTabColor;
                _tabImages[tabIndex].tintColor = selected
                    ? _settings.CompendiumIconColor
                    : _settings.CompendiumSecondaryTextColor;
                _tabUnderlines[tabIndex].style.display =
                    selected ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void RebuildRows()
        {
            _rowStarts.Clear();
            for (int i = 0; i < _visibleEntries.Count; i += Mathf.Max(1, _columnCount))
            {
                _rowStarts.Add(i);
            }
            _grid.itemsSource = _rowStarts;
            _grid.fixedItemHeight =
                _settings.CompendiumSlotHeight + _settings.CompendiumCardGap;
            _grid.Rebuild();
        }

        private void RefreshDetails()
        {
            DuneVectorCompendiumEntry entry = default;
            bool found = false;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].SubjectId, _selectedSubjectId, StringComparison.Ordinal))
                {
                    entry = _entries[i];
                    found = true;
                    break;
                }
            }
            _detail.style.display = found ? DisplayStyle.Flex : DisplayStyle.None;
            if (!found)
            {
                return;
            }
            bool documented = _storage.IsDocumented(entry.SubjectId);
            _detailImage.image = documented ? _storage.GetCanonicalTexture(entry.SubjectId) : null;
            _detailImage.style.display = documented ? DisplayStyle.Flex : DisplayStyle.None;
            _detailLocked.style.display = documented ? DisplayStyle.None : DisplayStyle.Flex;
            _detailTitle.text = documented
                ? ToTitleCase(entry.DisplayName)
                : _settings.CompendiumUnknownLabel;
            _detailMetadata.text = $"{GetCategoryLabel(entry.Category)}  ·  " +
                (documented
                    ? _settings.CompendiumDiscoveredLabel
                    : _settings.CompendiumUnknownLabel);
            _detailDescription.text = documented
                ? FirstNonEmpty(entry.Description, _settings.CompendiumDefaultDescription)
                : _settings.CompendiumUnknownDescription;
            _detailLocationLabel.style.display = documented ? DisplayStyle.Flex : DisplayStyle.None;
            _detailLocation.style.display = documented ? DisplayStyle.Flex : DisplayStyle.None;
            _detailNotesLabel.style.display = documented ? DisplayStyle.Flex : DisplayStyle.None;
            _detailNotes.style.display = documented ? DisplayStyle.Flex : DisplayStyle.None;
            _detailLocation.text = FirstNonEmpty(
                entry.DiscoveryLocation,
                _settings.CompendiumDefaultDiscoveryLocation);
            _detailNotes.text = FirstNonEmpty(
                entry.FieldNotes,
                _settings.CompendiumDefaultFieldNotes);
        }

        private void UpdateResponsiveLayout()
        {
            int targetColumns = Screen.height >= _settings.CompendiumWideScreenMinimumHeight
                ? _settings.CompendiumWideColumns
                : _settings.CompendiumCompactColumns;
            targetColumns = Mathf.Max(1, targetColumns);
            if (_columnCount == targetColumns)
            {
                return;
            }
            _columnCount = targetColumns;
            RebuildRows();
        }

        private void ApplyTheme()
        {
            _root.style.position = Position.Absolute;
            _root.style.left = 0f;
            _root.style.top = 0f;
            _root.style.right = 0f;
            _root.style.bottom = 0f;
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;
            _root.style.backgroundColor = _settings.GalleryBackdropColor;

            _window.style.width = _settings.CompendiumPanelWidth;
            _window.style.height = _settings.CompendiumPanelHeight;
            _window.style.backgroundColor = _settings.CompendiumMainBackgroundColor;
            _window.style.paddingLeft = _settings.GalleryPadding;
            _window.style.paddingRight = _settings.GalleryPadding;
            _window.style.paddingTop = _settings.GalleryPadding;
            _window.style.paddingBottom = _settings.GalleryPadding;
            Color faintBorder = _settings.GalleryAccentColor;
            faintBorder.a *= _settings.CompendiumPanelBorderOpacity;
            SetBorder(
                _window,
                faintBorder,
                _settings.CompendiumPanelBorderThickness,
                0f);

            VisualElement header = _window.Q("compendium-header");
            header.style.height = _settings.CompendiumHeaderHeight;
            header.style.marginBottom = _settings.CompendiumGap;
            SetTextStyle(_window.Q<Label>(className: "compendium-title"),
                _settings.CompendiumTitleFontSize, _settings.CompendiumPrimaryTextColor, true);
            SetTextStyle(_window.Q<Label>(className: "compendium-subtitle"),
                _settings.CompendiumSubtitleFontSize, _settings.CompendiumSecondaryTextColor, false);
            SetTextStyle(_progress,
                _settings.CompendiumMetadataFontSize, _settings.CompendiumSecondaryTextColor, true);
            Button close = _window.Q<Button>(className: "compendium-close");
            SetTextStyle(close,
                _settings.CompendiumMetadataFontSize, _settings.CompendiumSecondaryTextColor, true);
            close.style.backgroundColor = Color.clear;
            SetBorder(close, Color.clear, 0f, 0f);
            close.style.paddingLeft = _settings.CompendiumGap;
            close.style.paddingRight = 0f;

            VisualElement tabs = _window.Q("compendium-tabs");
            tabs.style.height = _settings.CompendiumTabHeight;
            tabs.style.marginBottom = _settings.CompendiumGap;
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].style.height = _settings.CompendiumTabHeight;
                _tabs[i].style.marginRight = i < _tabs.Length - 1
                    ? _settings.CompendiumGap
                    : 0f;
                _tabs[i].style.paddingLeft = _settings.CompendiumGap;
                _tabs[i].style.paddingRight = _settings.CompendiumGap;
                SetBorder(_tabs[i], Color.clear, 0f, 0f);
                Image icon = _tabs[i].Q<Image>();
                icon.style.width = _settings.CompendiumTabIconSize;
                icon.style.height = _settings.CompendiumTabIconSize;
                icon.style.marginRight = _settings.CompendiumGap;
                SetTextStyle(_tabLabels[i],
                    _settings.CompendiumTabFontSize, _settings.CompendiumPrimaryTextColor, true);
                _tabUnderlines[i].style.height = _settings.CompendiumActiveTabUnderlineHeight;
                _tabUnderlines[i].style.backgroundColor = _settings.GalleryAccentColor;
            }

            _grid.style.marginRight = _settings.CompendiumGap;
            _grid.style.paddingLeft = _settings.CompendiumGridPadding;
            _grid.style.paddingRight = _settings.CompendiumGridPadding;
            _grid.style.paddingTop = _settings.CompendiumGridPadding;
            _grid.style.paddingBottom = _settings.CompendiumGridPadding;
            if (_verticalScroller != null)
            {
                _verticalScroller.style.marginTop = _settings.CompendiumGridPadding;
                _verticalScroller.style.marginBottom = _settings.CompendiumGridPadding;
                _verticalScroller.style.backgroundColor = _settings.CompendiumScrollbarTrackColor;
                SetBorder(
                    _verticalScroller,
                    _settings.CompendiumScrollbarBorderColor,
                    _settings.CompendiumScrollbarBorderThickness,
                    0f);
                Slider slider = _verticalScroller.Q<Slider>();
                if (slider != null)
                {
                    slider.style.backgroundColor = _settings.CompendiumScrollbarTrackColor;
                    VisualElement dragContainer =
                        slider.Q(className: "unity-base-slider__drag-container");
                    if (dragContainer != null)
                    {
                        dragContainer.style.backgroundColor =
                            _settings.CompendiumScrollbarTrackColor;
                    }
                    VisualElement tracker =
                        slider.Q(className: "unity-base-slider__tracker");
                    if (tracker != null)
                    {
                        tracker.style.backgroundColor =
                            _settings.CompendiumScrollbarTrackColor;
                    }
                }
            }
            _detail.style.width = _settings.CompendiumDetailPanelWidth;
            _detail.style.backgroundColor = _settings.CompendiumRaisedSurfaceColor;
            _detail.style.paddingLeft = _settings.CompendiumGap;
            _detail.style.paddingRight = _settings.CompendiumGap;
            _detail.style.paddingTop = _settings.CompendiumGap;
            _detail.style.paddingBottom = _settings.CompendiumGap;
            _detailImage.parent.style.height = _settings.CompendiumDetailImageHeight;
            _detailImage.style.position = Position.Absolute;
            _detailImage.style.left = 0f;
            _detailImage.style.top = 0f;
            _detailImage.style.right = 0f;
            _detailImage.style.bottom = 0f;
            _detailLocked.style.position = Position.Absolute;
            _detailLocked.style.left = 0f;
            _detailLocked.style.top = 0f;
            _detailLocked.style.right = 0f;
            _detailLocked.style.bottom = 0f;
            SetTextStyle(_detailTitle,
                _settings.CompendiumDetailTitleFontSize, _settings.CompendiumPrimaryTextColor, true);
            SetTextStyle(_detailMetadata,
                _settings.CompendiumMetadataFontSize, _settings.CompendiumSecondaryTextColor, true);
            SetTextStyle(_detailDescription,
                _settings.GalleryBodyFontSize, _settings.CompendiumPrimaryTextColor, false);
            SetTextStyle(_detailLocationLabel,
                _settings.CompendiumMetadataFontSize, _settings.CompendiumActiveAccentColor, true);
            SetTextStyle(_detailNotesLabel,
                _settings.CompendiumMetadataFontSize, _settings.CompendiumActiveAccentColor, true);
            SetTextStyle(_detailLocation,
                _settings.GalleryBodyFontSize, _settings.CompendiumSecondaryTextColor, false);
            SetTextStyle(_detailNotes,
                _settings.GalleryBodyFontSize, _settings.CompendiumSecondaryTextColor, false);
            _detail.Q(className: "compendium-detail-rule").style.backgroundColor =
                _settings.CompendiumActiveAccentColor;
        }

        private void ApplyCardState(CardReferences card, bool selected)
        {
            card.Root.style.backgroundColor = _settings.CompendiumCardColor;
            card.Root.style.width = _settings.CompendiumSlotWidth;
            card.Root.style.height = _settings.CompendiumSlotHeight;
            card.Root.style.flexGrow = 0f;
            card.Root.style.flexShrink = 0f;
            card.Root.style.borderTopLeftRadius = _settings.CompendiumCardCornerRadius;
            card.Root.style.borderTopRightRadius = _settings.CompendiumCardCornerRadius;
            card.Root.style.borderBottomLeftRadius = _settings.CompendiumCardCornerRadius;
            card.Root.style.borderBottomRightRadius = _settings.CompendiumCardCornerRadius;
            SetBorder(
                card.Root,
                selected
                    ? _settings.CompendiumActiveAccentColor
                    : _settings.CompendiumCardBorderColor,
                _settings.CompendiumCardBorderThickness,
                _settings.CompendiumCardCornerRadius);
            card.Thumbnail.style.position = Position.Absolute;
            card.Thumbnail.style.left = 0f;
            card.Thumbnail.style.top = 0f;
            card.Thumbnail.style.right = 0f;
            card.Thumbnail.style.bottom = 0f;
            card.LockedVisual.style.position = Position.Absolute;
            card.LockedVisual.style.left = 0f;
            card.LockedVisual.style.top = 0f;
            card.LockedVisual.style.right = 0f;
            card.LockedVisual.style.bottom = 0f;
            card.LockedVisual.style.backgroundColor = _settings.CompendiumLockedColor;
            VisualElement overlay = card.Root.Q(className: "compendium-card-overlay");
            overlay.style.top = 0f;
            overlay.style.bottom = 0f;
            overlay.style.paddingLeft = _settings.CompendiumCardContentPadding;
            overlay.style.paddingRight = _settings.CompendiumCardContentPadding;
            overlay.style.paddingTop = _settings.CompendiumCardContentPadding;
            overlay.style.paddingBottom = _settings.CompendiumCardContentPadding;
            overlay.style.backgroundColor = _settings.CompendiumLockedOverlayColor;
            SetTextStyle(card.Title,
                _settings.CompendiumCardTitleFontSize, _settings.CompendiumPrimaryTextColor, true);
            SetTextStyle(card.Metadata,
                _settings.CompendiumMetadataFontSize, _settings.CompendiumSecondaryTextColor, true);
            card.SelectionMarker.style.backgroundColor = _settings.CompendiumActiveAccentColor;
            card.SelectionMarker.style.width = _settings.CompendiumSelectionMarkerSize;
            card.SelectionMarker.style.height = _settings.CompendiumSelectionMarkerSize;
            Image selectionIcon =
                card.SelectionMarker.Q<Image>(className: "compendium-card-selection-icon");
            if (selectionIcon != null)
            {
                selectionIcon.style.position = Position.Absolute;
                selectionIcon.style.left = _settings.CompendiumSelectionIconInset;
                selectionIcon.style.right = _settings.CompendiumSelectionIconInset;
                selectionIcon.style.top = _settings.CompendiumSelectionIconInset;
                selectionIcon.style.bottom = _settings.CompendiumSelectionIconInset;
            }
        }

        private static void SetBorder(
            VisualElement element,
            Color color,
            float width,
            float radius)
        {
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        private void SetTextStyle(
            TextElement element,
            int fontSize,
            Color color,
            bool semibold)
        {
            element.style.fontSize = fontSize;
            element.style.color = color;
            Font font = semibold ? _settings.HudSemiboldFont : _settings.HudRegularFont;
            if (font != null)
            {
                element.style.unityFont = font;
            }
        }

        private string GetTabLabel(int index)
        {
            return index switch
            {
                0 => _settings.CompendiumGlyphTabLabel,
                1 => _settings.CompendiumLandmarkTabLabel,
                2 => _settings.CompendiumEnemyTabLabel,
                _ => _settings.CompendiumMiscTabLabel,
            };
        }

        private static string GetCategoryLabel(PhotographableSubjectCategory category)
        {
            return category switch
            {
                PhotographableSubjectCategory.Glyph => "GLYPH",
                PhotographableSubjectCategory.Landmark => "LANDMARK",
                PhotographableSubjectCategory.Enemy => "ENEMY",
                PhotographableSubjectCategory.Creature => "CREATURE",
                PhotographableSubjectCategory.Plant => "PLANT",
                PhotographableSubjectCategory.AncientStructure => "ANCIENT STRUCTURE",
                PhotographableSubjectCategory.RarePhenomenon => "RARE PHENOMENON",
                _ => "MISC",
            };
        }

        private static string ToTitleCase(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
        }

        private static string FirstNonEmpty(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        public void Dispose()
        {
            if (_host != null)
            {
                UnityEngine.Object.Destroy(_host);
            }
            if (_panelSettings != null)
            {
                UnityEngine.Object.Destroy(_panelSettings);
            }
        }
    }
}
