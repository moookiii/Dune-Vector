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
            public VisualElement Accent;
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
        private readonly VisualElement _progressRail;
        private readonly VisualElement _progressFill;
        private readonly ListView _grid;
        private readonly Scroller _verticalScroller;
        private readonly VisualElement _detail;
        private readonly Image _detailImage;
        private readonly VisualElement _detailLocked;
        private readonly Label _detailTitle;
        private readonly Label _detailMetadata;
        private readonly Label _detailStatus;
        private readonly Label _detailDescription;
        private readonly VisualElement _detailHero;
        private readonly Label _detailHeroHint;
        private readonly VisualElement _lightbox;
        private readonly VisualElement _lightboxFrame;
        private readonly Image _lightboxImage;
        private readonly Label _lightboxCaption;
        private VisualElement _lightboxFooter;
        private IVisualElementScheduledItem _lightboxExpandSchedule;
        private IVisualElementScheduledItem _lightboxCloseSchedule;
        private int _selectedTab;
        private int _columnCount;
        private string _selectedSubjectId;
        private bool _visible;
        private bool _lightboxOpen;

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
            _progressRail = new VisualElement { name = "compendium-header-rule" };
            _progressRail.AddToClassList("compendium-separator");
            _progressFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _progressFill.AddToClassList("compendium-progress-fill");
            _progressRail.Add(_progressFill);
            _window.Add(_progressRail);
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
                out _detailStatus,
                out _detailDescription);
            content.Add(_detail);

            _detailHero = _detailImage.parent;
            _detailHeroHint = new Label(_settings.CompendiumEnlargeHint)
            {
                pickingMode = PickingMode.Ignore,
            };
            _detailHeroHint.AddToClassList("compendium-detail-hero-hint");
            _detailHero.Add(_detailHeroHint);
            _detailHero.RegisterCallback<PointerDownEvent>(OnHeroPointerDown);
            _detailHero.RegisterCallback<PointerEnterEvent>(_ => SetHeroHovered(true));
            _detailHero.RegisterCallback<PointerLeaveEvent>(_ => SetHeroHovered(false));

            _lightbox = BuildLightbox(out _lightboxFrame, out _lightboxImage, out _lightboxCaption);
            _root.Add(_lightbox);

            ApplyTheme();
            _root.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                UpdateResponsiveLayout();
                LayoutLightbox();
            });
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
            CloseLightbox();
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
            out Label status,
            out Label description)
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
            VisualElement chips = new VisualElement();
            chips.AddToClassList("compendium-chip-row");
            metadata = new Label();
            metadata.AddToClassList("compendium-chip");
            metadata.AddToClassList("compendium-detail-metadata");
            chips.Add(metadata);
            status = new Label();
            status.AddToClassList("compendium-chip");
            status.AddToClassList("compendium-detail-status");
            chips.Add(status);
            detail.Add(chips);
            VisualElement rule = new VisualElement();
            rule.AddToClassList("compendium-detail-rule");
            detail.Add(rule);
            description = new Label();
            description.AddToClassList("compendium-detail-description");
            detail.Add(description);
            return detail;
        }

        private VisualElement BuildLightbox(
            out VisualElement frame,
            out Image photo,
            out Label caption)
        {
            VisualElement lightbox = new VisualElement { name = "compendium-lightbox" };
            lightbox.AddToClassList("compendium-lightbox");
            // The frame is absolutely placed and driven by explicit rects so it can
            // animate from the detail hero's on-screen position out to full size.
            frame = new VisualElement();
            frame.AddToClassList("compendium-lightbox-frame");
            // ScaleToFit keeps the photo uncropped; the frame is measured to the
            // photo's aspect so the matte stays even on every side.
            photo = new Image { scaleMode = ScaleMode.ScaleToFit };
            photo.AddToClassList("compendium-lightbox-image");
            frame.Add(photo);
            lightbox.Add(frame);

            VisualElement footer = new VisualElement { name = "compendium-lightbox-footer" };
            footer.AddToClassList("compendium-lightbox-footer");
            caption = new Label();
            caption.AddToClassList("compendium-lightbox-caption");
            footer.Add(caption);
            Label hint = new Label(_settings.CompendiumLightboxCloseHint);
            hint.AddToClassList("compendium-lightbox-hint");
            footer.Add(hint);
            lightbox.Add(footer);
            _lightboxFooter = footer;

            lightbox.RegisterCallback<PointerDownEvent>(_ => CloseLightbox());
            return lightbox;
        }

        private void OnHeroPointerDown(PointerDownEvent evt)
        {
            if (_detailImage.image == null)
            {
                return;
            }
            evt.StopPropagation();
            OpenLightbox();
        }

        private void SetHeroHovered(bool hovered)
        {
            bool documented = _detailImage.image != null && !_lightboxOpen;
            _detailHeroHint.style.display = hovered && documented
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _detailImage.tintColor = hovered && documented
                ? Color.Lerp(Color.white, _settings.CompendiumHoverBorderColor, 0.18f)
                : Color.white;
        }

        private void OpenLightbox()
        {
            Texture texture = _detailImage.image;
            if (texture == null || _lightboxOpen)
            {
                return;
            }
            _lightboxCloseSchedule?.Pause();
            _lightboxImage.image = texture;
            _lightboxCaption.text = _detailTitle.text;
            _lightbox.style.display = DisplayStyle.Flex;
            _lightboxOpen = true;
            SetHeroHovered(false);

            // Start collapsed onto the thumbnail, then grow into the target rect on
            // the next frame so the style transition has two distinct values to run
            // between.
            ApplyFrameRect(GetHeroRect(), _settings.CompendiumDetailImageCornerRadius, 0f);
            _lightbox.style.opacity = 0f;
            _lightboxFooter.style.opacity = 0f;
            _lightboxExpandSchedule?.Pause();
            _lightboxExpandSchedule = _lightbox.schedule.Execute(() =>
            {
                if (!_lightboxOpen)
                {
                    return;
                }
                _lightbox.style.opacity = 1f;
                _lightboxFooter.style.opacity = 1f;
                LayoutLightbox();
            });
        }

        public bool CloseLightbox()
        {
            if (!_lightboxOpen)
            {
                return false;
            }
            _lightboxOpen = false;
            _lightboxExpandSchedule?.Pause();
            // Collapse back into the thumbnail it grew out of, then hide once the
            // transition has played out.
            ApplyFrameRect(
                GetHeroRect(),
                _settings.CompendiumDetailImageCornerRadius,
                _settings.CompendiumLightboxExpandSeconds);
            _lightbox.style.opacity = 0f;
            _lightboxFooter.style.opacity = 0f;
            _lightboxCloseSchedule?.Pause();
            _lightboxCloseSchedule = _lightbox.schedule.Execute(() =>
            {
                if (_lightboxOpen)
                {
                    return;
                }
                _lightbox.style.display = DisplayStyle.None;
                _lightboxImage.image = null;
            }).StartingIn(
                Mathf.RoundToInt(_settings.CompendiumLightboxExpandSeconds * 1000f));
            return true;
        }

        private Rect GetHeroRect()
        {
            Rect hero = _detailHero.worldBound;
            Rect root = _root.worldBound;
            Rect local = new Rect(
                hero.x - root.x,
                hero.y - root.y,
                hero.width,
                hero.height);
            if (local.width <= 1f || local.height <= 1f)
            {
                // Geometry is not resolved yet (first open of the session); fall back
                // to growing out of the middle of the screen.
                Vector2 center = new Vector2(root.width * 0.5f, root.height * 0.5f);
                return new Rect(center.x, center.y, 0f, 0f);
            }
            return local;
        }

        private void ApplyFrameRect(Rect rect, float cornerRadius, float seconds)
        {
            SetTransition(
                _lightboxFrame,
                seconds,
                "left",
                "top",
                "width",
                "height",
                "border-top-left-radius",
                "border-top-right-radius",
                "border-bottom-left-radius",
                "border-bottom-right-radius");
            _lightboxFrame.style.left = rect.x;
            _lightboxFrame.style.top = rect.y;
            _lightboxFrame.style.width = rect.width;
            _lightboxFrame.style.height = rect.height;
            _lightboxFrame.style.borderTopLeftRadius = cornerRadius;
            _lightboxFrame.style.borderTopRightRadius = cornerRadius;
            _lightboxFrame.style.borderBottomLeftRadius = cornerRadius;
            _lightboxFrame.style.borderBottomRightRadius = cornerRadius;
        }

        private void LayoutLightbox()
        {
            if (!_lightboxOpen)
            {
                return;
            }
            float rootWidth = _root.resolvedStyle.width;
            float rootHeight = _root.resolvedStyle.height;
            if (rootWidth <= 1f || rootHeight <= 1f)
            {
                rootWidth = _settings.GalleryReferenceWidth;
                rootHeight = _settings.GalleryReferenceHeight;
            }
            float margin = Mathf.Min(rootWidth, rootHeight) *
                _settings.CompendiumLightboxScreenMargin;
            float matte = _settings.CompendiumLightboxMattePadding;
            // Reserve room for the caption and the close hint so the photo grows
            // as large as it can without pushing either off screen.
            float footerHeight = _settings.CompendiumDetailTitleFontSize * 1.6f +
                (_settings.CompendiumMetadataFontSize * 2.4f) +
                _settings.CompendiumGap;
            float availableWidth = Mathf.Max(
                1f,
                rootWidth - (margin * 2f) - (matte * 2f));
            float availableHeight = Mathf.Max(
                1f,
                rootHeight - (margin * 2f) - (matte * 2f) - footerHeight);
            Texture texture = _lightboxImage.image;
            float aspect = texture != null && texture.height > 0
                ? (float)texture.width / texture.height
                : availableWidth / availableHeight;
            float photoWidth = availableWidth;
            float photoHeight = photoWidth / aspect;
            if (photoHeight > availableHeight)
            {
                photoHeight = availableHeight;
                photoWidth = photoHeight * aspect;
            }
            float frameWidth = photoWidth + (matte * 2f);
            float frameHeight = photoHeight + (matte * 2f);
            Rect target = new Rect(
                (rootWidth - frameWidth) * 0.5f,
                ((rootHeight - footerHeight) - frameHeight) * 0.5f,
                frameWidth,
                frameHeight);
            ApplyFrameRect(
                target,
                _settings.CompendiumLightboxCornerRadius,
                _settings.CompendiumLightboxExpandSeconds);
            _lightboxImage.style.left = matte;
            _lightboxImage.style.right = matte;
            _lightboxImage.style.top = matte;
            _lightboxImage.style.bottom = matte;
            _lightboxFooter.style.height = footerHeight;
            _lightboxCaption.style.maxWidth = frameWidth;
        }

        private static void SetTransition(
            VisualElement element,
            float seconds,
            params string[] properties)
        {
            List<StylePropertyName> names = new List<StylePropertyName>(properties.Length);
            List<TimeValue> durations = new List<TimeValue>(properties.Length);
            List<TimeValue> delays = new List<TimeValue>(properties.Length);
            List<EasingFunction> easings = new List<EasingFunction>(properties.Length);
            for (int i = 0; i < properties.Length; i++)
            {
                names.Add(new StylePropertyName(properties[i]));
                durations.Add(new TimeValue(seconds, TimeUnit.Second));
                delays.Add(new TimeValue(0f, TimeUnit.Second));
                easings.Add(new EasingFunction(EasingMode.EaseOutCubic));
            }
            element.style.transitionProperty = new StyleList<StylePropertyName>(names);
            element.style.transitionDuration = new StyleList<TimeValue>(durations);
            element.style.transitionDelay = new StyleList<TimeValue>(delays);
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(easings);
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
            VisualElement accent = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
            };
            accent.AddToClassList("compendium-card-accent");
            card.Add(accent);
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
            references.Accent = accent;
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
                ApplyCardState(card, selected, documented, entry.Category);
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
            float ratio = _entries.Count > 0
                ? Mathf.Clamp01(documented / (float)_entries.Count)
                : 0f;
            _progressFill.style.width = Length.Percent(ratio * 100f);
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
                    ? GetSelectedTabColor()
                    : _settings.CompendiumTabColor;
                _tabImages[tabIndex].tintColor = selected
                    ? _settings.CompendiumIconColor
                    : _settings.CompendiumSecondaryTextColor;
                _tabLabels[tabIndex].style.color = selected
                    ? _settings.CompendiumPrimaryTextColor
                    : _settings.CompendiumSecondaryTextColor;
                _tabUnderlines[tabIndex].style.display =
                    selected ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private Color GetSelectedTabColor()
        {
            return WithAlpha(
                _settings.CompendiumSelectedTabColor,
                _settings.CompendiumSelectedTabOpacity);
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
            Color categoryColor = GetCategoryColor(entry.Category);
            _detailMetadata.text = GetCategoryLabel(entry.Category);
            _detailMetadata.style.color = categoryColor;
            SetBorder(
                _detailMetadata,
                WithAlpha(categoryColor, 0.45f),
                _settings.CompendiumPanelBorderThickness,
                _settings.CompendiumChipCornerRadius);
            _detailMetadata.style.backgroundColor = WithAlpha(categoryColor, 0.12f);
            Color statusColor = documented
                ? _settings.CompendiumActiveAccentColor
                : _settings.CompendiumSecondaryTextColor;
            _detailStatus.text = documented
                ? _settings.CompendiumDiscoveredLabel
                : _settings.CompendiumUnknownLabel;
            _detailStatus.style.color = statusColor;
            SetBorder(
                _detailStatus,
                WithAlpha(statusColor, 0.45f),
                _settings.CompendiumPanelBorderThickness,
                _settings.CompendiumChipCornerRadius);
            _detailStatus.style.backgroundColor = WithAlpha(statusColor, 0.12f);
            _detailDescription.text = documented
                ? FirstNonEmpty(entry.Description, _settings.CompendiumDefaultDescription)
                : _settings.CompendiumUnknownDescription;
            SetHeroHovered(false);
            if (_lightboxOpen)
            {
                // Keep an open lightbox in sync when the selection changes behind it.
                if (!documented)
                {
                    CloseLightbox();
                }
                else
                {
                    _lightboxImage.image = _detailImage.image;
                    _lightboxCaption.text = _detailTitle.text;
                    LayoutLightbox();
                }
            }
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
                _settings.CompendiumPanelCornerRadius);

            VisualElement header = _window.Q("compendium-header");
            header.style.height = _settings.CompendiumHeaderHeight;
            _progressRail.style.height = Mathf.Max(
                _settings.CompendiumSeparatorThickness,
                _settings.CompendiumProgressRailHeight);
            _progressRail.style.backgroundColor = _settings.CompendiumSeparatorColor;
            _progressRail.style.marginBottom = _settings.CompendiumGap;
            _progressFill.style.backgroundColor = _settings.CompendiumActiveAccentColor;
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
                SetBorder(_tabs[i], Color.clear, 0f, _settings.CompendiumTabCornerRadius);
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
                Slider slider = _verticalScroller.Q<Slider>();
                if (slider != null)
                {
                    slider.style.backgroundColor = _settings.CompendiumScrollbarTrackColor;
                    slider.style.borderTopWidth =
                        _settings.CompendiumScrollbarEndBorderThickness;
                    slider.style.borderBottomWidth =
                        _settings.CompendiumScrollbarEndBorderThickness;
                    slider.style.borderTopColor =
                        _settings.CompendiumScrollbarEndBorderColor;
                    slider.style.borderBottomColor =
                        _settings.CompendiumScrollbarEndBorderColor;
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
                    VisualElement dragger =
                        slider.Q(className: "unity-base-slider__dragger");
                    if (dragger != null)
                    {
                        SetBorder(
                            dragger,
                            Color.clear,
                            0f,
                            _settings.CompendiumScrollbarWidth * 0.5f);
                        dragger.style.backgroundColor =
                            _settings.CompendiumScrollbarThumbColor;
                    }
                }
            }
            _detail.style.width = _settings.CompendiumDetailPanelWidth;
            _detail.style.backgroundColor = _settings.CompendiumRaisedSurfaceColor;
            _detail.style.paddingLeft = _settings.CompendiumGap;
            _detail.style.paddingRight = _settings.CompendiumGap;
            _detail.style.paddingTop = _settings.CompendiumGap;
            _detail.style.paddingBottom = _settings.CompendiumGap;
            SetBorder(
                _detail,
                _settings.CompendiumDetailBorderColor,
                _settings.CompendiumPanelBorderThickness,
                _settings.CompendiumDetailCornerRadius);
            VisualElement hero = _detailImage.parent;
            hero.style.height = _settings.CompendiumDetailImageHeight;
            hero.style.backgroundColor = _settings.CompendiumCardColor;
            SetBorder(
                hero,
                _settings.CompendiumCardBorderColor,
                _settings.CompendiumPanelBorderThickness,
                _settings.CompendiumDetailImageCornerRadius);
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
            SetTextStyle(_detailStatus,
                _settings.CompendiumMetadataFontSize, _settings.CompendiumSecondaryTextColor, true);
            StyleChip(_detailMetadata);
            StyleChip(_detailStatus);
            SetTextStyle(_detailDescription,
                _settings.GalleryBodyFontSize, _settings.CompendiumPrimaryTextColor, false);
            _detail.Q(className: "compendium-detail-rule").style.backgroundColor =
                _settings.CompendiumActiveAccentColor;
            ApplyLightboxTheme();
        }

        private void ApplyLightboxTheme()
        {
            _lightbox.style.position = Position.Absolute;
            _lightbox.style.left = 0f;
            _lightbox.style.top = 0f;
            _lightbox.style.right = 0f;
            _lightbox.style.bottom = 0f;
            _lightbox.style.backgroundColor = _settings.CompendiumLightboxBackdropColor;
            _lightbox.style.display = DisplayStyle.None;
            _lightbox.style.opacity = 0f;
            SetTransition(_lightbox, _settings.CompendiumLightboxExpandSeconds, "opacity");

            _lightboxFrame.style.position = Position.Absolute;
            _lightboxFrame.style.backgroundColor = _settings.CompendiumRaisedSurfaceColor;
            SetBorder(
                _lightboxFrame,
                _settings.CompendiumDetailBorderColor,
                _settings.CompendiumLightboxBorderThickness,
                _settings.CompendiumLightboxCornerRadius);
            _lightboxImage.style.position = Position.Absolute;
            SetBorder(
                _lightboxImage,
                _settings.CompendiumCardBorderColor,
                _settings.CompendiumPanelBorderThickness,
                _settings.CompendiumDetailImageCornerRadius);

            _lightboxFooter.style.position = Position.Absolute;
            _lightboxFooter.style.left = 0f;
            _lightboxFooter.style.right = 0f;
            _lightboxFooter.style.bottom = 0f;
            _lightboxFooter.style.opacity = 0f;
            SetTransition(_lightboxFooter, _settings.CompendiumLightboxExpandSeconds, "opacity");
            SetTextStyle(
                _lightboxCaption,
                _settings.CompendiumDetailTitleFontSize,
                _settings.CompendiumPrimaryTextColor,
                true);
            SetTextStyle(
                _lightbox.Q<Label>(className: "compendium-lightbox-hint"),
                _settings.CompendiumMetadataFontSize,
                _settings.CompendiumSecondaryTextColor,
                true);

            _detailHeroHint.style.position = Position.Absolute;
            _detailHeroHint.style.left = 0f;
            _detailHeroHint.style.right = 0f;
            _detailHeroHint.style.bottom = 0f;
            _detailHeroHint.style.paddingTop = _settings.CompendiumChipPaddingVertical;
            _detailHeroHint.style.paddingBottom = _settings.CompendiumChipPaddingVertical;
            _detailHeroHint.style.backgroundColor = _settings.CompendiumCardScrimColor;
            _detailHeroHint.style.display = DisplayStyle.None;
            SetTextStyle(
                _detailHeroHint,
                _settings.CompendiumMetadataFontSize,
                _settings.CompendiumPrimaryTextColor,
                true);
        }

        private void StyleChip(Label chip)
        {
            chip.style.paddingLeft = _settings.CompendiumChipPaddingHorizontal;
            chip.style.paddingRight = _settings.CompendiumChipPaddingHorizontal;
            chip.style.paddingTop = _settings.CompendiumChipPaddingVertical;
            chip.style.paddingBottom = _settings.CompendiumChipPaddingVertical;
        }

        private void ApplyCardState(
            CardReferences card,
            bool selected,
            bool documented,
            PhotographableSubjectCategory category)
        {
            card.Root.style.backgroundColor = selected
                ? Color.Lerp(
                    _settings.CompendiumCardColor,
                    _settings.CompendiumActiveAccentColor,
                    0.12f)
                : _settings.CompendiumCardColor;
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
            overlay.style.paddingTop = _settings.CompendiumCardTitleTopInset;
            overlay.style.paddingBottom = _settings.CompendiumCardMetadataBottomInset;
            overlay.style.backgroundColor = documented
                ? _settings.CompendiumCardScrimColor
                : _settings.CompendiumLockedOverlayColor;
            SetTextStyle(card.Title,
                _settings.CompendiumCardTitleFontSize,
                documented
                    ? _settings.CompendiumPrimaryTextColor
                    : _settings.CompendiumSecondaryTextColor,
                true);
            SetTextStyle(card.Metadata,
                _settings.CompendiumMetadataFontSize, _settings.CompendiumSecondaryTextColor, true);
            Color accent = GetCategoryColor(category);
            if (!documented)
            {
                accent.a *= _settings.CompendiumLockedAccentOpacity;
            }
            card.Accent.style.backgroundColor = accent;
            card.Accent.style.width = _settings.CompendiumCardAccentWidth;
            card.SelectionMarker.style.backgroundColor = _settings.CompendiumActiveAccentColor;
            card.SelectionMarker.style.width = _settings.CompendiumSelectionMarkerSize;
            card.SelectionMarker.style.height = _settings.CompendiumSelectionMarkerSize;
            card.SelectionMarker.style.borderTopRightRadius =
                _settings.CompendiumCardCornerRadius;
            card.SelectionMarker.style.borderBottomLeftRadius =
                _settings.CompendiumCardCornerRadius;
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

        private Color GetCategoryColor(PhotographableSubjectCategory category)
        {
            return category switch
            {
                PhotographableSubjectCategory.Glyph => _settings.CompendiumGlyphAccentColor,
                PhotographableSubjectCategory.Landmark => _settings.CompendiumLandmarkAccentColor,
                PhotographableSubjectCategory.Enemy => _settings.CompendiumEnemyAccentColor,
                _ => _settings.CompendiumMiscAccentColor,
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

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
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
