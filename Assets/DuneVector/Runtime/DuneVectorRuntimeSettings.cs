using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace DuneVector
{
    public enum DuneGenerationPreset
    {
        ClassicDesert,
        GentleCinematic,
        GrandErg,
        SharpRidges,
        WindCarved,
        RoundedWindDunes,
        WindRibbonDunes,
        GrandWindSwells,
        RollingSandSea,
        FineRipples,
        ExtremeDunes,
    }

    public enum CloudArrangementPreset
    {
        BalancedDesertSky,
        SparseCinematic,
        MonumentalBanks,
        DevelopingColumns,
        HighWisps,
    }

    [System.Serializable]
    public sealed class GeoglyphArtworkPlacement
    {
        [Tooltip("Unique black-and-white artwork mask. White pixels become linework; black pixels remain transparent.")]
        public Texture2D Mask;

        [Tooltip("Authoritative center in the desert's persistent logical X/Z world coordinates.")]
        public Vector2 WorldCenter;

        [Tooltip("Width and length of the artwork footprint in world metres.")]
        public Vector2 WorldSize = new Vector2(640f, 426.67f);

        [Tooltip("Center of the visible mask linework in normalized Unity texture coordinates.")]
        public Vector2 MaskContentCenter = new Vector2(0.5f, 0.5f);

        [Tooltip("Width and height of the visible mask linework as normalized fractions of the full texture.")]
        public Vector2 MaskContentSize = Vector2.one;

        [Tooltip("Convex boundary samples around the visible linework in normalized Unity texture coordinates. Camera framing uses these points instead of the rectangular texture footprint.")]
        public List<Vector2> MaskCaptureBoundary = new List<Vector2>();

        [Tooltip("Rotation of the artwork footprint around its world-space center.")]
        public float RotationDegrees;

        [Range(0f, 1f)]
        [Tooltip("Opacity of the artwork linework over the lit sand surface.")]
        public float BlendStrength = 0.9f;

        [ColorUsage(false)]
        [Tooltip("Ground pigment shown wherever the mask contains linework.")]
        public Color LineColor = new Color(0.12f, 0.055f, 0.018f, 1f);

        [Header("Mask Definition")]
        [Range(0f, 1f)] public float MaskThreshold = 0.48f;
        [Range(0.0001f, 0.25f)] public float EdgeSoftness = 0.025f;

        [Header("Optional Slope Correction")]
        [Range(0f, 1f)]
        [Tooltip("Blends toward slope-corrected sampling only on sufficiently steep terrain. Zero preserves pure overhead X/Z projection.")]
        public float SlopeCorrectionStrength = 0.16f;

        [Range(0f, 89f)]
        [Tooltip("Terrain slope angle where correction begins to blend in.")]
        public float SlopeCorrectionStartAngle = 42f;

        [Min(0f)]
        [Tooltip("Maximum world-space sampling offset allowed on steep dune faces.")]
        public float MaximumSlopeCorrection = 3f;

        [Tooltip("World height used as the gentle slope-projection reference plane.")]
        public float SlopeReferenceHeight;
    }

    [System.Serializable]
    public sealed class GeoglyphSystemTuning
    {
        public bool Enabled = true;

        [ColorUsage(false, true)]
        [Tooltip("HDR emission added to geoglyph linework. Values above 1 drive the URP bloom post-process.")]
        public Color BloomEmissionColor = new Color(2.4f, 2.55f, 3f, 1f);

        [Tooltip("Unique persistent geoglyph landmarks. Entries are never spawned, tiled, randomized, or repeated by chunks.")]
        public List<GeoglyphArtworkPlacement> Placements = new List<GeoglyphArtworkPlacement>();

        public void EnsureInitialized()
        {
            Placements ??= new List<GeoglyphArtworkPlacement>();
        }

        public bool OverlapsArtworkFootprint(double logicalX, double logicalZ, float clearance = 0f)
        {
            if (!Enabled || Placements == null)
            {
                return false;
            }

            float safeClearance = Mathf.Max(0f, clearance);
            for (int i = 0; i < Placements.Count; i++)
            {
                GeoglyphArtworkPlacement placement = Placements[i];
                if (placement == null || placement.Mask == null ||
                    placement.WorldSize.x <= 0f || placement.WorldSize.y <= 0f)
                {
                    continue;
                }

                Vector2 contentCenter = placement.MaskContentCenter;
                Vector2 contentSize = placement.MaskContentSize;
                if (contentSize.x <= 0f || contentSize.y <= 0f)
                {
                    contentCenter = new Vector2(0.5f, 0.5f);
                    contentSize = Vector2.one;
                }

                Quaternion artworkRotation = Quaternion.Euler(0f, -placement.RotationDegrees, 0f);
                Vector2 normalizedCenterOffset = contentCenter - new Vector2(0.5f, 0.5f);
                Vector3 centerOffset = artworkRotation * new Vector3(
                    normalizedCenterOffset.x * placement.WorldSize.x,
                    0f,
                    normalizedCenterOffset.y * placement.WorldSize.y);
                Vector3 relative = new Vector3(
                    (float)(logicalX - placement.WorldCenter.x) - centerOffset.x,
                    0f,
                    (float)(logicalZ - placement.WorldCenter.y) - centerOffset.z);
                Vector3 artworkSpace = Quaternion.Inverse(artworkRotation) * relative;
                Vector2 halfSize = Vector2.Scale(placement.WorldSize, contentSize) * 0.5f;
                if (Mathf.Abs(artworkSpace.x) <= halfSize.x + safeClearance &&
                    Mathf.Abs(artworkSpace.z) <= halfSize.y + safeClearance)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public enum PhotographableSubjectCategory
    {
        Glyph,
        Landmark,
        Creature,
        Enemy,
        Plant,
        AncientStructure,
        RarePhenomenon,
        Misc,
    }

    [System.Serializable]
    public sealed class CompendiumEntryDefinition
    {
        [Tooltip("Stable identifier shared by the camera subject marker and persistent photography archive.")]
        public string SubjectId;
        public string DisplayName;
        public PhotographableSubjectCategory Category;
        [TextArea(2, 5)] public string Description;
        public string DiscoveryLocation;
        [TextArea(2, 5)] public string FieldNotes;
    }

    public enum PhotographyFilmGrainPreset
    {
        Thin1,
        Thin2,
        Medium1,
        Medium2,
        Medium3,
        Medium4,
        Medium5,
        Medium6,
        Large01,
        Large02,
    }

    [System.Serializable]
    public sealed class PhotographyTuning
    {
        public bool Enabled = true;

        [Header("Camera and Capture")]
        [Min(320)] public int ImageWidth = 1280;
        [Min(180)] public int ImageHeight = 720;
        [Range(1, 100)] public int JpegQuality = 90;
        [Min(1)] public int MaximumGalleryPhotographs = 200;
        [Range(0.25f, 1f)] public float MinimumZoom = 0.55f;
        [Min(1f)] public float MaximumZoom = 8f;
        [Min(0f)] public float CameraDistance = 0f;
        [Min(0f)] public float CameraHeight = 0.65f;
        [Tooltip("Highest the grounded camera can look while camera mode is active. Negative values look upward.")]
        [Range(-89f, 89f)] public float MinPitch = -85f;
        [Tooltip("Lowest the grounded camera can look while camera mode is active. Positive values look downward.")]
        [Range(-89f, 89f)] public float MaxPitch = 85f;
        [Min(0f)] public float ZoomStep = 2f;
        [Min(0f)] public float ZoomSharpness = 18f;
        [Min(0f)] public float IdentificationHoldDuration = 2.4f;
        [Min(0f)] public float ShutterFlashDuration = 0.18f;
        [Min(0f)] public float CaptureHoldDuration = 0.12f;
        [Min(0f)] public float HudEnterDuration = 0.22f;
        [Min(0f)] public float TargetAcquireDuration = 0.2f;
        [Min(0f)] public float ZoomFeedbackDuration = 0.8f;
        [Min(0f)] public float BracketSharpness = 14f;
        [Min(0f)] public float AccentColorSharpness = 18f;
        [Min(0f)] public float TargetStateSharpness = 20f;
        [Min(0f)] public float ValidationInterval = 0.08f;
        [Range(0f, 0.2f)] public float ViewportEdgePadding = 0.035f;
        [Min(0f)] public float CaptureHeightOffset = 0.8f;
        [Min(0f)] public float OcclusionRayEndTolerance = 2f;
        public LayerMask OcclusionLayers = -1;
        [Tooltip("Subjects with less than this visible fraction are ignored by the viewfinder so it never frames something hidden behind geometry.")]
        [Range(0f, 1f)] public float SubjectDetectionMinimumVisiblePercentage = 0.25f;

        [Header("Viewfinder Layout")]
        [Min(360f)] public float HudReferenceHeight = 1080f;
        [Range(0.5f, 2f)] public float HudMinimumScale = 0.75f;
        [Range(1f, 3f)] public float HudMaximumScale = 2f;
        [Min(0f)] public float ScreenMargin = 34f;
        [Min(1f)] public float FrameThickness = 2f;
        [Min(1f)] public float FrameCornerLength = 52f;
        [Range(0f, 1f)] public float OuterFrameOpacity = 0.48f;
        [Min(0f)] public float HudEnterSlideDistance = 9f;
        [Min(1f)] public float TargetBracketThickness = 3f;
        [Min(1f)] public float TargetBracketLength = 34f;
        [Min(0f)] public float TargetBracketPadding = 10f;
        [Min(0f)] public float TargetAcquireExpansion = 28f;
        [Min(0f)] public float InvalidBracketExpansion = 8f;
        [Min(0f)] public float ValidBracketInset = 4f;
        [Min(0f)] public float CaptureUiRecoil = 5f;
        [Min(1f)] public float CrosshairSize = 16f;
        [Min(1f)] public float CrosshairThickness = 2f;
        [Range(0f, 1f)] public float CrosshairOpacity = 0.42f;
        [Min(60f)] public float SubjectLabelWidth = 300f;
        [Min(16f)] public float SubjectLabelHeight = 30f;
        [Min(10f)] public float TargetStatusHeight = 20f;
        [Min(0f)] public float TargetLabelGap = 7f;
        [Min(8)] public int SubjectLabelFontSize = 17;
        [Min(8)] public int StatusFontSize = 14;
        [Min(8)] public int ZoomFontSize = 14;
        [Tooltip("Maximum width of the control hint bar. The bar auto-sizes to its content below this.")]
        [Min(220f)] public float CommandBarWidth = 900f;
        [Min(24f)] public float CommandBarHeight = 74f;
        [Min(20f)] public float CommandKeyWidth = 92f;
        [Min(14f)] public float CommandKeyHeight = 40f;
        [Min(0f)] public float CommandBarPadding = 30f;
        [Min(0f)] public float CommandGroupGap = 34f;
        [Tooltip("Seconds the control hint bar stays fully visible before fading out.")]
        [Min(0f)] public float CommandHintDuration = 10f;
        [Min(0.05f)] public float CommandHintFadeDuration = 0.9f;
        [Tooltip("Show the control hints only the first time the camera is raised in a play session.")]
        public bool CommandHintFirstUseOnly = true;
        [Min(80f)] public float CornerMetadataWidth = 220f;
        [Min(20f)] public float CornerMetadataHeight = 42f;
        [Min(0f)] public float BottomInterfaceOffset = 12f;
        [Min(100f)] public float DocumentationToastWidth = 440f;
        [Min(24f)] public float DocumentationToastHeight = 72f;
        [Min(0f)] public float DocumentationToastBottomOffset = 78f;
        [Min(8)] public int MetadataFontSize = 11;
        [Min(8)] public int CommandFontSize = 16;
        [Min(8)] public int CommandKeyFontSize = 18;
        [Min(8)] public int ModeLabelFontSize = 12;
        [ColorUsage(false)] public Color NeutralColor = new Color(0.92f, 0.88f, 0.78f, 0.88f);
        [ColorUsage(false)] public Color InvalidColor = new Color(0.82f, 0.42f, 0.36f, 0.94f);
        [ColorUsage(false)] public Color ValidColor = new Color(0.67f, 0.82f, 0.64f, 0.96f);
        [ColorUsage(false)] public Color HudShadowColor = new Color(0.03f, 0.025f, 0.02f, 0.72f);
        public Vector2 HudShadowOffset = new Vector2(1.5f, 2f);
        [ColorUsage(false)] public Color ShutterFlashColor = new Color(0.98f, 0.94f, 0.84f, 0.52f);
        [ColorUsage(false)] public Color HudTextColor = new Color(0.94f, 0.9f, 0.82f, 0.94f);
        [ColorUsage(false)] public Color HudMutedColor = new Color(0.84f, 0.8f, 0.72f, 0.58f);
        [ColorUsage(false)] public Color CommandBackdropColor = new Color(0.025f, 0.022f, 0.018f, 0.24f);
        [ColorUsage(false)] public Color KeycapColor = new Color(0.92f, 0.88f, 0.78f, 0.12f);
        public Font HudRegularFont;
        public Font HudSemiboldFont;
        public string CameraTitle = "FIELD DOCUMENTATION CAMERA";
        public string TextTrackingSpacer = "\u2009";
        public string NeutralStatus = "SCANNING";
        public string InvalidStatus = "REFRAME";
        public string ValidStatus = "CAPTURE READY";
        public string UnknownSubjectLabel = "UNKNOWN GLYPH";
        public string UndocumentedLabel = "UNDOCUMENTED";
        public string DocumentedLabel = "DOCUMENTED";
        public string ExitKey = "RMB";
        public string ExitAction = "EXIT";
        public string CaptureKey = "LMB";
        public string CaptureAction = "CAPTURE";
        public string ZoomKey = "WHEEL";
        public string ZoomAction = "ZOOM";
        public string ZoomFormat = "{0:0.0}x";
        public string PhotoCountFormat = "{0:000} / {1:000}";

        [Header("Viewfinder Surface Textures")]
        public bool SurfaceTexturesEnabled = true;
        public bool UseHdrpFilmGrain = true;
        public bool UseCustomFilmGrainTexture;
        public PhotographyFilmGrainPreset FilmGrainPreset = PhotographyFilmGrainPreset.Thin1;
        [Range(0f, 1f)] public float HdrpFilmGrainIntensity = 0.267f;
        [Range(0f, 1f)] public float HdrpFilmGrainResponse = 0.8f;
        public Texture2D FilmGrainTexture;
        public Texture2D LensGlassTexture;
        public Texture2D VignetteTexture;
        public Texture2D TechnicalGridTexture;
        public Texture2D CaptureFlashTexture;
        [Range(0f, 0.2f)] public float FilmGrainOpacity = 0.026f;
        [Range(0f, 0.2f)] public float LensGlassOpacity = 0.018f;
        [Range(0f, 0.5f)] public float VignetteOpacity = 0.18f;
        [Range(0f, 0.2f)] public float TechnicalGridOpacity = 0.026f;
        [Range(0f, 1f)] public float CaptureFlashOpacity = 0.42f;
        [Range(0f, 1f)] public float ZoomIdleOpacity = 0.48f;

        [Header("Identification Presentation")]
        [Min(240f)] public float IdentificationPanelWidth = 720f;
        [Min(120f)] public float IdentificationPanelHeight = 190f;
        [Min(8)] public int IdentificationTitleFontSize = 28;
        [Min(8)] public int IdentificationNameFontSize = 22;
        [ColorUsage(false)] public Color IdentificationPanelColor = new Color(0.015f, 0.05f, 0.075f, 0.95f);
        public string IdentifiedTitle = "GLYPH IDENTIFIED";
        public string RegisteredText = "COMPENDIUM ENTRY REGISTERED";
        public string GlyphDiscoveryMetadataFormat = "GLYPH {0:000}  /  AERIAL GEOGLYPH";
        public string GlyphDiscoveryEntryFormat = "ENTRY {0:000}";
        public string GlyphDiscoveryArchivedLabel = "ARCHIVED";
        public string GlyphDiscoveryContinuePrompt = "CONTINUE  [LMB]  >";
        [Min(240f)] public float GlyphDiscoveryPanelWidth = 900f;
        [Min(120f)] public float GlyphDiscoveryPanelHeight = 300f;
        [Min(0f)] public float GlyphDiscoveryPanelBottomOffset = 56f;
        [Min(0f)] public float GlyphDiscoveryPanelPadding = 28f;
        [Min(0f)] public float GlyphDiscoveryElementGap = 6f;
        [Min(0f)] public float GlyphDiscoveryHeaderHeight = 22f;
        [Min(0f)] public float GlyphDiscoveryMetadataHeight = 18f;
        [Min(0f)] public float GlyphDiscoveryNameHeight = 42f;
        [Min(0f)] public float GlyphDiscoveryLoreHeight = 100f;
        [Min(0f)] public float GlyphDiscoveryFooterHeight = 38f;
        [Min(0f)] public float GlyphDiscoveryIdentityWidth = 150f;
        [Min(0f)] public float GlyphDiscoveryIdentityGap = 28f;
        [Min(0f)] public float GlyphDiscoveryThumbnailSize = 110f;
        [Min(0f)] public float GlyphDiscoveryIdentityLabelHeight = 22f;
        [Min(0f)] public float GlyphDiscoveryCommandWidth = 190f;
        [Min(0f)] public float GlyphDiscoveryBorderThickness = 1f;
        [Min(0f)] public float GlyphDiscoveryAccentWidth = 3f;
        [Min(0f)] public float GlyphDiscoveryVignettePadding = 54f;
        [Min(0f)] public float GlyphDiscoveryScanLineHeight = 2f;
        [Min(0.01f)] public float GlyphDiscoveryScanDuration = 1f;
        [Min(0f)] public float GlyphDiscoveryContinueRevealDelay = 0.65f;
        [Min(0.01f)] public float GlyphDiscoveryFocusSweepDuration = 1.1f;
        [Min(0f)] public float GlyphDiscoveryFocusSweepWidth = 54f;
        [Min(8)] public int GlyphDiscoveryHeaderFontSize = 12;
        [Min(8)] public int GlyphDiscoveryMetadataFontSize = 11;
        [Min(8)] public int GlyphDiscoveryTitleFontSize = 28;
        [Min(8)] public int GlyphDiscoveryLoreFontSize = 18;
        [Min(8)] public int GlyphDiscoveryContinueFontSize = 13;
        [ColorUsage(false)] public Color GlyphDiscoveryVignetteColor = new Color(0.01f, 0.018f, 0.018f, 0.62f);
        [ColorUsage(false)] public Color GlyphDiscoveryPanelColor = new Color(0.018f, 0.035f, 0.04f, 0.94f);
        [ColorUsage(false)] public Color GlyphDiscoveryRaisedColor = new Color(0.04f, 0.065f, 0.066f, 0.96f);
        [ColorUsage(false)] public Color GlyphDiscoveryPrimaryTextColor = new Color(0.94f, 0.91f, 0.84f, 1f);
        [ColorUsage(false)] public Color GlyphDiscoverySecondaryTextColor = new Color(0.68f, 0.65f, 0.58f, 1f);
        [ColorUsage(false)] public Color GlyphDiscoveryAccentColor = new Color(0.58f, 0.72f, 0.58f, 1f);
        [ColorUsage(false)] public Color GlyphDiscoveryBorderColor = new Color(0.72f, 0.69f, 0.61f, 0.22f);
        [ColorUsage(false)] public Color GlyphDiscoveryCommandColor = new Color(0.18f, 0.27f, 0.25f, 0.72f);
        [ColorUsage(false)] public Color GlyphDiscoveryCommandHoverColor = new Color(0.26f, 0.4f, 0.35f, 0.9f);
        [ColorUsage(false)] public Color GlyphDiscoveryFocusSweepColor = new Color(0.72f, 0.9f, 0.74f, 0.16f);
        public string AlreadyDocumentedText = "ALREADY DOCUMENTED";
        public string ReplacePrompt = "REPLACE ATLAS PHOTOGRAPH?";
        public string ComparisonNewLabel = "NEW";
        public string ComparisonCurrentLabel = "CURRENT";
        [ColorUsage(false)] public Color ComparisonLabelColor = new Color(0.9f, 0.88f, 0.8f, 1f);
        [ColorUsage(false)] public Color ComparisonCardColor = new Color(0.015f, 0.05f, 0.075f, 0.95f);
        [ColorUsage(false)] public Color ComparisonLabelPanelColor = new Color(0.01f, 0.025f, 0.035f, 0.98f);
        [Min(120f)] public float ComparisonImageWidth = 520f;
        [Min(68f)] public float ComparisonImageHeight = 292f;
        [Min(0f)] public float ComparisonImageGap = 24f;
        [Min(0f)] public float ComparisonCardPadding = 12f;
        [Min(24f)] public float ComparisonLabelHeight = 54f;
        [Min(8)] public int ComparisonLabelFontSize = 28;
        public string ReplaceButton = "REPLACE";
        public string KeepButton = "KEEP CURRENT";
        public string ReplaceButtonHint = "ENTER";
        public string KeepButtonHint = "ESC";
        [Min(0f)] public float ReplaceDecisionPanelPadding = 30f;
        [Min(0f)] public float ReplaceDecisionSectionGap = 18f;
        [Min(0f)] public float ReplaceDecisionEyebrowHeight = 20f;
        [Min(0f)] public float ReplaceDecisionNameHeight = 38f;
        [Min(0f)] public float ReplaceDecisionPromptHeight = 22f;
        [Min(0f)] public float ReplaceDecisionHintHeight = 18f;
        [Min(24f)] public float ReplaceDecisionButtonHeight = 46f;
        [Min(60f)] public float ReplaceDecisionButtonWidth = 248f;
        [Min(0f)] public float ReplaceDecisionButtonGap = 18f;
        [Min(0f)] public float ReplaceDecisionCornerLength = 18f;
        [Range(0f, 1f)] public float ReplaceDecisionRestingAccent = 0.4f;
        [ColorUsage(false)] public Color ReplaceDecisionBackdropColor = new Color(0.008f, 0.012f, 0.014f, 0.8f);

        [Header("Gallery Layout")]
        [Min(640f)] public float GalleryReferenceWidth = 1920f;
        [Min(360f)] public float GalleryReferenceHeight = 1080f;
        [Range(0.5f, 2f)] public float GalleryMinimumScale = 0.7f;
        [Range(0.5f, 2f)] public float GalleryMaximumScale = 1.2f;
        [Min(400f)] public float GalleryPanelWidth = 1420f;
        [Min(300f)] public float GalleryPanelHeight = 860f;
        [Min(8f)] public float GalleryPadding = 30f;
        [Range(2, 8)] public int GalleryColumns = 4;
        [Min(80f)] public float GalleryThumbnailWidth = 300f;
        [Min(45f)] public float GalleryThumbnailHeight = 169f;
        [Min(0f)] public float GalleryGap = 18f;
        [Min(20f)] public float GalleryHeaderHeight = 84f;
        [Min(32f)] public float GalleryButtonHeight = 44f;
        [Min(60f)] public float GalleryActionButtonWidth = 168f;
        [Min(0f)] public float GalleryFooterHeight = 34f;
        [Min(4f)] public float GalleryScrollbarWidth = 14f;
        [Min(8)] public int GalleryTitleFontSize = 32;
        [Min(8)] public int GalleryBodyFontSize = 15;
        [ColorUsage(false)] public Color GalleryBackdropColor = new Color(0.01f, 0.02f, 0.035f, 0.98f);
        [ColorUsage(false)] public Color GalleryPanelColor = new Color(0.035f, 0.065f, 0.085f, 0.98f);
        [ColorUsage(false)] public Color GalleryAccentColor = new Color(0.12f, 0.85f, 1f, 1f);
        [ColorUsage(false)] public Color GallerySelectionColor = new Color(0.35f, 0.95f, 1f, 1f);
        [ColorUsage(false)] public Color GalleryDangerColor = new Color(0.95f, 0.25f, 0.2f, 1f);
        [ColorUsage(false)] public Color GalleryTextColor = Color.white;
        public string GalleryTitle = "PHOTOGRAPHIC ARCHIVE";
        public string GalleryCountFormat = "{0}  /  {1:000}";
        public string GallerySubtitleFormat = "{0} ARCHIVED   ·   {1} DOCUMENTED   ·   CAPACITY {2}";
        public string GalleryEmptyText = "NO PHOTOGRAPHS ARCHIVED";
        public string GalleryEmptyHint = "CAPTURED FRAMES ARE FILED HERE AUTOMATICALLY";
        public string GalleryGridHint = "CLICK A FRAME TO INSPECT   ·   ESC TO CLOSE";
        public string GalleryViewerHint = "◄ ►  BROWSE   ·   ESC TO RETURN";
        public string GalleryViewerCountFormat = "{0} / {1}";
        public string GalleryDocumentedLabel = "DOCUMENTED GLYPH";
        public string GalleryDocumentedTag = "DOCUMENTED";
        public string GalleryPhotoLabelFormat = "PHOTO {0:000}";
        public string GalleryCaptureTimeFormat = "yyyy.MM.dd   HH:mm";
        public string GalleryUnknownCaptureTime = "CAPTURE TIME UNKNOWN";
        public string GalleryPreviousButton = "‹  PREV";
        public string GalleryNextButton = "NEXT  ›";
        public string GalleryBackButton = "‹  ARCHIVE";
        public string GalleryDoneButton = "DONE";
        public string GalleryDeleteButton = "DELETE";
        public string GalleryDeleteTitle = "DELETE PHOTOGRAPH";
        public string DeleteConfirmation = "ARE YOU SURE YOU WANT TO DELETE THIS PHOTOGRAPH?";
        public string DeleteCancelButton = "CANCEL";

        [Header("Atlas Integration")]
        public string PhotographRequiredText = "PHOTOGRAPH THIS GLYPH TO CATALOGUE IT";
        public string AtlasAvailableGlyphFormat = "{0}  /  PHOTOGRAPH THE GLYPH TO CATALOGUE IT";
        public string PauseMenuButtonLabel = "GALLERY";
        public string AtlasNewMarker = "NEW";
        public string AtlasNewTitleSuffix = "  ●";
        public string AtlasDocumentationProgressFormat = "GLYPHS  {0} / {1}   /   SURVEYED  {2} / {1}";
        [Min(80f)] public float AtlasPhotoWidth = 190f;
        [Min(45f)] public float AtlasPhotoHeight = 107f;

        [Header("Compendium Catalog")]
        [Tooltip("Non-glyph compendium entries. Glyph entries are sourced from the authored Desert Atlas sites.")]
        public List<CompendiumEntryDefinition> CompendiumEntries = new List<CompendiumEntryDefinition>();
        [Range(0.001f, 1f)] public float CompendiumMinimumPhotoScreenCoverage = 0.001f;
        [Range(0.001f, 1f)] public float CompendiumMaximumPhotoScreenCoverage = 0.92f;
        [Range(0f, 1f)] public float CompendiumRequiredVisiblePercentage = 0.6f;
        public string CompendiumPauseMenuButtonLabel = "COMPENDIUM";
        public string CompendiumTitle = "DESERT COMPENDIUM";
        public string CompendiumSubtitle = "Archive of documented desert phenomena";
        public string CompendiumCloseLabel = "ESC  CLOSE";
        public string CompendiumDiscoveryCountFormat = "{0} / {1} DISCOVERED";
        public string CompendiumTabCountFormat = "{0}  {1:00}/{2:00}";
        public string CompendiumUnknownLabel = "UNDISCOVERED";
        public string CompendiumDiscoveredLabel = "DISCOVERED";
        public string CompendiumUnknownDescription = "Survey data unavailable. Document this subject in the field to unlock its archive record.";
        public string CompendiumDefaultDescription = "A documented phenomenon recorded during courier field operations across the shifting dunes.";
        public string CompendiumDiscoveryLocationLabel = "DISCOVERED";
        public string CompendiumDefaultDiscoveryLocation = "Location data unavailable";
        public string CompendiumFieldNotesLabel = "FIELD NOTES";
        public string CompendiumDefaultFieldNotes = "Further observation is required to complete this archive entry.";
        public string CompendiumEnlargeHint = "CLICK TO ENLARGE";
        public string CompendiumLightboxCloseHint = "CLICK ANYWHERE OR PRESS ESC TO CLOSE";
        public string CompendiumGlyphTabLabel = "GLYPHS";
        public string CompendiumLandmarkTabLabel = "LANDMARKS";
        public string CompendiumEnemyTabLabel = "ENEMIES";
        public string CompendiumMiscTabLabel = "MISC";

        [Header("Compendium Icons")]
        public Texture2D CompendiumGlyphTabIcon;
        public Texture2D CompendiumLandmarkTabIcon;
        public Texture2D CompendiumEnemyTabIcon;
        public Texture2D CompendiumMiscTabIcon;
        public Texture2D CompendiumSelectedCardIcon;

        [Header("Compendium Layout")]
        [Min(400f)] public float CompendiumPanelWidth = 1420f;
        [Min(300f)] public float CompendiumPanelHeight = 860f;
        [Min(0f)] public float CompendiumPanelBorderThickness = 1f;
        [Range(0f, 1f)] public float CompendiumPanelBorderOpacity = 0.15f;
        [Min(20f)] public float CompendiumHeaderHeight = 92f;
        [Min(28f)] public float CompendiumTabHeight = 58f;
        [Min(12f)] public float CompendiumTabIconSize = 68f;
        [Range(1, 8)] public int CompendiumCompactColumns = 3;
        [Range(1, 8)] public int CompendiumWideColumns = 3;
        [Min(360f)] public float CompendiumWideScreenMinimumHeight = 1200f;
        [HideInInspector] public int CompendiumColumns = 3;
        [Min(80f)] public float CompendiumSlotWidth = 270f;
        [Min(45f)] public float CompendiumSlotHeight = 72f;
        [HideInInspector] public float CompendiumSlotLabelHeight = 38f;
        [Min(0f)] public float CompendiumCardCornerRadius = 5f;
        [Min(0f)] public float CompendiumCardBorderThickness = 2f;
        [Min(0f)] public float CompendiumCardGap = 8f;
        [Min(0f)] public float CompendiumCardContentPadding = 14f;
        [Min(0f)] public float CompendiumCardTitleTopInset = 4f;
        [Min(0f)] public float CompendiumCardMetadataBottomInset = 4f;
        [Min(1f)] public float CompendiumSelectionMarkerSize = 18f;
        [Min(0f)] public float CompendiumSelectionIconInset = 3f;
        [Min(180f)] public float CompendiumDetailPanelWidth = 410f;
        [Min(100f)] public float CompendiumDetailImageHeight = 260f;
        [Min(20f)] public float CompendiumDetailTitleHeight = 70f;
        [Min(8)] public int CompendiumDetailTitleFontSize = 23;
        [Min(0f)] public float CompendiumGap = 22f;
        [Min(0f)] public float CompendiumGridPadding = 22f;
        [Min(2f)] public float CompendiumScrollbarWidth = 5f;
        [ColorUsage(false)] public Color CompendiumScrollbarTrackColor = new Color(0.015f, 0.025f, 0.03f, 1f);
        [Min(0f)] public float CompendiumScrollbarEndBorderThickness = 1f;
        [ColorUsage(false)] public Color CompendiumScrollbarEndBorderColor = new Color(0.34f, 0.42f, 0.45f, 1f);
        [HideInInspector] public float CompendiumScrollbarReserve = 24f;
        [Min(8)] public int CompendiumTitleFontSize = 30;
        [Min(8)] public int CompendiumSubtitleFontSize = 13;
        [Min(8)] public int CompendiumCardTitleFontSize = 14;
        [Min(8)] public int CompendiumMetadataFontSize = 11;
        [Min(8)] public int CompendiumTabFontSize = 14;
        [HideInInspector] public int CompendiumUnknownFontSize = 30;
        [Min(0f)] public float CompendiumActiveTabUnderlineHeight = 3f;
        [ColorUsage(false)] public Color CompendiumMainBackgroundColor = new Color(0.018f, 0.032f, 0.043f, 0.99f);
        [ColorUsage(false)] public Color CompendiumRaisedSurfaceColor = new Color(0.035f, 0.057f, 0.071f, 1f);
        [ColorUsage(false)] public Color CompendiumCardColor = new Color(0.045f, 0.063f, 0.074f, 1f);
        [ColorUsage(false)] public Color CompendiumCardBorderColor = new Color(0.18f, 0.23f, 0.26f, 0.75f);
        [ColorUsage(false)] public Color CompendiumSecondaryTextColor = new Color(0.58f, 0.66f, 0.7f, 1f);
        [ColorUsage(false)] public Color CompendiumPrimaryTextColor = new Color(0.94f, 0.92f, 0.86f, 1f);
        [ColorUsage(false)] public Color CompendiumLockedColor = new Color(0.19f, 0.21f, 0.23f, 1f);
        [ColorUsage(false)] public Color CompendiumLockedOverlayColor = new Color(0.08f, 0.09f, 0.1f, 0.72f);
        [ColorUsage(false)] public Color CompendiumTabColor = new Color(0.06f, 0.11f, 0.15f, 0f);
        [ColorUsage(false)] public Color CompendiumSelectedTabColor = new Color(0.12f, 0.36f, 0.44f, 1f);
        [ColorUsage(false)] public Color CompendiumIconColor = new Color(0.72f, 0.94f, 1f, 1f);
        [ColorUsage(false)] public Color CompendiumActiveAccentColor = new Color(0.78f, 0.57f, 0.27f, 1f);
        [ColorUsage(false)] public Color CompendiumHoverBorderColor = new Color(0.62f, 0.86f, 0.91f, 1f);

        [Header("Compendium Lightbox")]
        [Tooltip("Screen fraction reserved as empty space around the enlarged photo.")]
        [Range(0.01f, 0.25f)] public float CompendiumLightboxScreenMargin = 0.05f;
        [Tooltip("Matte thickness drawn between the enlarged photo and its frame edge.")]
        [Min(0f)] public float CompendiumLightboxMattePadding = 18f;
        [Tooltip("Seconds the photo takes to grow from its thumbnail into the full frame.")]
        [Range(0.05f, 1f)] public float CompendiumLightboxExpandSeconds = 0.22f;
        [Min(0f)] public float CompendiumLightboxCornerRadius = 12f;
        [Min(0f)] public float CompendiumLightboxBorderThickness = 1f;
        [ColorUsage(false)] public Color CompendiumLightboxBackdropColor = new Color(0.004f, 0.008f, 0.012f, 0.93f);

        [Header("Compendium Style")]
        [Min(0f)] public float CompendiumPanelCornerRadius = 14f;
        [Min(0f)] public float CompendiumTabCornerRadius = 8f;
        [Min(0f)] public float CompendiumDetailCornerRadius = 12f;
        [Min(0f)] public float CompendiumDetailImageCornerRadius = 8f;
        [Min(0f)] public float CompendiumSeparatorThickness = 1f;
        [ColorUsage(false)] public Color CompendiumSeparatorColor = new Color(0.42f, 0.62f, 0.7f, 0.16f);
        [ColorUsage(false)] public Color CompendiumDetailBorderColor = new Color(0.42f, 0.62f, 0.7f, 0.2f);
        [ColorUsage(false)] public Color CompendiumCardScrimColor = new Color(0.02f, 0.03f, 0.04f, 0.42f);
        [Min(0f)] public float CompendiumProgressRailHeight = 3f;
        [Range(0f, 1f)] public float CompendiumSelectedTabOpacity = 0.22f;
        [Min(0f)] public float CompendiumCardAccentWidth = 3f;
        [Range(0f, 1f)] public float CompendiumLockedAccentOpacity = 0.32f;
        [Min(0f)] public float CompendiumChipCornerRadius = 10f;
        [Min(0f)] public float CompendiumChipPaddingHorizontal = 9f;
        [Min(0f)] public float CompendiumChipPaddingVertical = 3f;
        [ColorUsage(false)] public Color CompendiumGlyphAccentColor = new Color(0.42f, 0.78f, 0.92f, 1f);
        [ColorUsage(false)] public Color CompendiumLandmarkAccentColor = new Color(0.85f, 0.7f, 0.35f, 1f);
        [ColorUsage(false)] public Color CompendiumEnemyAccentColor = new Color(0.85f, 0.36f, 0.34f, 1f);
        [ColorUsage(false)] public Color CompendiumMiscAccentColor = new Color(0.55f, 0.74f, 0.68f, 1f);
        [ColorUsage(false)] public Color CompendiumScrollbarThumbColor = new Color(0.42f, 0.58f, 0.64f, 0.9f);

        public void EnsureInitialized()
        {
            CompendiumEntries ??= new List<CompendiumEntryDefinition>();
        }
    }

    [System.Serializable]
    public sealed class CloudLobeTuning
    {
        public Vector3 Position;
        public Vector3 Scale;
        public Vector3 Rotation;
    }

    [System.Serializable]
    public sealed class CloudArchetypeTuning
    {
        public string DisplayName;
        public Vector2 AltitudeOffsetRange;
        public Vector3 MinimumScale;
        public Vector3 MaximumScale;
        public Vector2 YawRange;
        [Min(0f)] public float PitchRollVariation;
        public CloudLobeTuning[] SunlitLobes;
        public CloudLobeTuning[] UnderbellyLobes;
    }

    [System.Serializable]
    public sealed class CloudArrangementTuning
    {
        public string DisplayName;
        [Min(0f)] public float ClusterCountMultiplier;
        [Range(1, 8)] public int CompositionRegionSizeInChunks;
        [Range(0f, 1f)] public float NegativeSpaceRegionChance;
        [Min(0f)] public float NegativeSpaceDensityMultiplier;
        [Min(0f)] public float CloudRegionDensityMultiplier;
        public float AltitudeOffset;
        public Vector3 ScaleMultiplier;

        [Header("Archetype Mix")]
        [Min(0f)] public float LongStretchedWeight;
        [Min(0f)] public float CompactPuffyWeight;
        [Min(0f)] public float WideLayeredBankWeight;
        [Min(0f)] public float TallDevelopingWeight;
        [Min(0f)] public float SmallDistantWispyWeight;

        public float GetArchetypeWeight(int archetypeIndex)
        {
            return archetypeIndex switch
            {
                0 => LongStretchedWeight,
                1 => CompactPuffyWeight,
                2 => WideLayeredBankWeight,
                3 => TallDevelopingWeight,
                4 => SmallDistantWispyWeight,
                _ => 0f,
            };
        }
    }

    [System.Serializable]
    public sealed class CloudTuning
    {
        public bool Enabled;
        [Tooltip("Approximate total cluster count across the preloaded chunk area.")]
        [Range(4, 60)] public int ClusterCount;
        [Min(20f)] public float Altitude;
        [Min(0f)] public float DriftSpeed;
        [Tooltip("Cloud drift direction on the world X/Z plane. Set both components to zero to stop cloud drift.")]
        public Vector2 DriftDirection;
        [Header("Weather Wind Response")]
        [Tooltip("Additional cloud drift speed contributed by each metre per second of the live desert-weather wind.")]
        [Min(0f)] public float WeatherWindSpeedMultiplier;
        public int RandomSeedOffset;

        [Header("Arrangement Presets")]
        [Tooltip("Selects the authored density, archetype mix, altitude, and scale arrangement used when the world starts.")]
        public CloudArrangementPreset ActiveArrangementPreset;
        public CloudArrangementTuning BalancedDesertSky;
        public CloudArrangementTuning SparseCinematic;
        public CloudArrangementTuning MonumentalBanks;
        public CloudArrangementTuning DevelopingColumns;
        public CloudArrangementTuning HighWisps;

        [Header("Shared Placement")]
        [Min(0f)] public float PlacementInset;
        [Min(0f)] public float MinimumLocalSeparation;
        [Range(1, 16)] public int PlacementAttempts;
        [Tooltip("Extra streamed-terrain chunks a drifting cloud can cross beyond its source chunk before wrapping. Keep this beyond the terrain unload radius so wrapping occurs outside the visible desert.")]
        [Range(0, 24)] public int DriftWrapPaddingInChunks;

        [Header("Appearance")]
        [ColorUsage(false)] public Color SunlitColor;
        [ColorUsage(false)] public Color UnderbellyColor;
        [Range(0f, 1f)] public float MaterialSmoothness;
        [Range(0f, 1f)] public float MaterialMetallic;
        [Range(0, 2)] public int FacetSubdivisions;
        [Range(0.0001f, 0.1f)] public float CullScreenRelativeHeight;

        [Header("Silhouette Roundness")]
        [Tooltip("Blends each cloud cluster's horizontal X/Z scale toward an even oval footprint.")]
        [Range(0f, 1f)] public float ClusterHorizontalRoundness = 0.65f;
        [Tooltip("Expands the narrower horizontal axis of each cloud lobe to prevent thin sausage silhouettes.")]
        [Range(0f, 1f)] public float LobeHorizontalRoundness = 0.72f;
        [Tooltip("Offsets lobes through the cloud's depth so side views remain broad instead of collapsing into a line.")]
        [Range(0f, 0.75f)] public float LobeDepthSpread = 0.28f;

        [Header("Authored Archetype Kit")]
        public CloudArchetypeTuning LongStretched;
        public CloudArchetypeTuning CompactPuffy;
        public CloudArchetypeTuning WideLayeredBank;
        public CloudArchetypeTuning TallDeveloping;
        public CloudArchetypeTuning SmallDistantWispy;

        public void EnsureInitialized()
        {
            BalancedDesertSky ??= new CloudArrangementTuning();
            SparseCinematic ??= new CloudArrangementTuning();
            MonumentalBanks ??= new CloudArrangementTuning();
            DevelopingColumns ??= new CloudArrangementTuning();
            HighWisps ??= new CloudArrangementTuning();
            LongStretched ??= new CloudArchetypeTuning();
            CompactPuffy ??= new CloudArchetypeTuning();
            WideLayeredBank ??= new CloudArchetypeTuning();
            TallDeveloping ??= new CloudArchetypeTuning();
            SmallDistantWispy ??= new CloudArchetypeTuning();
        }

        public CloudArrangementTuning GetActiveArrangement()
        {
            return ActiveArrangementPreset switch
            {
                CloudArrangementPreset.SparseCinematic => SparseCinematic,
                CloudArrangementPreset.MonumentalBanks => MonumentalBanks,
                CloudArrangementPreset.DevelopingColumns => DevelopingColumns,
                CloudArrangementPreset.HighWisps => HighWisps,
                _ => BalancedDesertSky,
            };
        }

        public CloudArchetypeTuning[] GetArchetypes()
        {
            return new[]
            {
                LongStretched,
                CompactPuffy,
                WideLayeredBank,
                TallDeveloping,
                SmallDistantWispy,
            };
        }
    }

    [System.Serializable]
    public sealed class DeliveryTuning
    {
        private const string ObjectiveHexagonResourcePath = "UI/ObjectiveIndicatorDoubleHexagon";
        private const string ObjectiveArrowResourcePath = "UI/ObjectiveIndicatorArrow";

        public bool Enabled = true;
        public bool RandomizeLocationsEachPlay = true;
        public int JobSeedOffset;
        [Min(20f)] public float MinimumPickupDistance = 75f;
        [Min(20f)] public float MaximumPickupDistance = 145f;
        [Min(20f)] public float MinimumDeliveryDistance = 110f;
        [Min(20f)] public float MaximumDeliveryDistance = 210f;
        [Tooltip("Radius used by pickup objective rings.")]
        [Min(1f)] public float ObjectiveRingRadius = 3.2f;
        [Header("Pickup Ground Ring")]
        [Tooltip("Ground-ring effect used to mark package pickup zones.")]
        public GameObject PickupRingGroundPrefab;
        [Tooltip("Radius, in meters, represented by the pickup prefab at its authored scale. The prefab is fitted from this radius to Objective Ring Radius.")]
        [Min(0.01f)] public float PickupRingPrefabAuthoredRadius = 1f;
        [Tooltip("Independent scale multiplier applied after the pickup prefab is fitted to Objective Ring Radius.")]
        public Vector3 PickupRingPrefabScale = Vector3.one;
        [Tooltip("Local position offset applied to the fitted pickup prefab.")]
        public Vector3 PickupRingPrefabLocalOffset = Vector3.zero;
        [Tooltip("Local rotation offset applied on top of the pickup prefab's authored rotation.")]
        public Vector3 PickupRingPrefabLocalEulerAngles = Vector3.zero;
        [Tooltip("Height of pickup ground rings above the sampled terrain surface, in meters.")]
        [Min(0f)] public float PickupRingGroundOffset = 0.1f;
        [Header("Delivery Ground Ring")]
        [Tooltip("Radius used by delivery objective rings.")]
        [Min(1f)] public float DeliveryRingRadius = 15f;
        [Tooltip("Ground-ring effect used to mark delivery drop zones.")]
        public GameObject DeliveryRingGroundPrefab;
        [Tooltip("Radius, in meters, represented by the ground-ring prefab at its authored scale. The prefab is fitted from this radius to Delivery Ring Radius.")]
        [Min(0.01f)] public float DeliveryRingPrefabAuthoredRadius = 1f;
        [Tooltip("Independent scale multiplier applied after the ground-ring prefab is fitted to Delivery Ring Radius.")]
        public Vector3 DeliveryRingPrefabScale = Vector3.one;
        [Tooltip("Local position offset applied to the fitted ground-ring prefab.")]
        public Vector3 DeliveryRingPrefabLocalOffset = Vector3.zero;
        [Tooltip("Local rotation offset applied on top of the ground-ring prefab's authored rotation.")]
        public Vector3 DeliveryRingPrefabLocalEulerAngles = Vector3.zero;
        [Tooltip("Height of delivery ground rings above the sampled terrain surface, in meters.")]
        [Min(0f)] public float DeliveryRingGroundOffset = 0.1f;
        [Tooltip("Depth below the sampled terrain surface where pickup and delivery ground-ring prefab roots begin, in meters.")]
        [Min(0f)] public float GroundRingPrefabTerrainInset = 1f;
        [Tooltip("Height of pickup rings above the sampled terrain surface, in meters.")]
        [Min(0f)] public float PickupRingHeight = 1f;
        [Tooltip("Height of delivery rings above the sampled terrain surface, in meters.")]
        [Min(0f)] public float ObjectiveRingHeight = 3.4f;
        [Header("Billboarding")]
        [Tooltip("Distance from the drone center at which pickup and delivery rings freeze their current orientation instead of continuing to face the camera.")]
        [Min(0f)] public float ObjectiveRingBillboardDisableRadius = 36f;

        [Header("Package Visual")]
        [Tooltip("Resources-relative path of the package prefab.")]
        public string PackageModelResourcePath = "box_packagePrefab";
        [Tooltip("Use the legacy procedural package instead of the imported model.")]
        public bool UseProceduralPackageFallback;
        public Vector3 PackageModelLocalOffset = Vector3.zero;
        public Vector3 PackageModelLocalEulerAngles = Vector3.zero;
        [Min(0.1f)] public float PackageScale = 0.8f;

        [Header("Package Drop")]
        [Min(0.01f)] public float PackageDropMass = 1f;
        [Tooltip("How much of the drone's velocity the package keeps when released. Set to 0 for an initial velocity of (0, 0, 0), or 1 to preserve all of it.")]
        [InspectorName("Drone Velocity Preserved")]
        [Range(0f, 1f)] public float PackageDropInheritedVelocityMultiplier = 1f;
        public Vector3 PackageDropAngularVelocity = new Vector3(0.7f, 1.5f, 0.4f);
        public Vector3 PackageDropColliderSize = new Vector3(1.2f, 0.82f, 1f);
        [Min(0f)] public float PackageDropGroundContactOffset = 0.03f;

        [Header("Objective Ring Appearance")]
        [ColorUsage(false, true)] public Color PickupRingBaseColor = new Color(0.32f, 0.015f, 0.48f);
        [ColorUsage(false, true)] public Color PickupRingEmissionColor = new Color(2.8f, 0.05f, 4.2f);
        [ColorUsage(false, true)] public Color DeliveryRingBaseColor = new Color(0.015f, 0.42f, 0.12f);
        [ColorUsage(false, true)] public Color DeliveryRingEmissionColor = new Color(0.05f, 3.8f, 0.45f);
        [Tooltip("How many complete RGB hue cycles the pickup and delivery rings make per second. Set to 0 to freeze the blend.")]
        [Min(0f)] public float ObjectiveRingRgbBlendSpeed = 0.12f;
        [Tooltip("Hue offset between pickup and delivery rings, measured as a normalized color wheel turn.")]
        [Range(0f, 1f)] public float DeliveryRingRgbHueOffset = 0.5f;
        [Tooltip("Brightness of the animated RGB base color.")]
        [Min(0f)] public float ObjectiveRingRgbBaseIntensity = 0.55f;
        [Tooltip("HDR brightness of the animated RGB emission color.")]
        [Min(0f)] public float ObjectiveRingRgbEmissionIntensity = 4.5f;

        [Header("Objective Indicator HUD")]
        [Min(240f)] public float ObjectiveIndicatorReferenceHeight = 1080f;
        [Range(0.25f, 2f)] public float ObjectiveIndicatorMinimumScale = 0.65f;
        [Range(0.25f, 2f)] public float ObjectiveIndicatorMaximumScale = 1.25f;
        public Texture2D ObjectiveIndicatorHexagonIcon;
        public Texture2D ObjectiveIndicatorArrowIcon;
        [Min(8f)] public float ObjectiveIndicatorHexagonRadius = 27f;
        [Min(4f)] public float ObjectiveIndicatorArrowLength = 22f;
        [Min(4f)] public float ObjectiveIndicatorArrowWidth = 21f;
        [Min(0f)] public float ObjectiveIndicatorArrowGap = 4f;
        [Min(0f)] public float ObjectiveIndicatorTextGap = 13f;
        [Min(12f)] public float ObjectiveIndicatorTextWidth = 300f;
        [Min(12f)] public float ObjectiveIndicatorTextHeight = 44f;
        [Min(8)] public int ObjectiveIndicatorFontSize = 30;
        [Min(0f)] public float ObjectiveIndicatorEdgePadding = 18f;
        [Min(0f)] public float ObjectiveIndicatorViewportHysteresis = 18f;
        [Min(0f)] public float ObjectiveIndicatorPositionSharpness = 14f;
        [Min(0f)] public float ObjectiveIndicatorTransitionSharpness = 18f;
        [Tooltip("Number of icon-only flashes when a pickup or delivery objective begins.")]
        [Min(0)] public int ObjectiveIndicatorStartFlashCount = 3;
        [Tooltip("Seconds the objective icon remains visible during each start flash.")]
        [Min(0.01f)] public float ObjectiveIndicatorStartFlashOnDuration = 0.15f;
        [Tooltip("Seconds the objective icon remains hidden between start flashes.")]
        [Min(0.01f)] public float ObjectiveIndicatorStartFlashOffDuration = 0.12f;
        public Vector2 ObjectiveIndicatorShadowOffset = new Vector2(2f, 3f);
        [ColorUsage(false)] public Color ObjectiveIndicatorColor = new Color(0.96f, 0.98f, 1f, 1f);
        [ColorUsage(false)] public Color ObjectiveIndicatorShadowColor = new Color(0f, 0f, 0f, 0.72f);

        [Header("Completion Message")]
        [ColorUsage(false)] public Color CompletionTextRed = new Color(1f, 0.55f, 0.68f);
        [ColorUsage(false)] public Color CompletionTextGreen = new Color(0.55f, 1f, 0.72f);
        [ColorUsage(false)] public Color CompletionTextBlue = new Color(0.55f, 0.78f, 1f);
        [Min(0f)] public float CompletionTextColorCyclesPerSecond = 0.45f;

        public void EnsureInitialized()
        {
            ObjectiveIndicatorHexagonIcon ??= Resources.Load<Texture2D>(ObjectiveHexagonResourcePath);
            ObjectiveIndicatorArrowIcon ??= Resources.Load<Texture2D>(ObjectiveArrowResourcePath);
        }
    }

    [System.Serializable]
    public sealed class LandmarkContractLocation
    {
        public DuneLandmarkType Type;
        public string DisplayName;
    }

    [System.Serializable]
    public sealed class CourierContractTuning
    {
        [Header("Debug")]
        [Tooltip("Immediately completes accepted contracts at the courier hub without awarding gold.")]
        public bool DebugCompleteContractsInstantlyWithoutPayout;

        [Header("Contract Board")]
        public bool Enabled = true;
        [Range(5, 8)] public int OfferedContractCount = 6;
        public int ContractSeedOffset = 18431;
        [Min(1)] public int DualModifierUnlockDeliveries = 50;
        [Min(1)] public int TripleModifierUnlockDeliveries = 150;
        [Range(0f, 1f)] public float DualModifierChance = 0.42f;
        [Range(0f, 1f)] public float TripleModifierChance = 0.09f;
        [Range(0f, 1f)] public float UnknownContractChance = 0.14f;
        [Tooltip("Shortest planned contract route at risk 0.")]
        [Min(10f)] public float MinimumRouteDistanceAtRiskZero = 500f;
        [Tooltip("Longest planned contract route at risk 0.")]
        [Min(10f)] public float MaximumRouteDistanceAtRiskZero = 1000f;
        [Tooltip("Minimum distance added to the route band for each risk tier.")]
        [Min(0f)] public float MinimumRouteDistanceAddedPerRisk = 500f;
        [Tooltip("Maximum distance added to the route band for each risk tier.")]
        [Min(0f)] public float MaximumRouteDistanceAddedPerRisk = 1000f;
        [Min(10f)] public float MinimumPickupInsertionDistance = 75f;
        [Min(10f)] public float MaximumPickupInsertionDistance = 135f;
        [Tooltip("Maximum horizontal distance from the player's desert insertion point to the resolved pickup package.")]
        [Min(1f)] public float MaximumPickupSpawnDistance = 500f;
        [Min(10f)] public float MinimumRouteOriginDistance = 320f;
        [Min(10f)] public float MaximumRouteOriginDistance = 620f;
        [Min(0)] public int MinimumBaseReward = 260;
        [Min(0)] public int MaximumBaseReward = 1300;
        [Min(0f)] public float DistanceRewardPerMeter = 0.32f;
        [Min(1f)] public float UnknownRewardMultiplier = 1.6f;
        [Min(1f)] public float DualModifierRewardMultiplier = 1.75f;
        [Min(1f)] public float TripleModifierRewardMultiplier = 2.4f;
        [Min(0f)] public float ContractRefreshSeconds = 240f;
        [Tooltip("Gold charged when the player manually refreshes the contract board.")]
        [Min(0)] public int ContractRefreshGoldCost = 100;
        [Tooltip("Designer-authored contract-board location label for each landmark type.")]
        public LandmarkContractLocation[] LandmarkLocations;
        [Tooltip("Landmark archetypes eligible for pickup and delivery contract objectives.")]
        public DuneLandmarkType[] ContractLandmarkTypes;

        public float EvaluateMinimumRouteDistance(int risk)
        {
            return Mathf.Max(10f, MinimumRouteDistanceAtRiskZero) +
                (Mathf.Max(0, risk) * Mathf.Max(0f, MinimumRouteDistanceAddedPerRisk));
        }

        public float EvaluateMaximumRouteDistance(int risk)
        {
            float minimum = EvaluateMinimumRouteDistance(risk);
            float maximum = Mathf.Max(10f, MaximumRouteDistanceAtRiskZero) +
                (Mathf.Max(0, risk) * Mathf.Max(0f, MaximumRouteDistanceAddedPerRisk));
            return Mathf.Max(minimum, maximum);
        }

        [Header("Risk Scaling")]
        [Range(1, 100)] public int MaximumRisk = 20;
        [Min(0f)] public float RiskRewardMultiplierPerTier = 0.12f;
        [Min(1f)] public float RiskEnemyMultiplierAtRankOne = 1.1f;
        [Min(1f)] public float RiskEnemyMultiplierAtMaximumRank = 3f;
        [Min(1)] public int RiskGroundEnemyReferenceCount = 8;

        [Header("Risk Sand Ambusher")]
        [Min(1)] public int SandAmbusherMinimumRisk = 2;
        [Min(0f)] public float SandAmbusherInitialDelay = 2f;
        [Min(0.1f)] public float SandAmbusherBaseInterval = 2.4f;
        [Min(0f)] public float SandAmbusherIntervalReductionPerRisk = 0.55f;
        [Min(0.1f)] public float SandAmbusherMinimumInterval = 0.55f;
        [Min(0f)] public float SandAmbusherMinimumTargetOffset = 0f;
        [Tooltip("Random horizontal offset around the predicted player position.")]
        [Min(0f)] public float SandAmbusherMaximumTargetOffset = 4f;
        [Tooltip("Target prediction time at risk 0.")]
        [Min(0f)] public float SandAmbusherTargetPredictionTime = 1.7f;
        [Tooltip("Target prediction time at the configured risk ceiling.")]
        [Min(0f)] public float SandAmbusherTargetPredictionTimeAtRiskCeiling = 0.7f;
        [Tooltip("Risk where target prediction time reaches its ceiling value.")]
        [Min(1)] public int SandAmbusherTargetPredictionRiskCeiling = 20;
        [InspectorName("Sand Ambusher Minimum Attack Angle")]
        [Tooltip("Minimum angle above the horizon for a Sand Ambusher's full attack path, whether the drone is grounded or airborne.")]
        [Range(0f, 90f)] public float SandAmbusherGroundedMinimumAttackAngle = 65f;
        [Min(0f)] public float SandAmbusherWarningDuration = 1.15f;
        [Tooltip("FMOD one-shot event played at the terrain rupture when a Sand Ambusher emerges.")]
        public string SandAmbusherEmergenceEvent = "event:/Explosion_Sand_Ambusher";
        [Tooltip("Effect spawned at the terrain rupture when a Sand Ambusher emerges.")]
        public GameObject SandAmbusherEmergencePrefab;
        [Tooltip("Local position applied to the spawned emergence effect.")]
        public Vector3 SandAmbusherEmergencePrefabLocalPosition = Vector3.zero;
        [Tooltip("Local Euler rotation offset added to the emergence prefab's authored rotation.")]
        public Vector3 SandAmbusherEmergencePrefabLocalEulerAngles = Vector3.zero;
        [Tooltip("Scale multiplier applied to the emergence prefab's authored scale.")]
        public Vector3 SandAmbusherEmergencePrefabLocalScale = Vector3.one;
        [Tooltip("Seconds before the spawned emergence effect is destroyed. Set to 0 to let the emergence object manage its lifetime.")]
        [Min(0f)] public float SandAmbusherEmergencePrefabLifetime = 5f;
        [Min(0.1f)] public float SandAmbusherBuriedDepth = 8f;
        [Min(0.1f)] public float SandAmbusherAttackSpeed = 48f;
        [Min(0f)] public float SandAmbusherAttackOvershoot = 5f;
        [Min(0.1f)] public float SandAmbusherMaximumAttackDuration = 3f;
        [Min(0.1f)] public float SandAmbusherRetreatSpeed = 32f;
        [Min(0f)] public float SandAmbusherBaseDamage = 18f;
        [Min(0f)] public float SandAmbusherDamagePerRisk = 5f;
        public string SandAmbusherDeathMessage = "Dragged beneath the dunes by a sand ambusher.";
        [Min(0.1f)] public float SandAmbusherCollisionRadius = 2.2f;
        [Min(0.1f)] public float SandAmbusherPlayerCollisionRadius = 3f;
        [Min(0.1f)] public float SandAmbusherHealth = 55f;
        [Range(1, 60)] public int SandAmbusherMaximumActive = 60;

        [Header("Risk Sand Ambusher Creature Visual")]
        public int SandAmbusherVisualSeed = 9317;
        [Range(3, 10)] public int SandAmbusherVisualSegmentCount = 6;
        [Min(0.1f)] public float SandAmbusherSegmentSpacing = 3.8f;
        [Min(0.1f)] public float SandAmbusherUpperSegmentRadius = 1.8f;
        [Min(0.1f)] public float SandAmbusherLowerSegmentRadius = 3.2f;
        [Min(0.1f)] public float SandAmbusherUpperSegmentHeight = 3.2f;
        [Min(0.1f)] public float SandAmbusherLowerSegmentHeight = 4.4f;
        [Range(0f, 0.5f)] public float SandAmbusherSegmentScaleVariation = 0.14f;
        [Range(0f, 45f)] public float SandAmbusherSegmentRotationVariation = 12f;
        [Range(3, 32)] public int SandAmbusherArmorMeshRings = 14;
        [Range(5, 48)] public int SandAmbusherArmorMeshRadialSegments = 24;
        [Range(0f, 0.8f)] public float SandAmbusherArmorIrregularity = 0.22f;
        [Min(0.1f)] public float SandAmbusherJointScale = 1f;
        [Min(0.1f)] public float SandAmbusherJointCompressedScale = 0.58f;
        [Min(0.01f)] public float SandAmbusherJointLengthMultiplier = 0.25f;
        [Range(8, 48)] public int SandAmbusherJointMeshRadialSegments = 32;
        [Range(3, 16)] public int SandAmbusherJointMeshHemisphereRings = 12;
        [Range(0f, 1f)] public float SandAmbusherSegmentCompressedSpacing = 0.34f;
        [Min(0f)] public float SandAmbusherSegmentEmergenceDelay = 0.06f;
        [Min(0.01f)] public float SandAmbusherSegmentExtensionDuration = 0.38f;
        [Range(0.1f, 1f)] public float SandAmbusherSegmentEmergenceScale = 0.62f;
        [Min(0f)] public float SandAmbusherFullSwayBlendDuration = 0.9f;
        [Min(0f)] public float SandAmbusherExposedDuration = 2.2f;
        [Min(0f)] public float SandAmbusherIdleSwayAmplitude = 0.42f;
        [Min(0f)] public float SandAmbusherIdleSwayFrequency = 1.15f;
        [Min(0f)] public float SandAmbusherCrossSwayAmplitude = 0.22f;
        [Min(0f)] public float SandAmbusherCrossSwayFrequencyMultiplier = 1.37f;
        [Min(0f)] public float SandAmbusherSwayPhasePerSegment = 0.55f;
        [Range(0f, 1f)] public float SandAmbusherTailSwayFalloff = 0.45f;
        [Min(0f)] public float SandAmbusherSwayRotationMultiplier = 5f;
        [Range(0, 6)] public int SandAmbusherRidgesPerSegment = 3;
        [Range(0f, 2f)] public float SandAmbusherRidgeRadialOffset = 0.78f;
        [Min(0.01f)] public float SandAmbusherRidgeWidth = 0.2f;
        [Min(0.01f)] public float SandAmbusherRidgeHeight = 0.62f;
        [Min(0.01f)] public float SandAmbusherRidgeDepth = 0.12f;
        public float SandAmbusherRidgeTilt = 18f;
        [Range(2, 16)] public int SandAmbusherRidgeMeshLengthSegments = 10;
        [Range(5, 32)] public int SandAmbusherRidgeMeshRadialSegments = 16;
        [Range(0f, 0.35f)] public float SandAmbusherRidgeTipScale = 0.04f;
        [Min(0f)] public float SandAmbusherRidgeVerticalOffset = 0.25f;
        [Min(0f)] public float SandAmbusherRidgeAngularVariation = 18f;
        [Range(0f, 1f)] public float SandAmbusherMissingRidgeChance = 0.2f;
        [Min(0.1f)] public float SandAmbusherCreaseSandRadius = 0.74f;
        [Min(0.01f)] public float SandAmbusherCreaseSandThickness = 0.055f;
        [Range(8, 64)] public int SandAmbusherCreaseSandMajorSegments = 48;
        [Range(3, 24)] public int SandAmbusherCreaseSandTubeSegments = 12;
        public float SandAmbusherCreaseSandVerticalPosition = 0.34f;
        public float SandAmbusherCreaseSandTilt = 7f;
        [Min(0f)] public float SandAmbusherCrownBaseHeight = 0.38f;
        [Min(0.01f)] public float SandAmbusherCrownCoreWidth = 0.7f;
        [Min(0.01f)] public float SandAmbusherCrownCoreHeight = 0.55f;
        [Min(0.01f)] public float SandAmbusherCrownCoreDepth = 0.7f;
        public float SandAmbusherCrownCoreTilt = -8f;
        [Range(6, 32)] public int SandAmbusherCrownCoreMeshRings = 24;
        [Range(8, 48)] public int SandAmbusherCrownCoreMeshRadialSegments = 32;
        [Min(0f)] public float SandAmbusherCrownProngBaseSeparation = 0.2f;
        [Min(0f)] public float SandAmbusherCrownProngSpread = 1.6f;
        [Min(0.1f)] public float SandAmbusherCrownProngHeight = 1.9f;
        [Min(0f)] public float SandAmbusherCrownProngDepthCurve = 0.16f;
        [Min(0.01f)] public float SandAmbusherCrownProngBaseRadius = 0.23f;
        [Min(0.01f)] public float SandAmbusherCrownProngTipRadius = 0.05f;
        [Min(0.1f)] public float SandAmbusherCrownProngTaperPower = 0.7f;
        [Range(3, 24)] public int SandAmbusherCrownProngPathSegments = 14;
        [Range(5, 24)] public int SandAmbusherCrownProngRadialSegments = 12;
        [Min(0f)] public float SandAmbusherProngMotionDegrees = 8f;
        [Min(0f)] public float SandAmbusherProngMotionFrequency = 0.85f;
        [Min(0f)] public float SandAmbusherProngMotionAsymmetry = 1.22f;
        public Color SandAmbusherArmorColor = new Color(0.18f, 0.095f, 0.045f, 1f);
        public Color SandAmbusherArmorEmission = new Color(0.08f, 0.018f, 0.004f, 1f);
        [Range(0f, 1f)] public float SandAmbusherArmorSmoothness = 0.24f;
        [Range(0f, 1f)] public float SandAmbusherArmorMetallic = 0.16f;
        public Color SandAmbusherUndersideColor = new Color(0.045f, 0.028f, 0.022f, 1f);
        [Range(0f, 1f)] public float SandAmbusherUndersideSmoothness = 0.12f;
        [Range(0f, 1f)] public float SandAmbusherUndersideMetallic = 0.05f;
        public Color SandAmbusherRidgeColor = new Color(0.42f, 0.22f, 0.08f, 1f);
        public Color SandAmbusherRidgeEmission = new Color(0.16f, 0.035f, 0.004f, 1f);
        [Range(0f, 1f)] public float SandAmbusherRidgeSmoothness = 0.31f;
        [Range(0f, 1f)] public float SandAmbusherRidgeMetallic = 0.22f;
        public Color SandAmbusherCreaseSandColor = new Color(0.68f, 0.38f, 0.16f, 1f);
        [Range(0f, 1f)] public float SandAmbusherCreaseSandSmoothness = 0.08f;

        [Header("Risk Sand Ambusher Fracture")]
        [ColorUsage(true, true)] public Color SandAmbusherFractureColor = new Color(12f, 14f, 16f, 1f);
        [Tooltip("Overall multiplier for the Sand Ambusher fracture's planar lengths, jitter, and widths.")]
        [Min(0.01f)] public float SandAmbusherFractureOverallScale = 1f;
        [Tooltip("World-space center angle of the allowed fracture direction cone. Zero points along world +X.")]
        [Range(-180f, 180f)] public float SandAmbusherFractureRotation = 90f;
        [Tooltip("Angular size of the fracture direction cone. Zero fixes the direction; 360 allows any rotation.")]
        [Range(0f, 360f)] public float SandAmbusherFractureAllowedRotation = 360f;
        [Min(1f)] public float SandAmbusherFractureMainLength = 38f;
        [Range(3, 48)] public int SandAmbusherFractureMainPointCount = 22;
        [Min(0f)] public float SandAmbusherFractureMainJitter = 2.2f;
        [Min(0.05f)] public float SandAmbusherFractureMainWidth = 1.8f;
        [Range(0, 20)] public int SandAmbusherFractureBranchCount = 10;
        [Min(0.1f)] public float SandAmbusherFractureBranchMinimumLength = 5f;
        [Min(0.1f)] public float SandAmbusherFractureBranchMaximumLength = 15f;
        [Range(2, 24)] public int SandAmbusherFractureBranchPointCount = 8;
        [Min(0f)] public float SandAmbusherFractureBranchJitter = 1.25f;
        [Min(0.01f)] public float SandAmbusherFractureBranchMinimumWidth = 0.38f;
        [Min(0.01f)] public float SandAmbusherFractureBranchMaximumWidth = 0.85f;
        [Range(0f, 1f)] public float SandAmbusherFractureBranchMinimumOrigin = 0.14f;
        [Range(0f, 1f)] public float SandAmbusherFractureBranchMaximumOrigin = 0.86f;
        [Range(0f, 2f)] public float SandAmbusherFractureBranchForwardBias = 0.65f;
        [Range(0f, 1f)] public float SandAmbusherFractureBranchMinimumDelay = 0.18f;
        [Range(0f, 1f)] public float SandAmbusherFractureBranchMaximumDelay = 0.58f;
        [Range(0.01f, 1f)] public float SandAmbusherFractureBranchMinimumSpread = 0.18f;
        [Range(0.01f, 1f)] public float SandAmbusherFractureBranchMaximumSpread = 0.42f;
        [Range(0.01f, 1f)] public float SandAmbusherFracturePrimarySpreadFraction = 0.72f;
        [Range(0f, 1f)] public float SandAmbusherFractureJitterPersistence = 0.56f;
        [Min(0f)] public float SandAmbusherFractureSurfaceOffset = 0.08f;
        [Min(0f)] public float SandAmbusherFractureEdgeNoiseScale = 0.55f;
        [Range(0f, 1f)] public float SandAmbusherFractureEdgeNoiseStrength = 0.45f;
        [Range(0f, 1f)] public float SandAmbusherFractureInitialWidth = 0.06f;
        [Range(0f, 1f)] public float SandAmbusherFracturePreBurstWidth = 0.28f;
        [Min(0f)] public float SandAmbusherFractureMinimumIntensity = 1.4f;
        [Min(0f)] public float SandAmbusherFractureMaximumIntensity = 8f;
        [Min(0f)] public float SandAmbusherFractureBurstIntensity = 24f;
        [Min(0.1f)] public float SandAmbusherFractureIntensityPower = 2.4f;
        [Min(0f)] public float SandAmbusherFractureBurstHoldDuration = 0.09f;
        [Min(0.01f)] public float SandAmbusherFractureFadeDuration = 0.65f;

        [Header("Risk Sand Ambusher Sand VFX")]
        [Range(8, 256)] public int SandAmbusherParticleTextureResolution = 64;
        public Color SandAmbusherDustColor = new Color(0.78f, 0.49f, 0.25f, 0.55f);
        [Range(0f, 1f)] public float SandAmbusherParticleFadeInFraction = 0.06f;
        [Range(0f, 1f)] public float SandAmbusherParticleFadeOutFraction = 0.72f;
        [Range(0f, 1f)] public float SandAmbusherPreBreakStartFraction = 0.62f;
        [Min(0f)] public float SandAmbusherPreBreakDustEmissionRate = 35f;
        [Min(0f)] public float SandAmbusherPreBreakDebrisEmissionRate = 6f;
        [Range(1, 12)] public int SandAmbusherDirectionalBurstEmitterCount = 5;
        [Range(0, 256)] public int SandAmbusherDirectionalBurstParticleCount = 18;
        [Range(1, 512)] public int SandAmbusherDirectionalBurstMaximumParticles = 30;
        [Min(0.01f)] public float SandAmbusherDirectionalBurstMinimumLifetime = 0.5f;
        [Min(0.01f)] public float SandAmbusherDirectionalBurstMaximumLifetime = 1.8f;
        [Min(0.01f)] public float SandAmbusherDirectionalBurstMinimumSize = 0.18f;
        [Min(0.01f)] public float SandAmbusherDirectionalBurstMaximumSize = 0.75f;
        [Min(0f)] public float SandAmbusherDirectionalBurstMinimumSpeed = 8f;
        [Min(0f)] public float SandAmbusherDirectionalBurstMaximumSpeed = 18f;
        [Min(0f)] public float SandAmbusherDirectionalBurstGravity = 1.1f;
        [Range(0f, 90f)] public float SandAmbusherDirectionalBurstConeAngle = 18f;
        [Min(0f)] public float SandAmbusherDirectionalBurstEmitterRadius = 2f;
        [Min(0f)] public float SandAmbusherDirectionalBurstUpwardBias = 0.72f;
        [Min(0f)] public float SandAmbusherDirectionalBurstStretch = 2.5f;
        [Min(0f)] public float SandAmbusherDirectionalBurstVelocityScale = 0.15f;
        [Range(0, 512)] public int SandAmbusherDustBurstParticleCount = 100;
        [Range(1, 1024)] public int SandAmbusherDustMaximumParticles = 160;
        [Min(0.01f)] public float SandAmbusherDustMinimumLifetime = 1.5f;
        [Min(0.01f)] public float SandAmbusherDustMaximumLifetime = 4f;
        [Min(0.01f)] public float SandAmbusherDustMinimumSize = 3f;
        [Min(0.01f)] public float SandAmbusherDustMaximumSize = 8f;
        [Min(0f)] public float SandAmbusherDustMinimumSpeed = 1f;
        [Min(0f)] public float SandAmbusherDustMaximumSpeed = 4f;
        public float SandAmbusherDustGravity = 0f;
        [Min(0.01f)] public float SandAmbusherDustEmitterHeight = 0.3f;
        [Min(0.01f)] public float SandAmbusherDustEmitterWidth = 8f;
        [Min(0f)] public float SandAmbusherDustTurbulence = 2f;
        [Min(0f)] public float SandAmbusherDustTurbulenceFrequency = 0.4f;
        [Range(0, 256)] public int SandAmbusherDebrisParticleCount = 28;
        [Range(1, 512)] public int SandAmbusherDebrisMaximumParticles = 40;
        [Min(0.01f)] public float SandAmbusherDebrisMinimumLifetime = 1.2f;
        [Min(0.01f)] public float SandAmbusherDebrisMaximumLifetime = 3f;
        [Min(0.01f)] public float SandAmbusherDebrisMinimumSize = 0.3f;
        [Min(0.01f)] public float SandAmbusherDebrisMaximumSize = 0.9f;
        [Min(0f)] public float SandAmbusherDebrisMinimumSpeed = 7f;
        [Min(0f)] public float SandAmbusherDebrisMaximumSpeed = 16f;
        [Min(0f)] public float SandAmbusherDebrisGravity = 1.3f;
        [Range(0f, 90f)] public float SandAmbusherDebrisConeAngle = 34f;
        [Min(0f)] public float SandAmbusherDebrisEmitterRadius = 2.5f;
        [Range(3, 8)] public int SandAmbusherDebrisMeshRings = 4;
        [Range(5, 12)] public int SandAmbusherDebrisMeshRadialSegments = 6;
        [Range(0f, 0.8f)] public float SandAmbusherDebrisMeshIrregularity = 0.3f;
        [Range(1, 1024)] public int SandAmbusherTrickleMaximumParticles = 180;
        [Min(0f)] public float SandAmbusherTrickleEmissionRate = 32f;
        [Min(0f)] public float SandAmbusherTrickleDuration = 3f;
        [Min(0.01f)] public float SandAmbusherTrickleMinimumLifetime = 0.4f;
        [Min(0.01f)] public float SandAmbusherTrickleMaximumLifetime = 1.2f;
        [Min(0.01f)] public float SandAmbusherTrickleMinimumSize = 0.05f;
        [Min(0.01f)] public float SandAmbusherTrickleMaximumSize = 0.22f;
        [Min(0f)] public float SandAmbusherTrickleMinimumSpeed = 0.2f;
        [Min(0f)] public float SandAmbusherTrickleMaximumSpeed = 1f;
        [Min(0f)] public float SandAmbusherTrickleGravity = 1f;
        [Min(0f)] public float SandAmbusherTrickleStretch = 1.4f;
        [Min(0f)] public float SandAmbusherTrickleVelocityScale = 0.08f;

        [Header("Cargo Modifiers")]
        [Range(0f, 100f)] public float FragileFailureIntegrity = 18f;
        [Min(0f)] public float FragileCargoDamageMultiplier = 1.45f;
        [Min(0f)] public float StandardCargoDamageMultiplier = 0f;
        [Min(0f)] public float HazardousCargoDamageMultiplier = 0.2f;
        [Min(0f)] public float CargoHardImpactSpeed = 18f;
        [Min(0f)] public float CargoHardImpactDamagePerSpeed = 1.4f;
        [Min(0.1f)] public float ExpressExpectedSpeed = 32f;
        [Min(0f)] public float ExpressGraceSeconds = 18f;
        [Range(0.1f, 1f)] public float OversizedSpeedMultiplier = 0.72f;
        [Range(0.1f, 1f)] public float OversizedAccelerationMultiplier = 0.64f;
        [Range(0.1f, 1f)] public float OversizedTurningMultiplier = 0.62f;
        [Min(1f)] public float OversizedVisualScale = 1.75f;
        [Min(0f)] public float UnknownRevealDelay = 5f;
        [Range(0f, 100f)] public float HazardousWarningIntegrity = 72f;
        [Range(0f, 100f)] public float HazardousUnstableIntegrity = 45f;
        [Range(0f, 100f)] public float HazardousCriticalIntegrity = 22f;
        [Min(0.1f)] public float HazardousPulseInterval = 3.2f;
        [Min(0f)] public float HazardousPulseDamage = 6f;
        [Tooltip("Lowest fraction of hazardous pulse damage applied on long routes. Pulse damage scales down from full damage at the minimum route distance based on the contract's total route distance.")]
        [Range(0f, 1f)] public float HazardousPulseMinimumDistanceMultiplier = 0.25f;
        public string HazardousPulseDeathMessage = "Destroyed by a Hazardous Cargo pulse.";
        [Range(2, 5)] public int MultiDropMinimumStops = 2;
        [Range(2, 5)] public int MultiDropMaximumStops = 3;
        [Range(0f, 1f)] public float IntegrityRewardFloor = 0.25f;

        [Header("Contract Runtime")]
        [Min(0f)] public float CompletionReturnDelay = 3.5f;
        [Min(0f)] public float FailureReturnDelay = 3f;
        [Min(0.1f)] public float ObjectivePackageScale = 0.8f;
        public Vector3 CarriedPackageOffset = new Vector3(0f, -0.62f, -0.28f);
        public Vector3 OversizedPackageOffset = new Vector3(0f, -0.82f, -0.48f);
        [Min(0f)] public float PackageSpinSpeed = 28f;
        [Min(0f)] public float CargoWarningScale = 0.38f;
        [Min(0f)] public float CargoWarningPulseSpeed = 8f;

        [Header("Cargo Presentation")]
        [Min(0f)] public float CargoDamagePulseAmount = 0.07f;
        [Min(0f)] public float CargoWarningHeight = 0.62f;
        [Range(1, 8)] public int CargoWarningLightCount = 4;
        [Min(0f)] public float CargoWarningLightRadius = 0.52f;
        [Min(0f)] public float CargoWarningOrbitSpeed = 140f;
        [Range(0f, 100f)] public float CargoCriticalEffectsThreshold = 32f;
        [Min(0f)] public float CargoCriticalSparkRate = 12f;
        [Min(0f)] public float CargoCriticalSparkLifetime = 0.45f;
        [Min(0f)] public float CargoCriticalSparkSpeed = 1.4f;
        [Min(0f)] public float CargoCriticalSparkSize = 0.08f;

        [Header("Contract HUD")]
        [Min(160f)] public float HudWidth = 330f;
        [Min(60f)] public float HudHeight = 128f;
        [Min(0f)] public float HudLeft = 24f;
        [Min(0f)] public float HudTop = 24f;
        [Min(10)] public int HudTitleFontSize = 17;
        [Min(9)] public int HudBodyFontSize = 13;
        [Min(9)] public int HudStatusFontSize = 14;
        [Min(0f)] public float ObjectiveEdgePadding = 64f;
        [ColorUsage(false)] public Color HudPanelColor = new Color(0.025f, 0.045f, 0.07f, 0.9f);
        [ColorUsage(false)] public Color HudAccentColor = new Color(1f, 0.62f, 0.16f, 1f);
        [ColorUsage(false)] public Color HudTextColor = new Color(0.9f, 0.96f, 1f, 1f);
        [ColorUsage(false)] public Color IntegrityHealthyColor = new Color(0.22f, 0.95f, 0.64f, 1f);
        [ColorUsage(false)] public Color IntegrityCriticalColor = new Color(1f, 0.16f, 0.08f, 1f);
    }

    [System.Serializable]
    public sealed class WorldHubTuning
    {
        public bool Enabled = true;
        [Tooltip("Height of the hub origin above the terrain it is anchored to. Set this so the lowest point of the authored hub meets the sand.")]
        [Min(0f)] public float PlatformHeightAboveTerrain = 16.53f;
        [Header("Premium Hub Visuals")]
        [Tooltip("Optional authored visual shell for the hub. Gameplay colliders and terminals remain runtime-built.")]
        public GameObject PremiumVisualPrefab;
        public Vector3 PremiumVisualLocalPosition = Vector3.zero;
        public Vector3 PremiumVisualLocalEulerAngles = Vector3.zero;
        public Vector3 PremiumVisualLocalScale = Vector3.one;
        [Tooltip("Walkable radius of the authored premium hub before applying its local X/Z scale. Used by the floor collider and invisible containment boundary.")]
        [Min(0f)] public float PremiumVisualSurfaceRadius = 25.35f;
        [Tooltip("Hub-local height of the authored outer deck ring the drone walks on.")]
        public float PremiumVisualDeckSurfaceHeight = 1.42f;
        [Tooltip("Hub-local height of the sunken centre plaza the drone spawns on. Terminals stand on this floor.")]
        public float PremiumVisualPlazaSurfaceHeight = 0.62f;
        [Tooltip("Radius of the sunken centre plaza before applying the hub's local X/Z scale. Used for the spawn height and the flight landing plane only; walking collision comes from the authored meshes.")]
        [Min(0f)] public float PremiumVisualPlazaRadius = 11f;
        [Tooltip("Collides the drone against the authored hub's own meshes, so modelled ramps, rails, and props are the collision. No extra floor, pad, or box colliders are generated.")]
        public bool PremiumVisualMeshCollisionEnabled = true;
        [Tooltip("When enabled, the authored visual shell replaces the primitive platform, braces, and pylons.")]
        public bool ReplaceProceduralStructureVisuals = true;
        [Min(8f)] public float PlatformRadius = 26f;
        [Min(0.5f)] public float PlatformThickness = 2.4f;
        [Tooltip("Hub-local standing spot for the contract terminal. Park it in front of the authored screen it reads from.")]
        public Vector3 ContractTerminalLocalPosition = new Vector3(0f, 0f, 16.5f);
        public Vector3 ContractTerminalLocalEulerAngles = Vector3.zero;
        [Min(1f)] public float TerminalInteractionRadius = 6f;
        public Vector3 ArchiveTerminalLocalPosition = new Vector3(16.5f, 0f, 0f);
        public Vector3 ArchiveTerminalLocalEulerAngles = new Vector3(0f, 90f, 0f);
        [Min(1f)] public float ArchiveTerminalInteractionRadius = 6f;
        public Vector3 FreeRoamTerminalLocalPosition = new Vector3(-16.5f, 0f, 0f);
        public Vector3 FreeRoamTerminalLocalEulerAngles = new Vector3(0f, -90f, 0f);
        [Min(1f)] public float FreeRoamTerminalInteractionRadius = 6f;
        public float FreeRoamDeploymentHeadingDegrees = 90f;
        [Tooltip("Hub-relative offset applied to the free roam deployment point. Keep the Z push large enough that the drone lands clear of the hub footprint instead of inside it.")]
        public Vector3 FreeRoamDeploymentLocalOffset = new Vector3(0f, 0f, 40f);
        [Tooltip("Hub-local position of the drone upgrade pad.")]
        public Vector3 UpgradeAreaLocalPosition = new Vector3(0f, 0f, -7.5f);
        [Min(0f)] public float PlayerSpawnHeight = 2.2f;
        public bool RestoreHealthOnReturn = true;
        public bool RestoreStaminaOnReturn = true;
        [Tooltip("Horizontal distance from the hub center inside which the hub is hidden from photography subject detection.")]
        [Min(0f)] public float PhotographySuppressionRadius;

        [Header("Physical Terminals")]
        [Tooltip("Skips the primitive pedestal, screen, header, and masts so the authored hub's own consoles act as the terminals. The runtime keeps an invisible interaction anchor at each terminal position.")]
        public bool UseAuthoredTerminalGeometry = true;
        public Vector3 TerminalPedestalLocalPosition = new Vector3(0f, 2f, 0f);
        public Vector3 TerminalPedestalScale = new Vector3(3f, 4f, 2f);
        public Vector3 TerminalScreenLocalPosition = new Vector3(0f, 4.1f, -0.45f);
        public Vector3 TerminalScreenScale = new Vector3(4.4f, 2.4f, 0.25f);
        public float TerminalScreenTilt = -12f;
        public Vector3 TerminalHeaderLocalPosition = new Vector3(0f, 5.7f, 0f);
        public Vector3 TerminalHeaderScale = new Vector3(5.8f, 0.32f, 1.2f);
        [Min(0f)] public float TerminalSignalMastHorizontalOffset = 2.25f;
        public Vector3 TerminalSignalMastLocalPosition = new Vector3(0f, 7.4f, 0.2f);
        public Vector3 TerminalSignalMastScale = new Vector3(0.12f, 1.8f, 0.12f);
        public string ContractTerminalName = "CONTRACT TERMINAL";
        public string ArchiveTerminalName = "MESSAGE ARCHIVE";
        public string FreeRoamTerminalName = "FREE ROAM TERMINAL";
        public string FreeRoamTerminalNearbyPrompt = "PRESS E — DEPLOY TO FREE ROAM";
        public string TerminalNearbyPromptFormat = "PRESS E — OPEN {0}";
        public string TerminalDistancePromptFormat = "{0}  {1:0} m";

        [Header("Hub Containment")]
        public bool ContainmentEnabled = true;
        [Min(0.5f)] public float ContainmentWallHeight = 8f;
        [Min(0.1f)] public float ContainmentWallThickness = 0.8f;
        [Range(8, 64)] public int ContainmentWallSegments = 32;
        [Tooltip("Extra horizontal clearance kept between the drone capsule and the containment wall.")]
        [Min(0f)] public float ContainmentSafetyPadding = 0.15f;

        [Min(0f)] public float DesertInsertionHeight = 8f;
        [Min(0.1f)] public float TeleportBuildDuration = 1.15f;
        [Min(0.1f)] public float TeleportFadeDuration = 0.45f;
        [Min(0.1f)] public float TeleportRebuildDuration = 0.8f;
        [Min(0f)] public float StabilizeSharpness = 6f;
        [Min(0.1f)] public float TeleportEffectRadius = 4.5f;
        [Min(4)] public int TeleportParticleCount = 28;
        [Min(0f)] public float TeleportParticleSpinSpeed = 150f;
        [Min(0f)] public float TeleportParticleLiftSpeed = 6f;

        [Header("Hub Presentation")]
        [Tooltip("Whether the cyan segmented energy lanes rotate around the hub platform.")]
        public bool PlatformEnergyLanesEnabled;
        [Range(8, 48)] public int PlatformEnergySegmentCount = 24;
        [Min(0f)] public float PlatformEnergyRingRadius = 19f;
        [Min(0f)] public float PlatformEnergySegmentLength = 4.2f;
        [Min(0f)] public float PlatformEnergySegmentWidth = 0.32f;
        [Min(0f)] public float PlatformEnergySegmentHeight = 0.12f;
        public float PlatformEnergyRotationSpeed = 7f;
        [ColorUsage(false, true)] public Color PlatformEnergyColor = new Color(3.8f, 3.8f, 3.8f, 1f);
        [Range(3, 12)] public int HubPylonCount = 6;
        [Min(0f)] public float HubPylonRadius = 22f;
        [Min(0f)] public float HubPylonHeight = 11f;
        [Min(0f)] public float HubPylonWidth = 1.1f;
        [Min(0f)] public float HubPylonLean = 16f;
        [Min(0f)] public float HubBeaconPulseSpeed = 2.8f;
        [Min(0f)] public float HubBeaconPulseAmount = 0.16f;
        [Range(1, 6)] public int UpgradePadArmCount = 3;
        [Min(0f)] public float UpgradePadArmLength = 7.5f;
        public float UpgradePadRotationSpeed = -11f;

        [Header("Teleport Presentation")]
        [Min(0f)] public float TeleportParticleMinimumSize = 0.05f;
        [Min(0f)] public float TeleportParticleMaximumSize = 0.16f;
        [Min(0f)] public float TeleportHelixHeight = 6f;
        [Min(0f)] public float TeleportConvergenceRadius = 0.25f;
        [Range(1, 6)] public int TeleportEnergyRingCount = 3;
        [Range(8, 32)] public int TeleportEnergyRingSegments = 16;
        [Min(0f)] public float TeleportEnergyRingSpacing = 1.7f;
        [Min(0f)] public float TeleportEnergyRingSegmentLength = 0.75f;
        [Min(0f)] public float TeleportEnergyRingThickness = 0.045f;
        public float TeleportEnergyRingRotationSpeed = 95f;

        [Header("Terminal UI")]
        [Min(480f)] public float TerminalReferenceWidth = 1600f;
        [Min(320f)] public float TerminalReferenceHeight = 900f;
        [Range(0.5f, 1.5f)] public float TerminalMinimumScale = 0.72f;
        [Range(0.5f, 1.5f)] public float TerminalMaximumScale = 1.08f;
        [Min(480f)] public float TerminalPanelWidth = 1120f;
        [Min(320f)] public float TerminalPanelHeight = 640f;
        [Min(0f)] public float TerminalScreenMargin = 28f;
        [Min(10f)] public float TerminalPadding = 28f;
        [Min(10f)] public float ContractCardGap = 12f;
        [Range(2, 4)] public int TerminalCardColumns = 3;
        [Range(3, 4)] public int TerminalExpandedGridColumns = 4;
        [Range(5, 8)] public int TerminalExpandedGridThreshold = 6;
        [Min(100f)] public float ContractCardHeight = 196f;
        [Min(10)] public int TerminalTitleFontSize = 30;
        [Min(9)] public int TerminalBodyFontSize = 13;
        [Min(9)] public int TerminalButtonFontSize = 13;
        [Min(9)] public int TerminalKickerFontSize = 11;
        [Min(9)] public int TerminalDestinationFontSize = 17;
        [Min(9)] public int TerminalRewardFontSize = 18;
        [Min(9)] public int TerminalMetaFontSize = 12;
        [Min(0f)] public float TerminalHeaderHeight = 122f;
        [Min(0f)] public float TerminalFooterHeight = 38f;
        [Min(1f)] public float TerminalContractRefreshButtonWidth = 260f;
        [Min(1f)] public float TerminalContractRefreshButtonHeight = 30f;
        [Min(0f)] public float TerminalAccentBarHeight = 4f;
        [Min(0f)] public float TerminalCardAccentWidth = 5f;
        [Min(0f)] public float TerminalContractOrderPipSize = 5f;
        [Min(0f)] public float TerminalContractOrderPipGap = 3f;
        [Range(1, 50)] public int TerminalRiskPipsPerRow = 10;
        [Min(0f)] public float TerminalRiskPipRowGap = 3f;
        [Min(0f)] public float TerminalPanelBorderThickness = 2f;
        public Vector2 TerminalPanelShadowOffset = new Vector2(12f, 14f);
        [Min(180f)] public float TerminalTooltipWidth = 360f;
        [Min(0f)] public float TerminalTooltipPadding = 14f;
        public Vector2 TerminalTooltipMouseOffset = new Vector2(18f, 20f);
        [Min(9)] public int TerminalTooltipTitleFontSize = 12;
        [Min(9)] public int TerminalTooltipBodyFontSize = 12;
        [Min(120f)] public float TerminalPromptWidth = 420f;
        [Min(16f)] public float TerminalPromptHeight = 32f;
        public float TerminalPromptVerticalOffset = -58f;
        [ColorUsage(false)] public Color HubMetalColor = new Color(0.055f, 0.09f, 0.12f, 1f);
        [ColorUsage(false, true)] public Color HubEnergyColor = new Color(0.02f, 2.2f, 3.8f, 1f);
        [ColorUsage(false)] public Color TerminalBackdropColor = new Color(0.006f, 0.012f, 0.022f, 0.9f);
        [ColorUsage(false)] public Color TerminalShadowColor = new Color(0f, 0f, 0f, 0.58f);
        [ColorUsage(false)] public Color TerminalBorderColor = new Color(0.18f, 0.3f, 0.38f, 0.9f);
        [ColorUsage(false)] public Color TerminalDividerColor = new Color(0.16f, 0.24f, 0.3f, 0.9f);
        [ColorUsage(false)] public Color TerminalPanelColor = new Color(0.018f, 0.035f, 0.055f, 0.98f);
        [ColorUsage(false)] public Color TerminalCardColor = new Color(0.045f, 0.072f, 0.095f, 1f);
        [ColorUsage(false)] public Color TerminalCardHoverColor = new Color(0.085f, 0.14f, 0.18f, 1f);
        [ColorUsage(false)] public Color TerminalAccentColor = new Color(1f, 0.58f, 0.12f, 1f);
        [ColorUsage(false)] public Color TerminalTextColor = new Color(0.9f, 0.96f, 1f, 1f);
        [ColorUsage(false)] public Color TerminalMutedTextColor = new Color(0.55f, 0.65f, 0.72f, 1f);
        [ColorUsage(false)] public Color TerminalUnknownColor = new Color(0.72f, 0.38f, 1f, 1f);
        [ColorUsage(false)] public Color TerminalHighValueColor = new Color(1f, 0.73f, 0.16f, 1f);
        [ColorUsage(false)] public Color TerminalMultiDropColor = new Color(0.12f, 0.85f, 0.8f, 1f);
        [ColorUsage(false)] public Color TerminalDangerColor = new Color(1f, 0.28f, 0.18f, 1f);
    }

    [System.Serializable]
    public sealed class LandmarkBoxColliderTuning
    {
        public Vector3 Center;
        public Vector3 Size = Vector3.one;
        public Vector3 EulerAngles;
    }

    [System.Serializable]
    public sealed class LandmarkSystemTuning
    {
        public bool Enabled = true;
        [Min(40f)] public float PlacementCellSize = 190f;
        [Range(1, 5)] public int ActiveCellRadius = 2;
        [Min(0.1f)] public float RefreshInterval = 0.55f;
        [Range(0f, 1f)] public float CommonCellChance = 0.42f;
        [Range(0f, 1f)] public float StandardCellChance = 0.22f;
        [Tooltip("Share of common and standard landmark selections that become DC-10 wrecks.")]
        [Range(0f, 1f)] public float CrashedCarrierSelectionChance = 0.08f;
        [Range(0f, 1f)] public float RareCellChance = 0.035f;
        [Tooltip("Chance that a cell proposes a region-defining landmark before other rarity tiers are evaluated.")]
        [Range(0f, 1f)] public float RegionDefiningCellChance;
        [Min(10f)] public float StandardMinimumSpacing = 310f;
        [Min(10f)] public float RareMinimumSpacing = 950f;
        [Min(0f)] public float SmallMediumLandmarkExclusionRadius;
        [Min(0f)] public float LargeLandmarkExclusionRadius;
        [Min(0f)] public float RegionDefiningExclusionRadius;
        [Tooltip("Maximum number of procedural placement cells searched from each planned contract stop when selecting an existing world landmark.")]
        [Range(1, 64)] public int ContractLandmarkSearchRadius = 16;
        [Tooltip("Large landmark archetypes selected for rare procedural cells.")]
        public DuneLandmarkType[] RareLandmarkTypes;
        [Tooltip("Mega landmark archetypes selected for region-defining procedural cells.")]
        public DuneLandmarkType[] RegionDefiningLandmarkTypes;
        [Min(0f)] public float HubExclusionRadius = 170f;
        [Range(0f, 50f)] public float MaximumPlacementSlope = 19f;
        [Min(0.1f)] public float RelayScale = 1f;
        [Min(0.1f)] public float CarrierScale = 1.15f;
        [Min(0.1f)] public float BeaconScale = 1f;
        [Min(0.1f)] public float SpireScale = 1.3f;
        [Min(0.1f)] public float ExcavationScale = 1.1f;
        [Min(4f)] public float RelayAntennaHeight = 42f;
        [Min(4f)] public float CarrierLength = 54f;
        [Min(4f)] public float BeaconHeight = 38f;
        [Min(8f)] public float SpireHeight = 96f;
        [Min(4f)] public float ExcavationCraneHeight = 34f;
        [Min(0f)] public float ContractSocketHeight = 5f;
        [Tooltip("Additional horizontal distance between a landmark and its pickup package and ring.")]
        [Min(0f)] public float PickupRingLandmarkClearance = 6f;
        [Tooltip("Vertical air gap between the landmark's highest rendered point and its delivery ring.")]
        [Min(0f)] public float DeliveryRingClearance = 8f;
        [Min(0f)] public float EncounterSocketHeight = 22f;
        [Min(0f)] public float FlightSocketHeight = 18f;

        [Header("Landmark Materials")]
        [ColorUsage(false)] public Color LandmarkStoneColor;
        [ColorUsage(false)] public Color LandmarkMetalColor;
        [ColorUsage(false)] public Color LandmarkSecondaryColor;
        [ColorUsage(false)] public Color LandmarkInteriorColor;
        [ColorUsage(false)] public Color LandmarkAccentColor;
        [ColorUsage(false, true)] public Color LandmarkAccentEmission;
        [Range(0f, 1f)] public float LandmarkStoneSmoothness;
        [Range(0f, 1f)] public float LandmarkMetalSmoothness;
        [Range(0f, 1f)] public float LandmarkMetallic;

        [Header("Landmark Contract Sockets")]
        [Tooltip("Objective socket offset for the ruins prefab used by the Relay Station landmark.")]
        public Vector3 RelayContractSocketOffset;
        [Tooltip("Objective socket offset for the DC-10 prefab used by the Crashed Carrier landmark.")]
        public Vector3 CarrierContractSocketOffset;
        [Tooltip("Objective socket offset for the desert obelisk prefab used by the Excavation landmark.")]
        public Vector3 ExcavationContractSocketOffset;
        [Tooltip("Objective socket offsets for the large and region-defining landmark compositions.")]
        public Vector3 OrbitalContractSocketOffset;
        public Vector3 MegagateContractSocketOffset;
        public Vector3 HarvesterContractSocketOffset;
        public Vector3 ArcologyContractSocketOffset;
        public Vector3 SandRingContractSocketOffset;

        [Header("Landmark Presentation")]
        [Range(1, 6)] public int VisualVariantCount = 4;
        [Min(0f)] public float DishRotationSpeed = 9f;
        [Min(0f)] public float BeaconOrbitSpeed = 16f;
        [Min(0f)] public float BeaconPulseSpeed = 2.6f;
        [Min(0f)] public float BeaconPulseAmount = 0.14f;
        public float SpireRelicRotationSpeed = -18f;
        [Min(0f)] public float SpireRelicFloatAmplitude = 1.8f;
        [Min(0f)] public float SpireRelicFloatSpeed = 1.2f;
        [Range(2, 8)] public int SpireShardCount = 5;
        [Range(2, 8)] public int ExcavationWorkLightCount = 4;
        [Min(0f)] public float ExcavationWorkLightPulseSpeed = 3.2f;
        [Range(0.2f, 1f)] public float LandmarkRingSegmentFill = 0.72f;

        [Header("Ruins Detail")]
        [Tooltip("Optional direct fallback prefab used when the ruins Resources path cannot be resolved. Its authored scale and rotation are preserved.")]
        public GameObject RelayStationPrefab;
        [Tooltip("Primary Resources path used to load the ruins prefab.")]
        public string RelayStationResourcePath = "ruinsPrefab";
        [Tooltip("Samples per axis used to fit the ruins to the lowest dune height beneath its rendered footprint.")]
        [Range(2, 9)] public int RelayGroundingSamplesPerAxis = 5;
        [Tooltip("Fraction of the sampled ruins foundation that must be below the dunes. Lower values keep more of the landmark visible; higher values bury more of its underside.")]
        [Range(0.5f, 1f)] public float RelayGroundingBurialCoverage = 0.8f;
        [Tooltip("Maximum distance adaptive grounding may sink the ruins below the highest placement that keeps their bottom edge at or below the lowest sampled dune. Keep at zero for maximum visibility.")]
        [Min(0f)] public float RelayMaximumAdditionalGroundSink;
        [Tooltip("Additional distance the ruins are sunk below the lowest sampled dune.")]
        [Min(0f)]
        public float RelayPrefabGroundOffsetDown = 1f;
        [Range(6, 24)] public int RelayDishRimSegments = 12;
        [Range(2, 9)] public int RelayWindowCount = 5;
        [Min(0f)] public float RelayWindowSpacing = 1.65f;
        [Min(0.05f)] public float RelayWindowSize = 0.58f;
        [Range(3, 8)] public int RelayMastBraceCount = 4;
        [Min(0.1f)] public float RelayMastBraceRadius = 4.2f;
        [Min(0.1f)] public float RelayMastBraceHeight = 13f;
        [Min(0.05f)] public float RelayMastBraceThickness = 0.22f;

        [Header("DC-10 Detail")]
        [Tooltip("Prefab used for the DC-10 landmark. Its authored transform scale is preserved when instantiated.")]
        public GameObject CrashedCarrierPrefab;
        [Tooltip("Resources path used when the direct crashed carrier prefab reference cannot be resolved.")]
        public string CrashedCarrierResourcePath = "DC-10/DC-10_Prefab";
        [Tooltip("Distance the crashed carrier prefab is sunk below the landmark's ground position.")]
        [Min(0f)] public float CrashedCarrierGroundSink = 2f;
        [Tooltip("Low-poly compound box collider fit for the crashed carrier prefab, in prefab-local coordinates.")]
        public LandmarkBoxColliderTuning[] CrashedCarrierColliderBoxes;
        [Range(1, 6)] public int CarrierEngineCount = 3;
        [Min(0.1f)] public float CarrierEngineRadius = 1.85f;
        [Min(0.1f)] public float CarrierEngineDepth = 3.6f;
        [Range(3, 12)] public int CarrierHullRibCount = 7;
        [Min(0.05f)] public float CarrierHullRibThickness = 0.32f;
        [Range(2, 10)] public int CarrierWreckageCount = 5;
        [Min(0.1f)] public float CarrierCockpitScale = 1f;

        [Header("Raider Beacon Detail")]
        [Range(3, 8)] public int BeaconFoundationArmCount = 3;
        [Range(6, 24)] public int BeaconSignalRingSegments = 14;
        [Min(0.1f)] public float BeaconSignalRingRadius = 7.2f;
        [Min(0.05f)] public float BeaconSignalRingThickness = 0.28f;
        [Range(3, 12)] public int BeaconTowerFinCount = 6;

        [Header("Ancient Spire Detail")]
        [Tooltip("Concrete surface applied to every Ancient Spire material while retaining each part's original color and emission.")]
        public Texture2D SpireConcreteTexture;
        [Tooltip("World-space width and height filled by one complete concrete image before the texture begins repeating on larger Spire parts.")]
        [Min(0.01f)] public float SpireConcreteTileWorldSize = 12f;
        [Range(5, 14)] public int SpireLayerCount = 9;
        [Min(0.02f)] public float SpireSeamHeight = 0.16f;
        [Range(3, 8)] public int SpireMonolithCount = 4;
        [Range(6, 24)] public int SpireBaseRingSegments = 12;
        [Min(1f)] public float SpireBaseRingRadius = 18f;
        [Min(0.05f)] public float SpireBaseRingThickness = 0.38f;

        [Header("Desert Obelisk Detail")]
        [Tooltip("Prefab used in place of the procedural excavation landmark. Its authored scale and rotation are preserved.")]
        public GameObject ExcavationPrefab;
        [Tooltip("Resources path used when the direct excavation prefab reference cannot be resolved.")]
        public string ExcavationResourcePath = "desert_obelisk_Prefab";
        [Tooltip("Samples per axis used to fit the desert obelisk foundation to the lowest dune height beneath its rendered footprint.")]
        [Range(2, 9)] public int ExcavationGroundingSamplesPerAxis = 5;
        [Tooltip("Fraction of sampled desert obelisk underside points that should be buried by adaptive grounding. Floating decorative pieces are ignored by keeping this below full coverage.")]
        [Range(0.5f, 0.95f)] public float ExcavationGroundingBurialCoverage = 0.8f;
        [Tooltip("Maximum distance adaptive grounding may sink a desert obelisk below its legacy footprint placement.")]
        [Min(0f)] public float ExcavationMaximumAdditionalGroundSink = 2f;
        [Range(2, 8)] public int ExcavationScaffoldCount = 4;
        [Range(1, 5)] public int ExcavationPitTerraceCount = 3;
        [Min(4f)] public float ExcavationPitWidth = 32f;
        [Min(4f)] public float ExcavationPitLength = 27f;
        [Min(0.1f)] public float ExcavationTerraceStep = 2.4f;
        [Range(2, 12)] public int ExcavationCraneTrussCount = 7;
        [Range(1, 10)] public int ExcavationCargoStackCount = 5;

        [Header("Fallen Orbital Array Detail")]
        [Tooltip("Concrete surface applied to the orbital dish rim and rectangular impact rubble while retaining their original gray and black colors.")]
        public Texture2D OrbitalConcreteTexture;
        [Tooltip("World-space width and height filled by one complete concrete image before the texture repeats across orbital rim and rubble pieces.")]
        [Min(0.01f)] public float OrbitalConcreteTileWorldSize = 4f;
        [Min(4f)] public float OrbitalDishRadius;
        [Range(8, 48)] public int OrbitalDishSegmentCount;
        [Range(0, 12)] public int OrbitalDishMissingSegmentCount;
        [Range(0f, 89f)] public float OrbitalDishTiltMinimum;
        [Range(0f, 89f)] public float OrbitalDishTiltMaximum;
        [Min(1f)] public float OrbitalMastHeight;
        [Range(0, 8)] public int OrbitalSolarWingCount;
        [Min(1f)] public float OrbitalSolarWingLength;
        [Range(0, 40)] public int OrbitalDebrisCount;
        [Min(0f)] public float OrbitalDebrisSpread;
        [Min(0f)] public float OrbitalBurialDepth;

        [Header("Desert Megagate Detail")]
        [Tooltip("Optional direct fallback prefab used when the Desert Megagate Resources path cannot be resolved. Its authored scale and rotation are preserved.")]
        public GameObject MegagatePrefab;
        [Tooltip("Primary Resources path used to load the Desert Megagate prefab.")]
        public string MegagateResourcePath = "DesertMegagatePrefab";
        [Tooltip("Samples per axis used to ground the Desert Megagate across the dunes.")]
        [Range(2, 9)] public int MegagateGroundingSamplesPerAxis = 5;
        [Tooltip("Adds mesh colliders to Desert Megagate meshes that do not already have colliders.")]
        public bool MegagateGenerateMeshColliders = true;
        [Range(2, 6)] public int MegagatePylonCount;
        [Min(8f)] public float MegagatePylonHeight;
        [Min(2f)] public float MegagatePylonWidth;
        [Min(4f)] public float MegagateOpeningWidth;
        [Range(0f, 0.9f)] public float MegagateTaper;
        [Range(0, 12)] public int MegagateBridgeFragmentCount;
        [Range(0, 20)] public int MegagateBaseRuinCount;
        [Range(0, 40)] public int MegagateDebrisCount;
        [Min(0f)] public float MegagateBurialDepth;
        [Tooltip("Generated albedo texture used by the megagate pylons and collapsed stonework.")]
        public Texture2D MegagateStoneTexture;
        [Tooltip("Generated albedo texture used by the megagate armor, bands, and bridge fragments.")]
        public Texture2D MegagateMetalTexture;
        [Tooltip("Generated albedo and emission texture used by the megagate signal strips.")]
        public Texture2D MegagateSignalTexture;
        [ColorUsage(false)] public Color MegagateStoneTextureTint;
        [Tooltip("HDR multiplier used to match the generated armor albedo under scene lighting.")]
        [ColorUsage(false, true)] public Color MegagateMetalTextureTint;
        [ColorUsage(false)] public Color MegagateSignalTextureTint;
        [Range(0f, 1f)] public float MegagateMetalSmoothness;
        [Range(0f, 1f)] public float MegagateMetallic;
        [Tooltip("Subtle texture-masked fill light that keeps the gray armor readable in deep desert shadows.")]
        [ColorUsage(false, true)] public Color MegagateMetalEmission;
        [Tooltip("Stone texture repetitions per meter on megagate geometry.")]
        [Min(0.001f)] public float MegagateStoneTextureTiling;
        [Tooltip("Metal texture repetitions per meter on megagate geometry.")]
        [Min(0.001f)] public float MegagateMetalTextureTiling;
        [Tooltip("Signal texture repetitions per meter on megagate geometry.")]
        [Min(0.001f)] public float MegagateSignalTextureTiling;
        [Tooltip("Local-space center of the megagate silhouette used by the photography camera frame.")]
        public Vector3 MegagatePhotographyBoundsCenter;
        [Tooltip("Local-space size of the megagate silhouette used by the photography camera frame. Set any axis to zero to use renderer bounds.")]
        public Vector3 MegagatePhotographyBoundsSize;

        [Header("Wind Harvester Graveyard Detail")]
        [Tooltip("Optional direct fallback prefab used when the turbine Resources path cannot be resolved. Its authored scale and rotation are preserved.")]
        public GameObject HarvesterPrefab;
        [Tooltip("Primary Resources path used to load the turbine prefab.")]
        public string HarvesterResourcePath = "turbine/turbinePrefab";
        [Tooltip("Name of the turbine child rotated independently for blade variation.")]
        public string HarvesterWingsTransformName = "Wings";
        [Range(1, 30)] public int HarvesterCount;
        [Min(2f)] public float HarvesterRingRadius;
        [Min(0.1f)] public float HarvesterRingThickness;
        [Range(8, 36)] public int HarvesterRingSegmentCount;
        [Min(4f)] public float HarvesterTowerHeight;
        [Min(4f)] public float HarvesterSpacing;
        [Range(0f, 1f)] public float HarvesterBrokenChance;
        [Range(0f, 1f)] public float HarvesterLeanChance;
        [Range(0f, 1f)] public float HarvesterFallenChance;
        [Min(0f)] public float HarvesterPrefabGroundSink = 1.5f;
        [Min(0f)] public float HarvesterPrefabFallenGroundSink = 3f;
        [Range(0f, 89f)] public float HarvesterPrefabLeanMinimumAngle = 5f;
        [Range(0f, 89f)] public float HarvesterPrefabLeanMaximumAngle = 13f;
        [Range(0f, 90f)] public float HarvesterPrefabFallenMinimumAngle = 68f;
        [Range(0f, 90f)] public float HarvesterPrefabFallenMaximumAngle = 86f;
        [Range(0f, 359f)] public float HarvesterWingsMinimumZRotation;
        [Range(0f, 359f)] public float HarvesterWingsMaximumZRotation = 359f;
        [Range(0, 60)] public int HarvesterDebrisCount;
        [Min(8f)] public float HarvesterFieldRadius;

        [Header("Desert Shop Detail")]
        [Tooltip("Prefab used for the desert shop landmark. Its authored scale and rotation are preserved.")]
        public GameObject BuriedArcologyPrefab;
        [Tooltip("Resources path used when the direct buried arcology prefab reference cannot be resolved.")]
        public string BuriedArcologyResourcePath = "desert_shop_Prefab";
        [Tooltip("Samples per axis used to fit the desert shop foundation to the lowest dune height beneath its rendered footprint.")]
        [Range(2, 9)] public int BuriedArcologyGroundingSamplesPerAxis = 5;
        [Min(8f)] public float ArcologyCoreRadius;
        [Min(8f)] public float ArcologyCoreHeight;
        [Range(0.5f, 0.95f)] public float ArcologyBurialRatio;
        [Range(1, 16)] public int ArcologyRoofClusterCount;
        [Min(8f)] public float ArcologyRoofClusterRadius;
        [Range(0, 20)] public int ArcologyVentTowerCount;
        [Range(0, 24)] public int ArcologyStructuralRibCount;
        [Range(0, 40)] public int ArcologyExposedWindowCount;
        [Range(0, 50)] public int ArcologyDebrisCount;

        [Header("Sand Ring Detail")]
        [Tooltip("Optional direct fallback prefab used when the ruined rings Resources path cannot be resolved. Its authored scale and rotation are preserved.")]
        public GameObject SandRingPrefab;
        [Tooltip("Primary Resources path used to load the ruined rings prefab.")]
        public string SandRingResourcePath = "RuinedRingsPrefab";
        [Tooltip("Samples per axis used to ground the ruined rings across the dunes.")]
        [Range(2, 9)] public int SandRingGroundingSamplesPerAxis = 5;
        [Min(4f)] public float SandRingRadius;
        [Range(12, 64)] public int SandRingSegmentCount;
        [Min(0.2f)] public float SandRingThickness;
        [Min(0f)] public float SandRingBurialDepth;
        [Range(0, 20)] public int SandRingMissingSegmentCount;
        [Range(0, 16)] public int SandRingSupportCount;
        [Min(1f)] public float SandRingSupportRadius;
        [Range(0, 50)] public int SandRingDebrisCount;
        [Min(0f)] public float SandRingDebrisSpread;
        [Range(-35f, 35f)] public float SandRingTilt;
        [Tooltip("Local-space center of the sand ring silhouette used by the photography camera frame.")]
        public Vector3 SandRingPhotographyBoundsCenter;
        [Tooltip("Local-space size of the sand ring silhouette used by the photography camera frame. Set any axis to zero to use renderer bounds.")]
        public Vector3 SandRingPhotographyBoundsSize;
    }

    [System.Serializable]
    public sealed class ProceduralBuildingSystemTuning
    {
        public bool Enabled = true;
        [Tooltip("Resources-relative folder containing every building prefab used by the procedural system.")]
        public string ResourceFolder = "buildings";
        [Tooltip("Density multiplier for procedural buildings. Zero disables placement; values above one allow multiple buildings per cell.")]
        [Range(0f, 4f)] public float AmountMultiplier = 1f;
        [Tooltip("Size of the coarse logical-world placement grid. Larger values spread buildings farther apart.")]
        [Min(100f)] public float PlacementCellSize = 300f;
        [Range(1, 5)] public int ActiveCellRadius = 3;
        [Tooltip("Expected building count per cell before applying Amount Multiplier.")]
        [Range(0f, 1f)] public float BaseCellAmount = 0.8f;
        [Tooltip("Keeps placements away from cell edges so neighboring cells remain visibly separated.")]
        [Range(0.05f, 0.45f)] public float CellInsetFraction = 0.25f;
        [Min(0f)] public float HubExclusionRadius = 100f;
        [Tooltip("Extra horizontal clearance around each geoglyph's visible authored footprint.")]
        [Min(0f)] public float GeoglyphClearance = 35f;
        [Tooltip("Extra horizontal clearance between a building's measured prefab footprint and every landmark exclusion footprint.")]
        [Min(0f)] public float LandmarkClearance = 20f;
        [Tooltip("Extra horizontal clearance between a building's measured prefab footprint and the space every already-placed portal reserves.")]
        [Min(0f)] public float PortalClearance = 6f;
        [Range(0f, 50f)] public float MaximumPlacementSlope = 35f;
        [Tooltip("Alternative deterministic positions tried when a candidate lands inside an exclusion zone or on an excessive slope.")]
        [Range(1, 8)] public int PlacementAttemptsPerBuilding = 4;
        [Tooltip("Terrain samples taken across each rotated building footprint before sinking it into the dunes.")]
        [Range(2, 9)] public int GroundingSamplesPerAxis = 5;
        [Tooltip("Additional vertical sink after footprint grounding.")]
        [Min(0f)] public float GroundOffsetDown = 0.5f;
        [Tooltip("Adds static mesh colliders to prefab meshes that do not already have colliders.")]
        public bool GenerateMeshColliders = true;
        [Min(0.1f)] public float RefreshInterval = 0.8f;
        [Header("GPU Instancing")]
        [Tooltip("Draws every placed building through GPU instancing instead of spawning a renderer GameObject per building. Colliders are still spawned when Generate Mesh Colliders is on.")]
        public bool GpuInstancingEnabled = true;
        [Tooltip("Instances submitted per RenderMeshInstanced call. Unity's ceiling is 1023, but the practical limit is lower once per-instance data grows.")]
        [Range(1, 1023)] public int MaxInstancesPerDraw = 500;
        [Tooltip("Lets instanced buildings sample light probes. Off is cheaper and matches the baked-free desert lighting.")]
        public bool InstancedLightProbes = false;
        [Tooltip("Lets instanced buildings sample reflection probes. Off is cheaper.")]
        public bool InstancedReflectionProbes = false;
        [Tooltip("Also instantiates the real prefab beside every instanced building so scale, rotation and grounding can be compared directly.")]
        public bool GpuInstancingDebugCompare = false;
        [Tooltip("World offset applied to the debug comparison prefab. Zero overlaps it exactly with the instanced draw.")]
        public Vector3 GpuInstancingDebugCompareOffset = new Vector3(0f, 0f, 0f);
        [Header("Hue Variation")]
        [Tooltip("Tints each placed building with a deterministically chosen hue from the palette below.")]
        public bool HueVariationEnabled = true;
        [Tooltip("How far each building's albedo is pushed toward its chosen palette hue. Zero leaves the prefab materials untouched.")]
        [Range(0f, 1f)] public float HueVariationStrength = 0.75f;
        [Tooltip("Palette the buildings are tinted with. Keep this list short so buildings sharing a hue still batch together.")]
        public Color[] HueTints =
        {
            new Color(1.00f, 0.93f, 0.82f),
            new Color(0.95f, 0.76f, 0.58f),
            new Color(0.85f, 0.58f, 0.50f),
            new Color(0.66f, 0.72f, 0.74f),
            new Color(0.78f, 0.80f, 0.60f),
            new Color(0.63f, 0.62f, 0.76f),
            new Color(0.52f, 0.70f, 0.68f),
            new Color(0.88f, 0.70f, 0.78f),
        };
    }

    [System.Serializable]
    public sealed class RouteEncounterTuning
    {
        public bool Enabled = true;
        [Min(0.1f)] public float MinimumEncounterInterval = 28f;
        [Min(0.1f)] public float MaximumEncounterInterval = 52f;
        [Min(0.1f)] public float HighValueIntervalMultiplier = 0.52f;
        [Header("High-Value Contracts")]
        [Min(0f)] public float HighValueInitialEncounterDelay = 3f;
        [Min(0f)] public float HighValueMinimumObjectiveDistance = 60f;
        [Range(0, 6)] public int HighValueFormationSizeBonus = 2;
        [Min(0.1f)] public float HighValueEnemySpeedMultiplier = 1.18f;
        [Min(0.1f)] public float HighValueEnemyHealthMultiplier = 1.25f;
        [Min(0f)] public float HighValueDamageMultiplier = 1.35f;
        [Min(0.1f)] public float HighValueShotIntervalMultiplier = 0.78f;
        [Range(0f, 1f)] public float HighValueSecondPassChanceBonus = 0.25f;
        [Header("High-Value World Threats")]
        [Range(0, 12)] public int HighValueGroundEnemyBonus = 4;
        [Min(0f)] public float HighValueGroundEnemyMinimumSpawnDistance = 35f;
        [Min(0f)] public float HighValueGroundEnemyMaximumSpawnDistance = 80f;
        [Range(0, 8)] public int HighValueStormPyramidBonus = 2;
        [Min(20f)] public float MinimumObjectiveDistance = 180f;
        [Min(10f)] public float EncounterVolumeRadius = 90f;
        [Range(1, 5)] public int VolumesPerRouteLeg = 2;
        [Range(2, 10)] public int MinimumFormationSize = 3;
        [Range(2, 12)] public int MaximumFormationSize = 6;
        [Min(10f)] public float SpawnDistance = 125f;
        [Min(1f)] public float FormationSpacing = 16f;
        [Header("Formation Choreography")]
        [Min(0f)] public float FormationCommitDistance = 28f;
        [Min(0.1f)] public float FormationCommitRadius = 5f;
        [Min(0f)] public float FormationAltitudeStagger = 4f;
        [Range(0f, 1f)] public float FormationPlayerAltitudeContribution = 0.35f;
        [Range(0f, 1f)] public float FormationApproachAltitudeBlend = 0.48f;
        [Range(0f, 1f)] public float FormationApproachLateralCompression = 0.65f;
        [Range(0f, 1f)] public float FormationDepthCommitContribution = 0.4f;
        [Min(0f)] public float CrossAttackExitDistanceMultiplier = 0.55f;
        [Range(0f, 1f)] public float VerticalApproachHeightMultiplier = 0.45f;
        [Range(0f, 1f)] public float FormationLowerBreakMultiplier = 0.42f;
        [Range(0f, 1f)] public float FormationObjectiveDirectionWeight = 0.35f;
        [Min(0f)] public float HeadOnWingDepthSpacing = 9f;
        [Min(0f)] public float HeadOnPassLateralSpacing = 5f;
        [Min(0f)] public float CrossAttackLaneSpacing = 11f;
        [Min(0f)] public float PursuitWingDepthSpacing = 7f;
        [Min(0f)] public float PursuitOvertakeDistance = 34f;
        [Range(0.1f, 2f)] public float VerticalFormationWidthMultiplier = 0.7f;
        [Min(0f)] public float FlyThroughFormationDepthSpacing = 24f;
        [Min(0f)] public float FormationBreakVerticalSeparation = 12f;
        [Min(0f)] public float FormationRepositionHeight = 10f;
        [Min(0f)] public float LowAltitude = 9f;
        [Min(0f)] public float MediumAltitude = 24f;
        [Min(0f)] public float HighAltitude = 46f;
        [Min(1f)] public float ApproachSpeed = 48f;
        [Min(1f)] public float AttackPassSpeed = 68f;
        [Min(1f)] public float BreakSpeed = 58f;
        [Min(1f)] public float TurnSharpness = 5.5f;
        [Min(1f)] public float BreakOffDistance = 210f;
        [Min(0f)] public float RepositionDelay = 1.2f;
        [Range(0, 3)] public int MaximumAttackPasses = 2;
        [Min(1f)] public float EnemyHealth = 55f;
        [Min(0)] public int EnemyGoldReward = 12;
        [Min(0.1f)] public float EnemyVisualScale = 1.25f;
        [Min(0f)] public float ContactDamage = 12f;
        public string ContactDeathMessage = "Destroyed by a Sky Piercer collision.";
        [Min(0.1f)] public float ContactRadius = 2.4f;
        [Min(0f)] public float ShotDamage = 7f;
        public string ShotDeathMessage = "Destroyed by a Sky Piercer shot.";
        [Min(0.1f)] public float ShotInterval = 1.1f;
        [Min(0.1f)] public float ShotTelegraphDuration = 0.22f;
        [Min(0.1f)] public float ShotHitRadius = 2.2f;
        [Min(0.05f)] public float ShotVisualDuration = 0.16f;
        [Min(0.01f)] public float ShotStartWidth = 0.1f;
        [Min(0.01f)] public float ShotEndWidth = 0.025f;
        [Range(0f, 1f)] public float SecondPassChance = 0.62f;
        [ColorUsage(false, true)] public Color FormationEmission = new Color(3.8f, 0.16f, 0.05f, 1f);
        [ColorUsage(false, true)] public Color ShotEmission = new Color(4.5f, 0.35f, 0.06f, 1f);

        [Header("Encounter Presentation")]
        [Min(0f)] public float WaveAnnouncementDuration = 2.2f;
        [Min(0f)] public float WaveAnnouncementTop = 142f;
        [Min(10)] public int WaveAnnouncementFontSize = 18;
        [ColorUsage(false)] public Color WaveAnnouncementColor = new Color(1f, 0.35f, 0.12f, 1f);
        [Min(0f)] public float EnemyTrailDuration = 0.42f;
        [Min(0f)] public float EnemyTrailStartWidth = 0.32f;
        [Min(0f)] public float EnemyTrailEndWidth = 0.02f;
        [Min(0f)] public float EnemyTrailMinimumVertexDistance = 0.08f;
        [Min(0f)] public float TelegraphPulseSpeed = 18f;
        [Min(0f)] public float TelegraphMinimumWidthMultiplier = 0.35f;
        [Min(0f)] public float FlyThroughGuideDuration = 5.5f;
        [Range(2, 8)] public int FlyThroughGuideGateCount = 4;
        [Min(0f)] public float FlyThroughGuideGateSpacing = 28f;
        [Min(0f)] public float FlyThroughGuideGateRadius = 6.5f;
        [Min(0f)] public float FlyThroughGuideGateThickness = 0.18f;
        [Min(0f)] public float FlyThroughGuidePulseSpeed = 3.6f;
        [Min(0f)] public float FlyThroughGuidePulseAmount = 0.1f;
    }

    [System.Serializable]
    public sealed class PyramidTuning
    {
        [Min(0f)] public float DensityPerChunk = 0.22f;
        [Tooltip("Minimum generated pyramid footprint half-width in world meters.")]
        [Min(0.1f)] public float MinimumScale = 2f;
        [Tooltip("Maximum generated pyramid footprint half-width in world meters.")]
        [Min(0.1f)] public float MaximumScale = 4.4f;
        [Range(0f, 89f)] public float MaximumPlacementSlope = 24f;
        [Min(0f)] public float MinimumBurialDepth = 0.75f;
        [Min(0f)] public float MaximumBurialDepth = 1.25f;

        [Header("GPU-Instanced LOD")]
        [Tooltip("Maximum camera distance for the highest-detail imported pyramid mesh.")]
        [Min(0.1f)] public float Lod1MaximumDistance = 120f;
        [Tooltip("Maximum camera distance for the second imported pyramid LOD.")]
        [Min(0.1f)] public float Lod2MaximumDistance = 240f;
        [Tooltip("Maximum camera distance for the third imported pyramid LOD.")]
        [Min(0.1f)] public float Lod3MaximumDistance = 420f;
        [Tooltip("Maximum camera distance for the lowest-detail imported pyramid mesh. Pyramids are culled beyond this distance.")]
        [Min(0.1f)] public float Lod4MaximumDistance = 700f;
    }

    [System.Serializable]
    public sealed class CactusTuning
    {
        [Header("Distribution")]
        [Tooltip("Expected cactus count per terrain chunk before biome and placement rejection.")]
        [Min(0f)] public float DensityPerChunk = 5.5f;
        [Range(0f, 89f)] public float MaximumPlacementSlope = 38f;
        [Min(0f)] public float BurialDepth = 0.18f;

        [Header("Overall Size")]
        [Min(0.1f)] public float MinimumHeight = 2.6f;
        [Min(0.1f)] public float MaximumHeight = 5.8f;
        [Min(0.05f)] public float MinimumThickness = 0.42f;
        [Min(0.05f)] public float MaximumThickness = 0.72f;
        [Range(0f, 15f)] public float MaximumLeanDegrees = 4f;
        [Range(0.4f, 1f)] public float TrunkTipScale = 0.82f;

        [Header("Arms")]
        [Range(0, 4)] public int MinimumArmCount = 1;
        [Range(0, 5)] public int MaximumArmCount = 3;
        public Vector2 ArmAttachmentHeightRange = new Vector2(0.36f, 0.7f);
        public Vector2 ArmReachInThicknesses = new Vector2(2.2f, 3.8f);
        public Vector2 ArmRiseAsHeight = new Vector2(0.22f, 0.4f);
        [Range(0.2f, 1f)] public float ArmThicknessMultiplier = 0.68f;
        [Range(0.4f, 1f)] public float ArmTipScale = 0.88f;
        [Range(-0.25f, 0.5f)] public float ArmShoulderLift = 0.12f;
        [Range(-0.25f, 0.5f)] public float ArmOutwardLean = 0.08f;
        [Range(0f, 90f)] public float ArmAzimuthJitter = 28f;

        [Header("Ribbed Surface")]
        [Range(3, 12)] public int RibCount = 7;
        [Range(4, 12)] public int HeightSegments = 10;
        [Range(0f, 0.35f)] public float RibDepth = 0.14f;
        [Range(0.1f, 0.45f)] public float RoundedCapLength = 0.25f;
        [Range(0.5f, 1.25f)] public float ArmJointScale = 0.92f;

        [Header("Appearance")]
        [ColorUsage(false)] public Color BodyColor = new Color(0.07f, 0.33f, 0.16f, 1f);
        [Range(0f, 1f)] public float Smoothness = 0.2f;
        [Range(0f, 1f)] public float BlossomChance = 0.18f;
        [Range(0.05f, 1f)] public float BlossomSizeInThicknesses = 0.34f;
        [Range(0.1f, 1f)] public float BlossomHeightScale = 0.48f;
        [Range(0f, 1f)] public float BlossomLiftInSizes = 0.25f;
        [ColorUsage(false)] public Color BlossomColor = new Color(1f, 0.48f, 0.16f, 1f);
        [Range(0f, 1f)] public float BlossomSmoothness = 0.28f;
    }

    [System.Serializable]
    public sealed class DesertShrubTuning
    {
        public bool Enabled = true;

        [Header("Distribution")]
        [Tooltip("Resources folder containing the authored shrub patch prefabs.")]
        public string PatchResourcePath = "shrubs";
        [Tooltip("Target patch population before slope, spacing, biome, and exclusion rejection.")]
        [Min(0f)] public float DensityPerChunk = 10f;
        [Min(8f)] public float ClusterCellSize = 62f;
        [Range(0f, 1f)] public float ClusterChance = 0.48f;
        [Range(1, 32)] public int MinimumClusterSize = 5;
        [Range(1, 48)] public int MaximumClusterSize = 15;
        [Min(0.1f)] public float ClusterRadius = 15f;
        [Min(0f)] public float MinimumSpacing = 1.7f;

        [Header("Biome Weighting")]
        [Min(1f)] public float BiomeNoiseScale = 230f;
        [Range(-1f, 1f)] public float MinimumBiomeNoise = -0.18f;
        [Range(-1f, 1f)] public float FullDensityBiomeNoise = 0.38f;
        [Range(0.1f, 5f)] public float BiomeWeightPower = 1.65f;
        [Range(0f, 1f)] public float MinimumRegionWeight = 0.08f;

        [Header("Surface Placement")]
        [Range(0f, 89f)] public float MaximumSlope = 27f;
        [Min(0.05f)] public float MinimumScale = 0.72f;
        [Min(0.05f)] public float MaximumScale = 1.35f;
        [Min(0f)] public float MinimumBurialDepth = 0.05f;
        [Min(0f)] public float MaximumBurialDepth = 0.18f;
        [Range(0f, 1f)] public float SurfaceAlignment = 0.42f;

        [Header("Exclusions")]
        [Min(0f)] public float GameplayExclusionRadius = 10f;
        [Min(0f)] public float HubExclusionRadius = 38f;
        [Min(0f)] public float LandmarkExclusionRadius = 42f;
        [Min(0f)] public float SceneryExclusionRadius = 4f;

        [Header("Rendering")]
        [Min(1f)] public float CullDistance = 215f;
        public bool CastShadows = true;
        public bool ReceiveShadows = true;

        public void EnsureInitialized()
        {
            PatchResourcePath ??= string.Empty;
        }
    }

    [System.Serializable]
    public sealed class RuntimePerformanceTuning
    {
        [Tooltip("Vertical-sync interval. Use zero for uncapped high-refresh rendering.")]
        [Range(0, 4)] public int VSyncCount;

        [Tooltip("Requested player frame-rate cap. Use -1 for the platform default.")]
        [Range(-1, 360)] public int TargetFrameRate = 165;

        [Tooltip("Keep development builds running at full speed while another window, such as the Unity Profiler, has focus.")]
        public bool RunDevelopmentBuildsInBackground = true;
    }

    [System.Serializable]
    public sealed class WorldStreamingTuning
    {
        [Tooltip("Chunk radius kept active around the player.")]
        [Range(1, 14)] public int ActiveRadius = 3;
        [Tooltip("Chunk radius generated ahead of the player.")]
        [Range(1, 9)] public int PreloadRadius = 3;
        [Tooltip("Chunks beyond this radius are removed.")]
        [Range(2, 12)] public int UnloadRadius = 4;
        [Tooltip("How often the desired visual terrain set is refreshed.")]
        [Min(0.05f)] public float RefreshInterval = 0.18f;
        [Tooltip("Maximum terrain chunks generated during one frame.")]
        [Range(1, 4)] public int ChunksGeneratedPerFrame = 1;
        [Tooltip("Main-thread time budget for queued chunk work. A single indivisible stage may exceed this budget.")]
        [Range(0.25f, 8f)] public float GenerationTimeBudgetMilliseconds = 1.25f;
        [Header("Camera Frustum Terrain")]
        [Tooltip("Stream terrain-only chunks intersecting the aerial camera frustum without enabling their gameplay content or collision.")]
        public bool EnableCameraFrustumTerrainStreaming = true;
        [Tooltip("Camera height above the procedural terrain at which frustum terrain streaming begins.")]
        [Min(0f)] public float CameraFrustumMinimumAltitude = 18f;
        [Tooltip("Camera height above terrain at which the maximum frustum terrain distance is reached.")]
        [Min(0.01f)] public float CameraFrustumFullDistanceAltitude = 140f;
        [Tooltip("Frustum terrain distance when aerial streaming first activates.")]
        [Min(1f)] public float CameraFrustumMinimumDistance = 480f;
        [Tooltip("Maximum distance of terrain-only camera-frustum coverage.")]
        [Min(1f)] public float CameraFrustumMaximumDistance = 1200f;
        [Tooltip("Extra chunk widths included around the visible frustum to hide generation during camera movement.")]
        [Range(0, 3)] public int CameraFrustumPaddingChunks = 1;
        [Tooltip("Additional chunk widths retained outside the generation frustum to prevent churn while the camera turns.")]
        [Range(0, 4)] public int CameraFrustumUnloadPaddingChunks = 1;
        [Tooltip("Vertical padding applied to sampled terrain chunk bounds during frustum intersection tests.")]
        [Min(0f)] public float CameraFrustumTerrainHeightPadding = 24f;
        [Tooltip("Maximum terrain-only chunks selected by camera-frustum streaming. Player-radius chunks are not included in this limit.")]
        [Range(16, 512)] public int MaximumCameraFrustumTerrainChunks = 192;
        [Tooltip("Seconds of current planar velocity used to preload collision terrain ahead of the drone.")]
        [Range(0f, 5f)] public float CollisionPredictionSeconds = 2.5f;
        [Tooltip("Chunk radius around the predicted flight position prepared for collision.")]
        [Range(0, 2)] public int CollisionPreloadRadius = 1;
        [Tooltip("Chunk radius around the player that keeps terrain colliders enabled.")]
        [Range(1, 4)] public int CollisionActiveRadius = 2;
        [Tooltip("Chunk radius around the player in which streamed rings and enemies are simulated.")]
        [Range(1, 4)] public int SimulationRadius = 3;
        [Tooltip("Resolution of the terrain collision mesh. Keep lower than the visual terrain mesh to reduce cooking cost.")]
        [Range(8, 64)] public int CollisionMeshResolution = 24;
        [Tooltip("Local distance at which the world recenters around the drone.")]
        [Min(50f)] public float FloatingOriginThreshold = 520f;
    }

    [System.Serializable]
    public sealed class RendererFrustumCullingTuning
    {
        [Tooltip("Disable rendering for scene renderers outside the gameplay camera's padded frustum.")]
        public bool Enabled = true;

        [Tooltip("World-space distance added beyond every frustum plane so camera movement reveals objects before they enter view.")]
        [Min(0f)] public float Padding = 30f;

        [Tooltip("How often newly spawned renderers are added to the culling set. Tracked renderers are culled every frame.")]
        [Min(0.05f)] public float RendererRefreshInterval = 0.5f;
    }

    [System.Serializable]
    public sealed class SpatialGpuInstancingTuning
    {
        [Tooltip("Use spatially bounded Graphics.RenderMeshInstanced batches for supported procedural visuals.")]
        public bool Enabled = true;

        [Tooltip("World-space width and depth of an instance culling cell.")]
        [Min(8f)] public float CellSizeMeters = 32f;

        [Tooltip("Maximum instances submitted by one RenderMeshInstanced call. Kept below Unity's theoretical limit for reliable custom instance data.")]
        [Range(1, 1023)] public int MaximumInstancesPerDraw = 500;

        [Tooltip("Minimum time between static distance-LOD batch rebuilds.")]
        [Range(0.02f, 1f)] public float LodRefreshInterval = 0.12f;

        [Tooltip("Camera movement required before static distance-LOD batches are rebuilt.")]
        [Range(0.1f, 32f)] public float LodCameraMovementThreshold = 4f;

        [Tooltip("Keep one captured source renderer visible and offset its instanced copy for visual transform comparison.")]
        public bool EnableDebugComparison;

        [Tooltip("World-space offset applied to the instanced side of the optional source-versus-instance comparison.")]
        public Vector3 DebugComparisonOffset = new Vector3(2f, 0f, 0f);
    }

    [System.Serializable]
    public sealed class PlayerHealthTuning
    {
        [Header("Debug")]
        [Tooltip("Starts the player with infinite health. This can also be changed at runtime from the F1 telemetry panel.")]
        public bool DebugInfiniteHealth;

        [Header("Health")]
        [Min(1f)] public float MaximumHealth = 100f;
        [Min(0f)] public float DamageInvulnerability = 0.45f;
    }

    [System.Serializable]
    public sealed class GameOverScreenTuning
    {
        [Header("Copy")]
        public string Eyebrow = "DUNE VECTOR  //  RECOVERY PROTOCOL";
        public string Title = "SIGNAL LOST";
        public string Subtitle = "COURIER UNIT OFFLINE";
        public string RestartButtonLabel;
        public string QuitButtonLabel = "END SESSION";
        public string FooterHint = "ENTER  /  INITIATE RECOVERY";

        [Header("Responsive Layout")]
        [Min(320f)] public float ReferenceWidth = 1920f;
        [Min(240f)] public float ReferenceHeight = 1080f;
        [Range(0.5f, 2f)] public float MinimumScale = 0.68f;
        [Range(0.5f, 2f)] public float MaximumScale = 1.2f;
        [Min(320f)] public float PanelWidth = 680f;
        [Min(360f)] public float PanelHeight = 500f;
        [Min(8f)] public float ScreenMargin = 28f;
        [Min(16f)] public float PanelPadding = 42f;
        [Min(0f)] public float ShadowOffset = 14f;
        [Min(1f)] public float BorderThickness = 1f;
        [Min(1f)] public float AccentBarHeight = 4f;
        [Min(2f)] public float CornerLength = 24f;
        [Min(1f)] public float CornerThickness = 2f;

        [Header("Content Rhythm")]
        [Min(8f)] public float EyebrowHeight = 18f;
        [Min(0f)] public float HeaderGap = 8f;
        [Min(24f)] public float TitleHeight = 66f;
        [Min(8f)] public float SubtitleHeight = 24f;
        [Min(0f)] public float SectionGap = 22f;
        [Min(36f)] public float EliminationHeight = 72f;
        [Min(0f)] public float ActionGap = 30f;
        [Min(32f)] public float PrimaryButtonHeight = 58f;
        [Min(28f)] public float SecondaryButtonHeight = 48f;
        [Min(0f)] public float ButtonGap = 12f;
        [Min(1f)] public float ButtonEdgeWidth = 4f;
        [Min(8f)] public float FooterHeight = 18f;
        [Min(0f)] public float FooterGap = 12f;

        [Header("Typography")]
        public Font InterfaceFont;
        [Min(8)] public int EyebrowFontSize = 12;
        [Min(16)] public int TitleFontSize = 52;
        [Min(9)] public int SubtitleFontSize = 14;
        [Min(10)] public int EliminationFontSize = 19;
        [Min(10)] public int PrimaryButtonFontSize = 16;
        [Min(9)] public int SecondaryButtonFontSize = 13;
        [Min(8)] public int FooterFontSize = 10;

        [Header("Animation")]
        [Min(0f)] public float EntranceDuration = 0.38f;
        [Min(0f)] public float EntranceVerticalOffset = 24f;
        [Min(0f)] public float AccentPulseSpeed = 2.2f;
        [Range(0f, 1f)] public float AccentPulseMinimum = 0.72f;
        [Range(0f, 1f)] public float AccentPulseMaximum = 1f;
        [Min(2f)] public float ScanlineSpacing = 5f;
        [Min(1f)] public float ScanlineThickness = 1f;

        [Header("Palette")]
        [ColorUsage(false)] public Color OverlayColor = new Color(0.008f, 0.012f, 0.02f, 0.9f);
        [ColorUsage(false)] public Color ScanlineColor = new Color(0.35f, 0.68f, 0.76f, 0.025f);
        [ColorUsage(false)] public Color ShadowColor = new Color(0f, 0f, 0f, 0.72f);
        [ColorUsage(false)] public Color PanelColor = new Color(0.025f, 0.037f, 0.052f, 0.985f);
        [ColorUsage(false)] public Color BorderColor = new Color(0.28f, 0.42f, 0.48f, 0.7f);
        [ColorUsage(false)] public Color AccentColor = new Color(1f, 0.3f, 0.12f, 1f);
        [ColorUsage(false)] public Color AccentSoftColor = new Color(0.54f, 0.14f, 0.08f, 0.82f);
        [ColorUsage(false)] public Color TitleColor = new Color(1f, 0.38f, 0.18f, 1f);
        [ColorUsage(false)] public Color PrimaryTextColor = new Color(0.92f, 0.97f, 1f, 1f);
        [ColorUsage(false)] public Color SecondaryTextColor = new Color(0.5f, 0.67f, 0.72f, 1f);
        [ColorUsage(false)] public Color SecondaryBorderColor = new Color(0.18f, 0.27f, 0.31f, 0.9f);
        [ColorUsage(false)] public Color PrimaryButtonColor = new Color(0.94f, 0.24f, 0.08f, 1f);
        [ColorUsage(false)] public Color PrimaryButtonHoverColor = new Color(1f, 0.38f, 0.12f, 1f);
        [ColorUsage(false)] public Color PrimaryButtonTextColor = new Color(1f, 0.97f, 0.92f, 1f);
        [ColorUsage(false)] public Color SecondaryButtonColor = new Color(0.075f, 0.105f, 0.13f, 1f);
        [ColorUsage(false)] public Color SecondaryButtonHoverColor = new Color(0.12f, 0.18f, 0.22f, 1f);

        [Header("First Strike Ring Death Note")]
        public bool ShowFirstStrikeOrbDeathNote = true;
        public string StrikeOrbNoteLabel = "FIELD NOTE  //  STRIKE RINGS";
        [TextArea(2, 4)]
        public string StrikeOrbNoteMessage =
            "Strike Rings have an ariel range and don't attack the drone on the ground.";
        [Min(180f)] public float StrikeOrbNoteWidth = 390f;
        [Min(100f)] public float StrikeOrbNoteHeight = 190f;
        [Min(0f)] public float StrikeOrbNotePanelGap = 28f;
        [Min(0f)] public float StrikeOrbNoteVerticalOffset = 74f;
        [Min(8f)] public float StrikeOrbNotePadding = 26f;
        [Min(1f)] public float StrikeOrbNoteAccentWidth = 4f;
        [Min(1f)] public float StrikeOrbNoteDividerThickness = 1f;
        [Min(0f)] public float StrikeOrbNoteLabelHeight = 24f;
        [Min(0f)] public float StrikeOrbNoteLabelGap = 12f;
        [Min(0f)] public float StrikeOrbNoteEntranceDelay = 0.18f;
        [Min(0.01f)] public float StrikeOrbNoteEntranceDuration = 0.42f;
        [Min(0f)] public float StrikeOrbNoteEntranceVerticalOffset = 18f;
        [Min(8)] public int StrikeOrbNoteLabelFontSize = 11;
        [Min(10)] public int StrikeOrbNoteMessageFontSize = 18;
        [ColorUsage(false)] public Color StrikeOrbNotePanelColor = new Color(0.018f, 0.04f, 0.052f, 0.97f);
        [ColorUsage(false)] public Color StrikeOrbNoteBorderColor = new Color(0.22f, 0.62f, 0.72f, 0.72f);
        [ColorUsage(false)] public Color StrikeOrbNoteAccentColor = new Color(0.18f, 0.88f, 1f, 1f);
        [ColorUsage(false)] public Color StrikeOrbNoteLabelColor = new Color(0.42f, 0.84f, 0.92f, 1f);
        [ColorUsage(false)] public Color StrikeOrbNoteMessageColor = new Color(0.9f, 0.97f, 1f, 1f);

        [Header("First Vesper Pilgrim Death Note")]
        public bool ShowFirstVesperPilgrimDeathNote = true;
        public string VesperPilgrimNoteLabel = "FIELD NOTE  //  VESPER MISSILES";
        [TextArea(2, 4)]
        public string VesperPilgrimNoteMessage =
            "Vesper missiles return to sender when the drone passes through any portal on the ground or in air.";
    }

    public enum CourierDroneFaction
    {
        Player,
        Rival,
        Neutral,
    }

    [System.Serializable]
    public sealed class DroneVisualTuning
    {
        [Header("Prefab")]
        [Tooltip("Use the Resources drone prefab. Disable this to use the original procedural drone visual.")]
        public bool UsePrefabVisual = true;
        [Tooltip("Resources path of the drone prefab used for player and courier visuals.")]
        public string PrefabResourcePath = "dronePrefab";
        [Tooltip("Local position applied to the instantiated drone prefab.")]
        public Vector3 PrefabLocalPosition = Vector3.zero;
        [Tooltip("Local Euler rotation applied to the instantiated drone prefab.")]
        public Vector3 PrefabLocalEulerAngles = Vector3.zero;
        [Tooltip("Local scale applied to the instantiated drone prefab.")]
        public Vector3 PrefabLocalScale = Vector3.one * 0.15f;

        [Header("Materials")]
        [ColorUsage(false)] public Color BodyColor = new Color(0.68f, 0.72f, 0.74f);
        [Range(0f, 1f)] public float BodySmoothness = 0.72f;
        [Range(0f, 1f)] public float BodyMetallic = 0.7f;
        [ColorUsage(false)] public Color FrameColor = new Color(0.018f, 0.025f, 0.033f);
        [Range(0f, 1f)] public float FrameSmoothness = 0.64f;
        [Range(0f, 1f)] public float FrameMetallic = 0.85f;
        [ColorUsage(false)] public Color TrailColor = new Color(0f, 0.06f, 0.08f);
        [ColorUsage(false, true)] public Color TrailEmission = new Color(0f, 0.8f, 1.4f);
        [Range(0f, 1f)] public float TrailSmoothness = 0.6f;
        [Range(0f, 1f)] public float TrailMetallic = 0.1f;

        [Header("Hull")]
        [Min(0f)] public float CourierVisualHeight = 0.92f;
        public Vector3 LowerHullPosition = new Vector3(0f, -0.08f, -0.04f);
        public Vector3 LowerHullScale = new Vector3(1.2f, 0.28f, 1.58f);
        public Vector3 UpperHullPosition = new Vector3(0f, 0.04f, 0.08f);
        public Vector3 UpperHullScale = new Vector3(1.08f, 0.34f, 1.5f);
        public Vector3 CanopyPosition = new Vector3(0f, 0.28f, 0.28f);
        public Vector3 CanopyScale = new Vector3(0.62f, 0.19f, 0.78f);
        public Vector3 NoseSensorPosition = new Vector3(0f, 0.05f, 1.46f);
        public Vector3 NoseSensorScale = new Vector3(0.28f, 0.15f, 0.14f);
        public Vector3 TailLightPosition = new Vector3(0f, 0.08f, -1.5f);
        public Vector3 TailLightScale = new Vector3(0.44f, 0.08f, 0.11f);

        [Header("Swept Wings")]
        [Min(0.05f)] public float WingInnerOffset = 0.38f;
        [Min(0.05f)] public float WingSpan = 1.42f;
        [Min(0.05f)] public float WingRootChord = 1.08f;
        [Min(0.05f)] public float WingTipChord = 0.48f;
        [Min(0f)] public float WingSweep = 0.5f;
        [Min(0.01f)] public float WingThickness = 0.11f;
        public float WingHeight = -0.015f;
        public float WingForwardOffset = 0.04f;
        [Min(0f)] public float WingAccentInset = 0.13f;
        [Min(0f)] public float WingAccentLift = 0.075f;
        [Min(0.005f)] public float WingAccentThickness = 0.014f;

        [Header("Rotors")]
        public Vector3 FrontRotorPosition = new Vector3(1.58f, 0.03f, 0.42f);
        public Vector3 RearRotorPosition = new Vector3(1.38f, 0.03f, -0.52f);
        public Vector3 RotorNacelleScale = new Vector3(0.52f, 0.18f, 0.52f);
        [Min(0.05f)] public float RotorGuardRadius = 0.48f;
        [Min(0.005f)] public float RotorGuardThickness = 0.055f;
        [Min(0.005f)] public float RotorGlowThickness = 0.024f;
        [Min(0f)] public float RotorGuardHeight = 0.14f;
        public Vector3 RotorHubScale = new Vector3(0.13f, 0.09f, 0.13f);
        [Min(0.02f)] public float RotorBladeLength = 0.72f;
        [Min(0.005f)] public float RotorBladeWidth = 0.055f;
        [Min(0.005f)] public float RotorBladeThickness = 0.018f;
        [Tooltip("Propeller rotation speed in degrees per second. The authored value matches the reference hover animation.")]
        [Min(0f)] public float RotorSpinSpeed = 1630f;
        [Range(0f, 0.25f)] public float RotorPulseAmount = 0.045f;
        [Min(0f)] public float RotorPulseSpeed = 4.5f;

        [Header("Trails")]
        public Vector3 TrailPosition = new Vector3(0.5f, -0.08f, -1.2f);
        [Min(0f)] public float TrailDuration = 0.3f;
        [Min(0f)] public float TrailStartWidth = 0.065f;
        [Min(0f)] public float TrailEndWidth;
        [Min(0.001f)] public float TrailMinimumVertexDistance = 0.12f;
    }

    [System.Serializable]
    public sealed class DynamicCourierTuning
    {
        public bool Enabled;

        [Header("Event Scheduling")]
        [Min(0f)] public float InitialEventDelay;
        [Min(0f)] public float MinimumEventInterval;
        [Min(0f)] public float MaximumEventInterval;
        [Min(1f)] public float MinimumSpawnDistance;
        [Min(1f)] public float MaximumSpawnDistance;
        [Min(1f)] public float MinimumRouteDistance;
        [Min(1f)] public float MaximumRouteDistance;
        [Min(0f)] public float ResultDisplayDuration;
        [Min(0f)] public float ConvoyEventWeight;

        [Header("Ambient Neutral Deliveries")]
        public bool AmbientNeutralCouriersEnabled;
        [Range(0, 24)] public int AmbientNeutralCourierCount;
        [Min(1f)] public float AmbientMinimumSpawnDistance;
        [Min(1f)] public float AmbientMaximumSpawnDistance;
        [Min(1f)] public float AmbientMinimumRouteDistance;
        [Min(1f)] public float AmbientMaximumRouteDistance;
        [Min(0f)] public float AmbientMinimumCruiseSpeed;
        [Min(0f)] public float AmbientMaximumCruiseSpeed;
        [Min(0f)] public float AmbientMinimumFlightHeight;
        [Min(0f)] public float AmbientMaximumFlightHeight;
        [Min(0f)] public float AmbientMinimumTurnaroundDelay;
        [Min(0f)] public float AmbientMaximumTurnaroundDelay;
        [Min(1f)] public float AmbientDespawnDistance;
        [Min(0.01f)] public float AmbientPackageScale;
        public Vector3 AmbientPackageOffset;

        [Header("Courier Flight")]
        [Min(0f)] public float FlightHeightAboveTerrain;
        [Min(0f)] public float CruiseSpeed;
        [Min(0f)] public float TurnSharpness;
        [Min(0.1f)] public float DestinationRadius;
        [Min(0f)] public float HoverAmplitude;
        [Min(0f)] public float HoverFrequency;
        [Min(0.1f)] public float VisualScale;
        [Min(1f)] public float MaximumCourierHealth;

        [Header("Moving Convoy")]
        [Range(0, 6)] public int ConvoyEscortCount;
        [Range(1, 10)] public int ConvoyAttackerCount;
        [Min(0f)] public float ConvoyEscortSpacing;
        [Range(0f, 1f)] public float ConvoyMinimumRewardFraction;
        [Min(0)] public int ConvoyMaximumReward;

        [Header("Event Attackers")]
        [Min(1f)] public float AttackerMaximumHealth;
        [Min(0.1f)] public float AttackerVisualScale;
        [Min(0f)] public float AttackerSpeed;
        [Min(0f)] public float AttackerTurnSharpness;
        [Min(0f)] public float AttackerOrbitRadius;
        [Min(0f)] public float AttackerHeightOffset;
        [Min(0.1f)] public float AttackerShotRange;
        [Min(0.1f)] public float AttackerShotInterval;
        [Min(0f)] public float AttackerShotDamage;
        [Min(0.01f)] public float AttackerCollisionRadius;
        [Min(0f)] public int AttackerGoldReward;
        [Min(0.01f)] public float AttackerShotVisualDuration;
        [Min(0.001f)] public float AttackerShotStartWidth;
        [Min(0.001f)] public float AttackerShotEndWidth;

        [Header("Faction Tops")]
        [ColorUsage(false)] public Color PlayerTopColor;
        [ColorUsage(false, true)] public Color PlayerTopEmission;
        [ColorUsage(false)] public Color RivalTopColor;
        [ColorUsage(false, true)] public Color RivalTopEmission;
        [ColorUsage(false)] public Color NeutralTopColor;
        [ColorUsage(false, true)] public Color NeutralTopEmission;
        [Range(0f, 1f)] public float TopMaterialSmoothness;
        [Range(0f, 1f)] public float TopMaterialMetallic;

        [Header("Event HUD")]
        [Min(100f)] public float HudWidth;
        [Min(60f)] public float HudHeight;
        [Min(0f)] public float HudLeft;
        [Min(0f)] public float HudTop;
        [Tooltip("Minimum vertical gap below another visible left-side HUD panel.")]
        [Min(0f)] public float HudOtherPanelGap;
        [Min(0f)] public float HudPadding;
        [Min(8)] public int HudTitleFontSize;
        [Min(8)] public int HudBodyFontSize;
        [Min(0f)] public float HudTitleHeight;
        [Min(0f)] public float HudLineHeight;
        [Min(8f)] public float ObjectiveMarkerSize;
        [Min(0f)] public float ObjectiveMarkerEdgePadding;
        [Min(80f)] public float ObjectiveMarkerLabelWidth;
        [Min(12f)] public float ObjectiveMarkerLabelHeight;
        [Min(8)] public int ObjectiveMarkerFontSize;
        [ColorUsage(false)] public Color HudPanelColor;
        [ColorUsage(false)] public Color HudTextColor;
        [ColorUsage(false)] public Color ConvoyHudColor;
        [ColorUsage(false)] public Color SuccessHudColor;
        [ColorUsage(false)] public Color FailureHudColor;
    }

    [System.Serializable]
    public sealed class EnergyLauncherTuning
    {
        public bool Enabled = true;

        [Header("Lock-On Targeting")]
        [Min(1f)] public float LockRange = 180f;
        [Tooltip("Targets closer than this distance from the drone cannot be acquired or retained.")]
        [Min(0f)] public float MinimumLockDistance = 15f;
        [Tooltip("Full angle of the view-centered targeting cone. Targets behind the camera are always rejected.")]
        [Range(1f, 179f)] public float LockConeAngle = 34f;
        [Min(0f)] public float AcquisitionTime = 0.55f;
        [Tooltip("Brief time that TARGET DETECTED is shown before acquisition begins.")]
        [Min(0f)] public float TargetDetectedDuration = 0.12f;
        [Tooltip("Grace time before an acquired target outside the cone or range is released.")]
        [Min(0f)] public float TargetLossTolerance = 0.32f;
        [Tooltip("Seconds between candidate scoring passes. Current-target validity is still checked every frame.")]
        [Min(0.01f)] public float TargetScanInterval = 0.05f;
        [Tooltip("How much better a new view-center score must be before replacing the current target.")]
        [Range(0f, 1f)] public float TargetSwitchAdvantage = 0.12f;
        [Tooltip("Small distance contribution to selection score; screen-center alignment remains dominant.")]
        [Range(0f, 0.5f)] public float DistanceScoreWeight = 0.08f;

        [Header("Energy Shot")]
        [Min(1f)] public float ProjectileSpeed = 155f;
        [Tooltip("Projectile speed multiplier reached at the maximum Energy Shot Cooldown upgrade tier. Intermediate tiers use the upgrade's progression curve.")]
        [Min(1f)] public float ProjectileSpeedAtMaximumFireRateTierMultiplier = 1.75f;
        [Tooltip("Maximum homing direction change in degrees per second.")]
        [Min(0f)] public float HomingTurnStrength = 430f;
        [Min(0f)] public float Damage = 45f;
        [Min(0f)] public float FireCooldown = 0.22f;
        [Min(0.05f)] public float ProjectileLifetime = 3f;
        [Min(0.01f)] public float ProjectileHitRadius = 0.32f;
        [Tooltip("Maximum look-ahead time used to lead a moving locked target.")]
        [Min(0f)] public float LeadPredictionTime = 0.65f;
        [Tooltip("Caps measured target velocity used for lead prediction, filtering floating-origin shifts and spikes.")]
        [Min(0f)] public float MaximumLeadSpeed = 140f;
        [Tooltip("Ship-relative launch offset from the drone center.")]
        public Vector3 MuzzleOffset = new Vector3(0f, -0.1f, 2.4f);

        [Header("Projectile Feedback")]
        [Tooltip("Resources path of the shot sprite drawn in place of the energy core.")]
        public string ShotSpriteResourcePath = "UI/T_SentryShot";
        [Tooltip("World length of the shot sprite along its travel direction.")]
        [Min(0.01f)] public float ShotSpriteLength = 4.5f;
        [Tooltip("World width of the shot sprite across its travel direction.")]
        [Min(0.01f)] public float ShotSpriteWidth = 1.5f;
        [Tooltip("Tint multiplied into the shot sprite. Alpha scales its overall brightness.")]
        [ColorUsage(false, true)] public Color ShotSpriteColor = new Color(1.4f, 3.2f, 5f, 1f);
        [Tooltip("Draw the ribbon trail behind the shot sprite.")]
        public bool ShotTrailEnabled = false;
        [Min(0.01f)] public float TrailDuration = 0.2f;
        [Min(0.001f)] public float TrailStartWidth = 0.2f;
        [Min(0f)] public float TrailMinimumVertexDistance = 0.08f;
        [Min(0.01f)] public float LaunchFlashScale = 0.85f;
        [Min(0.01f)] public float LaunchFlashDuration = 0.11f;
        [Min(0.01f)] public float ImpactFlashScale = 2.2f;
        [Min(0.01f)] public float ImpactFlashDuration = 0.24f;
        [ColorUsage(false, true)] public Color ProjectileColor = new Color(0.08f, 0.72f, 1f);
        [ColorUsage(false, true)] public Color ProjectileEmission = new Color(2f, 12f, 24f);

        [Header("Targeting HUD")]
        [Min(240f)] public float HudReferenceHeight = 1080f;
        [Tooltip("World-space distance between the ship and the near aim marker, and between the near and far markers.")]
        [Min(0.1f)] public float AimMarkerSpacing = 30f;
        [Min(1f)] public float NearAimMarkerSize = 23f;
        [Min(1f)] public float FarAimMarkerSize = 19f;
        [Min(0f)] public float AimMarkerCrossSize = 7f;
        [Min(0.5f)] public float AimMarkerLineThickness = 1.25f;
        [Min(1f)] public float ReticleLineThickness = 2f;
        [Min(1f)] public float TargetDetectedReticleSize = 84f;
        [Min(1f)] public float LockedReticleSize = 44f;
        [Min(1f)] public float TargetBracketLength = 18f;
        [Min(0f)] public float LockedPulseAmount = 4f;
        [Min(0f)] public float ReticlePulseSpeed = 8f;
        [Min(1f)] public float LockedConfirmationSize = 7f;
        [Min(0f)] public float TargetStatusOffset = 28f;
        [Min(0f)] public float TargetDistanceOffset = 46f;
        [Min(40f)] public float HudLabelWidth = 190f;
        [Min(8f)] public float HudLabelHeight = 22f;
        [Min(8)] public int TargetStatusFontSize = 14;
        [Min(8)] public int TargetDistanceFontSize = 12;
        [ColorUsage(false)] public Color AimMarkerColor = new Color(0.72f, 0.96f, 1f, 0.82f);
        [ColorUsage(false)] public Color TargetDetectedColor = new Color(1f, 0.72f, 0.18f, 0.95f);
        [ColorUsage(false)] public Color LockingColor = new Color(0.15f, 0.86f, 1f, 0.98f);
        [ColorUsage(false)] public Color LockedColor = new Color(0.35f, 1f, 0.62f, 1f);
    }

    [System.Serializable]
    public sealed class FlyingEnemyTuning
    {
        public bool Enabled = true;
        [Min(1f)] public float MaximumHealth = 90f;
        [Min(0)] public int GoldReward = 20;
        [Range(1, 12)] public int EnemyCount = 3;
        [Min(10f)] public float MinimumSpawnDistance = 55f;
        [Min(10f)] public float MaximumSpawnDistance = 105f;
        [Min(1f)] public float DetectionRange = 125f;
        [Min(1f)] public float HoverHeight = 20f;
        [Min(0f)] public float HoverAmplitude = 1.1f;
        [Tooltip("Player follow speed at risk 0.")]
        [Min(0f)] public float FollowSpeed = 11f;
        [Tooltip("Player follow speed at the speed risk scaling ceiling.")]
        [Min(0f)] public float FollowSpeedAtRiskCeiling = 33f;
        [Tooltip("Attack dive speed at risk 0.")]
        [Min(0f)] public float AttackSpeed = 66f;
        [Tooltip("Attack dive speed at the speed risk scaling ceiling.")]
        [Min(0f)] public float AttackSpeedAtRiskCeiling = 102f;
        [Tooltip("Risk at which follow and attack speeds reach their ceiling values.")]
        [Min(1)] public int SpeedRiskScalingCeiling = 20;
        [Min(0.1f)] public float AttackCooldown = 3.5f;
        [Min(0.25f)] public float AttackAlignmentDistance = 4f;

        [Header("Attack Ground Contact")]
        [Tooltip("Baseline height of the enemy root above the sampled terrain, multiplied by Visual Scale.")]
        [Min(0f)] public float StuckCenterHeightPerVisualScale = 1.05f;
        [Tooltip("Additional world-space depth pushed into the terrain during the attack dive and stuck state. Hovering is unaffected.")]
        [Min(0f)] public float AttackGroundPenetrationDepth = 0.75f;

        [Min(0f)] public float ImpactDamage = 25f;
        public string ImpactDeathMessage = "Destroyed by a Sky Piecer impact.";
        [Min(0.1f)] public float ImpactRadius = 3.4f;
        [Min(0f)] public float StuckDuration = 2.2f;
        [Min(0f)] public float ReturnSpeed = 13f;
        [Min(20f)] public float RepositionDistance = 240f;
        [Min(0.1f)] public float VisualScale = 1.35f;

        [Header("Visual Model")]
        [Tooltip("Use the original runtime-generated Sky Piercer visual instead of the imported kunai model.")]
        public bool UseProceduralVisualFallback;
        [Tooltip("Resources-relative path to the imported flying-enemy model.")]
        public string KunaiResourcePath = "Kunai";
        [Tooltip("Local position applied to the imported model beneath the flying-enemy visual root.")]
        public Vector3 KunaiLocalPosition = new Vector3(0f, 0.68f, 0f);
        [Tooltip("Local rotation applied to the imported model beneath the flying-enemy visual root.")]
        public Vector3 KunaiLocalEulerAngles = Vector3.zero;
        [Tooltip("Local scale applied before the shared flying-enemy Visual Scale.")]
        public Vector3 KunaiLocalScale = Vector3.one * 2.7f;

        public float EvaluateFollowSpeed(int risk)
        {
            return Mathf.Lerp(FollowSpeed, FollowSpeedAtRiskCeiling, EvaluateSpeedRisk(risk));
        }

        public float EvaluateAttackSpeed(int risk)
        {
            return Mathf.Lerp(AttackSpeed, AttackSpeedAtRiskCeiling, EvaluateSpeedRisk(risk));
        }

        private float EvaluateSpeedRisk(int risk)
        {
            return Mathf.Clamp01(risk / (float)Mathf.Max(1, SpeedRiskScalingCeiling));
        }
    }

    [System.Serializable]
    public sealed class StormPyramidTuning
    {
        public bool Enabled = true;
        [Min(1f)] public float MaximumHealth = 135f;
        [Min(0)] public int GoldReward = 50;

        [Header("Model")]
        [Tooltip("Uses Storm Pyramid Prefab for the body while preserving the procedural light-blue rings and combat effects. Disable to use the original procedural pyramid.")]
        public bool UsePrefabModel = true;
        [Tooltip("Optional GameObject prefab used only for the storm pyramid hull. Assign a prefab through this Inspector slot. If empty or invalid, the original procedural hull is used.")]
        public GameObject StormPyramidPrefab;
        [Tooltip("Local position applied to the instantiated body prefab.")]
        public Vector3 PrefabLocalPosition = Vector3.zero;
        [Tooltip("Local Euler rotation applied to the instantiated body prefab.")]
        public Vector3 PrefabLocalEulerAngles = Vector3.zero;
        [Tooltip("Local scale applied to the instantiated body prefab before the overall Visual Scale.")]
        public Vector3 PrefabLocalScale = Vector3.one;

        [Header("Spawning")]
        [Range(1, 10)] public int EnemyCount = 2;
        [Min(20f)] public float MinimumSpawnDistance = 90f;
        [Min(20f)] public float MaximumSpawnDistance = 180f;
        [Min(50f)] public float RepositionDistance = 360f;

        [Header("High-Altitude Patrol")]
        [Tooltip("Height above the terrain used as the center of this enemy's altitude range.")]
        [Min(10f)] public float HoverHeight = 72f;
        [Tooltip("Random amount added above or below Hover Height when each enemy spawns.")]
        [Min(0f)] public float HoverHeightVariance = 16f;
        [Min(0f)] public float PatrolDriftRange = 16f;
        [Min(0f)] public float PatrolDriftSpeed = 3f;

        [Header("Targeting")]
        [Tooltip("Ground strikes show a nearby warning when the impact point is inside this range of the drone.")]
        [Min(1f)] public float DetectionRange = 125f;

        [Header("Proximity Explosion")]
        [Tooltip("Multiplies the Ground Exploders detection radius when a storm pyramid checks whether the drone is close enough to trigger its detonation.")]
        [Min(0.1f)] public float ProximityDetectionRadiusMultiplier = 3f;
        [Tooltip("Multiplies the Ground Exploders risk-scaled explosion radius for storm pyramid damage and presentation.")]
        [Min(0.1f)] public float ProximityExplosionRadiusMultiplier = 3f;
        [Tooltip("Uses Explosion Prefab for storm pyramid detonations. Disable this, or leave the prefab empty, to use the original procedural explosion fallback.")]
        public bool UseExplosionPrefab = true;
        [Tooltip("Optional explosion effect spawned when a storm pyramid detonates. If empty, the original procedural explosion is used.")]
        public GameObject ExplosionPrefab;
        [Tooltip("Local position applied to the spawned explosion prefab.")]
        public Vector3 ExplosionPrefabLocalPosition = Vector3.zero;
        [Tooltip("Local Euler rotation offset added to the explosion prefab's authored rotation.")]
        public Vector3 ExplosionPrefabLocalEulerAngles = Vector3.zero;
        [Tooltip("Scale multiplier applied to the explosion prefab's authored scale.")]
        public Vector3 ExplosionPrefabLocalScale = Vector3.one;
        [Tooltip("Seconds before the spawned explosion prefab is destroyed. Set to 0 to let the prefab manage its own lifetime.")]
        [Min(0f)] public float ExplosionPrefabLifetime = 3f;
        [Tooltip("Optional second explosion effect spawned alongside Explosion Prefab when a storm pyramid detonates.")]
        public GameObject AdditionalExplosionPrefab;
        [Tooltip("Local position applied to the additional spawned explosion prefab.")]
        public Vector3 AdditionalExplosionPrefabLocalPosition = Vector3.zero;
        [Tooltip("Local Euler rotation offset added to the additional explosion prefab's authored rotation.")]
        public Vector3 AdditionalExplosionPrefabLocalEulerAngles = Vector3.zero;
        [Tooltip("Scale multiplier applied to the additional explosion prefab's authored scale.")]
        public Vector3 AdditionalExplosionPrefabLocalScale = Vector3.one;
        [Tooltip("Seconds before the additional spawned explosion prefab is destroyed. Set to 0 to let the prefab manage its own lifetime.")]
        [Min(0f)] public float AdditionalExplosionPrefabLifetime = 3f;

        [Header("Lightning Attack")]
        [Tooltip("Delay before beginning another straight-down ground strike after returning to idle at risk 0.")]
        [Min(0f)] public float AttackInterval = 4.5f;
        [Tooltip("Smallest fraction of the current attack interval used when staggering Storm Pyramid attacks after spawning, repositioning, or reactivation.")]
        [Range(0f, 1f)] public float MinimumInitialAttackDelayMultiplier = 0.35f;
        [Tooltip("Delay between ground strikes at the attack interval risk ceiling.")]
        [Min(0f)] public float AttackIntervalAtRiskCeiling = 0f;
        [Tooltip("Risk at which the storm pyramid reaches Attack Interval At Risk Ceiling.")]
        [Min(1)] public int AttackIntervalRiskCeiling = 20;
        [Tooltip("Charge duration for a straight-down ground strike.")]
        [InspectorName("Ground Strike Charge Time")]
        [Min(0.1f)] public float ChargeTime = 1.15f;
        [Tooltip("Post-strike cooldown at risk 0.")]
        [Min(0f)] public float Cooldown = 2.4f;
        [Tooltip("Post-strike cooldown at the cooldown risk ceiling.")]
        [Min(0f)] public float CooldownAtRiskCeiling = 0f;
        [Tooltip("Risk at which the storm pyramid reaches Cooldown At Risk Ceiling.")]
        [Min(1)] public int CooldownRiskCeiling = 20;
        [Min(0f)] public float LightningDamage = 32f;
        public string LightningDeathMessage = "Struck by Storm Pyramid ground lightning.";
        [Tooltip("Ground strike radius at risk 0.")]
        [Min(0.1f)] public float StrikeRadius = 5f;
        [Tooltip("Ground strike radius at the strike radius risk ceiling.")]
        [Min(0.1f)] public float StrikeRadiusAtRiskCeiling = 20f;
        [Tooltip("Risk at which the ground strike reaches Strike Radius At Risk Ceiling.")]
        [Min(1)] public int StrikeRadiusRiskCeiling = 20;
        [Min(0.05f)] public float LightningVisualDuration = 0.28f;
        [Min(0.01f)] public float ChargeTelegraphWidth = 0.12f;
        [Min(0.01f)] public float LightningWidth = 0.48f;
        [Tooltip("Multiplies only the lightning bolt emission, creating a stronger HDR bloom halo.")]
        [Min(0f)] public float LightningBloomIntensity = 4f;

        [Header("Ground Impact Effect")]
        [Tooltip("Optional effect spawned at the lightning strike point. If empty, the procedural ground shockwave is used.")]
        public GameObject GroundImpactPrefab;
        [Tooltip("Local position applied to the spawned ground impact effect.")]
        public Vector3 GroundImpactPrefabLocalPosition = Vector3.zero;
        [Tooltip("Local Euler rotation offset added to the ground impact prefab's authored rotation.")]
        public Vector3 GroundImpactPrefabLocalEulerAngles = Vector3.zero;
        [Tooltip("The prefab's world-space effect radius at a scale of one. The spawned effect is scaled so this radius matches the current gameplay Strike Radius.")]
        [Min(0.01f)] public float GroundImpactPrefabReferenceRadius = 4.5f;
        [Tooltip("Additional scale multiplier applied after fitting the ground impact prefab to the current gameplay Strike Radius.")]
        public Vector3 GroundImpactPrefabScale = Vector3.one;
        [Tooltip("Seconds before the spawned ground impact effect is destroyed. Set to 0 to let the prefab manage its own lifetime.")]
        [Min(0f)] public float GroundImpactPrefabLifetime = 2f;
        [Tooltip("Time for the ground shockwave to expand from the strike point to Strike Radius.")]
        [Min(0.01f)] public float GroundImpactExpansionDuration = 0.42f;
        [Tooltip("Time the ground shockwave remains at the full Strike Radius before disappearing.")]
        [Min(0f)] public float GroundImpactHoldDuration = 0.1f;
        [Tooltip("Initial shockwave size as a fraction of Strike Radius.")]
        [Range(0f, 1f)] public float GroundImpactStartScale = 0.08f;
        [Tooltip("Peak size of the central impact flash as a fraction of Strike Radius.")]
        [Min(0f)] public float GroundImpactFlashScaleMultiplier = 0.34f;
        [Tooltip("World-space thickness of the expanding ground shockwave ring.")]
        [Min(0.005f)] public float GroundImpactRingThickness = 0.14f;
        [Tooltip("Raises the shockwave slightly above the terrain to keep it visible on the ground.")]
        [Min(0f)] public float GroundImpactHeightOffset = 0.06f;

        [Header("Attack Warning HUD")]
        [Tooltip("Ground strikes show the attack warning when their impact point is within this distance of the drone. Player-targeted strikes always warn.")]
        [Min(1f)] public float NearbyWarningRange = 55f;
        [Tooltip("Speed of the warning border and marker pulse.")]
        [Min(0f)] public float WarningPulseSpeed = 9f;
        [Tooltip("Scales the warning panel, text, target marker, and screen border together.")]
        [Range(0.6f, 2f)] public float WarningHudScale = 1f;
        [Tooltip("Distance in pixels that the directional strike marker stays inside the screen edge before HUD scaling.")]
        [Min(12f)] public float WarningEdgePadding = 64f;

        [Header("Presentation")]
        [Min(0.1f)] public float VisualScale = 2.2f;
        [Min(0.1f)] public float BodyWidth = 4.8f;
        [Min(0.1f)] public float BodyHeight = 3.8f;
        [Min(0f)] public float BodyCornerCut = 0.38f;
        [Range(1, 8)] public int EnergyBandCount = 3;
        [Range(0f, 1f)] public float EnergyBandStart = 0.2f;
        [Range(0f, 1f)] public float EnergyBandEnd = 0.72f;
        [Min(0.005f)] public float EnergyBandThickness = 0.055f;
        [Min(0.005f)] public float EdgeConduitRadius = 0.035f;
        [Range(3, 8)] public int CrownFinCount = 4;
        [Min(0f)] public float CrownFinRadius = 2.15f;
        public Vector3 CrownFinSize = new Vector3(0.32f, 0.68f, 0.86f);
        [Range(-60f, 60f)] public float CrownFinOutwardTilt = 18f;
        [Min(0.1f)] public float CrownRingRadius = 1.9f;
        [Min(0.01f)] public float CrownRingThickness = 0.1f;
        public float CrownHeight = 0.14f;
        public float CoreHeight = 0.18f;
        public Vector3 CoreScale = new Vector3(0.78f, 0.24f, 0.78f);
        [Min(0.1f)] public float CoreRingRadius = 1.28f;
        [Min(0.01f)] public float CoreRingThickness = 0.055f;
        public float CoreRingHeight = 0.24f;
        [Min(0.1f)] public float ChargeHaloRadius = 1.72f;
        [Min(0.01f)] public float ChargeHaloThickness = 0.075f;
        public float ChargeHaloHeight = 0.46f;
        [Min(0f)] public float LightningOriginTipOffset = 0.08f;
        public float VisualRotationSpeed = 11f;
        public float CounterRotationSpeed = -24f;
        [Min(0f)] public float CorePulseSpeed = 4.5f;
        [Range(0f, 1f)] public float CorePulseAmount = 0.1f;
        [Min(1f)] public float CoreChargeScaleMultiplier = 1.7f;
        [ColorUsage(false)] public Color BodyColor = new Color(0.012f, 0.055f, 0.024f);
        [ColorUsage(false, true)] public Color BodyEmission = new Color(0.025f, 0.22f, 0.075f);
        [ColorUsage(false)] public Color CoreColor = new Color(0.018f, 0.11f, 0.045f);
        [ColorUsage(false, true)] public Color CoreEmission = new Color(0.12f, 1.65f, 0.48f);
        [ColorUsage(false)] public Color LightningColor = new Color(0.55f, 0.86f, 1f);
        [ColorUsage(false, true)] public Color LightningEmission = new Color(7.5f, 12f, 18f);
        [ColorUsage(false)] public Color WarningColor = new Color(0.18f, 0.42f, 0.62f);
        [ColorUsage(false, true)] public Color WarningEmission = new Color(0.45f, 2.8f, 5.8f);

        public float EvaluateStrikeRadius(int risk)
        {
            float riskProgress = Mathf.Clamp01(
                risk / (float)Mathf.Max(1, StrikeRadiusRiskCeiling));
            return Mathf.Lerp(StrikeRadius, StrikeRadiusAtRiskCeiling, riskProgress);
        }

        public float EvaluateCooldown(int risk)
        {
            float riskProgress = Mathf.Clamp01(
                risk / (float)Mathf.Max(1, CooldownRiskCeiling));
            return Mathf.Max(0f, Mathf.Lerp(Cooldown, CooldownAtRiskCeiling, riskProgress));
        }
    }

    [System.Serializable]
    public sealed class PlayerStrikeOrbSatelliteTuning
    {
        public float OrbitSpeed;
        [Range(0f, 360f)] public float StartAngle;
        [Range(-85f, 85f)] public float OrbitTilt;
    }

    [System.Serializable]
    public sealed class PlayerStrikeOrbTuning
    {
        public bool Enabled = true;
        [Min(1f)] public float MaximumHealth = 110f;
        [Min(0)] public int GoldReward = 55;

        [Header("Spawning")]
        [Range(1, 10)] public int EnemyCount = 2;
        [Min(20f)] public float MinimumSpawnDistance = 120f;
        [Min(20f)] public float MaximumSpawnDistance = 240f;
        [Min(50f)] public float RepositionDistance = 390f;

        [Header("High-Altitude Patrol")]
        [Min(10f)] public float HoverHeight = 68f;
        [Min(0f)] public float HoverHeightVariance = 14f;
        [Min(0f)] public float PatrolDriftRange = 20f;
        [Min(0f)] public float PatrolDriftSpeed = 5f;

        [Header("Airborne Player Targeting")]
        [Tooltip("Attack range at rank 0.")]
        [Min(1f)] public float DetectionRange = 50f;
        [Tooltip("Attack range at the rank scaling ceiling.")]
        [Min(1f)] public float DetectionRangeAtRankCeiling = 150f;
        [Tooltip("Rank at which the attack range reaches its ceiling value.")]
        [Min(1)] public int DetectionRangeRankCeiling = 20;
        [Tooltip("The drone must be at least this far above the dune surface before this enemy can attack it.")]
        [Min(0f)] public float MinimumTargetHeightAboveGround = 3f;
        [Tooltip("Time spent visibly following the airborne drone before locking the predicted strike point.")]
        [Min(0f)] public float TrackingDuration = 0.55f;
        [Tooltip("Multiplier applied to the exact time remaining until impact when predicting the drone's future position. A value of 1 aims at a constant-velocity intercept.")]
        [Min(0f)] public float PredictionTimeMultiplier = 1f;
        [Tooltip("Maximum distance the predicted strike point can lead ahead of the drone.")]
        [Min(0f)] public float MaximumPredictionDistance = 55f;

        [Header("Player Lightning Strike")]
        [Min(0.1f)] public float AttackInterval = 5.25f;
        [Range(0f, 1f)] public float MinimumInitialAttackDelayMultiplier = 0.35f;
        [Min(0.1f)] public float ChargeTime = 1.15f;
        [Min(0f)] public float Cooldown = 2.5f;
        [Min(0f)] public float LightningDamage = 34f;
        public string LightningDamageSource = "Strike Ring lightning";
        public string LightningDeathMessage = "Struck by Strike Ring lightning.";
        [Min(0.1f)] public float StrikeRadius = 4.25f;
        [Min(0.05f)] public float LightningVisualDuration = 0.32f;
        [Min(0.01f)] public float ChargeTelegraphWidth = 0.14f;
        [Min(0.01f)] public float LightningWidth = 0.52f;
        [Tooltip("Multiplies the strike ring lightning HDR emission without affecting storm pyramid lightning.")]
        [Min(0f)] public float LightningBloomIntensity = 1f;
        [Tooltip("Multiplies the strike ring charge telegraph and impact marker HDR emission.")]
        [Min(0f)] public float WarningBloomIntensity = 1f;
        [Min(0f)] public float ChargePulseSpeed = 12f;
        [Range(0f, 0.5f)] public float ChargePulseAmount = 0.12f;
        [Min(0.01f)] public float ChargeMarkerStartScale = 0.25f;
        [Min(0.01f)] public float ChargeHaloStartScale = 0.35f;
        [Min(0.01f)] public float ChargeHaloEndScale = 1.15f;
        [Min(0f)] public float ImpactFlashScaleMultiplier = 0.34f;
        [Min(0f)] public float MinimumLightningJitter = 0.35f;
        [Min(0f)] public float MaximumLightningJitter = 2.2f;
        [Min(0f)] public float LightningJitterPerMeter = 0.022f;
        [Range(0.1f, 1f)] public float LightningEndWidthMultiplier = 0.65f;

        [Header("Fly-Through Destruction")]
        [Tooltip("Fraction of the visible ring opening that counts as flying through its center.")]
        [Range(0.1f, 1f)] public float FlyThroughRadiusMultiplier = 0.78f;
        [Tooltip("Local-space radius of the opening inside the imported strike-ring ring.")]
        [Min(0.1f)] public float FlyThroughOpeningRadius;
        [Tooltip("World-space distance at which the ring stops turning while an airborne drone commits to a fly-through.")]
        [Min(0f)] public float FlyThroughFacingLockDistance = 75f;
        [Tooltip("FMOD one-shot event played at the strike ring when a player fly-through triggers its explosion.")]
        public string FlyThroughExplosionEvent = "event:/Explosion_Strike_Orb";
        [Min(0.05f)] public float FlyThroughExplosionDuration = 0.7f;
        [Min(0.1f)] public float FlyThroughFlashStartScale = 1.5f;
        [Min(0.1f)] public float FlyThroughFlashMaximumScale = 24f;
        [Range(0.05f, 0.95f)] public float FlyThroughFlashPeakTime = 0.28f;
        [Range(1, 8)] public int FlyThroughShockwaveCount = 3;
        [Min(0.01f)] public float FlyThroughShockwaveThickness = 0.16f;
        [Min(0.1f)] public float FlyThroughShockwaveStartRadius = 1.5f;
        [Min(0.1f)] public float FlyThroughShockwaveEndRadius = 27f;
        [Min(0f)] public float FlyThroughExplosionLightIntensity = 85000f;
        [Min(0f)] public float FlyThroughExplosionLightRange = 48f;
        [Tooltip("Multiplies both fly-through explosion HDR emission colors without changing the explosion light.")]
        [Min(0f)] public float FlyThroughExplosionBloomIntensity = 1f;
        [ColorUsage(false)] public Color FlyThroughExplosionWhiteColor = Color.white;
        [ColorUsage(false, true)] public Color FlyThroughExplosionWhiteEmission = new Color(18f, 22f, 28f);
        [ColorUsage(false)] public Color FlyThroughExplosionBlueColor = new Color(0.16f, 0.62f, 1f);
        [ColorUsage(false, true)] public Color FlyThroughExplosionBlueEmission = new Color(2.5f, 12f, 28f);

        [Header("Presentation")]
        [Min(0.1f)] public float VisualScale = 2.35f;
        [Min(0.1f)] public float RingRadius = 1.55f;
        public GameObject RingPrefab;
        public Vector3 RingPrefabLocalPosition;
        public Vector3 RingPrefabLocalEulerAngles;
        [Min(0.001f)] public float RingPrefabScale;
        [Tooltip("Uses ModularSphereMissile for each orbiting satellite. Disable this, or leave the prefab empty, to use the original procedural sphere and trail fallback.")]
        public bool UseModularSphereMissileVisual = true;
        public GameObject ModularSphereMissilePrefab;
        [Min(0.001f)] public float ModularSphereMissileScale = 0.3f;
        [Min(0.05f)] public float OrbitingOrbRadius = 0.3f;
        [Min(0.1f)] public float OrbitRadius = 2.2f;
        public PlayerStrikeOrbSatelliteTuning[] OrbitingOrbs;
        [Header("Orbiting Orb Trails")]
        [Min(0.01f)] public float OrbTrailDuration;
        [Min(0.001f)] public float OrbTrailStartWidth;
        [Min(0f)] public float OrbTrailEndWidth;
        [Min(0.001f)] public float OrbTrailMinimumVertexDistance;
        [Range(0, 8)] public int OrbTrailCornerVertices;
        [ColorUsage(false, true)] public Color OrbTrailColor;
        [ColorUsage(false, true)] public Color OrbTrailEmission;
        [Min(0.1f)] public float ChargeHaloRadius = 1.9f;
        [Min(0.01f)] public float ChargeHaloThickness = 0.075f;
        [Min(0f)] public float RingRotationSpeed = 18f;
        [Min(0f)] public float FacingSharpness = 9f;
        [ColorUsage(false)] public Color BodyColor = new Color(0.018f, 0.028f, 0.07f);
        [ColorUsage(false, true)] public Color BodyEmission = new Color(0.08f, 0.18f, 0.8f);
        [ColorUsage(false)] public Color LightningColor = new Color(0.55f, 0.86f, 1f);
        [ColorUsage(false, true)] public Color LightningEmission = new Color(7.5f, 12f, 18f);
        [ColorUsage(false)] public Color WarningColor = new Color(0.18f, 0.42f, 0.62f);
        [ColorUsage(false, true)] public Color WarningEmission = new Color(0.45f, 2.8f, 5.8f);
        [ColorUsage(false, true)] public Color OrbInnerColor;
        [ColorUsage(false, true)] public Color OrbOuterColor;
        [Range(0.01f, 1f)] public float OrbGradientWidth;

        public float EvaluateDetectionRange(int rank)
        {
            float rankProgress = Mathf.Clamp01(
                rank / (float)Mathf.Max(1, DetectionRangeRankCeiling));
            return Mathf.Lerp(DetectionRange, DetectionRangeAtRankCeiling, rankProgress);
        }
    }

    [System.Serializable]
    public sealed class VesperKiteTuning
    {
        public bool Enabled = true;
        [Min(1f)] public float MaximumHealth = 320f;
        [Min(0)] public int GoldReward = 120;

        [Header("High-Altitude Spawning")]
        [Range(1, 12)] public int EnemyCount = 5;
        [Min(20f)] public float MinimumSpawnDistance = 220f;
        [Min(20f)] public float MaximumSpawnDistance = 360f;
        [Min(50f)] public float RepositionDistance = 540f;
        [Tooltip("Minimum patrol height above the sampled dune surface.")]
        [Min(10f)] public float MinimumAltitude = 175f;
        [Tooltip("Maximum patrol height above the sampled dune surface.")]
        [Min(10f)] public float MaximumAltitude = 275f;
        [Min(1f)] public float PatrolOrbitRadius = 150f;
        [Min(0f)] public float PatrolSpeed = 13f;
        [Min(0f)] public float PatrolAngularSpeed = 4f;
        [Min(0f)] public float HoverAmplitude = 4.5f;
        [Min(0f)] public float HoverFrequency = 0.34f;

        [Header("Redshift Procession")]
        [Min(1f)] public float DetectionRange = 430f;
        [Tooltip("Multiplier applied to the Vesper Kite's detection range while the player is stably grounded.")]
        [Range(0f, 1f)] public float GroundedTargetDetectionRangeMultiplier = 0.2f;
        [Min(0.1f)] public float AttackInterval = 10f;
        [Range(0f, 1f)] public float MinimumInitialAttackDelayMultiplier = 0.25f;
        [Range(0f, 1f)] public float MaximumInitialAttackDelayMultiplier = 0.85f;
        [Min(0f)] public float AttackWindUpDuration = 2.25f;
        [Tooltip("Pilgrims fired per procession at ranks 0 and 1.")]
        [Range(1, 8)] public int PilgrimsPerProcessionAtRankOne = 1;
        [Tooltip("Pilgrims fired per procession at the pilgrim count rank ceiling.")]
        [Range(1, 8)] public int PilgrimsPerProcessionAtRankCeiling = 5;
        [Tooltip("Rank at which pilgrims per procession reaches its ceiling value.")]
        [Min(1)] public int PilgrimCountRankCeiling = 20;
        [Range(1, 16)] public int MaximumActivePilgrims = 6;
        [Min(0f)] public float PilgrimSpawnRadius = 4.2f;
        [Min(0f)] public float PilgrimSpawnForwardOffset = 1.2f;
        [Min(0.1f)] public float PilgrimInitialSpeed = 4f;
        [Tooltip("Pilgrim acceleration at risk 0.")]
        [Min(0f)] public float PilgrimAcceleration = 1.6f;
        [Tooltip("Pilgrim acceleration at the pilgrim movement risk ceiling.")]
        [Min(0f)] public float PilgrimAccelerationAtRiskCeiling = 4.8f;
        [Tooltip("Pilgrim maximum speed at risk 0.")]
        [Min(0.1f)] public float PilgrimMaximumSpeed = 36f;
        [Tooltip("Pilgrim maximum speed at the pilgrim movement risk ceiling.")]
        [Min(0.1f)] public float PilgrimMaximumSpeedAtRiskCeiling = 72f;
        [Tooltip("Pilgrim turn rate in degrees per second at risk 0.")]
        [Min(0f)] public float PilgrimTurnRate = 82f;
        [Tooltip("Pilgrim turn rate in degrees per second at the pilgrim movement risk ceiling.")]
        [Min(0f)] public float PilgrimTurnRateAtRiskCeiling = 100f;
        [Tooltip("Risk at which pilgrim acceleration, maximum speed, and turn rate reach their ceiling values.")]
        [Min(1)] public int PilgrimMovementRiskCeiling = 20;
        [Tooltip("Height above the local dune surface where pilgrim movement scaling and perfect turn tracking reach their maximum.")]
        [Min(0.1f)] public float PilgrimAltitudeScalingHeight = 225f;
        [Tooltip("Ease-in curve used to scale Pilgrim maximum speed from ground level to the altitude scaling height.")]
        public AnimationCurve PilgrimAltitudeSpeedCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(1f, 1f, 2f, 2f));
        [Tooltip("Maximum-speed multiplier reached at the pilgrim altitude scaling height.")]
        [Min(1f)] public float PilgrimMaximumSpeedAltitudeMultiplier = 3f;
        [Tooltip("Acceleration multiplier reached at the pilgrim altitude scaling height.")]
        [Min(1f)] public float PilgrimAccelerationAltitudeMultiplier = 3f;
        [Tooltip("Ease-in curve that scales Pilgrim acceleration and maximum speed as the drone approaches its current maximum speed.")]
        public AnimationCurve PilgrimDroneSpeedMovementCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(1f, 1f, 2f, 2f));
        [Tooltip("Pilgrim acceleration and maximum-speed multiplier reached when the drone reaches its current maximum speed.")]
        [Min(1f)] public float PilgrimMovementMultiplierAtDroneMaximumSpeed = 5f;
        [Min(1f)] public float PilgrimMaximumHealth = 30f;
        public bool PrioritizePilgrimsForTargeting = true;
        [Min(0.01f)] public float PilgrimCollisionRadius = 2.25f;
        [Min(0f)] public float PilgrimDamage = 32f;
        public string PilgrimDamageSource = "Vesper Kite Redshift Procession";
        public string PilgrimDeathMessage = "Consumed by the Vesper Kite's Redshift Procession.";

        public float EvaluateDetectionRange(bool targetIsGrounded)
        {
            float range = Mathf.Max(1f, DetectionRange);
            return targetIsGrounded
                ? range * Mathf.Clamp01(GroundedTargetDetectionRangeMultiplier)
                : range;
        }

        public int EvaluatePilgrimsPerProcession(int rank)
        {
            int rankOneCount = Mathf.Max(1, PilgrimsPerProcessionAtRankOne);
            if (rank <= 1)
            {
                return rankOneCount;
            }

            int ceilingCount = Mathf.Max(
                rankOneCount,
                PilgrimsPerProcessionAtRankCeiling);
            int ceilingRank = Mathf.Max(1, PilgrimCountRankCeiling);
            float rankProgress = ceilingRank > 1
                ? Mathf.InverseLerp(1, ceilingRank, Mathf.Clamp(rank, 1, ceilingRank))
                : 1f;
            return Mathf.RoundToInt(Mathf.Lerp(rankOneCount, ceilingCount, rankProgress));
        }

        public float EvaluatePilgrimAcceleration(
            int risk,
            float heightAboveGround,
            float droneSpeed,
            float droneMaximumSpeed)
        {
            float riskScaledAcceleration = Mathf.Lerp(
                PilgrimAcceleration,
                PilgrimAccelerationAtRiskCeiling,
                EvaluatePilgrimMovementRisk(risk));
            float altitudeScaledAcceleration = riskScaledAcceleration * Mathf.Lerp(
                1f,
                Mathf.Max(1f, PilgrimAccelerationAltitudeMultiplier),
                EvaluatePilgrimAltitude(heightAboveGround));
            return altitudeScaledAcceleration * EvaluatePilgrimDroneSpeedMultiplier(
                droneSpeed,
                droneMaximumSpeed);
        }

        public float EvaluatePilgrimMaximumSpeed(
            int risk,
            float heightAboveGround,
            float droneSpeed,
            float droneMaximumSpeed)
        {
            float riskScaledMaximumSpeed = Mathf.Lerp(
                PilgrimMaximumSpeed,
                PilgrimMaximumSpeedAtRiskCeiling,
                EvaluatePilgrimMovementRisk(risk));
            float altitudeScaledMaximumSpeed = riskScaledMaximumSpeed * Mathf.Lerp(
                1f,
                Mathf.Max(1f, PilgrimMaximumSpeedAltitudeMultiplier),
                EvaluatePilgrimAltitudeSpeed(heightAboveGround));
            return altitudeScaledMaximumSpeed * EvaluatePilgrimDroneSpeedMultiplier(
                droneSpeed,
                droneMaximumSpeed);
        }

        public float EvaluatePilgrimTurnRate(int risk)
        {
            return Mathf.Lerp(
                PilgrimTurnRate,
                PilgrimTurnRateAtRiskCeiling,
                EvaluatePilgrimMovementRisk(risk));
        }

        public float EvaluatePilgrimPerfectTurnBlend(float heightAboveGround)
        {
            return EvaluatePilgrimAltitude(heightAboveGround);
        }

        private float EvaluatePilgrimMovementRisk(int risk)
        {
            return Mathf.Clamp01(
                risk / (float)Mathf.Max(1, PilgrimMovementRiskCeiling));
        }

        private float EvaluatePilgrimAltitude(float heightAboveGround)
        {
            return Mathf.Clamp01(
                Mathf.Max(0f, heightAboveGround) /
                Mathf.Max(0.1f, PilgrimAltitudeScalingHeight));
        }

        private float EvaluatePilgrimAltitudeSpeed(float heightAboveGround)
        {
            float altitude = EvaluatePilgrimAltitude(heightAboveGround);
            if (PilgrimAltitudeSpeedCurve == null ||
                PilgrimAltitudeSpeedCurve.length == 0)
            {
                return altitude;
            }

            return Mathf.Clamp01(PilgrimAltitudeSpeedCurve.Evaluate(altitude));
        }

        private float EvaluatePilgrimDroneSpeedMultiplier(
            float droneSpeed,
            float droneMaximumSpeed)
        {
            float speedProgress = Mathf.Clamp01(
                Mathf.Max(0f, droneSpeed) / Mathf.Max(0.1f, droneMaximumSpeed));
            float easedProgress = PilgrimDroneSpeedMovementCurve == null ||
                PilgrimDroneSpeedMovementCurve.length == 0
                    ? speedProgress
                    : Mathf.Clamp01(PilgrimDroneSpeedMovementCurve.Evaluate(speedProgress));
            return Mathf.Lerp(
                1f,
                Mathf.Max(1f, PilgrimMovementMultiplierAtDroneMaximumSpeed),
                easedProgress);
        }

        [Header("Portal Reversal")]
        [Min(0f)] public float PortalExitOffset = 4f;
        [Min(0.1f)] public float ReflectedSpeedMultiplier = 1.45f;
        [Min(0f)] public float ReflectedTurnRate = 220f;
        [Min(0.01f)] public float ReflectedCollisionRadius = 3.4f;

        [Header("Vesper Kite Presentation")]
        [Tooltip("Use the original procedural Vesper Kite instead of the configured prefab.")]
        public bool UseProceduralVisualFallback;
        [Tooltip("Resources path for the Vesper Kite prefab visual.")]
        public string PrefabResourcePath = "mantaPrefab";
        [Tooltip("Full Animator state path played when the Vesper Kite prefab spawns.")]
        public string PrefabAnimationStateName = "Base Layer.Take 001";
        [Min(0f)] public float PrefabAnimationSpeed = 1f;
        public Vector3 PrefabLocalPosition = Vector3.zero;
        public Vector3 PrefabLocalEulerAngles = Vector3.zero;
        public Vector3 PrefabLocalScale = Vector3.one;
        [Min(0.1f)] public float VisualScale = 5.5f;
        public Vector3 BodyScale = new Vector3(1.25f, 0.22f, 1.65f);
        public Vector3 WingScale = new Vector3(1.85f, 0.12f, 1.15f);
        [Min(0f)] public float WingOffset = 1.25f;
        [Range(0f, 80f)] public float WingSweepDegrees = 18f;
        [Range(-45f, 45f)] public float WingDihedralDegrees = 7f;
        [Range(0f, 0.5f)] public float WingPulseAmount = 0.08f;
        [Min(0f)] public float WingPulseSpeed = 1.7f;
        [Range(0.1f, 1f)] public float AuroraScaleMultiplier = 0.78f;
        public float AuroraVerticalOffset = -0.08f;
        public Vector3 TailScale = new Vector3(0.16f, 0.1f, 2.2f);
        [Min(0.1f)] public float CollisionRadius = 12.5f;
        public Vector3 CoreScale = new Vector3(0.42f, 0.18f, 0.42f);
        public Vector3 CoreOffset = new Vector3(0f, -0.12f, 0.32f);
        [Range(1f, 3f)] public float CoreWindUpScale = 1.8f;
        [Min(0f)] public float FacingSharpness = 4.5f;

        [Header("Pilgrim Presentation")]
        [Min(0.1f)] public float PilgrimVisualScale = 1.15f;
        public Vector3 PilgrimCoreScale = new Vector3(0.42f, 0.42f, 0.72f);
        [Min(0.1f)] public float PilgrimRingRadius = 0.72f;
        [Min(0.01f)] public float PilgrimRingThickness = 0.055f;
        [Range(2, 8)] public int PilgrimNodeCount = 3;
        public Vector3 PilgrimNodeScale = new Vector3(0.12f, 0.12f, 0.3f);
        [Min(0f)] public float PilgrimRingRotationSpeed = 180f;
        [Tooltip("Resources-relative path of the rift effect centered inside each Vesper missile.")]
        public string PilgrimRiftEffectResourcePath = "RiftMissilePurple";
        public Vector3 PilgrimRiftEffectLocalPosition = Vector3.zero;
        public Vector3 PilgrimRiftEffectLocalEulerAngles = Vector3.zero;
        public Vector3 PilgrimRiftEffectLocalScale = Vector3.one;
        [Min(0.001f)] public float TetherWidth = 0.055f;
        [Tooltip("Width at both tether endpoints as a multiplier of its center width.")]
        [Range(0f, 1f)] public float TetherEndWidthMultiplier = 0.72f;
        [Tooltip("Opacity at both tether endpoints as a multiplier of its center opacity.")]
        [Range(0f, 1f)] public float TetherEndAlphaMultiplier = 0.7f;
        [Min(0f)] public float TetherPulseSpeed = 7f;
        [Range(0f, 1f)] public float TetherPulseAmount = 0.28f;

        [Header("Materials")]
        [ColorUsage(false)] public Color BodyColor = new Color(0.008f, 0.01f, 0.018f);
        [ColorUsage(false, true)] public Color BodyEmission = new Color(0.025f, 0.035f, 0.09f);
        [Range(0f, 1f)] public float BodySmoothness = 0.82f;
        [Range(0f, 1f)] public float BodyMetallic = 0.88f;
        [ColorUsage(false)] public Color AuroraColor = new Color(0.08f, 0.34f, 0.42f);
        [ColorUsage(false, true)] public Color AuroraEmission = new Color(0.18f, 2.4f, 3.8f);
        [ColorUsage(false)] public Color PilgrimColor = new Color(0.42f, 0.08f, 0.34f);
        [ColorUsage(false, true)] public Color PilgrimEmission = new Color(4.8f, 0.22f, 3.2f);
        [ColorUsage(false)] public Color ReflectedColor = new Color(1f, 0.72f, 0.18f);
        [ColorUsage(false, true)] public Color ReflectedEmission = new Color(10f, 4.8f, 0.45f);
        [ColorUsage(false)] public Color TetherColor = new Color(0.75f, 0.16f, 0.64f, 0.8f);
        [ColorUsage(false, true)] public Color TetherEmission = new Color(4.5f, 0.25f, 3.8f, 1f);
    }

    public enum DesertAtlasChallengeType
    {
        VectorPass = 1,
        OrbitTrace = 2,
        RelaySequence = 4,
        AerialSlalom = 5,
        DuneSkim = 6,
        PrecisionDive = 7,
        PulseDecode = 8,
        ReverseOrbit = 9,
        TouchdownScan = 10,
        FluxWeave = 11,
    }

    [System.Serializable]
    public sealed class DesertAtlasSiteDefinition
    {
        [Tooltip("Stable identifier written to the Desert Atlas .dat save file.")]
        public string PersistentId = "signal-site";
        public string DisplayName = "UNIDENTIFIED SIGNAL";
        [TextArea(2, 5)] public string Description = "No survey record available.";
        [Tooltip("Persistent logical X/Z position in the endless desert.")]
        public Vector2 WorldPosition;
        [Min(0)] public int GoldReward = 100;
        public Color SignalColor = new Color(0.12f, 0.85f, 1f, 1f);
        [Header("Progression")]
        [Min(0)] public int RequiredDiscoveries;
        public bool IsFinalSignal;
        [Header("Flight Challenge")]
        public DesertAtlasChallengeType ChallengeType = DesertAtlasChallengeType.VectorPass;
        public string ChallengeInstruction = "HIT THE CORE IN FLIGHT";
        [Tooltip("Seconds for timed challenges or degrees for orbit challenges.")]
        [Min(0.1f)] public float RequiredAmount = 4f;
        [Tooltip("Seconds used by the final phase of multi-stage relay challenges.")]
        [Min(0.1f)] public float SecondaryRequiredAmount = 4f;
        [Min(0f)] public float MinimumSpeed;
        [Min(0f)] public float TargetHeightAboveSignal = 12f;
        [Min(0.1f)] public float HeightTolerance = 5f;
        [Header("Optional Mastery Bonus")]
        [Min(0f)] public float BonusTimeLimit;
        [Min(0)] public int BonusGoldReward;

        [Header("Photography Capture Definition")]
        [Range(0.1f, 1.5f)] public float PhotoCaptureRegionScale = 0.92f;
        [Range(0.001f, 1f)] public float MinimumPhotoScreenCoverage = 0.035f;
        [Range(0.001f, 1f)] public float MaximumPhotoScreenCoverage = 0.92f;
        [Range(0f, 1f)] public float MinimumPhotoReadableAngle = 0.08f;
        [Range(0f, 1f)] public float RequiredPhotoVisiblePercentage = 0.75f;
        public bool AllowPartialPhotoOcclusion = true;
    }

    [System.Serializable]
    public sealed class DesertAtlasTuning
    {
        public bool Enabled = true;

        [Header("Unlock and Discovery")]
        [Min(0)] public int UnlockCompletedDeliveries;
        [Min(1f)] public float ScanRadius = 24f;
        [Min(0f)] public float ScanProgressDecayPerSecond = 0.7f;
        [Min(1f)] public float SiteVisualSpawnDistance = 950f;
        [Min(1f)] public float SiteVisualDespawnDistance = 1100f;
        public Key ScanKey = Key.E;
        public string ScanInterruptedText = "SIGNAL ANALYSIS INTERRUPTED";
        public string DiscoveryStatusFormat = "ATLAS UPDATED — {0}  +{1} GOLD  /  MASTERY +{2}";
        public string PhotographedDiscoveryStatusFormat = "ATLAS UPDATED — {0}  +{1} GOLD";
        [Min(1)] public int MilestoneInterval = 5;
        [Min(0)] public int MilestoneGoldReward = 300;
        public string MilestoneStatusFormat = "{0}\nSURVEY MILESTONE {1}/{2} — +{3} GOLD";
        [Min(0)] public int AtlasCompletionGoldReward = 5000;
        public string AtlasCompletionStatusFormat =
            "ALL GLYPHS DOCUMENTED — DESERT ATLAS COMPLETE  +{0} GOLD\n" +
            "BRUSHED METAL GLYPHS NOW AVAILABLE AT THE UPGRADE TERMINAL";

        [Header("Atlas Terminal")]
        public Vector3 TerminalLocalPosition = new Vector3(11f, 0f, 0f);
        public Vector3 TerminalLocalEulerAngles = new Vector3(0f, 90f, 0f);
        [Min(1f)] public float TerminalInteractionRadius = 6f;
        public string TerminalName = "DESERT ATLAS";
        public string TerminalNearbyPrompt = "PRESS E — OPEN DESERT ATLAS";
        public string LockedNearbyPromptFormat = "DESERT ATLAS LOCKED — COMPLETE {0} MORE CONTRACTS";
        public string TerminalTitle = "DESERT ATLAS";
        public string TerminalClosePrompt = "ESC  CLOSE";
        public string TerminalProgressFormat = "SURVEY COMPLETION  {0} / {1}";
        public string TerminalLockedTitle = "ATLAS NETWORK OFFLINE";
        public string TerminalLockedBodyFormat = "Complete {0} more delivery contract{1} to receive survey clearance.";
        public string TerminalUnknownSiteFormat = "SIGNAL {0:00} — UNRESOLVED";
        public string TerminalUnknownDescription = "Survey data encrypted. Locate this signal in free roam.";
        public string TerminalStagedLockFormat = "LOCKED — RESOLVE {0} MORE SIGNAL{1}";
        public string TerminalAvailableStatus = "SIGNAL AVAILABLE";
        public string TerminalFinalSignalStatus = "FINAL SIGNAL AVAILABLE";
        public string TerminalDiscoveredStatus = "CATALOGUED  +{0} GOLD";
        public string TerminalDiscoveredEntryFormat = "{0}  /  {1}";
        public string TerminalChallengeFormat = "{0}  /  {1}";
        public string TerminalChallengeWithBonusFormat = "{0}  /  {1}  /  MASTERY {2:0}s +{3} GOLD";
        public string HudTitleFormat = "DESERT ATLAS";
        public string HudAllDiscoveredText = "ALL SIGNALS CATALOGUED";
        public string HudSignalsLockedText = "FURTHER SIGNALS ENCRYPTED — REVIEW THE ATLAS";
        public string HudNearestSignalFormat = "NEAREST SIGNAL  {0:0} m  {1}";
        public string HudChallengeFormat = "{0}\n{1}";
        public string OrbitProgressFormat = "TRACE  {0:0}° / {1:0}°";
        public string VectorPassProgressFormat = "VECTOR PASS  {0:0}%  SPEED {1:0.0}";
        public string VectorPassNeedFlightText = "ENTER FLIGHT MODE THROUGH THE NEARBY RING";
        public string VectorPassNeedSpeedFormat = "MORE SPEED REQUIRED  {0:0.0} / {1:0.0} M/S";
        public string TimedBonusProgressFormat = "{0}  /  BONUS {1:0}s  +{2}";
        public string RelayStageOneApproachFormat = "PHASE 1/3 — APPROACH CORE  {0:0}m";
        public string RelayStageOneProgressFormat = "PHASE 1/3 — HOLD E TO SYNCHRONIZE  {0:0}%";
        public string RelayStageTwoNeedArmText = "PHASE 2/3 — FLY OUT THROUGH THE RELAY RING";
        public string RelayStageTwoProgressFormat = "PHASE 2/3 — STRIKE THE CORE  {0:0}%";
        public string RelayStageThreeProgressFormat = "PHASE 3/3 — HOLD ALTITUDE  {0:0.0}s / {1:0.0}s";
        public string RelayStageAdvancedFormat = "RELAY PHASE {0}/3 COMPLETE";
        public string RelayObjectiveIndicatorLabel = "RELAY";
        public string SlalomProgressFormat = "SLALOM GATE  {0}/{1}  SPEED {2:0.0}";
        public string SkimProgressFormat = "DUNE SKIM  {0:0.0}s / {1:0.0}s  HEIGHT {2:0.0}m";
        public string DiveClimbFormat = "CLIMB TO ARM DIVE  {0:0}m / {1:0}m";
        public string DiveArmedFormat = "DIVE ARMED — STRIKE CORE  DESCENT {0:0.0} M/S";
        public string PulseReadyFormat = "PULSE OPEN — PRESS E  {0}/{1}";
        public string PulseWaitFormat = "WAIT FOR PULSE  {0}/{1}";
        public string ReverseOrbitFirstFormat = "ORBIT PHASE 1  {0:0}° / {1:0}°";
        public string ReverseOrbitSecondFormat = "REVERSE DIRECTION  {0:0}° / {1:0}°";
        public string TouchdownArmFormat = "FLY ABOVE CORE TO ARM  {0:0}m / {1:0}m";
        public string TouchdownLandText = "TOUCH DOWN INSIDE THE SIGNAL RING";
        public string TouchdownScanFormat = "LANDED SCAN  {0:0.0}s / {1:0.0}s";
        public string FluxOuterText = "FLUX WEAVE — EXIT TO THE OUTER RING";
        public string FluxInnerFormat = "FLUX WEAVE — CUT THROUGH CORE  {0}/{1}";
        [Min(0f)] public float DiscoveryStatusDuration = 4f;
        [Min(0f)] public float ScanInterruptedStatusDuration = 2f;

        [Header("Signal Visual")]
        [Min(0.1f)] public float BaseRadius = 5.5f;
        [Min(0.1f)] public float BaseHeight = 0.7f;
        [Min(0.1f)] public float CoreRadius = 1.25f;
        [Min(0.1f)] public float CoreHeight = 4.8f;
        [Range(3, 32)] public int RingSegmentCount = 16;
        [Range(1, 5)] public int RingCount = 3;
        [Min(0.1f)] public float RingRadius = 5.8f;
        [Min(0.01f)] public float RingSegmentWidth = 0.16f;
        [Min(0.01f)] public float RingSegmentDepth = 1.65f;
        [Min(0.1f)] public float RingHeightSpacing = 1.35f;
        [Min(0f)] public float RingRotationSpeed = 28f;
        [Min(0f)] public float PulseSpeed = 2.4f;
        [Range(0f, 1f)] public float PulseScaleAmount = 0.12f;
        [Min(0f)] public float HeightAboveTerrain = 0.3f;
        [ColorUsage(false)] public Color DiscoveredColor = new Color(0.25f, 0.72f, 0.42f, 1f);
        [Min(1f)] public float DiscoveredLoreRadius = 32f;
        [Min(0f)] public float DiscoveredMarkerHeight = 8f;
        [Min(0.5f)] public float DiscoveredMarkerRadius = 4.5f;
        [Range(6, 32)] public int DiscoveredMarkerSegmentCount = 14;
        [Min(0.01f)] public float DiscoveredMarkerSegmentThickness = 0.18f;
        [Min(0.01f)] public float DiscoveredMarkerSegmentLength = 1.4f;
        [Min(0.1f)] public float DiscoveredMarkerDiamondSize = 0.7f;
        [Min(0f)] public float DiscoveredMarkerDiamondHeight = 1.4f;
        [Min(0f)] public float DiscoveredMarkerRotationSpeed = 38f;
        [Min(0f)] public float DiscoveredMarkerPulseSpeed = 2.2f;
        [Range(0f, 1f)] public float DiscoveredMarkerPulseAmount = 0.12f;
        [Min(0f)] public float SignalBaseColorMultiplier = 0.24f;
        [Min(0f)] public float SignalEmissionMultiplier = 3.5f;
        [Min(1f)] public float ActiveChallengeEmissionMultiplier = 2f;

        [Header("Sky Beacon Visibility")]
        [Min(1f)] public float SignalBeamHeight = 90f;
        [Min(0.01f)] public float SignalBeamRadius = 0.22f;
        [Range(0f, 1f)] public float SignalBeamPulseAmount = 0.22f;
        [Tooltip("HDR emission used only by the vertical Atlas beacon so it produces a strong bloom halo.")]
        [Min(0f)] public float SignalBeamEmissionMultiplier = 10.5f;
        [Range(0.05f, 1f)] public float SignalBeamCoreRadiusMultiplier = 0.38f;
        [ColorUsage(false)] public Color SignalBeamCoreColor = Color.white;
        [Range(0f, 1f)] public float SignalBeamCoreWhiteness = 0.72f;
        [Min(1f)] public float SignalBeamCoreEmissionMultiplier = 2.25f;
        [Range(0, 8)] public int SignalBeamLocatorBandCount = 3;
        [Range(6, 32)] public int SignalBeamLocatorBandSegments = 16;
        [Min(0.1f)] public float SignalBeamLocatorBandRadius = 1.8f;
        [Min(0.01f)] public float SignalBeamLocatorBandThickness = 0.12f;
        [Min(0.01f)] public float SignalBeamLocatorBandLength = 0.7f;
        [Min(0f)] public float SignalBeamLocatorBandBottomHeight = 24f;
        [Min(0f)] public float SignalBeamLocatorBandSpacing = 22f;
        [Min(0f)] public float SignalBeamLocatorBandRotationSpeed = 20f;
        [Min(1f)] public float ActiveChallengeRotationMultiplier = 3f;
        [Min(0)] public int CompletionBurstParticleCount = 54;
        [Min(0.1f)] public float CompletionBurstLifetime = 2.4f;
        [Min(0f)] public float CompletionBurstSpeed = 13f;
        [Min(0.01f)] public float CompletionBurstMinimumSize = 0.12f;
        [Min(0.01f)] public float CompletionBurstMaximumSize = 0.42f;

        [Header("Ambient Signal Particles")]
        [Min(0f)] public float AmbientParticleRate = 8f;
        [Min(1f)] public float ActiveChallengeParticleMultiplier = 3f;
        [Min(0.1f)] public float AmbientParticleLifetime = 2.8f;
        [Min(0f)] public float AmbientParticleSpeed = 1.5f;
        [Min(0.01f)] public float AmbientParticleMinimumSize = 0.05f;
        [Min(0.01f)] public float AmbientParticleMaximumSize = 0.16f;
        [Min(0.1f)] public float AmbientParticleRadius = 5.8f;

        [Header("Discovery Presentation")]
        [Min(0f)] public float DiscoveryPresentationDuration = 3.2f;
        [Min(0f)] public float DiscoveryFlashDuration = 0.7f;
        [Min(1f)] public float CompletionNotificationDurationMultiplier = 4f;
        [Min(36f)] public float CompletionNotificationBannerHeight = 128f;
        [Min(0f)] public float CompletionNotificationTextPadding = 12f;
        [ColorUsage(false)] public Color DiscoveryFlashColor = new Color(0.1f, 0.85f, 1f, 0.18f);
        [Min(160f)] public float DiscoveryBannerWidth = 620f;
        [Min(36f)] public float DiscoveryBannerHeight = 84f;
        [Range(0f, 1f)] public float DiscoveryBannerVerticalFraction = 0.24f;
        [Min(0f)] public float DiscoveryBannerSlideDistance = 28f;
        [ColorUsage(false)] public Color DiscoveryBannerColor = new Color(0.015f, 0.05f, 0.08f, 0.96f);
        [ColorUsage(false)] public Color DiscoveryBannerAccentColor = new Color(0.12f, 0.85f, 1f, 1f);
        [Min(1f)] public float DiscoveryBannerAccentHeight = 4f;
        [Min(9)] public int DiscoveryBannerFontSize = 18;
        [Header("Flight Challenge Rules")]
        [Min(0.1f)] public float OrbitMinimumRadius = 13f;
        [Min(0.1f)] public float OrbitMaximumRadius = 30f;
        [Min(0f)] public float OrbitProgressDecayPerSecond = 0.04f;
        [Min(0.1f)] public float AltitudeHoldHorizontalRadius = 20f;
        [Min(0f)] public float AltitudeHoldMaximumVerticalSpeed = 5f;
        [Min(0.1f)] public float VectorPassStartRadius = 62f;
        [Min(0.1f)] public float VectorPassFinishRadius = 8f;
        [Min(0f)] public float VectorPassProgressDecayPerSecond = 0.8f;
        [Min(0.1f)] public float RelayVectorArmRadius = 42f;

        [Header("Aerial Slalom")]
        [Range(3, 9)] public int SlalomMinimumGateCount = 3;
        [Range(3, 12)] public int SlalomMaximumGateCount = 9;
        [Min(1f)] public float SlalomGateSpacing = 18f;
        [Min(0f)] public float SlalomGateLateralOffset = 12f;
        [Min(0f)] public float SlalomGateVerticalOffset = 5f;
        [Min(0.5f)] public float SlalomGateRadius = 5f;
        [Min(0.05f)] public float SlalomGateThickness = 0.18f;
        [Range(6, 32)] public int SlalomGateSegments = 18;
        [Min(0.5f)] public float SlalomPassRadius = 5.5f;
        [Min(1f)] public float SlalomActivationPadding = 26f;
        [Range(0.1f, 1f)] public float SlalomPassedGateScale = 0.65f;
        [Min(0f)] public float SlalomCurrentGatePulseAmount = 0.12f;

        [Header("Dune Skim")]
        [Min(1f)] public float SkimChallengeRadius = 70f;
        [Min(0f)] public float SkimMinimumTerrainClearance = 1.5f;
        [Min(0f)] public float SkimProgressDecayPerSecond = 0.5f;

        [Header("Precision Dive")]
        [Min(1f)] public float DiveChallengeRadius = 70f;
        [Min(0f)] public float DiveMinimumDownwardSpeed = 12f;
        [Min(0.5f)] public float DiveCoreRadius = 7f;

        [Header("Pulse Decode")]
        [Min(0.2f)] public float PulseDecodeCycleDuration = 1.6f;
        [Range(0.05f, 0.45f)] public float PulseDecodeWindowFraction = 0.2f;
        [Range(0f, 1f)] public float PulseDecodeMistakePenalty = 0.5f;
        [Min(1f)] public float PulseDecodeOpenScaleMultiplier = 1.35f;

        [Header("Reverse Orbit")]
        [Min(0f)] public float ReverseOrbitDirectionToleranceDegrees = 0.1f;

        [Header("Touchdown Scan")]
        [Min(1f)] public float TouchdownChallengeRadius = 28f;
        [Min(0f)] public float TouchdownMaximumGroundSpeed = 3f;
        [Min(0f)] public float TouchdownProgressDecayPerSecond = 0.35f;

        [Header("Flux Weave")]
        [Min(1f)] public float FluxInnerRadius = 10f;
        [Min(1f)] public float FluxOuterRadius = 42f;
        [Min(0f)] public float FluxActivationPadding = 18f;
        [Min(0f)] public float FluxProgressDecayPerSecond = 0.15f;

        [Header("Site Flight Ring")]
        public bool SpawnChallengeFlightRing = true;
        [Min(1f)] public float ChallengeFlightRingDistance = 74f;
        [Min(0f)] public float ChallengeFlightRingHeight = 8f;
        [Min(0.75f)] public float ChallengeFlightRingRadius = 5.5f;

        [Header("Free Roam HUD")]
        [Min(0f)] public float HudGapBelowCompass = 10f;
        [Min(0f)] public float HudLeftMargin = 18f;
        [Min(0f)] public float HudTopMargin = 18f;
        [Min(160f)] public float HudWidth = 360f;
        [Min(60f)] public float HudHeight = 108f;
        [Min(60f)] public float HudExpandedHeight = 142f;
        [Min(0f)] public float HudPadding = 12f;
        public Vector2 HudShadowOffset = new Vector2(4f, 5f);
        [Min(0f)] public float HudHeaderHeight = 32f;
        [Min(1f)] public float HudAccentWidth = 4f;
        [Min(1f)] public float HudBorderThickness = 1f;
        [Min(0f)] public float HudDividerHeight = 1f;
        [Min(40f)] public float HudCountBadgeWidth = 62f;
        [Min(16f)] public float HudCountBadgeHeight = 20f;
        [Min(9)] public int HudTitleFontSize = 12;
        [Min(9)] public int HudBodyFontSize = 11;
        [Min(9)] public int HudMetaFontSize = 9;
        [Min(9)] public int HudMetricFontSize = 22;
        [Min(9)] public int HudCountFontSize = 11;
        [Min(0f)] public float HudContentTop = 39f;
        [Min(0f)] public float HudMetaHeight = 14f;
        [Min(0f)] public float HudMetricTop = 54f;
        [Min(0f)] public float HudMetricHeight = 30f;
        [Min(0f)] public float HudChallengeBodyTop = 54f;
        [Min(0f)] public float HudChallengeBodyHeight = 28f;
        [Min(0f)] public float HudChallengeProgressTop = 84f;
        [Min(0f)] public float HudChallengeProgressHeight = 30f;
        [Min(1f)] public float ScanBarHeight = 4f;
        [Min(0f)] public float HudScanBarBottomOffset = 14f;
        [Min(1f)] public float HudSurveyBarHeight = 5f;
        [Min(0f)] public float HudSurveyBarBottomMargin = 0f;
        [Range(3, 30)] public int HudSurveySegmentCount = 10;
        [Min(0f)] public float HudSurveySegmentGap = 2f;
        [Min(0f)] public float HudActivePulseSpeed = 3f;
        [Range(0f, 1f)] public float HudActivePulseAmount = 0.18f;
        public string HudSignalLabel = "ACTIVE SIGNAL";
        public string HudChallengeLabel = "GLYPH DOCUMENTATION";
        public string HudCountFormat = "{0} / {1}";
        public string HudDistanceFormat = "{0:0} m";
        public string HudNoSignalLabel = "ATLAS NETWORK";
        public string HudDiscoveredLabelFormat = "ARCHIVE RECOVERED  /  {0:0} m";
        [Min(9)] public int HudLoreTitleFontSize = 12;
        [Min(9)] public int HudLoreBodyFontSize = 10;
        [Min(0f)] public float HudLoreTitleTop = 54f;
        [Min(0f)] public float HudLoreTitleHeight = 18f;
        [Min(0f)] public float HudLoreBodyTop = 74f;
        [Min(0f)] public float HudLoreBodyHeight = 50f;
        [ColorUsage(false)] public Color HudPanelColor = new Color(0.012f, 0.025f, 0.045f, 0.95f);
        [ColorUsage(false)] public Color HudShadowColor = new Color(0f, 0f, 0f, 0.42f);
        [ColorUsage(false)] public Color HudHeaderColor = new Color(0.02f, 0.065f, 0.09f, 0.96f);
        [ColorUsage(false)] public Color HudBorderColor = new Color(0.16f, 0.36f, 0.44f, 0.9f);
        [ColorUsage(false)] public Color HudBadgeColor = new Color(0.035f, 0.14f, 0.18f, 0.96f);
        [ColorUsage(false)] public Color HudDiscoveredAccentColor = new Color(0.3f, 0.95f, 0.58f, 1f);
        [ColorUsage(false)] public Color HudTextColor = new Color(0.9f, 0.96f, 1f, 1f);
        [ColorUsage(false)] public Color HudMutedColor = new Color(0.55f, 0.65f, 0.72f, 1f);
        [ColorUsage(false)] public Color HudAccentColor = new Color(0.12f, 0.85f, 1f, 1f);
        [ColorUsage(false)] public Color ScanBarBackgroundColor = new Color(0.04f, 0.08f, 0.11f, 1f);
        [Range(0f, 1f)] public float StatusVerticalFraction = 0.18f;
        [Min(0f)] public float StatusHeight = 36f;

        [Header("Terminal UI")]
        [Min(480f)] public float TerminalReferenceWidth = 1600f;
        [Min(320f)] public float TerminalReferenceHeight = 900f;
        [Range(0.5f, 1.5f)] public float TerminalMinimumScale = 0.72f;
        [Range(0.5f, 1.5f)] public float TerminalMaximumScale = 1.08f;
        [Min(480f)] public float TerminalPanelWidth = 1060f;
        [Min(320f)] public float TerminalPanelHeight = 650f;
        [Min(0f)] public float TerminalScreenMargin = 28f;
        [Min(0f)] public float TerminalPadding = 28f;
        [Min(0f)] public float TerminalHeaderHeight = 116f;
        [Min(0f)] public float TerminalFooterHeight = 42f;
        [Min(0f)] public float TerminalEntryHeight = 78f;
        [Min(0f)] public float TerminalEntryGap = 8f;
        [Min(0f)] public float TerminalAccentBarHeight = 4f;
        [Min(0f)] public float TerminalBorderThickness = 2f;
        [Min(10)] public int TerminalTitleFontSize = 30;
        [Min(9)] public int TerminalBodyFontSize = 13;
        [Min(9)] public int TerminalMetaFontSize = 11;
        [Min(0f)] public float TerminalTitleTop = 18f;
        [Min(0f)] public float TerminalTitleHeight = 42f;
        [Min(0f)] public float TerminalCloseWidth = 150f;
        [Min(0f)] public float TerminalCloseHeight = 24f;
        [Min(0f)] public float TerminalProgressTop = 65f;
        [Min(0f)] public float TerminalProgressHeight = 24f;
        [Min(0f)] public float TerminalEntryPadding = 12f;
        [Min(0f)] public float TerminalEntryTitleTop = 7f;
        [Min(0f)] public float TerminalEntryTitleHeight = 22f;
        [Min(0f)] public float TerminalEntryDescriptionTop = 31f;
        [ColorUsage(false)] public Color TerminalBackdropColor = new Color(0.006f, 0.012f, 0.022f, 0.9f);
        [ColorUsage(false)] public Color TerminalPanelColor = new Color(0.018f, 0.035f, 0.055f, 0.98f);
        [ColorUsage(false)] public Color TerminalEntryColor = new Color(0.045f, 0.072f, 0.095f, 1f);
        [ColorUsage(false)] public Color TerminalAvailableEntryColor = new Color(0.035f, 0.13f, 0.16f, 1f);
        [ColorUsage(false)] public Color TerminalDiscoveredEntryColor = new Color(0.035f, 0.11f, 0.075f, 1f);
        [Range(0f, 1f)] public float TerminalAvailablePulseAmount = 0.18f;
        [Min(0f)] public float TerminalAvailablePulseSpeed = 2.2f;
        [ColorUsage(false)] public Color TerminalBorderColor = new Color(0.18f, 0.3f, 0.38f, 0.9f);
        [ColorUsage(false)] public Color TerminalTextColor = new Color(0.9f, 0.96f, 1f, 1f);
        [ColorUsage(false)] public Color TerminalMutedColor = new Color(0.55f, 0.65f, 0.72f, 1f);
        [ColorUsage(false)] public Color TerminalAccentColor = new Color(0.12f, 0.85f, 1f, 1f);

        [Header("Authored Signal Sites")]
        public List<DesertAtlasSiteDefinition> Sites = new List<DesertAtlasSiteDefinition>();

        public void EnsureInitialized()
        {
            Sites ??= new List<DesertAtlasSiteDefinition>();
        }
    }

    [System.Serializable]
    public sealed class GroundExploderTuning
    {
        public bool Enabled = true;
        [Min(1f)] public float MaximumHealth = 70f;
        [Min(0)] public int GoldReward = 15;
        [Tooltip("Expected number of ground exploders generated in each streamed desert chunk.")]
        [Min(0f)] public float DensityPerChunk = 0.28f;
        [Header("Patrol")]
        [Min(0f)] public float MovementSpeed = 5.5f;
        [Min(2f)] public float PatrolRadius = 18f;
        [Range(0f, 60f)] public float MaximumGroundSlope = 34f;
        [Header("Proximity Explosion")]
        [Min(0.5f)] public float DetectionRadius = 18f;
        [Min(0.1f)] public float WindUpDuration = 1.25f;
        [Tooltip("Explosion radius at risk 0.")]
        [Min(0.5f)] public float ExplosionRadius = 11f;
        [Tooltip("Explosion radius at the risk scaling ceiling.")]
        [Min(0.5f)] public float ExplosionRadiusAtRiskCeiling = 18.3f;
        [Min(0f)] public float MaximumDamage = 65f;
        [Tooltip("FMOD one-shot event played at the ground exploder when it detonates.")]
        public string ExplosionEvent = "event:/Explosion_Ground_Exploder";
        public string ExplosionDeathMessage = "Destroyed by a Ground Exploder blast.";
        [Header("Presentation")]
        [Tooltip("Optional visual effect spawned when the ground exploder detonates. If empty, the procedural explosion flash is used.")]
        public GameObject ExplosionPrefab;
        [Tooltip("Local position applied to the spawned explosion prefab.")]
        public Vector3 ExplosionPrefabLocalPosition = Vector3.zero;
        [Tooltip("Local Euler rotation offset added to the explosion prefab's authored rotation.")]
        public Vector3 ExplosionPrefabLocalEulerAngles = Vector3.zero;
        [Tooltip("Scale multiplier applied to the explosion prefab's authored scale.")]
        public Vector3 ExplosionPrefabLocalScale = Vector3.one;
        [Tooltip("Seconds before the spawned explosion prefab is destroyed. Set to 0 to let the prefab manage its own lifetime.")]
        [Min(0f)] public float ExplosionPrefabLifetime = 5.5f;
        [Tooltip("Optional second visual effect spawned alongside Explosion Prefab when the ground exploder detonates.")]
        public GameObject AdditionalExplosionPrefab;
        [Tooltip("Local position applied to the additional spawned explosion prefab.")]
        public Vector3 AdditionalExplosionPrefabLocalPosition = Vector3.zero;
        [Tooltip("Local Euler rotation offset added to the additional explosion prefab's authored rotation.")]
        public Vector3 AdditionalExplosionPrefabLocalEulerAngles = Vector3.zero;
        [Tooltip("Scale multiplier applied to the additional explosion prefab's authored scale.")]
        public Vector3 AdditionalExplosionPrefabLocalScale = Vector3.one;
        [Tooltip("Seconds before the additional spawned explosion prefab is destroyed. Set to 0 to let the prefab manage its own lifetime.")]
        [Min(0f)] public float AdditionalExplosionPrefabLifetime = 5.5f;
        [Tooltip("Visual scale at risk 0.")]
        [Min(0.1f)] public float VisualScale = 3f;
        [Tooltip("Visual scale at the risk scaling ceiling.")]
        [Min(0.1f)] public float VisualScaleAtRiskCeiling = 5f;
        [Tooltip("Risk at which visual scale and explosion radius reach their ceiling values.")]
        [Min(1)] public int RiskScalingCeiling = 20;

        public float EvaluateExplosionRadius(int risk)
        {
            return Mathf.Lerp(ExplosionRadius, ExplosionRadiusAtRiskCeiling, EvaluateRisk(risk));
        }

        public float EvaluateVisualScale(int risk)
        {
            return Mathf.Lerp(VisualScale, VisualScaleAtRiskCeiling, EvaluateRisk(risk));
        }

        private float EvaluateRisk(int risk)
        {
            return Mathf.Clamp01(risk / (float)Mathf.Max(1, RiskScalingCeiling));
        }
    }

    [System.Serializable]
    public sealed class RingTuning
    {
        [Header("Starting Size")]
        [Min(0.75f)] public float GroundRingRadius = 3.25f;
        [Min(0.75f)] public float FlightRingRadius = 3.55f;

        [Header("Placement")]
        [Tooltip("Minimum horizontal center-to-center distance between any two procedurally generated traversal rings, regardless of ring type.")]
        [Min(0f)] public float MinimumRingSeparation = 10f;
        [Tooltip("Minimum horizontal distance between a newly generated traversal ring center and any player, neutral, or rival courier drone.")]
        [Min(0f)] public float MinimumDroneSpawnSeparation = 14f;
        [Tooltip("Minimum horizontal distance from the hub center where traversal ring centers may spawn.")]
        [Min(0f)] public float HubExclusionRadius = 38f;
        [Tooltip("Multiplier applied to a portal's visual radius when reserving clear space around it. A portal spins to face the camera, so the reserved space is a sphere covering every direction the portal can face. Pyramids, obelisks, buildings, and landmarks may not overlap it.")]
        [Min(1f)] public float PortalStructureClearanceMultiplier = 1.15f;
        [Tooltip("Extra world units added to a portal's reserved clear space on top of its scaled visual radius.")]
        [Min(0f)] public float PortalStructureClearancePadding = 2f;

        [Header("Billboarding")]
        [Tooltip("Distance from the drone center at which rings freeze their current orientation instead of continuing to face the camera.")]
        [Min(0f)] public float BillboardDisableRadius = 14f;

        [Header("Ground Boost Ring Generation")]
        [Tooltip("Multiplier for the expected number of procedurally generated ground boost rings.")]
        [Min(0f)] public float GroundBoostRingAmountMultiplier = 1f;

        [Header("Blue Flight Ring Generation")]
        [Tooltip("Multiplier for the expected number of procedurally generated blue flight rings when the flight meter is empty.")]
        [FormerlySerializedAs("FlightRingAmountMultiplier")]
        [Min(1f)] public float FlightRingAmountMultiplierAtMinimumMeter = 5f;
        [Tooltip("Multiplier for the expected number of procedurally generated blue flight rings when the flight meter is full.")]
        [Min(1f)] public float FlightRingAmountMultiplierAtMaximumMeter = 1f;
        [Tooltip("A second multiplier applied after the flight-meter-based blue flight ring amount multiplier. Values below the reciprocal meter multiplier preserve the baseline flight ring density.")]
        [Min(0f)] public float SecondFlightRingAmountMultiplier = 1f;
        [Tooltip("Seconds before the same flight ring can restore the flight meter again.")]
        [Min(0f)] public float FlightMeterRewardCooldown = 5f;

        public float GetFlightRingAmountMultiplier(float flightMeterNormalized)
        {
            float meterBasedMultiplier = Mathf.Lerp(
                Mathf.Max(1f, FlightRingAmountMultiplierAtMinimumMeter),
                Mathf.Max(1f, FlightRingAmountMultiplierAtMaximumMeter),
                Mathf.Clamp01(flightMeterNormalized));
            return Mathf.Max(
                1f,
                meterBasedMultiplier * Mathf.Max(0f, SecondFlightRingAmountMultiplier));
        }

        [Header("Boost and Flight Ring Appearance")]
        [ColorUsage(false, true)] public Color BoostRingBaseColor = new Color(0.42f, 0.09f, 0.008f);
        [ColorUsage(false, true)] public Color BoostRingEmissionColor = new Color(3.6f, 0.72f, 0.025f);
        [ColorUsage(false, true)] public Color FlightRingBaseColor = new Color(0.004f, 0.19f, 0.32f);
        [ColorUsage(false, true)] public Color FlightRingEmissionColor = new Color(0f, 2f, 3.6f);

        [Header("Portal Prefab Visuals")]
        [Tooltip("When enabled, every traversal ring uses the built-in procedural portal visual instead of its assigned prefab. Individual missing prefab references also fall back procedurally.")]
        public bool UseProceduralPortalFallbackForAll;
        [Tooltip("Prefab used by standard flight portals.")]
        public GameObject FlightPortalPrefab;
        [Tooltip("Prefab used by ground boost portals.")]
        public GameObject GroundBoostPortalPrefab;
        [Tooltip("Prefab used by second-layer (upper flight) portals.")]
        public GameObject UpperFlightPortalPrefab;
        [Tooltip("Radius represented by one unit of prefab scale. Used to fit assigned portal prefabs to each ring's gameplay radius.")]
        [Min(0.01f)] public float PortalPrefabAuthoredRadius = 5f;
        [Tooltip("Additional scale applied after fitting an assigned portal prefab to its ring radius.")]
        [Min(0.01f)] public float PortalPrefabScaleMultiplier = 1f;

        [Header("Portal Linework")]
        [Tooltip("Opacity of the emissive rings, spokes, glyphs, and exterior rays. Empty space remains fully transparent.")]
        [Range(0f, 1f)] public float PortalLineOpacity = 0.9f;
        [Tooltip("HDR brightness multiplier applied only to portal energy, pushing its lines and halo into bloom without brightening the rest of the scene.")]
        [Min(0f)] public float PortalBloomIntensity = 0.6f;
        [Tooltip("Strength of derivative-based edge smoothing applied to portal strokes as they approach subpixel size.")]
        [Min(0f)] public float PortalScreenSpaceAntiAliasing = 0.25f;
        [Tooltip("Scales authored gameplay radii before drawing and testing the visible portal opening.")]
        [Range(0.25f, 1.5f)] public float PortalVisualRadiusMultiplier = 0.82f;
        [Min(0.5f)] public float PortalMinimumVisualRadius = 3.2f;
        [Min(0.5f)] public float PortalMaximumVisualRadius = 6.8f;
        [Min(0.01f)] public float PortalOuterLineThickness = 0.16f;
        [Min(0.01f)] public float PortalInnerLineThickness = 0.09f;
        [Range(0.01f, 0.49f)] public float PortalLineEdgeSoftness = 0.22f;
        [Range(1f, 8f)] public float PortalHaloWidthMultiplier = 3.2f;
        [Range(0f, 1f)] public float PortalHaloOpacity = 0.18f;
        [Range(24, 192)] public int PortalCircleSegments = 96;
        [Range(1, 6)] public int PortalConcentricRingCount = 2;
        [Range(0.1f, 0.9f)] public float PortalInnermostRingRadiusFraction = 0.38f;
        [Range(3, 32)] public int PortalSpokeCount = 12;
        [Range(0.1f, 0.9f)] public float PortalSpokeInnerRadiusFraction = 0.6f;
        [Min(0.01f)] public float PortalSpokeThickness = 0.075f;
        [Range(3, 32)] public int PortalGlyphCount = 16;
        [Range(0.1f, 0.95f)] public float PortalGlyphRadiusFraction = 0.74f;
        [Min(0.01f)] public float PortalGlyphStrokeThickness = 0.06f;
        [Range(0.01f, 0.25f)] public float PortalGlyphSizeFraction = 0.052f;
        [Header("Portal Rune Variation")]
        [Tooltip("Maximum proportional size difference above or below the base rune size. A value of 0.22 produces rune sizes from 78% to 122% of the base size.")]
        [Range(0f, 0.5f)] public float PortalRuneSizeVariation = 0.22f;
        [Tooltip("Maximum angular offset as a fraction of one evenly spaced rune slot.")]
        [Range(0f, 0.45f)] public float PortalRuneSpacingVariation = 0.24f;
        [Tooltip("Maximum clockwise or counterclockwise tilt from the rune's radial alignment.")]
        [Range(0f, 45f)] public float PortalRuneRotationVariationDegrees = 18f;
        [Tooltip("Maximum proportional brightness difference above or below the base rune brightness.")]
        [Range(0f, 0.4f)] public float PortalRuneBrightnessVariation = 0.18f;
        [Header("Portal Exterior Rays")]
        [Range(0, 24)] public int PortalExteriorRayCount = 9;
        [Range(0f, 0.5f)] public float PortalExteriorRayLengthFraction = 0.13f;

        [Header("Portal Traveling Light Pulses")]
        [Tooltip("Number of bright tracer segments traveling around each circular portal line.")]
        [Range(1, 8)] public int PortalTravelPulseCount = 3;
        [Tooltip("Tracer travel speed in complete rotations per second.")]
        [Min(0f)] public float PortalTravelPulseSpeed = 0.22f;
        [Tooltip("Length of each bright tracer as a fraction of the spacing between tracers.")]
        [Range(0.01f, 0.8f)] public float PortalTravelPulseWidth = 0.16f;
        [Tooltip("Additional HDR brightness applied at the center of a traveling tracer.")]
        [Min(0f)] public float PortalTravelPulseBrightness = 0.9f;
        [Tooltip("Phase difference between portal circles, preventing their tracers from lining up like spokes.")]
        [Range(0f, 1f)] public float PortalTravelPulseRingPhaseOffset = 0.17f;

        [Header("Portal Edge Sparks")]
        [Tooltip("Average number of sparks emitted from each portal per second.")]
        [Min(0f)] public float PortalSparkEmissionRate = 1.1f;
        [Min(0.01f)] public float PortalSparkMinimumLifetime = 0.55f;
        [Min(0.01f)] public float PortalSparkMaximumLifetime = 1.1f;
        [Min(0f)] public float PortalSparkMinimumSpeed = 0.7f;
        [Min(0f)] public float PortalSparkMaximumSpeed = 1.8f;
        [Min(0.001f)] public float PortalSparkMinimumSize = 0.08f;
        [Min(0.001f)] public float PortalSparkMaximumSize = 0.16f;
        [Tooltip("Distance outside the portal rim where sparks originate.")]
        [Min(0f)] public float PortalSparkEdgeOffset = 0.08f;
        [Tooltip("Normalized lifetime at which sparks begin fading.")]
        [Range(0f, 1f)] public float PortalSparkFadeStart = 0.25f;
        [Tooltip("Variation applied to the outward launch direction.")]
        [Range(0f, 1f)] public float PortalSparkDirectionRandomness = 0.24f;
        [Tooltip("HDR brightness multiplier applied only to portal sparks.")]
        [Min(0f)] public float PortalSparkBloomIntensity = 4.2f;
        [Tooltip("Length multiplier for the velocity-stretched spark streak.")]
        [Min(0f)] public float PortalSparkLengthScale = 2.4f;
        [Tooltip("Additional stretching contributed by each spark's velocity.")]
        [Min(0f)] public float PortalSparkVelocityScale = 0.3f;
        [Min(1)] public int PortalSparkMaximumParticles = 16;

        [Header("Portal Brightness Hierarchy")]
        [Min(0f)] public float PortalOuterRimBrightnessMultiplier = 1.25f;
        [Min(0f)] public float PortalStructuralLineBrightnessMultiplier = 0.82f;
        [Min(0f)] public float PortalRuneBrightnessMultiplier = 0.72f;
        [Min(0f)] public float PortalInnerRingBrightnessMultiplier = 0.58f;

        [Header("Portal Activation Reaction")]
        [Min(0.01f)] public float PortalActivationReactionDuration = 0.58f;
        [Tooltip("Peak HDR brightness multiplier during a portal activation.")]
        [Min(1f)] public float PortalActivationBloomMultiplier = 2.35f;
        [Tooltip("Minimum camera-fade visibility temporarily preserved at the start of an activation.")]
        [Range(0f, 1f)] public float PortalActivationMinimumVisibility = 0.45f;
        [Tooltip("Temporary multiplier applied to the portal's normal rotation speed.")]
        [Min(1f)] public float PortalActivationRotationMultiplier = 3.1f;
        [Tooltip("Final scale reached by the expanding activation rim.")]
        [Min(1f)] public float PortalActivationPulseExpansion = 1.42f;
        [Range(0f, 1f)] public float PortalActivationPulseOpacity = 0.9f;
        [Min(0.01f)] public float PortalActivationPulseLineThickness = 0.12f;

        [Header("Portal Depth Layers")]
        [Tooltip("Forward and backward spacing between selected portal linework layers.")]
        [Min(0f)] public float PortalLineLayerDepth = 0.22f;
        [Tooltip("Opacity multiplier for the recessed circles and runes.")]
        [Range(0f, 1f)] public float PortalRearLayerOpacityMultiplier = 0.62f;
        [Tooltip("Opacity multiplier for the forward circles, runes, and exterior rays.")]
        [Range(0f, 1f)] public float PortalFrontLayerOpacityMultiplier = 0.82f;

        [Header("Portal Camera Fade")]
        [Tooltip("Portal energy is invisible this close to the rendering camera, preventing giant screen-filling linework.")]
        [Min(0f)] public float PortalCameraFadeStartDistance = 5f;
        [Tooltip("Portal energy reaches full opacity at this camera distance.")]
        [Min(0f)] public float PortalCameraFadeEndDistance = 14f;

        [Header("Portal Distance Legibility")]
        [Tooltip("Camera distance where portal emission begins increasing to remain readable through haze and bright terrain.")]
        [Min(0f)] public float PortalDistanceVisibilityStartDistance = 35f;
        [Tooltip("Camera distance where the portal reaches its maximum distance-visibility emission boost.")]
        [Min(0f)] public float PortalDistanceVisibilityEndDistance = 180f;
        [Tooltip("Maximum HDR emission multiplier for distant portals. A value of one disables additional distance bloom.")]
        [Min(1f)] public float PortalDistanceVisibilityBloomMultiplier = 1.1f;
        [Header("Portal Animation and Layering")]
        [Min(0f)] public float PortalPulseSpeed = 1f;
        [Range(0f, 0.5f)] public float PortalPulseAmount = 0.08f;
        [Min(0f)] public float PortalLayerDepth = 0.32f;

        [Header("Upper Flight Ring Unlock")]
        [Tooltip("Number of distinct blue flight rings the player must cross before the upper-layer ring appears.")]
        [Min(1)] public int UpperFlightRingRequiredPasses = 100;

        [Header("Upper Flight Ring Generation")]
        [Tooltip("Independent procedural salt used for upper-layer positions, altitudes, rotations, and movement.")]
        public int UpperFlightRingSeedOffset = 19031;
        [Min(0.75f)] public float UpperFlightRingRadius = 5f;
        [Min(0f)] public float UpperFlightRingMinimumHeight = 45f;
        [Min(0f)] public float UpperFlightRingMaximumHeight = 70f;

        [Header("Upper Flight Ring Appearance")]
        [ColorUsage(false, true)] public Color UpperFlightRingBaseColor = new Color(0.24f, 0.015f, 0.42f);
        [ColorUsage(false, true)] public Color UpperFlightRingEmissionColor = new Color(4.8f, 0.08f, 8f);
        [Min(1f)] public float UpperFlightRingActiveScale = 1.35f;
        [Tooltip("Scale reached by upper flight rings when the drone reaches its current maximum flight speed.")]
        [Min(1f)] public float UpperFlightRingMaximumSpeedScale = 3f;
        [Min(0f)] public float UpperFlightRingScaleSharpness = 4.5f;
        [Min(0f)] public float UpperFlightRingRotationSpeed = 56f;

        [Header("Upper Flight Ring Motion and Speed")]
        [Min(0f)] public float UpperFlightModeMinimumHeightOffset;
        [Min(0f)] public float UpperFlightModeMaximumHeightOffset = 18f;
        [Min(0f)] public float UpperFlightModeHeightSharpness = 3f;
        [Tooltip("Multiplier applied to normal and maximum flight speed after crossing an upper-layer ring. A blue ring resets it to one.")]
        [Min(1f)] public float UpperFlightSpeedMultiplier = 1.6f;

        [Header("Upper Flight Ring HUD")]
        public bool ShowUpperFlightRingHud = true;
        public string UpperFlightRingHudTitle = "UPPER FLIGHT LAYER";
        public string UpperFlightRingHudProgressLabel = "UNIQUE WHITE RINGS";
        public string UpperFlightRingHudUnlockedLabel = "UPPER RING UNLOCKED";
        [Min(0f)]
        [Tooltip("Seconds the HUD remains visible after the upper flight layer unlocks. Uses unscaled time.")]
        public float UpperFlightRingHudUnlockedDuration = 5f;
        [Min(240f)] public float UpperFlightRingHudReferenceHeight = 1080f;
        [Range(0.25f, 2f)] public float UpperFlightRingHudMinimumScale = 0.65f;
        [Range(0.25f, 2f)] public float UpperFlightRingHudMaximumScale = 1.25f;
        [Min(0f)] public float UpperFlightRingHudTopMargin = 28f;
        [Min(0f)] public float UpperFlightRingHudRightMargin = 28f;
        [Tooltip("Minimum vertical gap between the gold panel and the upper-flight-layer tracker.")]
        [Min(0f)] public float UpperFlightRingHudGoldGap = 14f;
        [Min(160f)] public float UpperFlightRingHudWidth = 340f;
        [Min(60f)] public float UpperFlightRingHudHeight = 92f;
        [Min(0f)] public float UpperFlightRingHudPadding = 14f;
        [Min(1f)] public float UpperFlightRingHudAccentWidth = 5f;
        [Min(1f)] public float UpperFlightRingHudProgressBarHeight = 8f;
        [Min(8)] public int UpperFlightRingHudTitleFontSize = 13;
        [Min(8)] public int UpperFlightRingHudStatusFontSize = 17;
        public Vector2 UpperFlightRingHudShadowOffset = new Vector2(5f, 6f);
        [ColorUsage(false)] public Color UpperFlightRingHudShadowColor = new Color(0f, 0f, 0f, 0.42f);
        [ColorUsage(false)] public Color UpperFlightRingHudPanelColor = new Color(0.025f, 0.07f, 0.11f, 0.9f);
        [ColorUsage(false)] public Color UpperFlightRingHudAccentColor = new Color(0f, 0.82f, 1f, 1f);
        [ColorUsage(false)] public Color UpperFlightRingHudTrackColor = new Color(0.12f, 0.24f, 0.3f, 1f);
        [ColorUsage(false)] public Color UpperFlightRingHudTitleColor = new Color(0.55f, 0.78f, 0.86f, 1f);
        [ColorUsage(false)] public Color UpperFlightRingHudStatusColor = new Color(0.88f, 0.97f, 1f, 1f);
        [ColorUsage(false)] public Color UpperFlightRingHudUnlockedColor = new Color(0.35f, 1f, 0.7f, 1f);

        [Header("Health Rings")]
        [Tooltip("Expected health-ring count per streamed terrain chunk. Values well below one keep pickups scarce.")]
        [Range(0f, 1f)] public float HealthRingDensityPerChunk = 0.035f;
        [Min(0.75f)] public float HealthRingRadius = 4.2f;
        [Min(0f)] public float HealthRingMinimumHeight = 4f;
        [Min(0f)] public float HealthRingMaximumHeight = 10f;
        [Min(0f)] public float HealthRestored = 35f;
        [Tooltip("Target size of the imported heartpiece model at the center of a health ring.")]
        [Min(0.1f)] public float HealthHeartScale = 2.4f;
        public Vector3 HealthHeartOffset;
        public Vector3 HealthHeartEulerAngles;
        [Tooltip("Rotation speed around the health ring's local Y axis after its XZ plane billboards toward the camera.")]
        [Min(0f)] public float HealthRingRotationSpeed = 24f;

        [Header("Health Ring Appearance")]
        [ColorUsage(false, true)] public Color HealthRingBaseColor = new Color(0.035f, 0.48f, 0.12f);
        [ColorUsage(false, true)] public Color HealthRingEmissionColor = new Color(0.06f, 4.8f, 0.85f);
        [ColorUsage(false, true)] public Color HealthHeartBaseColor = new Color(0.055f, 0.8f, 0.18f);
        [ColorUsage(false, true)] public Color HealthHeartEmissionColor = new Color(0.12f, 8f, 1.4f);
        [Range(0f, 1f)] public float HealthMaterialSmoothness = 0.72f;
        [Range(0f, 1f)] public float HealthMaterialMetallic = 0.22f;

        [Header("Health Pickup Feedback")]
        [Min(0.1f)] public float HealthPickupFeedbackDuration = 1.4f;
        [Min(8)] public int HealthPickupFeedbackFontSize = 28;
        [Min(0f)] public float HealthPickupFeedbackTop = 170f;
        [Tooltip("Additional vertical offset as a fraction of screen height. Positive values move the text down.")]
        [Range(-1f, 1f)] public float HealthPickupFeedbackVerticalScreenOffset = 0.05f;
        [Min(24f)] public float HealthPickupFeedbackHeight = 48f;
        [ColorUsage(false)] public Color HealthPickupFeedbackColor = new Color(0.22f, 1f, 0.48f, 1f);
        [Tooltip("{0} is replaced with the amount of health restored.")]
        public string HealthPickupFeedbackFormat = "HEALTH RESTORED: {0}";

        [Header("Coin Rings")]
        [Tooltip("Expected coin-ring count per streamed terrain chunk.")]
        [Range(0f, 1f)] public float CoinRingDensityPerChunk = 0.12f;
        [Min(0.75f)] public float CoinRingRadius = 4.2f;
        [Min(1)] public int GoldReward = 25;
        [Min(0)] public int StartingGold;
        [Tooltip("Target size of the imported coin model at the center of a coin ring.")]
        [Min(0.1f)] public float CoinModelScale = 2.4f;
        public Vector3 CoinModelOffset;
        public Vector3 CoinModelEulerAngles;
        [Min(0f)] public float CoinRingRotationSpeed = 24f;

        [Header("Coin Ring Appearance")]
        [ColorUsage(false, true)] public Color CoinRingBaseColor = new Color(0.64f, 0.3f, 0.015f);
        [ColorUsage(false, true)] public Color CoinRingEmissionColor = new Color(6.5f, 2.2f, 0.05f);
        [ColorUsage(false, true)] public Color CoinBaseColor = new Color(0.95f, 0.58f, 0.04f);
        [ColorUsage(false, true)] public Color CoinEmissionColor = new Color(8f, 3.2f, 0.08f);
        [Range(0f, 1f)] public float CoinMaterialSmoothness = 0.82f;
        [Range(0f, 1f)] public float CoinMaterialMetallic = 0.72f;

        [Header("Gold HUD and Pickup Feedback")]
        [Min(0f)] public float GoldHudRightMargin = 28f;
        [Min(0f)] public float GoldHudTopMargin = 28f;
        [Min(100f)] public float GoldHudWidth = 180f;
        [Min(30f)] public float GoldHudHeight = 48f;
        [Min(8)] public int GoldHudFontSize = 18;
        public Vector2 GoldHudShadowOffset = new Vector2(5f, 6f);
        [ColorUsage(false)] public Color GoldHudShadowColor = new Color(0f, 0f, 0f, 0.42f);
        [Min(0.1f)] public float GoldPickupFeedbackDuration = 1.4f;
        [Min(8)] public int GoldPickupFeedbackFontSize = 28;
        [Min(0f)] public float GoldPickupFeedbackTop = 118f;
        [Min(24f)] public float GoldPickupFeedbackHeight = 48f;
        [ColorUsage(false)] public Color GoldHudPanelColor = new Color(0.08f, 0.045f, 0.01f, 0.9f);
        [ColorUsage(false)] public Color GoldHudTextColor = new Color(1f, 0.75f, 0.2f, 1f);
        [ColorUsage(false)] public Color GoldPickupFeedbackColor = new Color(1f, 0.82f, 0.22f, 1f);

        [Header("Height Above Ground")]
        [Min(0f)] public float GroundRingMinimumHeight = 1.75f;
        [Min(0f)] public float GroundRingMaximumHeight = 3.25f;
        [Min(0f)] public float FlightRingMinimumHeight = 5f;
        [Min(0f)] public float FlightRingMaximumHeight = 8f;

        [Header("Active Size")]
        [Min(1f)] public float BoostRingActiveScale = 1.25f;
        [Tooltip("Initial scale used when a primary flight ring spawns while the drone is already in flight mode.")]
        [Range(0.01f, 1f)] public float FlightRingSpawnScale = 0.9f;
        [InspectorName("Flight Ring Active Scale")]
        [Min(1f)] public float FlightModeScale = 1.3f;
        [Min(0f)] public float ScaleSharpness = 4.5f;

        [Header("Rotation")]
        [Tooltip("Clockwise visual rotation speed for both yellow boost rings and blue flight rings, in degrees per second.")]
        [Min(0f)] public float ClockwiseRotationSpeed = 32f;

        [Header("Flight Mode Height Offset")]
        [Min(0f)] public float FlightModeMinimumHeightOffset;
        [Min(0f)] public float FlightModeMaximumHeightOffset;
        [Min(0f)] public float FlightModeHeightSharpness;
        [Tooltip("Smoothing sharpness used when the flight meter runs out and flight-height rings return to their resting ground height. Landing normally continues to use Flight Mode Height Sharpness. Lower values produce a smoother return.")]
        [Min(0f)] public float FlightModeGroundResetHeightSharpness = 0.433333f;
    }

    [System.Serializable]
    public sealed class DesertWeatherCycleTuning
    {
        [Header("Storm Frequency")]
        [Tooltip("Keep the dynamic weather in a full sandstorm for the entire session, without fading back to clear weather.")]
        public bool AlwaysFullSandstorm;
        [Tooltip("Start a new game directly in a full sandstorm, bypassing the initial clear, dust-building, and approaching phases.")]
        public bool StartWithFullSandstorm;
        [Tooltip("Clear time before the first storm begins, in seconds.")]
        [Min(0f)] public float InitialClearDuration = 35f;
        [Tooltip("Minimum clear interval between completed storms, in seconds.")]
        [Min(1f)] public float MinimumClearDuration = 90f;
        [Tooltip("Maximum clear interval between completed storms, in seconds.")]
        [Min(1f)] public float MaximumClearDuration = 180f;

        [Header("Storm Progression")]
        [Min(0.1f)] public float DustBuildingDuration = 12f;
        [Tooltip("Storm intensity reached at the end of the initial dust-building phase.")]
        [Range(0f, 1f)] public float DustBuildingIntensity = 0.28f;
        [Min(0.1f)] public float ApproachingStormDuration = 18f;
        [Min(1f)] public float MinimumFullStormDuration = 35f;
        [Min(1f)] public float MaximumFullStormDuration = 60f;
        [Min(0.1f)] public float FadingDuration = 18f;
        [Range(0f, 1f)] public float MaximumStormIntensity = 0.85f;
        public int RandomSeedOffset = 6317;
    }

    [System.Serializable]
    public sealed class DesertWeatherWindTuning
    {
        [Tooltip("Global wind direction on the world X/Z plane.")]
        public Vector2 Direction = new Vector2(1f, 0.18f);
        [Min(0f)] public float ClearWindSpeed = 5.5f;
        [Min(0f)] public float StormWindSpeed = 24f;
        [Min(0f)] public float WindZoneStrengthMultiplier = 0.08f;
        [Min(0f)] public float ClearTurbulence = 0.12f;
        [Min(0f)] public float StormTurbulence = 0.72f;
        [Tooltip("How strongly the drone's velocity changes the apparent speed of nearby sand.")]
        [Range(0f, 1.5f)] public float PlayerVelocityInfluence = 0.65f;
    }

    [System.Serializable]
    public sealed class DesertWeatherAtmosphereTuning
    {
        [Header("Desert Sun")]
        public Vector3 SunRotation = new Vector3(38f, -28f, 0f);
        [ColorUsage(false)] public Color SunColor = new Color(1f, 0.78f, 0.58f);
        [Min(0f)] public float SunIntensity = 3f;
        [Range(0f, 1f)] public float SunShadowDimmer = 0.75f;
        public LightShadows SunShadowType = LightShadows.Soft;
        public LightShadowResolution SunShadowResolution = LightShadowResolution.VeryHigh;
        public SoftShadowQuality SunSoftShadowQuality = SoftShadowQuality.High;

        [Header("Visibility")]
        [ColorUsage(false)] public Color ClearFogColor = new Color(0.45f, 0.52f, 0.6f);
        [ColorUsage(false)] public Color StormFogColor = new Color(0.32f, 0.3f, 0.28f);
        [Min(0f)] public float ClearFogStartDistance = 100f;
        [Min(0f)] public float StormFogStartDistance = 45f;
        [Min(10f)] public float ClearVisibilityDistance = 330f;
        [Min(10f)] public float StormVisibilityDistance = 72f;
        [Min(20f)] public float ClearMaximumFogDistance = 780f;
        [Min(20f)] public float StormMaximumFogDistance = 260f;
        public float FogBaseHeight = -12f;
        [Min(1f)] public float ClearFogHeight = 85f;
        [Min(1f)] public float StormFogHeight = 160f;
        [Range(0f, 1f)] public float VolumetricFogThreshold = 0.08f;

        [Header("Y2K Sky Gradient & Exposure")]
        [ColorUsage(false, true)] public Color ClearSkyTop = new Color(0.018f, 0.24f, 1.65f);
        [ColorUsage(false, true)] public Color ClearSkyMiddle = new Color(0.04f, 0.72f, 2.15f);
        [ColorUsage(false, true)] public Color ClearSkyBottom = new Color(0.42f, 2.25f, 3.2f);
        [ColorUsage(false, true)] public Color StormSkyTop = new Color(0.24f, 0.14f, 0.065f);
        [ColorUsage(false, true)] public Color StormSkyMiddle = new Color(0.54f, 0.29f, 0.1f);
        [ColorUsage(false, true)] public Color StormSkyBottom = new Color(1.15f, 0.54f, 0.15f);
        [Min(0f)] public float SkyGradientDiffusion = 1.48f;
        [Min(0f)] public float SkyMultiplier = 0.82f;
        public float ClearExposure = 2f;
        public float StormExposure = 1.35f;

        [Header("Y2K Horizon Glow")]
        [ColorUsage(false, true)] public Color ClearHorizonGlowColor = new Color(0.38f, 2.8f, 4.4f, 1f);
        [ColorUsage(false, true)] public Color StormHorizonGlowColor = new Color(1.3f, 0.42f, 0.08f, 1f);
        [Range(0.01f, 0.5f)] public float HorizonGlowSize = 0.14f;
        [Min(0f)] public float ClearHorizonGlowIntensity = 0.72f;
        [Min(0f)] public float StormHorizonGlowIntensity = 0.28f;

        [Header("Y2K Sky Clouds")]
        [ColorUsage(false, true)] public Color ClearSkyCloudColor = new Color(1.1f, 1.75f, 2.05f, 1f);
        [ColorUsage(false, true)] public Color ClearSkyCloudHighlight = new Color(1.8f, 2.7f, 3.1f, 1f);
        [ColorUsage(false, true)] public Color ClearSkyCloudPearl = new Color(0.62f, 1.8f, 2.4f, 1f);
        [ColorUsage(false, true)] public Color StormSkyCloudColor = new Color(0.38f, 0.2f, 0.08f, 1f);
        [ColorUsage(false, true)] public Color StormSkyCloudHighlight = new Color(0.9f, 0.43f, 0.12f, 1f);
        [ColorUsage(false, true)] public Color StormSkyCloudPearl = new Color(0.55f, 0.25f, 0.08f, 1f);
        [Range(0f, 1f)] public float ClearSkyCloudOpacity = 0.82f;
        [Range(0f, 1f)] public float StormSkyCloudOpacity = 0.45f;
        [Range(0.05f, 0.8f)] public float SkyCloudAltitude = 0.28f;
        [Range(0.03f, 0.5f)] public float SkyCloudThickness = 0.2f;
        [Min(0.1f)] public float SkyCloudScale = 3.8f;
        [Range(0.005f, 0.25f)] public float SkyCloudSoftness = 0.075f;
        [Range(0f, 2f)] public float SkyCloudHighlightStrength = 0.62f;
        [Range(0f, 2f)] public float SkyCloudPearlStrength = 0.24f;
        [Min(0f)] public float SkyCloudDriftSpeed = 0.012f;

        [Header("Y2K Digital Structures")]
        [ColorUsage(false, true)] public Color DigitalStructureColor = new Color(0.32f, 1.9f, 3.1f, 1f);
        [Range(0f, 1f)] public float ClearDigitalStructureOpacity = 0f;
        [Range(0f, 1f)] public float StormDigitalStructureOpacity = 0f;
        [Range(0.02f, 0.65f)] public float DigitalArcAltitude = 0.2f;
        [Range(0f, 1f)] public float DigitalArcCurvature = 0.32f;
        [Range(0.001f, 0.05f)] public float DigitalArcThickness = 0.006f;
        [Range(1f, 12f)] public float DigitalArcFrequency = 3f;
        [Range(0.02f, 0.6f)] public float DigitalRingAltitude = 0.12f;
        [Range(0.01f, 0.3f)] public float DigitalRingSpacing = 0.075f;
        [Range(0.001f, 0.04f)] public float DigitalRingThickness = 0.0035f;
        [Range(0f, 1f)] public float DigitalGridOpacity = 0.42f;
        [Range(2f, 40f)] public float DigitalGridScale = 14f;
        [Range(0.02f, 0.35f)] public float DigitalGridHeight = 0.11f;
        [Range(0.001f, 0.08f)] public float DigitalGridLineThickness = 0.018f;

        [Header("Bloom Integration")]
        [Min(0f)] public float BloomIntensity = 0.2f;
        [Min(0f)] public float BloomThreshold = 1.05f;
        [Range(0f, 1f)] public float BloomScatter = 0.62f;
    }

    [System.Serializable]
    public sealed class DesertWeatherDustTuning
    {
        [Header("Density & Coverage")]
        [Range(0f, 1f)] public float AmbientDustDensity = 0.18f;
        [Range(0f, 1f)] public float AmbientAirborneSandDensity = 1f;
        [Range(0f, 1f)] public float AmbientAirborneSandProximityDensityMultiplier = 0.65f;
        [Range(0f, 1.5f)] public float StormDustDensity = 0.95f;
        [Min(10f)] public float FieldRadius = 80f;
        [Min(1f)] public float GroundLayerHeight = 6f;
        [Min(1f)] public float AirborneLayerHeight = 32f;
        [Min(4f)] public float CloseLayerRadius = 22f;

        [Header("Particle Shape")]
        [Min(0.01f)] public float MinimumParticleSize = 0.05f;
        [Min(0.01f)] public float MaximumParticleSize = 0.22f;
        [Min(0.1f)] public float MinimumParticleLifetime = 2.2f;
        [Min(0.1f)] public float MaximumParticleLifetime = 5.5f;
        [Min(0f)] public float TurbulenceStrength = 1.8f;
        [Min(0.01f)] public float TurbulenceFrequency = 0.22f;
        [Min(0f)] public float SandStreakLength = 3.8f;
        [Min(0f)] public float ParticleVelocityStretch = 0.06f;
        [Min(0f)] public float CameraVelocityStretch = 0.12f;
        [Min(0f)] public float AmbientAirborneSandSizeMultiplier = 2.5f;
        [Min(0f)] public float AmbientAirborneSandOpacityMultiplier = 1.8f;
        [ColorUsage(false)] public Color AmbientDustColor = new Color(0.9f, 0.61f, 0.3f, 0.26f);
        [ColorUsage(false)] public Color StormDustColor = new Color(0.88f, 0.47f, 0.16f, 0.58f);

        [Header("Layer Motion & Placement")]
        [Min(0f)] public float GroundWindResponse = 0.62f;
        [Min(0f)] public float AirborneWindResponse = 0.9f;
        [Min(0f)] public float CloseWindResponse = 1.12f;
        [Min(0f)] public float ApproachingFrontStartDistance = 1.55f;
        [Min(0f)] public float ApproachingFrontEndDistance = 0.22f;
        [Range(0f, 1.5f)] public float FullStormFrontDensity = 0.55f;

        [Header("Performance Budgets")]
        [Range(32, 1000)] public int GroundParticleBudget = 240;
        [Range(32, 1200)] public int AirborneParticleBudget = 420;
        [Range(32, 1200)] public int ApproachingFrontParticleBudget = 350;
        [Range(32, 800)] public int CloseParticleBudget = 220;
        [Min(0f)] public float GroundEmissionRate = 50f;
        [Min(0f)] public float AirborneEmissionRate = 110f;
        [Min(0f)] public float ApproachingFrontEmissionRate = 90f;
        [Min(0f)] public float CloseEmissionRate = 70f;
    }

    [System.Serializable]
    public sealed class MusicReactiveSkyTuning
    {
        public bool Enabled = true;

        [Header("Orchestration")]
        [Tooltip("Legacy visualizer profile fallback used when the audio playlist has no authored entries.")]
        public MusicVisualTrackProfile TrackProfile;
        [Tooltip("Fixed capacity of the FMOD callback transfer ring. Exhaustion drops cosmetic timeline events.")]
        [Range(16, 1024)] public int TimelineCallbackQueueCapacity = 128;
        [Tooltip("A polled FMOD timeline change larger than this is treated as a seek or restart.")]
        [Range(250, 10000)] public int TimelineJumpThresholdMilliseconds = 2000;
        [Tooltip("Allowed event duration error when validating the song profile.")]
        [Range(0.01f, 5f)] public float DurationValidationToleranceSeconds = 0.5f;
        [Tooltip("Show the development-only music visualizer diagnostics panel.")]
        public bool ShowDevelopmentDebugPanel;
        public Rect DevelopmentDebugPanelRect = new Rect(12f, 12f, 440f, 640f);

        [Header("Dreamloader Cat Overlay")]
        [Tooltip("Enables the semi-transparent dancing cat overlay during the authored Dreamloader timeline window.")]
        public bool DreamloaderCatOverlayEnabled = true;
        [Tooltip("Active song/profile name fragment required before the dancing cat overlay can render.")]
        public string DreamloaderCatTrackName = "Dreamloader";
        [Tooltip("Resources path containing the transparent DreamloaderCat_01..NN frame sequence.")]
        public string DreamloaderCatResourcesPath = "MusicVisuals/DreamloaderCat";
        [Tooltip("FMOD song timeline millisecond where cat overlay section 1 first appears.")]
        [FormerlySerializedAs("DreamloaderCatStartTimelineMilliseconds")]
        [Min(0)] public int DreamloaderCatSection1StartTimelineMilliseconds = 84000;
        [Tooltip("FMOD song timeline millisecond where cat overlay section 1 disappears.")]
        [FormerlySerializedAs("DreamloaderCatEndTimelineMilliseconds")]
        [Min(0)] public int DreamloaderCatSection1EndTimelineMilliseconds = 104500;
        [Tooltip("FMOD song timeline millisecond where cat overlay section 2 first appears. Leave start and end equal to disable section 2.")]
        [Min(0)] public int DreamloaderCatSection2StartTimelineMilliseconds;
        [Tooltip("FMOD song timeline millisecond where cat overlay section 2 disappears. Leave start and end equal to disable section 2.")]
        [Min(0)] public int DreamloaderCatSection2EndTimelineMilliseconds;
        [Tooltip("Authored frame rate of the cat animation before applying the speed multiplier.")]
        [Min(0.01f)] public float DreamloaderCatFramesPerSecond = 25f;
        [Tooltip("Multiplier for the cat loop playback speed.")]
        [Min(0.01f)] public float DreamloaderCatLoopSpeedMultiplier = 1f;
        [Tooltip("Final screen alpha applied to both cats. The frame alpha remains fully transparent where the source background is transparent.")]
        [FormerlySerializedAs("DreamloaderCatOpacity")]
        [Range(0f, 1f)] public float DreamloaderCatAlpha = 0.45f;
        [Tooltip("Horizontal screen padding in pixels from the left and right edges.")]
        [Min(0f)] public float DreamloaderCatHorizontalPadding = 48f;

        [Header("Transient Classification")]
        [Range(0f, 1f)] public float MinorKickThreshold = 0.28f;
        [Range(0f, 1f)] public float MajorKickThreshold = 0.62f;
        [Range(0f, 1f)] public float KickHysteresisRelease = 0.14f;
        [Range(0, 2000)] public int KickCooldownMilliseconds = 130;
        [Range(1, 16)] public int MaximumKicksPerBar = 4;
        [Range(0f, 1f)] public float MinorSnareThreshold = 0.3f;
        [Range(0f, 1f)] public float AccentSnareThreshold = 0.66f;
        [Range(0f, 1f)] public float SnareHysteresisRelease = 0.15f;
        [Range(0, 2000)] public int SnareCooldownMilliseconds = 110;
        [Range(1, 32)] public int MaximumSnaresPerBar = 8;

        [Header("Perspective Pressure Front")]
        [Range(1, 4)] public int OrdinaryPressureFrontPoolSize = 2;
        [Range(1, 4)] public int ReactorPressureFrontPoolSize = 4;
        [Range(8, 96)] public int PressureFrontSegments = 40;
        [Min(1f)] public float PressureFrontStartDistance = 180f;
        [Min(0.1f)] public float PressureFrontEndDistance = 12f;
        [Min(1f)] public float PressureFrontWidth = 240f;
        [Min(0f)] public float PressureFrontCameraHeightOffset = 7f;
        [Min(0f)] public float PressureFrontGroundProbeHeight = 64f;
        [Min(0.1f)] public float PressureFrontGroundProbeDistance = 256f;
        public LayerMask PressureFrontGroundProbeLayers = -1;
        [Min(0f)] public float PressureFrontGroundClearance = 0.25f;
        [Min(0f)] public float PressureFrontHorizonHeight = 18f;
        [Min(0f)] public float PressureFrontArcDepth = 28f;
        [Min(0.01f)] public float PressureFrontDurationSeconds = 1.15f;
        [Min(0.001f)] public float PressureFrontStartWidth = 0.22f;
        [Min(0.001f)] public float PressureFrontEndWidth = 1.4f;
        [Range(0f, 1f)] public float PressureFrontMaximumAlpha = 0.76f;
        [Range(0f, 1f)] public float PressureFrontNearFadeStart = 0.72f;
        [ColorUsage(false, true)] public Color PressureFrontColor = new Color(0.18f, 2.4f, 4.2f, 1f);
        [Range(0.001f, 0.5f)] public float MusicReactiveAdditiveEdgeSoftness = 0.18f;
        [Min(0f)] public float ReactorFrontStaggerSeconds = 0.09f;
        [Min(0f)] public float ReactorFrontWidthMultiplier = 1.25f;
        [Tooltip("Render authored pressure fronts as smooth closed rings centered on the viewer instead of open horizon arcs.")]
        public bool PressureFrontUseEnclosingHalo;
        [Min(0.1f)] public float PressureFrontEnclosingHaloStartRadius;
        [Min(0.1f)] public float PressureFrontEnclosingHaloEndRadius;
        [FormerlySerializedAs("PressureFrontEnclosingHaloVerticalScale")]
        [Min(0f)] public float PressureFrontEnclosingHaloHeightOffset;

        [Header("Authored Front Arrival")]
        [Min(0f)] public float DefaultFrontLeadBeats;
        [Min(0f)] public float DefaultFrontTravelBeats;
        [Min(0f)] public float PressureFrontNearFadeBeats;
        [Min(0f)] public float OrdinaryArrivalCoreMultiplier;
        [Min(0f)] public float StrongArrivalCoreMultiplier;
        [Min(0f)] public float ReactorArrivalCoreMultiplier;
        [Min(0f)] public float PressureFrontHaloIntensityMultiplier;
        [ColorUsage(false, true)] public Color PressureFrontHaloColor;
        [Range(0f, 1f)] public float OrdinaryFrontEdgeBreakup;
        [Range(0f, 1f)] public float StrongFrontEdgeBreakup;
        [Range(0f, 1f)] public float ReactorFrontEdgeBreakup;
        [Min(0f)] public float PressureFrontArrivalThicknessGrowth;
        public bool PressureFrontFadeBeforeNearPlane;
        public bool PressureFrontDepthTest;
        [Range(0f, 1f)] public float SplitFrontLateralOffset;

        [Header("Foreground Response")]
        [Range(1, 4)] public int MaximumRoadPulseCount = 4;
        [Min(0.01f)] public float RoadPulseDurationSeconds = 2.4f;
        [Min(0f)] public float RoadPulseTravelSpeed = 68f;
        [Min(0.01f)] public float RoadPulseWidth = 7.5f;
        [Range(0f, 4f)] public float RoadPulseEmissionIntensity = 1.25f;
        [ColorUsage(false, true)] public Color RoadPulseColor = new Color(0.1f, 1.8f, 3.8f, 1f);
        [Min(0f)] public float StructureLightMaximumIntensity = 1800f;
        [Min(0f)] public float StructureLightRange = 32f;
        [Min(0.01f)] public float StructureLightDurationSeconds = 0.32f;
        [Min(0f)] public float StructureLightForwardOffset = 8f;
        [ColorUsage(false, true)] public Color StructureLightColor = new Color(0.25f, 0.85f, 1f, 1f);
        [Range(8, 1024)] public int ForegroundStreakParticleBudget = 160;
        [Range(1, 64)] public int ForegroundStreakBurstCount = 12;
        [FormerlySerializedAs("OpeningStreakLimitDurationSeconds")]
        [Min(0f)] public float OpeningPeripheralStreakLimitDurationSeconds;
        [FormerlySerializedAs("OpeningStreakMaximumVisibleLines")]
        [Range(0, 256)] public int OpeningPeripheralStreakMaximumVisibleLines;
        [Min(0.01f)] public float ForegroundStreakLifetime = 0.48f;
        [Min(0f)] public float ForegroundStreakSpeed = 34f;
        [Min(0.001f)] public float ForegroundStreakSize = 0.045f;
        [Min(0f)] public float ForegroundStreakPeripheralWidth = 8f;
        [Min(0f)] public float ForegroundStreakPeripheralHeight = 4.5f;
        [Min(0f)] public float ForegroundStreakForwardOffset = 4f;
        [Min(0f)] public float ForegroundStreakVelocityScale = 0.18f;
        [Min(0f)] public float ForegroundStreakLengthScale = 1.6f;
        [Tooltip("Music energy at or below which center-out streak bursts receive the slow-passage punch multiplier.")]
        [Range(0f, 1f)] public float ForegroundStreakSlowEnergyThreshold = 0.62f;
        [Tooltip("Burst-count and travel-speed multiplier used during low-energy passages.")]
        [Range(1f, 5f)] public float ForegroundStreakSlowPunchMultiplier = 3f;
        [Tooltip("Number of evenly spaced hues in the center-out streak palette.")]
        [Range(2, 64)] public int ForegroundStreakPaletteColorCount = 20;
        [Range(0f, 1f)] public float ForegroundStreakPaletteSaturation = 0.9f;
        [Range(0f, 1f)] public float ForegroundStreakPaletteValue = 1f;
        [Min(0f)] public float ForegroundStreakPaletteEmission = 3.2f;
        [Tooltip("Controls how sharply the premium streak silhouette tapers toward both ends.")]
        [Range(0.25f, 4f)] public float ForegroundStreakTipSharpness = 1.35f;
        [Range(0f, 0.5f)] public float ForegroundStreakMinimumWidth = 0.06f;
        [Range(0.02f, 1f)] public float ForegroundStreakCoreWidth = 0.24f;
        [Min(0f)] public float ForegroundStreakCoreBrightness = 1.8f;
        [Min(0f)] public float ForegroundStreakHaloBrightness = 0.42f;
        [Range(0.001f, 0.25f)] public float ForegroundStreakEndFade = 0.08f;
        [ColorUsage(false, true)] public Color ForegroundStreakColor = new Color(1.2f, 0.25f, 2.8f, 0.8f);

        [Header("Authored Center-Out Screen Flare Lines")]
        [Range(0f, 0.5f)] public float CenterOutInitialViewportRadius;
        [Range(0f, 0.5f)] public float CenterOutProtectedViewportRadius;
        [Tooltip("Optional drone child transform whose live renderer center anchors center-out flares.")]
        public string CenterOutAnchorTransformName;
        [Tooltip("Viewport-space offset from the drone visual center used as the center-out flare origin.")]
        public Vector2 CenterOutAnchorViewportOffset;
        [Range(1, 256)] public int CenterOutParticlePoolCapacity;
        public bool CenterOutUseColorWheelPalette;
        [Min(0f)] public float CenterOutBurstCountMultiplier;
        public Vector2Int OrdinaryCenterOutCountRange;
        public Vector2Int StrongCenterOutCountRange;
        public Vector2Int ReactorCenterOutCountRange;
        public Vector2 OrdinaryCenterOutLifetimeRange;
        public Vector2 StrongCenterOutLifetimeRange;
        public Vector2 ReactorCenterOutLifetimeRange;
        [Min(0f)] public float CenterOutRadialSpeed;
        [Range(0f, 1f)] public float CenterOutTowardCameraSpeedFraction;
        [Tooltip("Cancel drone anchor translation from already-emitted center-out particles while preserving their authored outward motion.")]
        public bool CenterOutCompensateAnchorMotion;
        [Range(0f, 1f)] public float CenterOutDirectionalVariation;
        [Range(0f, 1f)] public float CenterOutFineLineFraction = 0.55f;
        [Range(0.05f, 1f)] public float CenterOutFineLineWidthMultiplier = 0.32f;
        [Range(1f, 4f)] public float CenterOutFineLineSpeedMultiplier = 1.55f;
        [Range(0.25f, 2f)] public float CenterOutBroadRayWidthMultiplier = 1f;
        [ColorUsage(false, true)] public Color CenterOutCyanColor;
        [ColorUsage(false, true)] public Color CenterOutMagentaColor;
        [ColorUsage(false, true)] public Color CenterOutWarmWhiteColor;
        [ColorUsage(false, true)] public Color CenterOutAccentColor;
        [Min(0f)] public float CenterOutCyanWeight;
        [Min(0f)] public float CenterOutMagentaWeight;
        [Min(0f)] public float CenterOutWarmWhiteWeight;
        [Min(0f)] public float CenterOutAccentWeight;

        [Header("Authored Response Strengths")]
        [Min(0f)] public float OrdinaryRoadArrivalResponse;
        [Min(0f)] public float StrongRoadArrivalResponse;
        [Min(0f)] public float ReactorRoadArrivalResponse;
        [Min(0f)] public float MinorKickRoadRippleResponse;
        [Min(0f)] public float OrdinaryStructureResponse;
        [Min(0f)] public float StrongStructureResponse;
        [Min(0f)] public float ReactorStructureResponse;
        [Range(0, 4)] public int MaximumTemporaryReactionLights;

        [Header("Camera Response")]
        [Min(0f)] public float MaximumVisualizerFovOffset = 3.5f;
        [Min(0f)] public float MaximumVisualizerRollDegrees = 0.65f;
        [Min(0f)] public float MaximumVisualizerPositionOffset = 0.055f;
        [Min(0.01f)] public float CameraKickAttackSeconds = 0.025f;
        [Min(0.01f)] public float CameraKickReleaseSeconds = 0.28f;
        [Min(0.01f)] public float VisualizerFovDisableReleaseSeconds = 0.08f;
        [Range(0f, 1f)] public float MajorKickFovStrength = 0.55f;
        [Range(0f, 1f)] public float ReactorFovStrength = 1f;
        [Range(0f, 1f)] public float MinorKickPositionStrength = 0.25f;
        [Range(0f, 1f)] public float MajorKickPositionStrength = 0.5f;
        [Range(0f, 1f)] public float MinorSnareRollStrength = 0.25f;
        [Range(0f, 1f)] public float SnareRollStrength = 0.55f;
        [Range(0f, 1f)] public float ReactorPositionStrength = 1f;

        [Header("Authored Camera Limits")]
        [Min(0f)] public float OrdinaryFrontFovDegrees;
        [Min(0f)] public float StrongFrontFovDegrees;
        [Min(0f)] public float FirstDropFovDegrees;
        [Min(0f)] public float ReactorFovDegrees;
        [Min(0f)] public float OrdinaryPositionImpulseMeters;
        [Min(0f)] public float StrongPositionImpulseMeters;
        [Min(0f)] public float ReactorPositionImpulseMeters;
        [Min(0f)] public float OrdinarySnareRollDegrees;
        [Min(0f)] public float AccentSnareRollDegrees;

        [Header("World-Only Glitch")]
        [Range(0f, 1f)] public float WorldGlitchMaximumIntensity = 0.16f;
        [Min(0.01f)] public float WorldGlitchDurationSeconds = 0.085f;
        [Range(2, 64)] public int WorldGlitchSliceCount = 18;
        [Range(0f, 0.1f)] public float WorldGlitchHorizontalShift = 0.018f;
        [Range(1, 16)] public int AccentSnaresPerGlitch = 4;
        [Range(0, 5)] public int WorldGlitchMinimumVisualTier = 2;
        [Range(0f, 0.5f)] public float WorldGlitchProtectedHalfWidth = 0.2f;
        [Range(0f, 0.5f)] public float WorldGlitchProtectedHalfHeight = 0.15f;
        [Range(0.001f, 0.25f)] public float WorldGlitchProtectedFeather = 0.08f;
        [Range(0f, 1f)] public float WorldGlitchProtectedIntensityMultiplier = 0.18f;
        [ColorUsage(false, true)] public Color WorldGlitchTint = new Color(2.8f, 0.55f, 2.2f, 0.24f);

        [Header("Authored Glitch Events")]
        [Min(0f)] public float OrdinaryGlitchUvDisplacement;
        [Min(0f)] public float AccentGlitchUvDisplacement;
        [Min(0f)] public float ClimaxGlitchUvDisplacement;
        [Min(0f)] public float MaximumGlitchUvDisplacement;
        [Min(0f)] public float OrdinaryGlitchDurationBeats;
        [Min(0f)] public float AccentGlitchDurationBeats;
        [Min(0f)] public float ReactorGlitchDurationBeats;

        [Header("Music HUD Border")]
        [Min(0f)] public float HudBorderInset;
        [Min(0f)] public float HudBorderThickness;
        [Min(0f)] public float HudBorderCornerLength;
        [ColorUsage(false)] public Color HudBorderColor;
        [Min(0.01f)] public float HudBorderFallbackDurationSeconds = 0.12f;

        [Header("Music Analysis")]
        [Tooltip("FFT sample count used to separate bass, midrange, and high-frequency energy.")]
        [Range(128, 2048)] public int FftWindowSize = 1024;
        [Tooltip("Maximum number of FFT reads per second. Visual interpolation still runs every frame.")]
        [Range(10f, 120f)] public float AnalysisRate = 60f;
        [Min(20f)] public float MinimumFrequency = 35f;
        [Min(40f)] public float BassMaximumFrequency = 190f;
        [Min(200f)] public float MidMaximumFrequency = 4300f;
        [Min(1000f)] public float HighMaximumFrequency = 12000f;
        [Min(0f)] public float SpectrumGain = 27f;
        [Range(0f, 1f)] public float SpectrumNoiseFloor = 0.02f;
        [Min(0f)] public float BassGain = 1.05f;
        [Min(0f)] public float MidGain = 1.65f;
        [Min(0f)] public float HighGain = 2.65f;
        [Min(0f)] public float AttackSpeed = 24f;
        [Min(0f)] public float ReleaseSpeed = 6f;
        [Min(0f)] public float BassTransientSensitivity = 8f;
        [Min(0f)] public float HighTransientSensitivity = 11f;
        [Min(0f)] public float PulseDecaySpeed = 5.5f;

        [Header("Resonance Front")]
        [ColorUsage(false, true)] public Color FrontColor = new Color(0.12f, 1.1f, 1.45f, 1f);
        [Min(0f)] public float FrontIntensity = 0.85f;
        [Tooltip("Whole number of pressure fronts around the sky. Integer counts keep the spherical wrap seamless.")]
        [Range(1, 12)] public int FrontCount = 5;
        [Min(0f)] public float FrontTravelSpeed = 0.095f;
        [Range(0.001f, 0.15f)] public float FrontThickness = 0.028f;
        [Range(0f, 2f)] public float FrontCurvature = 0.72f;
        [Range(-0.2f, 0.8f)] public float FrontAltitude = 0f;
        [Range(0.05f, 1f)] public float FrontVerticalSpan = 0.66f;
        [Range(0f, 2f)] public float BassFrontExpansion = 0.48f;
        [Range(0f, 2f)] public float FrontEnergyResponse = 0.18f;
        [Range(0f, 2f)] public float FrontBassResponse = 0.72f;
        [Range(0f, 2f)] public float FrontPulseResponse = 1.15f;
        [Range(1f, 8f)] public float FrontPressureWidth = 4.2f;
        [Range(0f, 1f)] public float FrontPressureOpacity = 0.12f;

        [Header("Melodic Sky Currents")]
        [ColorUsage(false, true)] public Color AuroraColor = new Color(0.85f, 0.18f, 4.8f, 1f);
        [Min(0f)] public float AuroraIntensity = 0.55f;
        [Range(-0.1f, 0.9f)] public float AuroraAltitude = 0.42f;
        [Range(0.001f, 0.2f)] public float AuroraThickness = 0.047f;
        [Range(0f, 1f)] public float AuroraWaviness = 0.3f;
        [Min(0f)] public float AuroraTravelSpeed = 0.09f;
        [Range(-1f, 1f)] public float AuroraTravelDirection = 1f;
        [Tooltip("Horizontal screen-space shove applied to the melodic current on authored heavy drops. Positive values move right.")]
        [Range(-0.25f, 0.25f)] public float AuroraDropHorizontalShift;
        [Min(0.01f)] public float AuroraDropShiftReleaseBeats = 1.5f;
        [Tooltip("Whole number of melodic current waves around the sky. Integer counts keep the spherical wrap seamless.")]
        [Range(1, 12)] public int AuroraFrequency = 4;
        [Range(0f, 1f)] public float AuroraSecondaryIntensity = 0.5f;
        [Range(0f, 1f)] public float AuroraShimmerAmount = 0.46f;

        [Header("Bass Shock Rings")]
        [ColorUsage(false, true)] public Color ShockRingColor = new Color(5.2f, 1.1f, 0.12f, 1f);
        [Min(0f)] public float ShockRingIntensity = 0.9f;
        [Range(1, 16)] public int ShockRingCount = 6;
        [Range(0.0005f, 0.08f)] public float ShockRingThickness = 0.006f;
        [Min(0f)] public float ShockRingTravelSpeed = 0.7f;
        [Range(0.05f, 1f)] public float ShockRingVerticalSpan = 0.72f;
        [Range(0f, 2f)] public float ShockRingBassResponse = 1.2f;
        [Range(0f, 1f)] public float ShockRingSustainResponse = 0.5f;
        [Min(1f)] public float ShockRingBeatRateBpm = 167f;
        [Range(0.02f, 0.8f)] public float ShockRingBeatDutyCycle = 0.24f;
        [Range(0f, 1f)] public float ShockRingBreakup = 0.42f;
        [Tooltip("Vertical deformation applied to each bass ring's repeating zigzag profile.")]
        [Range(0f, 1.5f)] public float ShockRingZigzagAmount = 0f;
        [Tooltip("Whole number of zigzag peaks wrapped around each bass ring.")]
        [Range(1, 32)] public int ShockRingZigzagFrequency = 5;

        [Header("Percussive Sky Filaments")]
        [ColorUsage(false, true)] public Color LightningColor = new Color(2.8f, 4.4f, 7f, 1f);
        [Min(0f)] public float LightningIntensity = 0f;
        [Tooltip("Number of possible lightning strike slots distributed across the visible camera frustum.")]
        [Range(1, 32)] public int LightningSectorCount = 16;
        [Range(0.0001f, 0.08f)] public float LightningWidth = 0.0005f;
        [Range(0f, 1f)] public float LightningJaggedness = 0.18f;
        [Min(0.1f)] public float LightningRetargetRate = 5.5f;
        [Tooltip("Fraction of the horizontal camera frustum reserved at each edge so jagged bolts stay fully on screen.")]
        [Range(0f, 0.8f)] public float LightningFrustumEdgePadding = 0.35f;
        [Range(0f, 1f)] public float LightningSustainResponse = 0.06f;
        [Range(0f, 1f)] public float LightningBranchIntensity = 0.62f;
        [Range(1, 4)] public int LightningStrikeCount = 2;
        [Range(1f, 12f)] public float LightningHaloWidthMultiplier = 5f;
        [Range(0f, 1f)] public float LightningHaloIntensity = 0.18f;
        [Range(0f, 2f)] public float LightningNodeIntensity = 0.7f;
        [Range(2f, 24f)] public float LightningNodeSpacing = 9f;

        [Header("Authored Filament Events")]
        [Min(0f)] public float MinorFilamentEventIntensity;
        [Min(0f)] public float AccentFilamentEventIntensity;
        [Min(0f)] public float DropFilamentEventIntensity;
        [Min(0f)] public float TrebleClimaxFilamentEventIntensity;
        [Min(0f)] public float ReactorFilamentEventIntensity;
        [Range(1, 4)] public int MaximumVisiblePrimaryFilaments;

        [Header("Treble Star Bursts")]
        [ColorUsage(false, true)] public Color SparkColor = new Color(1.2f, 4.2f, 2.8f, 1f);
        [Min(0f)] public float SparkIntensity = 0f;
        [Range(4f, 64f)] public float SparkGridScale = 28f;
        [Range(0f, 1f)] public float SparkDensity = 0f;
        [Range(0.002f, 0.2f)] public float SparkSize = 0.035f;
        [Min(0f)] public float SparkTwinkleSpeed = 9f;
        [Range(0f, 1f)] public float SparkSustainResponse = 0.08f;

        [Header("Authored Treble Events")]
        public Vector2Int MinorTrebleEventCountRange;
        public Vector2Int NormalTrebleEventCountRange;
        public Vector2Int StrongTrebleEventCountRange;
        public Vector2Int ClimaxTrebleEventCountRange;
        public Vector2Int ReactorTrebleEventCountRange;
        [Min(0f)] public float MinorTrebleEventBrightness;
        [Min(0f)] public float NormalTrebleEventBrightness;
        [Min(0f)] public float StrongTrebleEventBrightness;
        [Min(0f)] public float ClimaxTrebleEventBrightness;
        [Min(0f)] public float ReactorTrebleEventBrightness;

        [Header("Continuous Song Response")]
        [Min(0f)] public float SubPressureAttackBeats;
        [Min(0f)] public float SubPressureReleaseBeats;
        [Min(0f)] public float MaximumBassCurrentThicknessContribution;
        [Min(0f)] public float MaximumBassCurrentProximityContribution;
        [Min(0f)] public float OrdinaryHudBorderResponse;
        [Min(0f)] public float StrongHudBorderResponse;
        [Min(0f)] public float ReactorHudBorderResponse;

        [Header("Global Bloom Response")]
        [Tooltip("Maximum global bloom intensity allowed during the strongest musical peak. Never lowers the authored environment baseline.")]
        [Min(0f)] public float BloomMaximumIntensity = 0.2f;
        [Min(0f)] public float BloomEnergyBoost = 0.01f;
        [Min(0f)] public float BloomBassPulseBoost = 0.02f;
        [Range(0f, 1f)] public float BloomThresholdReduction = 0f;
        [Min(0f)] public float BloomAttackSpeed = 12f;
        [Min(0f)] public float BloomReleaseSpeed = 3.8f;
    }

    [System.Serializable]
    public sealed class DesertWeatherTuning
    {
        public bool Enabled = true;
        public DesertWeatherCycleTuning Cycle = new DesertWeatherCycleTuning();
        public DesertWeatherWindTuning Wind = new DesertWeatherWindTuning();
        public DesertWeatherAtmosphereTuning Atmosphere = new DesertWeatherAtmosphereTuning();
        public DesertWeatherDustTuning Dust = new DesertWeatherDustTuning();

        public void EnsureInitialized()
        {
            Cycle ??= new DesertWeatherCycleTuning();
            Wind ??= new DesertWeatherWindTuning();
            Atmosphere ??= new DesertWeatherAtmosphereTuning();
            Dust ??= new DesertWeatherDustTuning();
        }
    }

    [System.Serializable]
    public sealed class ElectricalStormVisualTuning
    {
        public bool Enabled = true;

        [Header("Storm Presence & Severity")]
        [Range(0f, 1f)] public float VisualActivationIntensity = 0.08f;
        [Range(0f, 1f)] public float FullVisualIntensity = 0.78f;
        [Min(0f)] public float VisualBlendSharpness = 3.5f;
        [Range(0f, 1f)] public float SevereVisualThreshold = 0.58f;
        [Range(0f, 1f)] public float ExtremeVisualThreshold = 0.88f;

        [Header("Regional Stormfront")]
        public Vector2 StormfrontDirection = new Vector2(-1f, 0.2f);
        [Min(0f)] public float StormfrontFarDistance = 460f;
        [Min(0f)] public float StormfrontNearDistance = 36f;
        [Min(1f)] public float StormfrontWidth = 390f;
        [Min(1f)] public float StormfrontHeight = 165f;
        [Min(1f)] public float StormfrontDepth = 115f;
        [Min(0f)] public float StormfrontBaseHeight = 22f;

        [Header("Risk Player Pursuit")]
        [Tooltip("First contract risk at which the electrical storm continuously follows the player.")]
        [Range(1, 20)] public int PlayerFollowStartRank = 1;
        [Range(1, 20)] public int PlayerFollowEndRank = 20;
        [Tooltip("Storm follow speed at Player Follow Start Rank.")]
        [Min(0.01f)] public float PlayerFollowSpeedAtStartRank = 8f;
        [Tooltip("Storm follow speed at Player Follow End Rank.")]
        [Min(0.01f)] public float PlayerFollowSpeedAtEndRank = 80f;

        [Header("Supercell Shelf")]
        [Range(4, 48)] public int StormShelfLobeCount = 20;
        [Range(0.1f, 1.5f)] public float StormShelfWidthFraction = 1.08f;
        [Range(0f, 1f)] public float StormShelfHeightFraction = 0.2f;
        [Range(0.1f, 1f)] public float StormShelfDepthFraction = 0.76f;
        [Min(0f)] public float StormShelfVerticalVariation = 7f;
        [Min(1f)] public float StormShelfMinimumLobeWidth = 44f;
        [Min(1f)] public float StormShelfMaximumLobeWidth = 76f;
        [Min(1f)] public float StormShelfMinimumThickness = 18f;
        [Min(1f)] public float StormShelfMaximumThickness = 34f;
        [Min(1f)] public float StormShelfMinimumDepth = 42f;
        [Min(1f)] public float StormShelfMaximumDepth = 82f;
        [Range(0.1f, 1f)] public float StormShelfEdgeScale = 0.62f;
        [Min(0f)] public float StormShelfMotionMultiplier = 0.5f;
        public float StormShelfRotationSpeed = 0.65f;

        [Header("Cumulonimbus Towers")]
        [Range(1, 8)] public int StormTowerCount = 4;
        [Range(2, 10)] public int StormTowerTierCount = 5;
        [Range(-0.5f, 0.5f)] public float PrimaryTowerHorizontalOffset = -0.12f;
        [Min(1f)] public float PrimaryTowerHeight = 185f;
        [Min(1f)] public float PrimaryTowerWidth = 92f;
        [Min(1f)] public float SecondaryTowerMinimumHeight = 92f;
        [Min(1f)] public float SecondaryTowerMaximumHeight = 154f;
        [Min(1f)] public float SecondaryTowerMinimumWidth = 48f;
        [Min(1f)] public float SecondaryTowerMaximumWidth = 76f;
        [Range(0f, 0.5f)] public float StormTowerHorizontalSpread = 0.4f;
        [Range(0f, 0.5f)] public float StormTowerDepthSpread = 0.28f;
        [Range(0.1f, 1f)] public float StormTowerTopScale = 0.48f;
        [Range(0f, 1f)] public float StormTowerTierOffset = 0.24f;
        [Range(0f, 1f)] public float StormTowerDepthVariation = 0.38f;
        [Min(0.1f)] public float StormTowerVerticalOverlap = 1.72f;
        [Min(0.1f)] public float StormTowerMinimumScaleVariation = 0.82f;
        [Min(0.1f)] public float StormTowerMaximumScaleVariation = 1.18f;
        [Min(0.1f)] public float StormTowerMinimumDepthScale = 0.72f;
        [Min(0.1f)] public float StormTowerMaximumDepthScale = 1.08f;
        [Min(0f)] public float StormTowerBottomMotionMultiplier = 0.35f;
        [Min(0f)] public float StormTowerTopMotionMultiplier = 0.75f;
        public float StormTowerRotationSpeed = 0.18f;
        [Range(0f, 1f)] public float StormUpperColorThreshold = 0.55f;

        [Header("Supporting Masses & Scud")]
        [Range(0, 48)] public int StormSupportLobeCount = 18;
        [Range(0f, 1f)] public float StormSupportMinimumHeight = 0.16f;
        [Range(0f, 1f)] public float StormSupportMaximumHeight = 0.68f;
        [Range(0f, 0.5f)] public float StormSupportHorizontalSpread = 0.5f;
        [Range(0f, 0.5f)] public float StormSupportDepthSpread = 0.5f;
        [Min(0f)] public float StormSupportMotionMultiplier = 0.8f;
        public float StormSupportRotationSpeed = -0.12f;
        public Vector3 StormCloudMinimumScale = new Vector3(42f, 24f, 34f);
        public Vector3 StormCloudMaximumScale = new Vector3(92f, 64f, 72f);
        [Range(0, 32)] public int StormScudLobeCount = 12;
        public float StormScudMinimumHeight = -18f;
        public float StormScudMaximumHeight = 18f;
        [Range(0f, 0.5f)] public float StormScudHorizontalSpread = 0.46f;
        [Range(0f, 0.5f)] public float StormScudDepthSpread = 0.5f;
        public Vector3 StormScudMinimumScale = new Vector3(15f, 7f, 12f);
        public Vector3 StormScudMaximumScale = new Vector3(38f, 18f, 30f);
        [Min(0f)] public float StormScudMotionMultiplier = 1.45f;
        public float StormScudRotationSpeed = 1.35f;

        [Header("Cloud Mesh Families & Motion")]
        [Range(1, 8)] public int StormCloudMeshFamilyCount = 4;
        [Range(6, 24)] public int StormCloudLongitudeSegments = 10;
        [Range(4, 16)] public int StormCloudLatitudeSegments = 7;
        [Range(0f, 0.45f)] public float StormCloudSurfaceVariation = 0.16f;
        [Range(0f, 0.35f)] public float StormCloudBroadVariation = 0.1f;
        [Min(0.1f)] public float StormCloudSurfaceFrequency = 3f;
        [Min(0.1f)] public float StormCloudVerticalFrequency = 2f;
        [Range(0f, 45f)] public float StormCloudMaximumTilt = 18f;
        [Range(0f, 1f)] public float StormCloudVerticalDriftRatio = 0.35f;
        [Min(0f)] public float StormCloudRollAmount = 3.5f;
        [Min(0f)] public float StormCloudRollSpeed = 0.16f;
        [Min(0f)] public float StormCloudRockAngle = 1.8f;

        [Header("Intelligent Cloud Morphing")]
        [Tooltip("Allows the storm mass to billow coherently and reshape in response to movement, turning, and intensity.")]
        public bool IntelligentStormCloudMorphing = true;
        [Min(0f)] public float StormCloudMorphSpeed = 0.22f;
        [Min(1f)] public float StormCloudMorphCoherenceDistance = 115f;
        public Vector3 StormCloudMorphScaleAmount = new Vector3(0.28f, 0.36f, 0.24f);
        [Min(0f)] public float StormCloudMorphPositionAmount = 14f;
        [Min(0f)] public float StormCloudMorphVerticalLift = 18f;
        [Min(1f)] public float StormCloudTravelMorphDistance = 32f;
        [Min(1f)] public float StormCloudMovementMorphMultiplier = 1.65f;
        [Min(0f)] public float StormCloudMorphResponseSharpness = 4f;
        [Min(0.1f)] public float StormCloudMovementReferenceSpeed = 12f;
        [Range(0f, 1f)] public float StormCloudMovementStretch = 0.45f;
        [Range(0f, 0.5f)] public float StormCloudMovementVerticalGrowth = 0.2f;
        [Min(0f)] public float StormCloudMovementLag = 28f;
        [Min(0.1f)] public float StormCloudTurnReferenceSpeed = 25f;
        [Min(0f)] public float StormCloudTurnShear = 32f;
        [Range(0f, 0.5f)] public float StormCloudIntensityConvection = 0.2f;

        [Header("Cloud Lighting & Value Layers")]
        [ColorUsage(false)] public Color StormCloudTopColor = new Color(0.24f, 0.29f, 0.36f, 1f);
        [ColorUsage(false)] public Color StormCloudMiddleColor = new Color(0.095f, 0.12f, 0.16f, 1f);
        [ColorUsage(false)] public Color StormCloudUndersideColor = new Color(0.032f, 0.045f, 0.065f, 1f);
        [ColorUsage(false)] public Color StormCloudScudColor = new Color(0.065f, 0.085f, 0.11f, 1f);
        [ColorUsage(false, true)] public Color StormCloudFlashEmission = new Color(1.7f, 3.8f, 6.5f, 1f);
        [Range(0f, 1f)] public float StormCloudSmoothness = 0.12f;

        [Header("Internal Lightning Rhythm")]
        [Min(0.1f)] public float InternalFlashMinimumInterval = 1.8f;
        [Min(0.1f)] public float InternalFlashMaximumInterval = 5.5f;
        [Min(0.01f)] public float InternalFlashDuration = 0.24f;
        [Min(0f)] public float InternalFlashEmissionMultiplier = 2.4f;
        [Range(0, 8)] public int InternalFlashLightCount = 3;
        [Min(0f)] public float InternalFlashLightRange = 130f;
        [Min(0f)] public float InternalFlashLightIntensity = 1300f;
        [Range(0f, 1f)] public float InternalLightHorizontalSpread = 0.35f;
        [Range(0f, 1f)] public float InternalLightMinimumHeight = 0.2f;
        [Range(0f, 1f)] public float InternalLightMaximumHeight = 0.8f;
        [Min(0f)] public float InternalFlashMinimumFrequencyMultiplier = 0.65f;
        [Min(0f)] public float InternalFlashMaximumFrequencyMultiplier = 1.7f;
        [ColorUsage(false, true)] public Color InternalFlashLightColor = new Color(0.54f, 0.82f, 1f, 1f);

        [Header("Cloud-to-Cloud Electrical Arcs")]
        [Range(0f, 1f)] public float CloudArcActivationIntensity = 0.22f;
        [Min(0.1f)] public float CloudArcMinimumInterval = 2.4f;
        [Min(0.1f)] public float CloudArcMaximumInterval = 6.8f;
        [Min(0f)] public float CloudArcMinimumLength = 24f;
        [Min(0f)] public float CloudArcMaximumLength = 145f;
        [Range(1, 32)] public int CloudArcSelectionAttempts = 10;
        [Min(0.001f)] public float CloudArcWidth = 0.2f;
        [Min(0.01f)] public float CloudArcDuration = 0.16f;

        [Header("Charged Dust Veil")]
        [Range(0, 800)] public int ChargedDustParticleBudget = 360;
        [Min(0f)] public float ChargedDustEmissionRate = 90f;
        [Min(0.1f)] public float ChargedDustLifetime = 4.5f;
        [Min(0.01f)] public float ChargedDustMinimumSize = 0.05f;
        [Min(0.01f)] public float ChargedDustMaximumSize = 0.18f;
        [Min(1f)] public float ChargedDustRadius = 72f;
        [Min(1f)] public float ChargedDustHeight = 42f;
        public Vector3 ChargedDustVelocity = new Vector3(10f, 0.8f, 2f);
        [Min(0f)] public float ChargedDustTurbulence = 1.15f;
        [Min(0f)] public float ChargedDustLengthScale = 0.45f;
        [Min(0f)] public float ChargedDustVelocityStretch = 0.12f;
        [ColorUsage(false)] public Color ChargedDustColor = new Color(0.48f, 0.58f, 0.66f, 0.24f);

        [Header("Static Motes & Air Streaks")]
        [Range(0, 500)] public int StaticMoteParticleBudget = 180;
        [Min(0f)] public float StaticMoteEmissionRate = 32f;
        [Min(0.1f)] public float StaticMoteLifetime = 1.5f;
        [Min(0.01f)] public float StaticMoteMinimumSize = 0.025f;
        [Min(0.01f)] public float StaticMoteMaximumSize = 0.09f;
        [Min(0f)] public float StaticMoteSpeed = 18f;
        [Min(0f)] public float StaticMoteLength = 2.8f;
        [Min(0f)] public float StaticMoteVelocityStretch = 1f;
        [Min(1f)] public float StaticMoteRadius = 42f;
        [Min(1f)] public float StaticMoteHeight = 28f;
        [Min(1f)] public float ChargeBuildupParticleMultiplier = 2.2f;
        [Range(0f, 1f)] public float ParticleFadeInFraction = 0.18f;
        [Range(0f, 1f)] public float ParticleFadeOutFraction = 0.78f;
        [Range(16, 256)] public int ParticleTextureResolution = 64;
        [ColorUsage(false, true)] public Color StaticMoteColor = new Color(0.45f, 1.6f, 3.2f, 0.82f);

        [Header("Distant Probing Strikes")]
        [Min(0.1f)] public float ProbeMinimumInterval = 4.5f;
        [Min(0.1f)] public float ProbeMaximumInterval = 9f;
        [Min(0f)] public float ProbeMinimumDistance = 85f;
        [Min(0f)] public float ProbeMaximumDistance = 260f;
        [Min(1f)] public float ProbeOriginHeight = 125f;
        [Range(0f, 1f)] public float ProbeActivationIntensity = 0.12f;
        [Min(0f)] public float ProbeWidthMultiplier = 0.7f;
        [Min(0f)] public float ProbeMinimumFrequencyMultiplier = 0.7f;
        [Min(0f)] public float ProbeMaximumFrequencyMultiplier = 1.6f;

        [Header("Readable Strike Telegraph")]
        [Range(8, 96)] public int TargetMarkerSegments = 40;
        [Min(0f)] public float TargetMarkerStartRadius = 1.4f;
        [Min(0f)] public float TargetMarkerEndRadius = 5.2f;
        [Min(0.001f)] public float TargetMarkerWidth = 0.12f;
        [Min(0f)] public float TargetMarkerHeightOffset = 0.14f;
        [Min(0f)] public float AirTargetMarkerRadius = 2.8f;
        [Min(0f)] public float TargetPulseSpeed = 12f;
        [Range(0f, 1f)] public float TargetPulseAmount = 0.16f;
        [Min(1f)] public float ChargeColumnHeight = 68f;
        [Min(0.001f)] public float ChargeColumnStartWidth = 0.025f;
        [Min(0.001f)] public float ChargeColumnEndWidth = 0.22f;
        [Range(0f, 1f)] public float ChargeColumnTipWidthMultiplier = 0.35f;
        [Range(0, 240)] public int ConvergingSparkBudget = 90;
        [Min(0f)] public float ConvergingSparkEmissionRate = 48f;
        [Min(0.1f)] public float ConvergingSparkLifetime = 0.7f;
        [Min(0.01f)] public float ConvergingSparkSize = 0.075f;
        [Min(0f)] public float ConvergingSparkSpeed = 8f;
        [Range(0f, 1f)] public float ConvergingSparkInitialEmissionFraction = 0.35f;
        [ColorUsage(false, true)] public Color TelegraphColor = new Color(0.24f, 2.8f, 6.8f, 1f);

        [Header("Lightning Release")]
        [Range(4, 32)] public int LightningSegments = 15;
        [Min(0.001f)] public float LightningStartWidth = 0.52f;
        [Range(0.1f, 1f)] public float LightningEndWidthMultiplier = 0.42f;
        [Min(0f)] public float LightningMinimumJitter = 0.45f;
        [Min(0f)] public float LightningMaximumJitter = 3.8f;
        [Min(0f)] public float LightningJitterPerMeter = 0.026f;
        [Min(0.01f)] public float LightningVisualDuration = 0.34f;
        [Range(0, 8)] public int LightningBranchCount = 4;
        [Min(0f)] public float LightningBranchLength = 9f;
        [Min(0.001f)] public float LightningBranchWidthMultiplier = 0.46f;
        [ColorUsage(false, true)] public Color LightningColor = new Color(5.8f, 11f, 18f, 1f);
        [Min(0f)] public float ImpactFlashRadius = 5.8f;
        [Min(0.01f)] public float ImpactFlashDuration = 0.38f;

        [Header("Near-Field Arc Snaps")]
        [Min(0.1f)] public float NearArcMinimumInterval = 1.4f;
        [Min(0.1f)] public float NearArcMaximumInterval = 3.8f;
        [Min(0f)] public float NearArcMinimumRadius = 2.4f;
        [Min(0f)] public float NearArcMaximumRadius = 8f;
        [Min(0.001f)] public float NearArcWidth = 0.055f;
        [Min(0.01f)] public float NearArcDuration = 0.12f;

        [Header("Landmark Electrical Reactions")]
        public bool LandmarkReactionsEnabled = true;
        [Min(0f)] public float LandmarkReactionRange = 240f;
        [Min(0.1f)] public float LandmarkReactionMinimumInterval = 3.5f;
        [Min(0.1f)] public float LandmarkReactionMaximumInterval = 7.5f;
        [Min(0.001f)] public float LandmarkArcWidth = 0.11f;
        [Min(0.01f)] public float LandmarkArcDuration = 0.2f;

        [Header("Interior Storm Atmosphere")]
        public float InteriorVolumePriority = 210f;
        [Min(0f)] public float InteriorBlendSharpness = 5f;
        public float InteriorPostExposure = -0.52f;
        [Range(-100f, 100f)] public float InteriorSaturation = -28f;
        [Range(-100f, 100f)] public float InteriorContrast = 22f;
        [ColorUsage(false)] public Color InteriorColorFilter = new Color(0.72f, 0.82f, 0.92f, 1f);
        [Min(0f)] public float InteriorBloomIntensity = 0.68f;
        [Min(0f)] public float InteriorBloomThreshold = 0.82f;

        [Header("Electrical HUD")]
        [Min(100f)] public float HudWidth = 304f;
        [Min(40f)] public float HudHeight = 72f;
        [Min(0f)] public float HudLeft = 24f;
        [Min(0f)] public float HudTop = 118f;
        [Tooltip("Minimum vertical gap below other visible left-side HUD panels.")]
        [Min(0f)] public float HudOtherPanelGap = 14f;
        [Min(0f)] public float HudPadding = 12f;
        [Min(1f)] public float HudAccentWidth = 4f;
        [Min(8)] public int HudTitleFontSize = 12;
        [Min(8)] public int HudStatusFontSize = 15;
        [Min(8f)] public float HudTitleRowHeight = 18f;
        [Min(8f)] public float HudStatusRowHeight = 21f;
        [Min(0f)] public float HudTextRowGap = 2f;
        public string HudStormLabel = "ELECTRICAL STORM REGION";
        public string HudIonizationLabel = "IONIZATION SPIKE DETECTED";
        public string HudInterferenceLabel = "DRONE SYSTEM INTERFERENCE";
        [ColorUsage(false)] public Color HudPanelColor = new Color(0.018f, 0.028f, 0.052f, 0.9f);
        [ColorUsage(false)] public Color HudAccentColor = new Color(0.18f, 0.74f, 1f, 1f);
        [ColorUsage(false)] public Color HudTextColor = new Color(0.78f, 0.9f, 1f, 1f);
        [ColorUsage(false)] public Color HudStaticColor = new Color(0.35f, 0.82f, 1f, 0.14f);
        [Range(0, 16)] public int HudStaticLineCount = 5;
        [Min(0f)] public float HudStaticLineHeight = 1f;
        [Min(0f)] public float HudStaticJitter = 4f;
        [Min(0f)] public float HudStaticSpeed = 18f;
        [Range(0f, 1f)] public float HudApproachStaticMultiplier = 0.35f;
    }

    [System.Serializable]
    public sealed class ElectricalSandstormTuning
    {
        public bool Enabled = true;
        [Range(0f, 1f)] public float MinimumStormIntensity = 0.7f;
        [Tooltip("Horizontal distance beyond the visible stormfront footprint where electrical interference and lightning become active.")]
        [Min(0f)] public float ElectricalEffectRange = 35f;
        [Min(0f)] public float InitialStrikeDelay = 4f;
        [Min(0.1f)] public float ElectricalBuildupDuration = 1.4f;
        [Min(0.1f)] public float TargetTelegraphDuration = 1.8f;
        [Min(0.1f)] public float MinimumStrikeInterval = 5.5f;
        [Min(0.1f)] public float MaximumStrikeInterval = 8.5f;
        [Min(0f)] public float TargetPredictionTime = 0.65f;
        [Min(0f)] public float MaximumPredictionDistance = 28f;
        [Min(0f)] public float AirTargetMinimumHeight = 4f;
        [Min(0.1f)] public float StrikeRadius = 4.5f;
        [Min(0f)] public float StrikeDamage = 24f;
        public string StrikeDeathMessage = "Struck by Electrical Sandstorm lightning.";
        [Min(1f)] public float HazardousCargoDamageMultiplier = 1.35f;
        [Range(0.1f, 1f)] public float HighValueStrikeIntervalMultiplier = 0.82f;
        [Min(1f)] public float WeaponCooldownMultiplier = 1.3f;
        public int RandomSeedOffset = 22483;
        public ElectricalStormVisualTuning Visuals = new ElectricalStormVisualTuning();

        public void EnsureInitialized()
        {
            Visuals ??= new ElectricalStormVisualTuning();
        }
    }

    [System.Serializable]
    public sealed class HeatZoneTuning
    {
        public bool Enabled = true;

        [Header("Regional Heat Zones")]
        [Min(20f)] public float ZoneCellSize = 260f;
        [Range(0f, 1f)] public float ZoneChance = 0.32f;
        [Min(1f)] public float MinimumZoneRadius = 70f;
        [Min(1f)] public float MaximumZoneRadius = 125f;
        [Range(0f, 1f)] public float ZoneEdgeFalloff = 0.35f;
        public int RandomSeedOffset = 30871;

        [Header("Zone Severity")]
        [Range(0f, 1f)] public float SevereZoneChance = 0.28f;
        [Range(0f, 1f)] public float ExtremeZoneChance = 0.07f;
        [Range(0f, 1f)] public float MildSeverity = 0.55f;
        [Range(0f, 1f)] public float SevereSeverity = 0.78f;
        [Range(0f, 1f)] public float ExtremeSeverity = 1f;

        [Header("Drone Temperature")]
        [Min(1f)] public float MaximumTemperature = 100f;
        [Min(0f)] public float ZoneHeatPerSecond = 8f;
        [Min(0f)] public float BoostHeatPerSecond = 5f;
        [Min(0f)] public float WeaponHeatPerShot = 8f;
        [Min(0f)] public float PassiveCoolingPerSecond = 7f;
        [Min(0f)] public float CoolingAltitudeStart = 18f;
        [Min(0f)] public float CoolingAltitudeFull = 65f;
        [Min(1f)] public float HighAltitudeCoolingMultiplier = 2.4f;
        [Min(1f)] public float HotZoneBoostHeatMultiplier = 1.6f;
        [Min(1f)] public float HotZoneWeaponHeatMultiplier = 1.5f;

        [Header("Mechanical Consequences")]
        [Range(0f, 1f)] public float ConsequenceTemperatureThreshold = 0.55f;
        [Min(1f)] public float MaximumBoostDrainMultiplier = 1.45f;
        [Min(1f)] public float MaximumWeaponCooldownMultiplier = 1.6f;

        [Header("Visual Range & Refresh")]
        public bool VisualsEnabled = true;
        [Min(20f)] public float VisualRange = 540f;
        [Range(1, 8)] public int MaximumVisibleZones = 4;
        [Min(0.05f)] public float VisualRefreshInterval = 0.75f;
        [Min(8)] public int CurtainSegments = 48;

        [Header("Refractive Air")]
        [Min(1f)] public float ShimmerCurtainHeight = 62f;
        [Range(0.1f, 1.5f)] public float ShimmerCurtainRadiusMultiplier = 1f;

        [Header("Independent Ground Distortion")]
        [Tooltip("Keeps the terrain-following ground distortion active independently of heat-zone gameplay.")]
        public bool GroundDistortionEnabled = true;
        [Min(2)] public int GroundMirageRings = 7;
        [Min(8)] public int GroundMirageSegments = 48;
        [Min(0f)] public float GroundMirageHeightOffset = 0.16f;
        [Range(1, 8)] public int GroundDistortionShellCount = 3;
        [Min(0f)] public float GroundDistortionShellSpacing = 0.55f;
        [Range(0f, 1f)] public float GroundDistortionShellStrengthFalloff = 0.72f;
        [Min(20f)] public float GroundDistortionFollowRadius = 420f;
        [Min(5f)] public float GroundDistortionRecenterDistance = 40f;
        [Range(1, 64)] public int GroundHeatVeilRingCount = 48;
        [Range(16, 192)] public int GroundHeatVeilSegments = 128;
        [Min(0.25f)] public float GroundHeatVeilMinimumRadius = 1.5f;
        [Range(0.25f, 4f)] public float GroundHeatVeilRadiusDistribution = 1.6f;
        [Min(0.1f)] public float GroundHeatVeilMinimumHeight = 1.5f;
        [Min(0.1f)] public float GroundHeatVeilMaximumHeight = 3.5f;
        [Min(0f)] public float GroundHeatVeilBaseOffset = 0.1f;
        [Range(0f, 0.3f)] public float GroundHeatShimmerOpacity = 0.14f;
        [ColorUsage(false, true)] public Color GroundHeatShimmerColor = new Color(1.15f, 0.9f, 0.62f, 1f);
        [Range(0.05f, 1f)] public float GroundMirageRadiusMultiplier = 0.98f;
        [Min(0f)] public float DistantDistortionStrength = 0.34f;
        [Min(0f)] public float InteriorDistortionStrength = 0.78f;
        [Min(0f)] public float DistortionBlurStrength = 0.18f;
        [Min(0f)] public float DistortionTextureScale = 4.5f;
        public Vector2 DistortionScrollVelocity = new Vector2(0.035f, 0.12f);
        [Range(16, 256)] public int DistortionTextureResolution = 96;
        [Min(0f)] public float MirageSurfaceOpacity = 0.09f;
        [ColorUsage(false, true)] public Color MirageSurfaceColor = new Color(1.15f, 0.96f, 0.62f, 0.09f);

        [Header("Rising Heat Columns")]
        [Range(0, 240)] public int HeatPlumeParticleBudget = 72;
        [Min(0f)] public float HeatPlumeEmissionRate = 7f;
        [Min(0.1f)] public float HeatPlumeMinimumLifetime = 4.5f;
        [Min(0.1f)] public float HeatPlumeMaximumLifetime = 8f;
        [Min(0.01f)] public float HeatPlumeMinimumSize = 4f;
        [Min(0.01f)] public float HeatPlumeMaximumSize = 10f;
        [Min(1f)] public float HeatPlumeMinimumHeightMultiplier = 3.2f;
        [Min(1f)] public float HeatPlumeMaximumHeightMultiplier = 5.8f;
        [Min(0f)] public float HeatPlumeRiseSpeed = 5.5f;
        [Min(0f)] public float HeatPlumeTurbulence = 0.45f;

        [Header("Heat Plume Distortion Mask")]
        [Min(0f)] public float HeatPlumeDistortionStrength = 0.12f;
        [Range(0f, 1f)] public float HeatPlumeDistortionBlur = 0.04f;
        public Vector2 HeatPlumePrimaryTiling = new Vector2(2.4f, 3.2f);
        public Vector2 HeatPlumeSecondaryTiling = new Vector2(4.1f, 2.3f);
        public Vector2 HeatPlumePrimaryVelocity = new Vector2(0.035f, 0.12f);
        public Vector2 HeatPlumeSecondaryVelocity = new Vector2(-0.055f, 0.073f);
        [Range(0f, 1f)] public float HeatPlumeSecondaryStrength = 0.48f;
        [Range(0f, 1f)] public float HeatPlumeHorizontalTurbulence = 0.24f;
        [Range(0.05f, 0.5f)] public float HeatPlumeCoreWidth = 0.28f;
        [Range(0.05f, 0.5f)] public float HeatPlumeTopWidth = 0.4f;
        [Range(0f, 0.5f)] public float HeatPlumeWidthVariation = 0.16f;
        [Min(0f)] public float HeatPlumeWidthFrequency = 5.2f;
        [Range(0.01f, 1f)] public float HeatPlumeSideFeather = 0.42f;
        [Range(0.01f, 1f)] public float HeatPlumeBottomFeather = 0.2f;
        [Range(0.01f, 1f)] public float HeatPlumeTopFeather = 0.34f;
        [Range(0f, 1f)] public float HeatPlumeVerticalDissipationStart = 0.3f;
        [Min(0.01f)] public float HeatPlumeVerticalDissipationPower = 1.4f;
        [Range(0f, 0.5f)] public float HeatPlumeMaximumLean = 0.12f;
        [Min(0f)] public float HeatPlumeMinimumAnimationSpeedMultiplier = 0.78f;
        [Min(0f)] public float HeatPlumeMaximumAnimationSpeedMultiplier = 1.22f;
        [Min(0f)] public float HeatPlumeMinimumStrengthMultiplier = 0.75f;
        [Min(0f)] public float HeatPlumeMaximumStrengthMultiplier = 1.2f;
        [Min(0f)] public float HeatPlumePhaseRange = 17.371f;
        public float HeatPlumePrimaryPhaseOffset = 0.37f;
        public float HeatPlumeSecondaryPhaseOffset = -0.23f;
        [Range(0.001f, 0.25f)] public float HeatPlumeCardEdgeFeather = 0.04f;
        [Range(0f, 1f)] public float HeatPlumeEdgeNoiseBase = 0.72f;
        [Range(0f, 1f)] public float HeatPlumePrimaryEdgeNoise = 0.56f;
        [Range(0f, 1f)] public float HeatPlumeSecondaryEdgeNoise = 0.28f;
        [Range(0f, 1f)] public float HeatPlumeFadeProfileVariation = 0.18f;
        [Range(0f, 1f)] public float HeatPlumeLifetimeFadeInFraction = 0.12f;
        [Range(0f, 1f)] public float HeatPlumeLifetimeFadeOutFraction = 0.72f;
        [Min(0f)] public float HeatPlumeDistanceFadeStart = 160f;
        [Min(0f)] public float HeatPlumeDistanceFadeEnd = 480f;
        [Min(0f)] public float HeatPlumeDetailFadeStart = 120f;
        [Min(0f)] public float HeatPlumeDetailFadeEnd = 320f;
        [Min(0f)] public float HeatPlumeDepthFadeDistance = 2.5f;
        [Min(0f)] public float HeatPlumeMaskClipThreshold = 0.001f;

        [Header("Hot Wind Streaks")]
        [Range(0, 320)] public int HeatStreakParticleBudget = 110;
        [Min(0f)] public float HeatStreakEmissionRate = 16f;
        [Min(0.1f)] public float HeatStreakLifetime = 2.4f;
        [Min(0.01f)] public float HeatStreakSize = 0.07f;
        [Min(0f)] public float HeatStreakLength = 3.2f;
        public Vector2 HeatStreakDirection = new Vector2(1f, 0.22f);
        [Min(0f)] public float HeatStreakSpeed = 18f;
        [Min(0f)] public float HeatStreakHeightFraction = 0.32f;
        [Min(0f)] public float HeatStreakVolumeRadiusMultiplier = 1.6f;
        [Min(0f)] public float HeatStreakVolumeHeightMultiplier = 0.5f;
        [Min(0f)] public float HeatStreakVelocityStretch = 1f;
        [ColorUsage(false)] public Color HeatStreakColor = new Color(1f, 0.9f, 0.65f, 0.16f);

        [Header("Terrain Heat Pockets")]
        [Range(0, 24)] public int HotSpotCount = 9;
        [Min(0f)] public float HotSpotMinimumRadius = 1.6f;
        [Min(0f)] public float HotSpotMaximumRadius = 4.8f;
        [Min(0f)] public float HotSpotHeightOffset = 0.08f;
        [Min(0f)] public float HotSpotPlateThickness = 0.18f;
        [Min(0f)] public float HotSpotGlowScale = 0.62f;
        [Range(0f, 1f)] public float HotSpotMinimumDistanceFraction = 0.14f;
        [Range(0f, 1f)] public float HotSpotMaximumDistanceFraction = 0.82f;
        [Min(0f)] public float HotSpotPlateAspect = 1.45f;
        [Min(0f)] public float HotSpotGlowHeightMultiplier = 0.6f;
        [ColorUsage(false)] public Color HotSpotPlateColor = new Color(0.09f, 0.075f, 0.06f, 1f);
        [ColorUsage(false, true)] public Color HotSpotGlowColor = new Color(3.2f, 1.25f, 0.18f, 1f);
        [Range(0f, 1f)] public float HotSpotSmoothness = 0.22f;

        [Header("Interior Atmosphere")]
        public float InteriorVolumePriority = 200f;
        [Min(0f)] public float InteriorBlendSharpness = 4f;
        public float InteriorPostExposure = 0.28f;
        [Range(-100f, 100f)] public float InteriorSaturation = -16f;
        [Range(-100f, 100f)] public float InteriorContrast = 8f;
        [ColorUsage(false)] public Color InteriorColorFilter = new Color(1f, 0.96f, 0.82f, 1f);
        [Min(0f)] public float InteriorBloomIntensity = 0.34f;
        [Min(0f)] public float InteriorBloomThreshold = 1.08f;

        [Header("Thermal HUD")]
        [Range(0f, 1f)] public float HudVisibilityThreshold = 0.08f;
        [Min(100f)] public float HudWidth = 268f;
        [Min(40f)] public float HudHeight = 82f;
        [Min(0f)] public float HudRight = 24f;
        [Min(0f)] public float HudTop = 118f;
        [Tooltip("Minimum vertical gap below the upper-flight-ring HUD while both panels are visible.")]
        [Min(0f)] public float HudUpperFlightGap = 14f;
        [Min(0f)] public float HudPadding = 12f;
        [Min(1f)] public float HudAccentWidth = 4f;
        [Min(1f)] public float HudBarHeight = 8f;
        [Min(8)] public int HudTitleFontSize = 12;
        [Min(8)] public int HudStatusFontSize = 15;
        [Min(8f)] public float HudTitleRowHeight = 18f;
        [Min(8f)] public float HudStatusRowHeight = 21f;
        [Min(0f)] public float HudTextRowGap = 2f;
        public string HudZoneLabel = "HIGH THERMAL ZONE";
        public string HudRisingLabel = "DRONE HEAT RISING";
        public string HudBoostLabel = "BOOST EFFICIENCY REDUCED";
        [ColorUsage(false)] public Color HudPanelColor = new Color(0.035f, 0.045f, 0.052f, 0.88f);
        [ColorUsage(false)] public Color HudAccentColor = new Color(1f, 0.56f, 0.12f, 1f);
        [ColorUsage(false)] public Color HudTrackColor = new Color(0.13f, 0.15f, 0.16f, 1f);
        [ColorUsage(false)] public Color HudCoolColor = new Color(1f, 0.78f, 0.26f, 1f);
        [ColorUsage(false)] public Color HudHotColor = new Color(1f, 0.19f, 0.06f, 1f);
        [ColorUsage(false)] public Color HudTextColor = new Color(0.96f, 0.94f, 0.86f, 1f);
    }

    [System.Serializable]
    public sealed class EnvironmentalHazardTuning
    {
        public ElectricalSandstormTuning ElectricalSandstorms = new ElectricalSandstormTuning();
        public HeatZoneTuning HeatZones = new HeatZoneTuning();

        public void EnsureInitialized()
        {
            ElectricalSandstorms ??= new ElectricalSandstormTuning();
            ElectricalSandstorms.EnsureInitialized();
            HeatZones ??= new HeatZoneTuning();
        }
    }

    [System.Serializable]
    public sealed class PauseMenuVisualTuning
    {
        [Header("Responsive Layout")]
        [Min(320f)] public float ReferenceWidth = 1920f;
        [Min(240f)] public float ReferenceHeight = 1080f;
        [Tooltip("Preferred minimum UI scale. The menu can scale below this when required to fit the viewport.")]
        [Range(0.5f, 2f)] public float MinimumScale = 0.7f;
        [Range(0.5f, 2f)] public float MaximumScale = 1.2f;
        [Min(280f)] public float PanelWidth = 540f;
        [Min(340f)] public float PanelHeight = 824f;
        [Min(8f)] public float ScreenMargin = 24f;
        [Min(12f)] public float PanelPadding = 36f;
        [Min(1f)] public float AccentBarHeight = 6f;
        [Min(0f)] public float ShadowOffset = 10f;

        [Header("Mixer Controls")]
        [Min(40f)] public float SliderRowHeight = 76f;
        [Min(2f)] public float SliderTrackHeight = 9f;
        [Min(4f)] public float SliderThumbWidth = 12f;
        [Min(8f)] public float SliderThumbHeight = 22f;
        [Min(0f)] public float DialogueButtonGap = 24f;
        [Min(24f)] public float ButtonHeight = 44f;
        [Min(0f)] public float ButtonGap = 10f;

        [Header("Song Controls")]
        [Min(0f)] public float SongControlsLeft = 28f;
        [Min(0f)] public float SongControlsTop = 24f;
        [Min(120f)] public float SongControlsWidth = 360f;
        [Min(16f)] public float SongTitleHeight = 30f;
        [Min(18f)] public float SongControlSize = 38f;
        [Min(0f)] public float SongControlGap = 10f;
        [Min(0f)] public float SongTextShadowOffset = 2f;
        [Min(10)] public int SongTitleFontSize = 18;
        [Min(10)] public int SongControlFontSize = 20;
        [Min(10)] public int SongPauseFontSize = 24;
        public float SongPauseVerticalOffset = 4f;
        [ColorUsage(false)] public Color SongTextColor = Color.white;
        [ColorUsage(false)] public Color SongTextShadowColor = Color.black;

        [Header("Controls Screen")]
        [Tooltip("Full-screen controls reference shown from the pause menu.")]
        public Texture2D ControlsImage;
        [Tooltip("Label used by the pause-menu button that opens the controls reference.")]
        public string ControlsButtonLabel = "CONTROLS";
        [Tooltip("Seconds used to fade between the pause menu and the controls reference.")]
        [Min(0f)] public float ControlsFadeDuration = 0.3f;
        [Tooltip("Letterbox and fade color behind the controls reference.")]
        [ColorUsage(false)] public Color ControlsBackgroundColor = Color.black;
        [Tooltip("Label shown on the music visualizer toggle.")]
        public string ControlsVisualizerLabel = "MUSIC VISUALIZER";
        [Tooltip("State label shown while the music visualizer is active.")]
        public string ControlsVisualizerEnabledLabel = "ALL";
        [Tooltip("State label shown when the visualizer is active without bass rings.")]
        public string ControlsVisualizerNoFlashLabel = "NO FLASH";
        [Tooltip("State label shown while the music visualizer is inactive.")]
        public string ControlsVisualizerDisabledLabel = "OFF";

        [Header("Music Visualizer Settings")]
        public const MusicVisualEffectGroups FlashMusicVisualizerEffects =
            MusicVisualEffectGroups.PressureFront
            | MusicVisualEffectGroups.Structures
            | MusicVisualEffectGroups.Streaks
            | MusicVisualEffectGroups.Glitch
            | MusicVisualEffectGroups.HudBorder
            | MusicVisualEffectGroups.Bloom
            | MusicVisualEffectGroups.TrebleParticles;
        public string MusicVisualizerSettingsButtonLabel = "MUSIC VISUALIZER";
        public string MusicVisualizerSettingsTitle = "MUSIC VISUALIZER";
        public string MusicVisualizerSettingsSubtitle = "DUNE VECTOR  /  MUSIC-REACTIVE EFFECTS";
        public string MusicVisualizerSettingsSectionLabel = "VISUAL EFFECTS";
        public string MusicVisualizerSettingsBackButtonLabel = "BACK TO PAUSE";
        public string MusicVisualizerSettingsResetButtonLabel = "RESET DEFAULTS";
        public string MusicVisualizerSettingsHintLabel = "ESC  /  BACK TO PAUSE";
        public string MusicVisualizerMasterLabel = "MASTER VISUALIZER";
        public string MusicVisualizerSkyLabel = "SKY & AURORA";
        public string MusicVisualizerBloomLabel = "BLOOM & FLASHES";
        public string MusicVisualizerPressureFrontsLabel = "PRESSURE FRONTS";
        public string MusicVisualizerWorldResponseLabel = "WORLD PULSES";
        public string MusicVisualizerStreaksLabel = "FOREGROUND STREAKS";
        public string MusicVisualizerCameraLabel = "CAMERA MOTION";
        public string MusicVisualizerFovLabel = "CAMERA FOV";
        public string MusicVisualizerGlitchLabel = "GLITCH & HUD";
        public string MusicVisualizerEffectEnabledLabel = "ON";
        public string MusicVisualizerEffectDisabledLabel = "OFF";
        public bool DefaultMusicVisualizerEnabled = true;
        public bool DefaultMusicVisualizerSkyEnabled = true;
        public bool DefaultMusicVisualizerBloomEnabled = true;
        public bool DefaultMusicVisualizerPressureFrontsEnabled = true;
        public bool DefaultMusicVisualizerWorldResponseEnabled = true;
        public bool DefaultMusicVisualizerStreaksEnabled = true;
        public bool DefaultMusicVisualizerCameraEnabled = true;
        public bool DefaultVisualizerFovEnabled = true;
        public bool DefaultMusicVisualizerGlitchEnabled = true;

        public MusicVisualEffectGroups BuildDefaultMusicVisualizerEffectMask()
        {
            MusicVisualEffectGroups mask = MusicVisualEffectGroups.None;
            if (DefaultMusicVisualizerSkyEnabled)
            {
                mask |= MusicVisualEffectGroups.Sky
                    | MusicVisualEffectGroups.Filaments
                    | MusicVisualEffectGroups.TrebleParticles;
            }
            if (DefaultMusicVisualizerBloomEnabled)
            {
                mask |= MusicVisualEffectGroups.Bloom;
            }
            if (DefaultMusicVisualizerPressureFrontsEnabled)
            {
                mask |= MusicVisualEffectGroups.PressureFront;
            }
            if (DefaultMusicVisualizerWorldResponseEnabled)
            {
                mask |= MusicVisualEffectGroups.Road
                    | MusicVisualEffectGroups.Structures
                    | MusicVisualEffectGroups.Drone;
            }
            if (DefaultMusicVisualizerStreaksEnabled)
            {
                mask |= MusicVisualEffectGroups.Streaks;
            }
            if (DefaultMusicVisualizerCameraEnabled)
            {
                mask |= MusicVisualEffectGroups.Camera;
            }
            if (DefaultMusicVisualizerGlitchEnabled)
            {
                mask |= MusicVisualEffectGroups.Glitch | MusicVisualEffectGroups.HudBorder;
            }
            return mask;
        }

        [Header("Video Settings")]
        public string VideoSettingsButtonLabel = "VIDEO SETTINGS";
        public string VideoSettingsTitle = "VIDEO SETTINGS";
        public string VideoSettingsSubtitle = "DUNE VECTOR  /  DISPLAY EFFECTS";
        public string VideoSettingsSectionLabel = "POST-PROCESSING";
        public string VideoSettingsBackButtonLabel = "BACK TO PAUSE";
        public string VideoSettingsResetButtonLabel = "RESET DEFAULTS";
        public string VideoSettingsHintLabel = "ESC  /  BACK TO PAUSE";
        public string VideoAntiAliasingLabel = "ANTI-ALIASING";
        public string VideoAntiAliasingOffLabel = "OFF";
        public string VideoAntiAliasingSmaaLabel = "SMAA";
        public string VideoAntiAliasingTaaLabel = "TAA";
        public string VideoChromaticAberrationLabel = "CHROMATIC ABERRATION";
        public string VideoLensDistortionLabel = "LENS DISTORTION";
        public string VideoCrtLinesLabel = "CRT LINES";
        public string VideoFilmGrainLabel = "FILM GRAIN";
        public string VideoVignetteLabel = "VIGNETTE";
        public string VideoBloomLabel = "BLOOM";
        public string VideoVisualizerFovLabel = "VISUALIZER FOV";
        public string VideoEffectEnabledLabel = "ON";
        public string VideoEffectDisabledLabel = "OFF";
        public bool DefaultChromaticAberrationEnabled;
        public bool DefaultLensDistortionEnabled;
        public bool DefaultCrtLinesEnabled = true;
        public bool DefaultFilmGrainEnabled;
        public bool DefaultVignetteEnabled;
        public bool DefaultBloomEnabled;
        [Range(0f, 1f)] public float VideoFilmGrainIntensity = 0.18f;
        [Range(0f, 1f)] public float VideoFilmGrainResponse = 0.8f;

        [Header("Cheat Codes")]
        [Tooltip("Typing this phrase while the pause screen is open permanently unlocks every upgrade.")]
        public string UpgradeUnlockCheatCode = "giveallupgradespls";

        [Header("Panel Styling")]
        [Tooltip("Vertical gradient applied across the panel body. 0 keeps the panel flat.")]
        [Range(0f, 1f)] public float PanelGradientStrength = 0.45f;
        [Tooltip("Length of the accent brackets drawn at each panel corner. 0 hides them.")]
        [Min(0f)] public float CornerBracketLength = 22f;
        [Min(1f)] public float CornerBracketThickness = 3f;
        [Tooltip("Accent-colored glow drawn behind panel titles. 0 disables the glow.")]
        [Range(0f, 1f)] public float TitleGlowStrength = 0.4f;
        [Tooltip("Darkening drawn around the screen edges behind the panel. 0 disables it.")]
        [Range(0f, 1f)] public float OverlayVignetteStrength = 0.5f;
        [Tooltip("Seconds the panel takes to fade and settle into place when the game pauses.")]
        [Min(0f)] public float OpenAnimationDuration = 0.16f;
        [Tooltip("Pixels the panel rises through while it fades in.")]
        [Min(0f)] public float OpenAnimationRise = 14f;
        [Tooltip("Width of the accent stripe drawn down the left edge of an enabled toggle row.")]
        [Min(0f)] public float ButtonHoverStripeWidth = 4f;

        [Header("Letter Spacing")]
        [Min(0f)] public float TitleTracking = 6f;
        [Min(0f)] public float SubtitleTracking = 1.5f;
        [Min(0f)] public float SectionTracking = 2.5f;
        [Min(0f)] public float HintTracking = 1.5f;

        [Header("Typography")]
        [Min(12)] public int TitleFontSize = 36;
        [Min(10)] public int SubtitleFontSize = 13;
        [Min(10)] public int SectionFontSize = 14;
        [Min(10)] public int MixerLabelFontSize = 16;
        [Min(10)] public int ValueFontSize = 15;
        [Min(10)] public int ButtonFontSize = 15;
        [Min(9)] public int HintFontSize = 12;

        [Header("Desert Palette")]
        [ColorUsage(false)] public Color OverlayColor = new Color(0.015f, 0.025f, 0.045f, 0.86f);
        [ColorUsage(false)] public Color ShadowColor = new Color(0f, 0f, 0f, 0.48f);
        [ColorUsage(false)] public Color PanelColor = new Color(0.026f, 0.034f, 0.048f, 0.98f);
        [ColorUsage(false)] public Color PanelBorderColor = new Color(0.92f, 0.5f, 0.16f, 0.82f);
        [ColorUsage(false)] public Color AccentColor = new Color(1f, 0.61f, 0.18f, 1f);
        [ColorUsage(false)] public Color TitleColor = new Color(1f, 0.76f, 0.3f, 1f);
        [ColorUsage(false)] public Color PrimaryTextColor = new Color(0.92f, 0.96f, 1f, 1f);
        [ColorUsage(false)] public Color SecondaryTextColor = new Color(0.57f, 0.67f, 0.76f, 1f);
        [ColorUsage(false)] public Color DividerColor = new Color(0.3f, 0.37f, 0.44f, 0.75f);
        [ColorUsage(false)] public Color SliderTrackColor = new Color(0.12f, 0.16f, 0.21f, 1f);
        [ColorUsage(false)] public Color SliderFillColor = new Color(1f, 0.54f, 0.13f, 1f);
        [ColorUsage(false)] public Color SliderThumbColor = new Color(1f, 0.8f, 0.42f, 1f);
        [ColorUsage(false)] public Color ButtonColor = new Color(0.096f, 0.144f, 0.192f, 1f);
        [ColorUsage(false)] public Color ButtonHoverColor = new Color(0.152f, 0.232f, 0.304f, 1f);
        [ColorUsage(false)] public Color ButtonActiveColor = new Color(0.93f, 0.47f, 0.12f, 1f);
        [ColorUsage(false)] public Color DangerButtonColor = new Color(0.248f, 0.096f, 0.08f, 1f);
        [ColorUsage(false)] public Color DangerButtonHoverColor = new Color(0.4f, 0.136f, 0.096f, 1f);
        [Tooltip("Label color used on accent-filled buttons, where light text has too little contrast.")]
        [ColorUsage(false)] public Color PrimaryButtonTextColor = new Color(0.05f, 0.07f, 0.1f, 1f);
        [Tooltip("Label color used while a dark button is hovered.")]
        [ColorUsage(false)] public Color ButtonHoverTextColor = new Color(1f, 0.87f, 0.63f, 1f);
        [Tooltip("Hairline highlight drawn directly beneath the panel accent bar.")]
        public Color PanelHighlightColor = new Color(1f, 0.78f, 0.42f, 0.16f);
    }

    [System.Serializable]
    public sealed class MusicPlaylistTrack
    {
        [Tooltip("Song name shown by the pause-menu music controls.")]
        public string DisplayName;
        [Tooltip("FMOD event played for this playlist entry.")]
        public string FmodEventPath;
        [Tooltip("Visualizer composition and cue profile selected while this song is active.")]
        public MusicVisualTrackProfile VisualizerProfile;
    }

    [System.Serializable]
    public sealed class AudioTuning
    {
        [Header("FMOD Events")]
        [Tooltip("Songs played as a shuffled playlist. Each entry selects its matching visualizer profile.")]
        public MusicPlaylistTrack[] BackgroundMusicPlaylist = System.Array.Empty<MusicPlaylistTrack>();
        [Tooltip("Playlist entry played first when the game starts. Remaining entries stay shuffled.")]
        [Min(0)] public int StartingBackgroundMusicTrackIndex;
        [Tooltip("Legacy fallback used only when the background-music playlist is empty.")]
        public string BackgroundMusicEvent = "event:/Shadows on the Mesa";
        [Tooltip("One-shot event played whenever the drone successfully loses health.")]
        public string DroneDamageEvent = "event:/Drone_Damage";
        [Tooltip("One-shot event played when the drone successfully launches an energy shot.")]
        public string DroneFireEvent = "event:/Drone_Fire";
        [Tooltip("Looped event played while the drone uses its stamina boost in flight mode.")]
        public string DroneFlightBoostEvent = "event:/Drone_Boost";
        [Tooltip("One-shot event played when the drone passes through a flight ring.")]
        public string FlightRingSwooshEvent = "event:/Flight_Ring_Swoosh";
        [Tooltip("One-shot event played when the drone passes through a delivery ring.")]
        public string DeliveryRingEvent = "event:/Delivery";
        [Tooltip("Seconds used to fade the drone flight boost loop in after boosting starts.")]
        [Min(0f)] public float DroneFlightBoostFadeInDuration = 0.2f;
        [Tooltip("Seconds used to fade the drone flight boost loop to silence after boosting stops.")]
        [Min(0f)] public float DroneFlightBoostFadeOutDuration = 0.35f;
        [Tooltip("One-shot event played when a new lock-on target is initially detected.")]
        public string LockOnEvent = "event:/Lock_On";
        [Tooltip("One-shot event played when lock-on acquisition becomes fully locked.")]
        public string LockOnFullEvent = "event:/Lock_On_Full";
        [Tooltip("One-shot event played when a Vesper missile starts targeting the drone.")]
        public string VesperMissileAlertEvent = "event:/Alert";

        [Header("July Mixer Routing")]
        [Tooltip("FMOD master bus used for pause-menu volume ducking.")]
        public string MasterBusPath = "bus:/";
        [Tooltip("FMOD group bus used by background music.")]
        public string MusicBusPath = "bus:/Music";
        [Tooltip("FMOD group bus reserved for gameplay and interface sound effects.")]
        public string SoundEffectsBusPath = "bus:/SFX";
        [Tooltip("FMOD group bus used by dialogue and voice-over events.")]
        public string DialogueBusPath;

        [Header("Default Volumes")]
        [Range(0f, 1f)] public float DefaultMusicVolume = 1f;
        [Range(0f, 1f)] public float DefaultSoundEffectsVolume = 1f;
        [Range(0f, 1f)] public float DefaultDialogueVolume;
        [Tooltip("Remember pause-menu volume choices between runs.")]
        public bool PersistVolumeSettings = true;

        [Header("Pause Audio Ducking")]
        [Tooltip("Master volume multiplier used while the game is paused.")]
        [Range(0f, 1f)] public float PausedVolumeMultiplier = 0.333333f;
        [Tooltip("Seconds used to fade between full and paused FMOD volume.")]
        [Min(0f)] public float PauseFadeDuration = 0.35f;

        [Header("Pause Menu Presentation")]
        public PauseMenuVisualTuning PauseMenu = new PauseMenuVisualTuning();

        public void EnsureInitialized()
        {
            PauseMenu ??= new PauseMenuVisualTuning();
        }
    }

    [System.Serializable]
    public sealed class StaminaBoostTuning
    {
        [Header("Stamina")]
        [Min(0.01f)] public float MaxStamina = 100f;
        [Min(0f)] public float DrainRate = 25f;
        [Tooltip("Delay before stamina begins regenerating after it has been fully exhausted. Partial stamina use regenerates immediately.")]
        [Min(0f)] public float RegenDelay = 0.8f;
        [Min(0f)] public float RegenRate = 30f;
        [Tooltip("Stamina restored per second after stamina has bottomed out.")]
        [Min(0f)] public float ExhaustedRegenRate = 15f;

        [Header("Speed Boost")]
        [Min(0f)] public float BoostAcceleration = 2.4f;
        [Min(0f)] public float BoostDeceleration = 3.2f;
        [Min(1f)] public float BoostSpeedMultiplier = 1.5f;
        [Tooltip("Absolute target-speed ceiling while boosting. Set to 0 for no additional ceiling.")]
        [Min(0f)] public float BoostMaximumSpeed = 150f;

        [Header("World-Following Meter")]
        [Tooltip("Screen-space offset from the drone whenever the stamina boost is inactive, including normal forward movement.")]
        public Vector2 MeterScreenOffset = new Vector2(62f, 4f);
        [Tooltip("Screen-space offset from the drone at full stamina boost. The meter follows the boost acceleration and deceleration blend between the two offsets.")]
        public Vector2 MeterMaximumSpeedScreenOffset = new Vector2(62f, 4f);
        [Tooltip("While sprint is held without movement input, moves the meter outward by this multiple of its normal sprint inward travel. Values above 1 place it slightly farther out than its non-sprinting position.")]
        [Min(0f)] public float StationarySprintOutwardCompensation = 1.1f;
        [Min(8f)] public float MeterRadius = 28f;
        [Min(1f)] public float MeterThickness = 5f;
        [Tooltip("Non-procedural texture drawn behind the live stamina fill.")]
        public Texture2D MeterBackgroundIcon;
        [Tooltip("Screen-space width and height of the stamina background texture.")]
        [Min(1f)] public float MeterBackgroundIconSize = 76f;
        [Tooltip("Screen-space correction used to align the background texture's ring center with the live fill.")]
        public Vector2 MeterBackgroundIconOffset = Vector2.zero;
        [Tooltip("Tessellation used to keep the continuous ring visually smooth; this does not create visible tick marks.")]
        [Range(32, 256)] public int MeterArcResolution = 128;
        [Range(90f, 360f)] public float MeterArcDegrees = 280f;
        public float MeterArcStartDegrees = 130f;
        [Min(0f)] public float ScreenEdgePadding = 38f;
        [Tooltip("How quickly the stamina bar follows the drone in screen space.")]
        [Min(0f)] public float MeterFollowSharpness = 16f;
        [Min(0f)] public float VisibilityFadeSpeed = 7f;
        [Min(0f)] public float FullIdleFadeDelay = 1.2f;
        [Range(0f, 1f)] public float FullIdleAlpha = 0.12f;
        [Min(0f)] public float RestoredFeedbackDuration = 0.9f;

        [Header("Restore Notification")]
        [Min(0.1f)] public float RestoreNotificationDuration = 1.4f;
        [Min(8)] public int RestoreNotificationFontSize = 28;
        [Min(0f)] public float RestoreNotificationTop = 218f;
        [Min(24f)] public float RestoreNotificationHeight = 48f;
        [ColorUsage(false)] public Color RestoreNotificationColor = new Color(1f, 0.82f, 0.12f, 1f);
        [Tooltip("{0} is replaced with the amount of stamina restored.")]
        public string RestoreNotificationFormat = "STAMINA RESTORED: {0}";

        [Header("Meter Feel")]
        [Tooltip("How quickly the drawn fill chases the true stamina value. Higher is snappier, lower is smoother.")]
        [Min(0f)] public float MeterFillSharpness = 16f;
        [Tooltip("How quickly the meter blends between its state colors instead of snapping between them.")]
        [Min(0f)] public float MeterColorBlendSharpness = 12f;
        [Tooltip("Draws a lagging segment behind the fill showing stamina that was just spent.")]
        public bool ChipTrailEnabled = true;
        [Tooltip("Seconds the spent-stamina segment holds in place before it drains away.")]
        [Min(0f)] public float ChipTrailDelay = 0.25f;
        [Tooltip("Fraction of the full meter the spent-stamina segment drains per second once it starts catching up.")]
        [Min(0f)] public float ChipTrailCatchUpRate = 0.85f;
        [ColorUsage(false)] public Color ChipTrailColor = new Color(1f, 0.38f, 0.16f, 0.55f);
        [Tooltip("Extra brightness pulsed into the meter while stamina is low or exhausted.")]
        [Range(0f, 1f)] public float LowPulseStrength = 0.35f;
        [Tooltip("Pulses per second while stamina is low or exhausted.")]
        [Min(0f)] public float LowPulseSpeed = 1.6f;
        [Tooltip("Strength of the flash blended into the meter when stamina is restored.")]
        [Range(0f, 1f)] public float RestoreFlashStrength = 0.65f;
        [ColorUsage(false)] public Color RestoreFlashColor = new Color(1f, 1f, 1f, 1f);

        [Range(0f, 1f)] public float LowStaminaThreshold = 0.25f;
        [ColorUsage(false)] public Color ReadyColor = new Color(0.35f, 1f, 0.72f, 1f);
        [ColorUsage(false)] public Color BoostingColor = new Color(0.2f, 0.95f, 1f, 1f);
        [ColorUsage(false)] public Color LowColor = new Color(1f, 0.7f, 0.12f, 1f);
        [ColorUsage(false)] public Color EmptyColor = new Color(1f, 0.16f, 0.08f, 1f);
        [ColorUsage(false)] public Color RegeneratingColor = new Color(0.38f, 0.72f, 1f, 1f);
        [ColorUsage(false)] public Color MeterBackgroundColor = new Color(0.015f, 0.035f, 0.05f, 0.72f);
    }

    [System.Serializable]
    public sealed class FlightSwooshTuning
    {
        public bool Enabled = true;

        [Header("Pool & Density")]
        [Range(8, 256)] public int MaximumStreakCount = 96;
        [Tooltip("Maximum streaks spawned per second at full intensity before the boost multiplier is applied.")]
        [Min(0f)] public float Density = 52f;
        [Min(0.01f)] public float DensityCurvePower = 0.8f;
        [Range(0f, 1f)] public float TimingVariation = 0.38f;

        [Header("Speed Response")]
        [Min(0f)] public float SpeedThreshold = 12f;
        [Min(0.01f)] public float MaximumIntensitySpeed = 38f;
        [Min(0f)] public float IntensitySharpness = 8f;
        [Min(0f)] public float BoostMultiplier = 1.35f;

        [Header("Streak Shape")]
        public Vector2 LengthRange = new Vector2(5.5f, 18f);
        public Vector2 WidthRange = new Vector2(0.045f, 0.14f);
        public Vector2 LifetimeRange = new Vector2(0.28f, 0.52f);
        public Vector2 SweepSpeedRange = new Vector2(38f, 96f);
        [Range(0f, 12f)] public float DirectionJitterDegrees = 3.2f;
        [Min(0f)] public float MovementAlignmentSharpness = 18f;

        [Header("Camera-Edge Spawn Area")]
        [Tooltip("Viewport-space radial band around screen center. Values near 0.5 place streaks at the outer view edges.")]
        public Vector2 SpawnRadiusRange = new Vector2(0.3f, 0.54f);
        [Tooltip("World-space distance in front of the player camera where streaks originate.")]
        public Vector2 SpawnDepthRange = new Vector2(7f, 20f);

        [Header("Appearance")]
        [ColorUsage(false, true)] public Color Color = new Color(0.3f, 2.4f, 4.5f, 1f);
        [Range(0f, 1f)] public float Opacity = 0.82f;
        [Range(0f, 1f)] public float BrightnessVariation = 0.18f;
        [Range(0.01f, 0.49f)] public float FadeInFraction = 0.1f;
        [Range(0.01f, 0.49f)] public float FadeOutFraction = 0.32f;
        [Range(0.01f, 0.49f)] public float EdgeSoftness = 0.22f;
        [Range(0.01f, 0.49f)] public float TipSoftness = 0.14f;
    }

    [System.Serializable]
    public sealed class BoostRingTrailTuning
    {
        public bool Enabled = true;

        [Header("Pool & Emission")]
        [Range(4, 128)] public int MaximumRingCount = 48;
        [Tooltip("World-space distance flown between successive trail rings.")]
        [Min(0.1f)] public float SpawnSpacing = 2.5f;
        [Tooltip("Optional distance behind the visible drone center where each ring is placed.")]
        [Min(0f)] public float SpawnBehindDistance = 0f;
        [Tooltip("Seconds each emitted ring remains visible in the world.")]
        [Min(0.05f)] public float Lifetime = 1.35f;

        [Header("Portal Shape")]
        [Tooltip("World-space outer portal radius used to derive the trail's innermost ring radius.")]
        [Min(0.25f)] public float Radius = 3.1f;
        [Tooltip("Multiplier applied to the portal line thicknesses.")]
        [Min(1f)] public float LineThicknessMultiplier = 1.15f;
        [Tooltip("Ring scale when it first appears.")]
        [Min(0.01f)] public float StartScale = 0.86f;
        [Tooltip("Ring scale immediately before it expires.")]
        [Min(0.01f)] public float EndScale = 1.12f;
        [Tooltip("Clockwise roll applied to each stationary ring over its lifetime, in degrees per second.")]
        public float RotationSpeed = 55f;

        [Header("Ordered RGB Color Wheel")]
        [Tooltip("Number of evenly spaced hues in the sequence. Each new ring advances by one hue.")]
        [Range(2, 64)] public int HueStepCount = 20;
        [Tooltip("Hue assigned to the first ring in each 20-color sequence.")]
        [Range(0f, 1f)] public float StartingHue = 0f;
        [Tooltip("HDR multiplier applied to the selected color-wheel hue.")]
        [Min(0f)] public float ColorIntensity = 3.2f;
        [Min(0f)] public float BloomIntensity = 1.8f;
        [Range(0f, 1f)] public float Opacity = 0.86f;

        [Header("Camera Distance Fade")]
        [Tooltip("Rings closer than this world-space distance to the camera are fully hidden.")]
        [Min(0f)] public float NearCameraHiddenDistance = 4f;
        [Tooltip("World-space distance from the camera where rings finish fading back to normal opacity.")]
        [Min(0f)] public float NearCameraFadeEndDistance = 8f;

        [Header("Camera Angle Transparency")]
        [Tooltip("Opacity multiplier when the camera looks directly down the trail rings. Reduces additive stacking without changing their angled appearance.")]
        [Range(0f, 1f)] public float HeadOnOpacityMultiplier = 0.28f;
        [Tooltip("View angle, in degrees from face-on, through which the head-on opacity multiplier is fully applied.")]
        [Range(0f, 89f)] public float HeadOnFadeStartAngle = 8f;
        [Tooltip("View angle, in degrees from face-on, at which the rings return to their normal opacity.")]
        [Range(1f, 90f)] public float HeadOnFadeEndAngle = 28f;
        [Range(0.01f, 0.49f)] public float FadeInFraction = 0.08f;
        [Range(0.01f, 0.99f)] public float FadeOutFraction = 0.62f;
    }

    public enum DuneVectorCameraAntiAliasingMode
    {
        None,
        TemporalAntiAliasing,
        SubpixelMorphologicalAntiAliasing,
    }

    public enum DuneVectorSmaaQuality
    {
        Low,
        Medium,
        High,
    }

    public enum DuneVectorMsaaSampleCount
    {
        Disabled = 1,
        TwoSamples = 2,
        FourSamples = 4,
        EightSamples = 8,
    }

    public enum DuneVectorTaaQuality
    {
        Low,
        Medium,
        High,
    }

    public enum DuneVectorTaaSharpenMode
    {
        LowQuality,
        PostSharpen,
        ContrastAdaptiveSharpening,
    }

    [System.Serializable]
    public sealed class DroneTuning
    {
        [Header("Ground Movement")]
        [Tooltip("Vertical distance between the grounded character root and the drone visual.")]
        [Min(0f)] public float GroundVisualHeight = 0.45f;
        [Min(0f)] public float MaxGroundSpeed = 18f;
        [Min(0f)] public float GroundMovementSharpness = 8.5f;
        [Min(0f)] public float GroundBrakingSharpness = 5.5f;
        [Min(0f)] public float GroundSteeringSharpness = 11f;
        [Min(0f)] public float TrailMinimumSpeed = 0.35f;
        [Tooltip("Turns the drone's speed trails on or off. Disable to fly without any trail ribbons.")]
        public bool TrailsEnabled = true;
        [Tooltip("Tallest ledge the drone can walk up without flying. Ramps are handled by the slope limit, not this.")]
        [Min(0f)] public float MaxStepHeight = 0.25f;

        [Header("Jump")]
        [Min(0f)] public float JumpSpeed = 13f;

        [Header("Flight Start Effect")]
        [Tooltip("Effect spawned on the ground beneath the drone when flight begins.")]
        public GameObject FlightStartEffectPrefab;
        [Tooltip("Euler rotation offset applied after local +Z is aligned with the back of the drone.")]
        public Vector3 FlightStartEffectEulerAngles;
        [Tooltip("Vertical offset from the grounded surface for the spawned flight-start effect.")]
        public float FlightStartEffectGroundOffset;
        [Tooltip("Scale multiplier applied to the spawned flight-start effect's authored scale.")]
        public Vector3 FlightStartEffectScale = Vector3.one;
        [Tooltip("Seconds before the spawned flight-start effect root is destroyed.")]
        [Min(0f)] public float FlightStartEffectLifetime = 3f;

        [Header("Hub Return Effect")]
        [Tooltip("Effect spawned on the hub floor whenever the drone returns to the hub.")]
        public GameObject HubReturnEffectPrefab;
        [Tooltip("World-space offset from the hub floor position where the return effect is spawned.")]
        public Vector3 HubReturnEffectFloorOffset;
        [Tooltip("Euler rotation offset added to the effect prefab's authored rotation.")]
        public Vector3 HubReturnEffectEulerOffset;
        [Tooltip("Scale multiplier applied to the hub return effect's authored scale.")]
        public Vector3 HubReturnEffectScale = Vector3.one;
        [Tooltip("Seconds before the hub return effect root is destroyed. Set to 0 to let the prefab manage its own lifetime.")]
        [Min(0f)] public float HubReturnEffectLifetime = 3f;

        [Header("Boost Rings")]
        [Min(0f)] public float RingBoostAcceleration = 9.5f;
        [Min(0f)] public float BoostDuration = 2.6f;
        [Min(0f)] public float BoostMaxSpeed = 39f;

        [Header("Shift Stamina Boost")]
        public StaminaBoostTuning StaminaBoost = new StaminaBoostTuning();

        [Header("Ring Entry Burst")]
        [Min(1f)] public float RingBurstSpeedMultiplier = 1.45f;
        [Min(0.05f)] public float RingBurstDuration = 0.7f;
        [Min(0f)] public float RingBurstAcceleration = 28f;

        [Header("Flight")]
        [Min(0f)] public float FlightSpeed = 27f;
        [Min(0f)] public float MaximumFlightSpeed = 38f;
        [Min(0f)] public float FlightAcceleration = 3.8f;
        [Tooltip("Target flight speed while Space is held as an air brake.")]
        [Min(0f)] public float FlightBrakeSpeed = 12f;
        [Tooltip("How quickly held Space pulls flight velocity toward the brake speed.")]
        [Min(0f)] public float FlightBrakeSharpness = 9f;
        [Min(0f)] public float FlightSteeringSharpness = 10f;
        [Min(0f)] public float FlightLevelingSharpness = 5f;
        [Min(0f)] public float FlightYawRate = 125f;
        [Tooltip("Maximum capacity of the flight meter in seconds.")]
        [Min(0.1f)] public float FlightDuration = 60f;
        [Tooltip("Seconds restored to the flight meter by each flight ring pass.")]
        [Min(0f)] public float FlightRingRechargeSeconds = 7f;
        [Tooltip("Debug option that prevents the flight meter from depleting while flying.")]
        public bool DebugInfiniteFlight;
        [Min(0f)] public float FlightEntryLiftDuration = 0.75f;
        [Min(0f)] public float FlightEntryLiftSpeed = 16f;

        [Header("Flight Landing Visual")]
        [Tooltip("Ground clearance where the drone begins easing its preserved flight pitch and roll back to rest.")]
        [Min(0f)] public float FlightLandingVisualBlendStartClearance = 6f;
        [Tooltip("Ground clearance where the drone has fully reached its resting pitch and roll.")]
        [Min(0f)] public float FlightLandingVisualBlendCompleteClearance = 0.75f;

        [Header("Camera")]
        [Min(0f)] public float CameraLookSensitivity = 0.085f;
        [Min(0f)] public float CameraRotationSharpness = 30f;
        [Min(0f)] public float CameraFollowSharpness = 4.2f;
        [Min(0.001f)] public float CameraNearClipPlane = 0.01f;
        [Min(0.01f)] public float CameraFarClipPlane = 10000f;

        [Header("Camera Anti-Aliasing (URP)")]
        public DuneVectorCameraAntiAliasingMode CameraAntiAliasingMode =
            DuneVectorCameraAntiAliasingMode.SubpixelMorphologicalAntiAliasing;
        public DuneVectorSmaaQuality SmaaQuality = DuneVectorSmaaQuality.High;
        [Tooltip("Multisample coverage used with SMAA so transparent geometry edges, including distant portal rings, receive hardware anti-aliasing.")]
        public DuneVectorMsaaSampleCount CameraMsaaSampleCount = DuneVectorMsaaSampleCount.FourSamples;

        [Header("Camera Temporal Anti-Aliasing Tuning")]
        public DuneVectorTaaQuality TemporalAntiAliasingQuality = DuneVectorTaaQuality.High;
        public DuneVectorTaaSharpenMode TemporalSharpenMode = DuneVectorTaaSharpenMode.PostSharpen;
        [Range(0f, 2f)] public float TemporalSharpenStrength = 0.65f;
        [Range(0f, 1f)] public float TemporalRingingReduction = 0.35f;
        [Range(0f, 1f)] public float TemporalHistorySharpening = 0.25f;
        [Range(0f, 1f)] public float TemporalAntiFlicker = 0.4f;
        [Range(0f, 1f)] public float TemporalMotionVectorRejection = 0.35f;
        public bool TemporalAntiHistoryRinging = true;
        [Range(0.6f, 0.95f)] public float TemporalBaseBlendFactor = 0.8f;
        [Range(0.1f, 1f)] public float TemporalJitterScale = 0.9f;

        public void EnsureInitialized()
        {
            StaminaBoost ??= new StaminaBoostTuning();
        }

        public void ApplyTo(DroneCharacterController drone)
        {
            drone.MaxGroundSpeed = MaxGroundSpeed;
            drone.GroundMovementSharpness = GroundMovementSharpness;
            drone.GroundBrakingSharpness = GroundBrakingSharpness;
            drone.RotationSharpness = GroundSteeringSharpness;
            drone.TrailMinimumSpeed = TrailMinimumSpeed;
            drone.TrailsEnabled = TrailsEnabled;
            drone.JumpSpeed = JumpSpeed;
            drone.ConfigureFlightStartEffect(
                FlightStartEffectPrefab,
                FlightStartEffectEulerAngles,
                FlightStartEffectGroundOffset,
                FlightStartEffectScale,
                FlightStartEffectLifetime);
            drone.ConfigureHubReturnEffect(
                HubReturnEffectPrefab,
                HubReturnEffectFloorOffset,
                HubReturnEffectEulerOffset,
                HubReturnEffectScale,
                HubReturnEffectLifetime);
            drone.RingBoostAcceleration = RingBoostAcceleration;
            drone.RingBoostDuration = BoostDuration;
            drone.RingBoostMaxSpeed = BoostMaxSpeed;
            drone.RingBurstSpeedMultiplier = RingBurstSpeedMultiplier;
            drone.RingBurstDuration = RingBurstDuration;
            drone.RingBurstAcceleration = RingBurstAcceleration;
            drone.FlightSpeed = FlightSpeed;
            drone.MaximumFlightSpeed = MaximumFlightSpeed;
            drone.FlightAcceleration = FlightAcceleration;
            drone.FlightBrakeSpeed = FlightBrakeSpeed;
            drone.FlightBrakeSharpness = FlightBrakeSharpness;
            drone.FlightSteeringSharpness = FlightSteeringSharpness;
            drone.FlightLevelingSharpness = FlightLevelingSharpness;
            drone.FlightYawRate = FlightYawRate;
            drone.ConfigureFlightMeter(FlightDuration, FlightRingRechargeSeconds, DebugInfiniteFlight);
            drone.FlightEntryLiftDuration = FlightEntryLiftDuration;
            drone.FlightEntryLiftSpeed = FlightEntryLiftSpeed;
            drone.ConfigureFlightLandingVisual(
                FlightLandingVisualBlendStartClearance,
                FlightLandingVisualBlendCompleteClearance);
        }

        public void ApplyTo(DroneCameraController camera)
        {
            camera.LookSensitivity = CameraLookSensitivity;
            camera.RotationSharpness = CameraRotationSharpness;
            camera.FollowingSharpness = CameraFollowSharpness;
            camera.Camera.nearClipPlane = CameraNearClipPlane;
            camera.Camera.farClipPlane = Mathf.Max(CameraNearClipPlane, CameraFarClipPlane);
        }
    }

    [System.Serializable]
    public sealed class CompassHudTuning
    {
        public bool Enabled = true;
        [Min(240f)] public float ReferenceHeight = 1080f;
        [Range(0.25f, 2f)] public float MinimumScale = 0.65f;
        [Range(0.25f, 2f)] public float MaximumScale = 1.25f;
        [Min(160f)] public float Width = 720f;
        [Min(24f)] public float Height = 72f;
        [Min(0f)] public float TopMargin = 18f;
        [Range(30f, 240f)] public float VisibleDegrees = 120f;
        [Range(1f, 45f)] public float TickStepDegrees = 5f;
        [Min(1)] public int MajorTickEvery = 3;
        public bool ShowIntercardinalLabels = true;
        [Min(1f)] public float MinorTickHeight = 9f;
        [Min(1f)] public float MajorTickHeight = 18f;
        [Min(1f)] public float CardinalTickHeight = 27f;
        [Min(1f)] public float TickWidth = 2f;
        [Min(0f)] public float TickBottomMargin = 8f;
        [Min(1f)] public float BaselineHeight = 1f;
        [Min(8)] public int LabelFontSize = 15;
        [Min(12f)] public float LabelWidth = 64f;
        [Min(12f)] public float LabelHeight = 24f;
        [Min(1f)] public float CenterMarkerWidth = 3f;
        [Min(1f)] public float CenterMarkerHeight = 34f;
        public string NorthLabel = "N";
        public string EastLabel = "E";
        public string SouthLabel = "S";
        public string WestLabel = "W";
        public string NorthEastLabel = "NE";
        public string SouthEastLabel = "SE";
        public string SouthWestLabel = "SW";
        public string NorthWestLabel = "NW";
        public Vector2 ShadowOffset = new Vector2(1.5f, 2f);
        [ColorUsage(false)] public Color PanelColor = new Color(0.015f, 0.045f, 0.07f, 0.48f);
        [ColorUsage(false)] public Color TickColor = new Color(0.72f, 0.87f, 0.92f, 0.88f);
        [ColorUsage(false)] public Color CardinalColor = new Color(0.95f, 0.99f, 1f, 1f);
        [ColorUsage(false)] public Color CenterMarkerColor = new Color(0f, 0.86f, 1f, 1f);
        [ColorUsage(false)] public Color ShadowColor = new Color(0f, 0f, 0f, 0.7f);
    }

    [System.Serializable]
    public sealed class LaunchHudTuning
    {
        [Min(0f)] public float GapBelowCompass = 18f;
    }

    [System.Serializable]
    public sealed class BottomHudTuning
    {
        [Header("Responsive Layout")]
        [Min(320f)] public float ReferenceWidth = 1600f;
        [Min(240f)] public float ReferenceHeight = 900f;
        [Range(0.25f, 2f)] public float MinimumScale = 0.58f;
        [Range(0.25f, 2f)] public float MaximumScale = 1.15f;
        [Min(0f)] public float SideMargin = 30f;
        [Min(0f)] public float BottomMargin = 32f;
        [Min(0f)] public float MinimumPanelGap = 18f;
        [Min(48f)] public float PanelHeight = 72f;
        [Min(140f)] public float SpeedPanelWidth = 360f;
        [Min(140f)] public float FlightPanelWidth = 400f;
        [Min(140f)] public float HealthPanelWidth = 300f;
        public Vector2 ShadowOffset = new Vector2(5f, 6f);

        [Header("Panel Structure")]
        [Min(0f)] public float ContentPadding = 14f;
        [Min(1f)] public float BorderThickness = 1f;
        [Min(1f)] public float AccentWidth = 4f;
        [Min(1f)] public float TopRuleHeight = 2f;
        [Range(0f, 1f)] public float TopRuleOpacity = 0.55f;
        [Min(2f)] public float MeterHeight = 10f;
        [Min(0f)] public float MeterBottomPadding = 12f;
        [Min(0f)] public float MeterInset = 2f;
        [Range(0f, 1f)] public float MeterHighlightFraction = 0.5f;
        [Range(0, 12)] public int MeterDivisionCount = 5;
        [Min(0.5f)] public float MeterDivisionWidth = 1f;

        [Header("Typography")]
        [Min(8)] public int LabelFontSize = 12;
        [Min(8)] public int ValueFontSize = 17;
        [Min(10f)] public float TextRowHeight = 28f;
        [Min(0f)] public float TextTopPadding = 6f;
        [Range(0.25f, 0.8f)] public float LabelWidthFraction = 0.58f;
        public string GroundSpeedLabel = "GROUND VELOCITY";
        public string FlightSpeedLabel = "FLIGHT VELOCITY";
        public string FlightTimeLabel = "FLIGHT RESERVE";
        public string HealthLabel = "DRONE INTEGRITY";
        public string SpeedUnit = "m/s";
        public string FlightTimeUnit = "sec";

        [Header("Shared Palette")]
        [ColorUsage(false)] public Color ShadowColor = new Color(0f, 0f, 0f, 0.42f);
        [ColorUsage(false)] public Color PanelColor = new Color(0.012f, 0.032f, 0.05f, 0.62f);
        [ColorUsage(false)] public Color BorderColor = new Color(0.14f, 0.32f, 0.4f, 0.78f);
        [ColorUsage(false)] public Color TrackColor = new Color(0.025f, 0.075f, 0.095f, 1f);
        [ColorUsage(false)] public Color MeterDivisionColor = new Color(0.42f, 0.65f, 0.7f, 0.24f);
        [ColorUsage(false)] public Color MeterHighlightColor = new Color(1f, 1f, 1f, 0.2f);
        [ColorUsage(false)] public Color LabelColor = new Color(0.5f, 0.72f, 0.78f, 1f);
        [ColorUsage(false)] public Color ValueColor = new Color(0.92f, 0.98f, 1f, 1f);

        [Header("Meter Colors")]
        [ColorUsage(false)] public Color GroundSpeedColor = new Color(1f, 0.63f, 0.16f, 1f);
        [ColorUsage(false)] public Color FlightSpeedColor = new Color(0.08f, 0.82f, 1f, 1f);
        [ColorUsage(false)] public Color FlightReserveFullColor = new Color(0.08f, 0.82f, 1f, 1f);
        [ColorUsage(false)] public Color FlightReserveLowColor = new Color(1f, 0.24f, 0.12f, 1f);
        [ColorUsage(false)] public Color HealthFullColor = new Color(0.16f, 0.95f, 0.7f, 1f);
        [ColorUsage(false)] public Color HealthLowColor = new Color(1f, 0.2f, 0.1f, 1f);
    }

    [System.Serializable]
    public sealed class MapHudTuning
    {
        [Header("Controls")]
        public bool Enabled = true;
        public Key WorldMapKey = Key.M;
        public bool PauseGameWhenWorldMapOpen = true;

        [Header("Drone Scan")]
        [Min(1f)] public float DroneRevealRadius = 240f;
        [Tooltip("World-space width of one persistent explored-map cell. Smaller cells create a finer tunnel edge but consume more memory.")]
        [Min(1f)] public float ExplorationCellSize = 4f;
        [Tooltip("Distance traveled before the drone paints another reveal-radius stamp into persistent exploration.")]
        [Min(0.1f)] public float ExplorationUpdateMovement = 4f;
        [Tooltip("Persistent exploration filename. Map memory is always stored as a .dat file.")]
        public string ExplorationFileName = "DuneVectorMapExploration.dat";
        [Tooltip("Seconds between background journal appends when new map cells have been discovered.")]
        [Min(1f)] public float ExplorationSaveInterval = 10f;
        [Tooltip("Exploration cells reserved in the append journal during initialization so flight-time reveals do not resize the buffer.")]
        [Min(0)] public int ExplorationJournalBufferCapacity = 32768;
        [Range(32, 512)] public int ScanTextureResolution = 256;
        [Tooltip("Full world-map terrain resolution. Refinement happens only after navigation settles.")]
        [Range(256, 1024)] public int WorldMapScanTextureResolution = 512;
        [Tooltip("High-resolution world-map rows sampled per frame when the tiled terrain renderer is unavailable.")]
        [Range(1, 32)] public int WorldMapScanRowsPerFrame = 4;
        [Tooltip("Seconds between procedural terrain resamples. Cached map pixels still scroll every rendered frame.")]
        [Min(0.02f)] public float ScanRefreshInterval = 0.3f;
        [Tooltip("Distance traveled before the cached terrain scan is regenerated. Cached pixels translate smoothly below this threshold.")]
        [Min(0f)] public float ScanRefreshMovement = 8f;
        [Min(0.01f)] public float RadiusLineThickness = 5f;
        [Min(0.01f)] public float ContourSpacing = 4f;
        [Min(0f)] public float ContourThickness = 0.35f;
        [Range(0f, 1f)] public float ContourStrength = 0.32f;
        [Min(0.01f)] public float HeightContrast = 1.15f;
        [Tooltip("Terrain height mapped to the low map color.")]
        public float TerrainHeightMinimum = -20f;
        [Tooltip("Terrain height mapped to the high map color.")]
        public float TerrainHeightMaximum = 45f;

        [Header("World Map Layout")]
        [Tooltip("Expand the world-map panel to the safe area while preserving the configured screen padding. Disable to use the legacy maximum size and fixed aspect ratio.")]
        public bool WorldMapExpandToAvailableScreen = true;
        [Min(160f)] public float WorldMapMaximumSize = 780f;
        [Min(0f)] public float WorldMapScreenPadding = 42f;
        [Min(1f)] public float WorldMapWorldSize = 3200f;
        [Tooltip("Closest vertical world span reachable with the world-map mouse wheel.")]
        [Min(1f)] public float WorldMapMinimumWorldSize = 500f;
        [Tooltip("Farthest vertical world span reachable with the world-map mouse wheel.")]
        [Min(1f)] public float WorldMapMaximumWorldSize = 16000f;
        [Tooltip("Exponential mouse-wheel zoom response. Higher values zoom farther per wheel step.")]
        [Range(0.01f, 0.25f)] public float WorldMapZoomScrollSensitivity = 0.08f;
        [Tooltip("Mouse button used to drag the world map: 0 left, 1 right, 2 middle.")]
        [Range(0, 2)] public int WorldMapPanMouseButton = 0;
        [Tooltip("Cursor movement in screen pixels required before a map click becomes a pan.")]
        [Min(0f)] public float WorldMapPanDragThreshold = 4f;
        [Tooltip("Seconds without pan or zoom input before high-resolution terrain refinement resumes.")]
        [Range(0f, 1f)] public float WorldMapNavigationRefineDelay = 0.2f;
        [Tooltip("Build a broad explored-terrain atlas in the background during startup so panning does not wait for viewport scans.")]
        public bool PrebuildWorldMapAtlasOnLoad = true;
        [Tooltip("Resolution of the startup world atlas. The atlas uses Color32 storage and is generated off the main thread.")]
        [Range(512, 4096)] public int WorldMapAtlasTextureResolution = 2048;
        [Tooltip("Additional world-space coverage around all explored cells when sizing the startup atlas.")]
        [Min(0f)] public float WorldMapAtlasExplorationMargin = 800f;

        [Header("World Map Tiled Terrain")]
        [Tooltip("Use persistent multiresolution height tiles and shader-rendered contours instead of rebuilding the world-map viewport on the CPU.")]
        public bool WorldMapTiledTerrainEnabled = true;
        [Tooltip("Persistent height-tile cache filename. Terrain cache data is always stored as a .dat file.")]
        public string WorldMapTerrainCacheFileName = "DuneVectorWorldMapTerrainCache.dat";
        [Tooltip("Height samples along each side of a terrain tile. Higher values use more memory and take longer to generate the first time.")]
        [Range(64, 512)] public int WorldMapTerrainTileResolution = 256;
        [Tooltip("World-space size of a level-zero tile. Higher zoom levels automatically combine this area by powers of two.")]
        [Min(32f)] public float WorldMapTerrainBaseTileWorldSize = 256f;
        [Tooltip("Maximum multiresolution level available when the map is zoomed far out.")]
        [Range(0, 10)] public int WorldMapTerrainMaximumLod = 7;
        [Tooltip("Terrain samples targeted per on-screen pixel. One preserves native detail without oversampling.")]
        [Range(0.25f, 2f)] public float WorldMapTerrainTexelsPerScreenPixel = 1f;
        [Tooltip("Worker threads allowed to generate or load terrain tiles simultaneously.")]
        [Range(1, 4)] public int WorldMapTerrainConcurrentBuilds = 2;
        [Tooltip("Visible cached tiles whose exploration masks may be restyled per frame after new terrain is revealed.")]
        [Range(1, 8)] public int WorldMapTerrainStyleRefreshesPerFrame = 2;
        [Tooltip("Baseline number of styled terrain tiles retained in GPU memory. The cache automatically grows when the active viewport and its fallback tiles require more. Evicted tiles remain in the .dat cache.")]
        [Range(4, 128)] public int WorldMapTerrainMemoryTileLimit = 32;
        [Tooltip("Reference world-map viewport height used to prefetch the opening view before the map is shown.")]
        [Min(128f)] public float WorldMapTerrainPrefetchViewportPixels = 680f;
        [Tooltip("How much coarser the first prefetched coverage is than the final opening view. Coarse parent tiles prevent black gaps while detailed children stream.")]
        [Range(1f, 8f)] public float WorldMapTerrainPrefetchCoarseFactor = 4f;
        [Tooltip("Screen-space antialiasing width applied to shader-rendered contour lines.")]
        [Range(0.25f, 3f)] public float WorldMapContourAntialiasPixels = 1f;
        [Tooltip("Interpolation width at explored/unexplored boundaries. Small values preserve the carved edge while removing cell stair-stepping.")]
        [Range(0.001f, 0.49f)] public float WorldMapExplorationEdgeSoftness = 0.08f;

        [Range(1f, 2f)] public float WorldMapPanelAspectRatio = 1.42f;
        [Min(20f)] public float WorldMapHeaderHeight = 58f;
        [Min(20f)] public float WorldMapFooterHeight = 44f;
        [Range(0f, 1f)] public float OverlayOpacity = 0.96f;
        [Tooltip("Fullscreen artwork shown behind the world-map panel.")]
        public Texture2D WorldMapBackdropImage;
        [Tooltip("Color tint applied to the world-map backdrop artwork.")]
        [ColorUsage(false)] public Color WorldMapBackdropTint = Color.white;
        [Tooltip("Opacity of the world-map backdrop artwork over the fallback overlay color.")]
        [Range(0f, 1f)] public float WorldMapBackdropOpacity = 1f;

        [Header("Panel")]
        [Min(0f)] public float ContentPadding = 12f;
        [Min(1f)] public float BorderThickness = 1f;
        [Min(8)] public int WorldMapTitleFontSize = 20;
        [Min(8)] public int WorldMapFooterFontSize = 18;
        [Min(8)] public int DetailFontSize = 12;
        [Min(8)] public int DroneMarkerFontSize = 24;
        [Min(12f)] public float DroneMarkerBoxSize = 42f;
        [Range(0.25f, 0.75f)] public float DetailSplitFraction = 0.58f;
        public string WorldMapTitle = "WORLD MAP";
        public string NorthLabel = "↑ N";
        public string DroneGlyph = "▲";
        public string WorldMapHint = "LMB PAN  •  WHEEL ZOOM  •  M CLOSE";
        public string CoordinateFormat = "X: {0:0}   Y: {1:0}   Z: {2:0}   •   SCAN RADIUS {3:0} m";

        [Header("Map Icons")]
        public bool ShowLandmarks = true;
        public bool ShowGeoglyphs = true;
        [Tooltip("Hide points of interest until the drone has explored the cell containing them.")]
        public bool OnlyShowExploredIcons = true;
        [Tooltip("Seconds between lightweight point-of-interest cache refreshes.")]
        [Min(0.1f)] public float IconRefreshInterval = 1f;
        [Min(8)] public int LandmarkIconFontSize = 20;
        [Min(12f)] public float IconBoxSize = 34f;
        [Tooltip("Smallest landmark-icon multiplier reached while zooming out.")]
        [Range(0.25f, 1f)] public float LandmarkIconMinimumZoomScale = 0.7f;
        [Tooltip("Largest landmark-icon multiplier reached while zooming in.")]
        [Range(1f, 3f)] public float LandmarkIconMaximumZoomScale = 1.85f;
        [Tooltip("How strongly landmark icon size responds to world-map zoom.")]
        [Range(0.1f, 2f)] public float LandmarkIconZoomScaleExponent = 0.45f;
        public Vector2 IconShadowOffset = new Vector2(1f, 2f);
        [Header("Landmark Symbols")]
        public string RelayStationIcon = "⌁";
        public string CrashedCarrierIcon = "✈";
        public string RaiderBeaconIcon = "⚑";
        public string AncientSpireIcon = "▲";
        public string ExcavationSiteIcon = "⛏";
        public string OrbitalArrayIcon = "◒";
        public string DesertMegagateIcon = "Π";
        public string WindHarvesterIcon = "✣";
        public string BuriedArcologyIcon = "⬢";
        public string SandRingIcon = "◎";

        [Header("Landmark Icon Images")]
        [Tooltip("Multicolor map icon. The matching landmark symbol is used as a fallback when no texture is assigned.")]
        public Texture2D RelayStationIconImage;
        public Texture2D CrashedCarrierIconImage;
        public Texture2D RaiderBeaconIconImage;
        public Texture2D AncientSpireIconImage;
        public Texture2D ExcavationSiteIconImage;
        public Texture2D OrbitalArrayIconImage;
        public Texture2D DesertMegagateIconImage;
        public Texture2D WindHarvesterIconImage;
        public Texture2D BuriedArcologyIconImage;
        public Texture2D SandRingIconImage;

        [Header("Icon Colors")]
        [ColorUsage(false)] public Color LandmarkIconColor = new Color(1f, 0.67f, 0.18f, 1f);
        [ColorUsage(false)] public Color GeoglyphMapColor = Color.white;
        [Range(0f, 1f)] public float GeoglyphMapOpacity = 1f;
        [Tooltip("Dark backing stroke drawn around map geoglyph linework so it remains readable over bright terrain contours.")]
        [ColorUsage(false)] public Color GeoglyphMapHaloColor = new Color(0.01f, 0.008f, 0.005f, 0.9f);
        [Tooltip("Width of the geoglyph backing stroke in cached map-texture pixels.")]
        [Range(0f, 8f)] public float GeoglyphMapHaloWidthPixels = 4f;
        [Tooltip("Maximum width or height of each cached world-map geoglyph texture.")]
        [Range(64, 512)] public int GeoglyphMapTextureResolution = 512;
        [Tooltip("Number of geoglyph map textures converted from source masks per frame.")]
        [Range(1, 4)] public int GeoglyphTextureBuildsPerFrame = 1;
        [ColorUsage(false)] public Color IconShadowColor = new Color(0f, 0f, 0f, 0.85f);

        [Header("World Map Chrome")]
        [Tooltip("Darkened frame drawn around the fullscreen backdrop so the map panel reads as a lit surface.")]
        [Range(0f, 1f)] public float WorldMapBackdropVignette = 0.75f;
        [Tooltip("Shadow softness under the world-map panel.")]
        [Min(0f)] public float WorldMapPanelShadowSpread = 26f;
        [Tooltip("Inner shadow drawn along the map viewport edges so the terrain sits inside the frame.")]
        [Min(0f)] public float WorldMapViewportVignette = 54f;
        [Tooltip("Draw a world-aligned coordinate graticule beneath the map icons.")]
        public bool ShowWorldMapGraticule = true;
        [Tooltip("Approximate number of minor graticule divisions across the map viewport height.")]
        [Range(2, 16)] public int WorldMapGraticuleDivisions = 6;
        [Tooltip("Draw a zoom-aware distance scale bar in the map viewport.")]
        public bool ShowWorldMapScaleBar = true;

        [Header("Palette")]
        [ColorUsage(false)] public Color UnexploredColor = Color.black;
        [ColorUsage(false)] public Color OverlayColor = new Color(0f, 0f, 0f, 1f);
        [ColorUsage(false)] public Color PanelColor = new Color(0.005f, 0.012f, 0.016f, 1f);
        [ColorUsage(false)] public Color WorldMapChromeColor = new Color(0.01f, 0.035f, 0.05f, 1f);
        [ColorUsage(false)] public Color BorderColor = new Color(1f, 1f, 1f, 0.9f);
        [ColorUsage(false)] public Color TerrainLowColor = new Color(0.18f, 0.11f, 0.045f, 1f);
        [ColorUsage(false)] public Color TerrainHighColor = new Color(0.92f, 0.62f, 0.2f, 1f);
        [ColorUsage(false)] public Color ContourColor = new Color(1f, 0.82f, 0.42f, 1f);
        [ColorUsage(false)] public Color RadiusLineColor = new Color(0f, 0.9f, 1f, 1f);
        [ColorUsage(false)] public Color DroneMarkerColor = new Color(0.92f, 1f, 1f, 1f);
        [ColorUsage(false)] public Color TitleColor = Color.white;
        [ColorUsage(false)] public Color DetailColor = Color.white;
        [Tooltip("Signal color shared by the world-map chrome: corner brackets, header rule and title tick.")]
        [ColorUsage(false)] public Color WorldMapAccentColor = new Color(0.35f, 0.86f, 1f, 1f);
        [Tooltip("Coordinate graticule drawn over unexplored space.")]
        [ColorUsage(false)] public Color WorldMapGraticuleColor = new Color(0.34f, 0.68f, 0.82f, 0.13f);
    }

    [System.Serializable]
    public sealed class RetroCrtScanlineTuning
    {
        [Tooltip("Enables the fullscreen scanline treatment derived from URP_RetroCRTShader-master.")]
        public bool Enabled = true;

        [Min(1f)]
        [Tooltip("Vertical scanline density. This matches the source Retro shader's Scanline Height control.")]
        public float ScanlineHeight = 2.5f;

        [Range(0f, 1f)]
        [Tooltip("Amount by which every other scan band darkens the rendered scene.")]
        public float ScanlineStrength = 0.3f;

        [Tooltip("Fullscreen material consumed by the Dune Vector URP renderer feature.")]
        public Material Material;
    }

    [CreateAssetMenu(fileName = "Dune Vector Runtime Settings", menuName = "Dune Vector/Runtime Settings", order = 0)]
    public sealed class DuneVectorRuntimeSettings : ScriptableObject
    {
        [Header("Runtime Camera Rendering")]
        [Tooltip("Data-driven SRP lens flare assigned to the dynamically created gameplay camera.")]
        public LensFlareDataSRP RuntimeCameraLensFlare;

        [Tooltip("Fullscreen scanline presentation based on URP_RetroCRTShader-master.")]
        public RetroCrtScanlineTuning RetroCrtScanlines = new RetroCrtScanlineTuning();

        [Tooltip("Movement, flight, boost, and camera controls for the drone.")]
        public DroneTuning PlayerTuning = new DroneTuning();

        [Tooltip("Top-center heading ribbon driven by the gameplay camera's yaw.")]
        public CompassHudTuning CompassHud = new CompassHudTuning();

        [Tooltip("Opening control reminder panel displayed beneath the compass.")]
        public LaunchHudTuning LaunchHud = new LaunchHudTuning();

        [Tooltip("Shared responsive layout, typography, and palette for the speed, flight reserve, and health panels.")]
        public BottomHudTuning BottomHud = new BottomHudTuning();

        [Tooltip("World map, terrain scan radius, controls, layout, and presentation.")]
        public MapHudTuning MapHud = new MapHudTuning();

        [Tooltip("Shared player, rival, and neutral drone model, materials, rotor animation, and trails.")]
        public DroneVisualTuning DroneVisuals = new DroneVisualTuning();

        [Tooltip("Local camera-edge anime motion streaks driven by the player drone's real flight velocity.")]
        public FlightSwooshTuning FlightSwooshes = new FlightSwooshTuning();

        [Tooltip("World-space rune-free portal rings emitted in ordered RGB hues while stamina boosting in flight.")]
        public BoostRingTrailTuning BoostRingTrail = new BoostRingTrailTuning();

        [Tooltip("World-space wind regions, authoritative forces, placement, falloff, and streamline presentation.")]
        public WindFieldSystemTuning WindFields = new WindFieldSystemTuning();

        [Tooltip("Procedural dust-devil spawning, traversal forces, fragile-cargo damage, and distant column presentation.")]
        public DustDevilTuning DustDevils = new DustDevilTuning();

        [Tooltip("Authored stylized cloud archetypes, placement, shading, and motion.")]
        public CloudTuning Clouds = new CloudTuning();

        [Tooltip("Dynamic clear-weather dust, sandstorm timing, wind, URP atmosphere, and particle layers.")]
        public DesertWeatherTuning Weather = new DesertWeatherTuning();

        [Tooltip("Electrical sandstorm strikes, regional heat, temperature, cooling, and gameplay consequences.")]
        public EnvironmentalHazardTuning EnvironmentalHazards = new EnvironmentalHazardTuning();

        [Tooltip("FMOD background music, July mixer bus routing, and pause-menu volume defaults.")]
        public AudioTuning Audio = new AudioTuning();

        [Tooltip("FFT-driven pressure fronts, melodic currents, percussive filaments, and global bloom response.")]
        public MusicReactiveSkyTuning MusicReactiveSky = new MusicReactiveSkyTuning();

        [Tooltip("Pickup, package, and drop-off job generation.")]
        public DeliveryTuning Deliveries = new DeliveryTuning();

        [Tooltip("Courier contract generation, modifiers, rewards, cargo rules, and HUD.")]
        public CourierContractTuning Contracts = new CourierContractTuning();

        [Tooltip("Authored post-delivery narrative sequence, typewriter timing, and FMOD typing loop.")]
        public DeliveryMessageTuning DeliveryMessages = new DeliveryMessageTuning();

        [Tooltip("World hub geometry, terminal interaction, and teleport presentation.")]
        public WorldHubTuning WorldHub = new WorldHubTuning();

        [Tooltip("Authored procedural landmark placement and archetype dimensions.")]
        public LandmarkSystemTuning Landmarks = new LandmarkSystemTuning();

        [Tooltip("Coarse streamed procedural building placement, density, dune grounding, and collision.")]
        public ProceduralBuildingSystemTuning Buildings = new ProceduralBuildingSystemTuning();

        [Tooltip("Unique mask-authored ground artworks placed in persistent logical world coordinates.")]
        public GeoglyphSystemTuning Geoglyphs = new GeoglyphSystemTuning();

        [Tooltip("Persistent free-roam signal discoveries, scanner presentation, rewards, and Atlas terminal.")]
        public DesertAtlasTuning DesertAtlas = new DesertAtlasTuning();

        [Tooltip("In-game camera, generic subject detection, persistent photographs, Gallery, and glyph documentation.")]
        public PhotographyTuning Photography = new PhotographyTuning();

        [Tooltip("Route-aware open-world enemy formation choreography.")]
        public RouteEncounterTuning RouteEncounters = new RouteEncounterTuning();

        [Tooltip("Ambient rival couriers, rescues, races, moving convoys, rewards, and faction presentation.")]
        public DynamicCourierTuning DynamicCouriers = new DynamicCourierTuning();

        [Tooltip("Procedural pyramid density and size range.")]
        public PyramidTuning Pyramids = new PyramidTuning();

        [Tooltip("Procedural obelisk density, size range, burial, and LOD distances.")]
        public PyramidTuning Obelisks = new PyramidTuning();

        [Tooltip("Darker pyramid variant density, size range, burial, and LOD distances. Uses the PyramidDarker model and its own independent scale controls.")]
        public PyramidTuning DarkPyramids = new PyramidTuning
        {
            DensityPerChunk = 0.12f,
            MinimumScale = 5f,
            MaximumScale = 9f,
        };

        [Tooltip("Pyramid 2 variant density, size range, burial, and LOD distances. Uses the NewPyramidPrefab model and its own independent scale controls.")]
        public PyramidTuning Pyramid2 = new PyramidTuning
        {
            DensityPerChunk = 0.12f,
            MinimumScale = 5f,
            MaximumScale = 9f,
        };

        [Tooltip("Procedural saguaro distribution, silhouette, ribbing, and blossoms.")]
        public CactusTuning Cacti = new CactusTuning();

        [Tooltip("Clustered, biome-weighted, instanced desert shrub generation and silhouettes.")]
        public DesertShrubTuning DesertShrubs = new DesertShrubTuning();

        [Tooltip("Frame pacing and high-refresh runtime target.")]
        public RuntimePerformanceTuning Performance = new RuntimePerformanceTuning();

        [Tooltip("Chunk loading, unloading, and floating-origin behavior.")]
        public WorldStreamingTuning WorldStreaming = new WorldStreamingTuning();

        [Tooltip("Padded camera-frustum renderer suppression and dynamic-renderer discovery.")]
        public RendererFrustumCullingTuning RendererFrustumCulling = new RendererFrustumCullingTuning();

        [Tooltip("Spatial RenderMeshInstanced batching shared by high-volume procedural visuals.")]
        public SpatialGpuInstancingTuning SpatialGpuInstancing = new SpatialGpuInstancingTuning();

        [Tooltip("Player hull strength and damage protection.")]
        public PlayerHealthTuning HealthSettings = new PlayerHealthTuning();

        [Tooltip("Responsive layout, typography, animation, copy, and palette for the death recovery screen.")]
        public GameOverScreenTuning GameOverScreen = new GameOverScreenTuning();

        [Tooltip("Drone lock-on targeting, energy projectile, cooldown, feedback, and HUD presentation.")]
        public EnergyLauncherTuning EnergyLauncher = new EnergyLauncherTuning();

        [Tooltip("Airborne enemy spawning and combat behavior.")]
        public FlyingEnemyTuning FlyingEnemies = new FlyingEnemyTuning();

        [Tooltip("High-altitude upside-down pyramid lightning turrets.")]
        public StormPyramidTuning StormPyramids = new StormPyramidTuning();

        [Tooltip("High-altitude ring enemies that predict and strike only airborne players.")]
        public PlayerStrikeOrbTuning PlayerStrikeOrbs = new PlayerStrikeOrbTuning();

        [Tooltip("Extreme-altitude portal-duelist enemies and their accelerating Redshift Procession attacks.")]
        public VesperKiteTuning VesperKites = new VesperKiteTuning();

        [Tooltip("Ground enemy spawning, patrol, and explosion behavior.")]
        public GroundExploderTuning GroundExploders = new GroundExploderTuning();

        [Tooltip("Boost and flight ring sizes, height ranges, and animation.")]
        public RingTuning Rings = new RingTuning();

        [Tooltip("Permanent drone stat definitions, tier curves, gold costs, and upgrade-shop presentation.")]
        public DronePermanentUpgradeTuning PermanentUpgrades = new DronePermanentUpgradeTuning();

        [Tooltip("Layered procedural dune-shape controls.")]
        public DuneFieldSettings DuneGeneration = new DuneFieldSettings();

        [Tooltip("PNG texture used by the streamed dune terrain material.")]
        public Texture2D DuneTexture;

        [Tooltip("World-space width and length, in meters, covered by one repeat of the dune texture.")]
        [Min(0.01f)] public float DuneTextureTileSize = 18f;

        [Tooltip("Clockwise world-space rotation of the dune texture. Use this to run the texture's ripple bands parallel with the generated dune crests.")]
        [Range(-180f, 180f)] public float DuneTextureRotationDegrees;

        [Header("Dune Sand Surface Recoloring")]
        [Tooltip("Enables world-space macro sand colors and independent secondary surface variation.")]
        public bool DuneColorVariationEnabled = true;

        [Tooltip("Warm gold assigned to the lightest macro sand regions.")]
        [ColorUsage(false)] public Color DuneSandLightColor = new Color(1f, 0.78f, 0.36f, 1f);

        [Tooltip("Primary orange sand color used as the neutral macro-region reference.")]
        [ColorUsage(false)] public Color DuneSandMidColor = new Color(0.92f, 0.45f, 0.13f, 1f);

        [Tooltip("Darker, slightly desaturated reddish-brown assigned to low macro-noise regions.")]
        [ColorUsage(false)] public Color DuneSandDarkColor = new Color(0.62f, 0.24f, 0.08f, 1f);

        [Tooltip("World-space width, in meters, of the broad geographic sand-color pattern.")]
        [Range(300f, 700f)] public float DuneMacroColorPatternSize = 340f;

        [Tooltip("World-space width, in meters, of the secondary brightness and saturation pattern.")]
        [Range(60f, 150f)] public float DuneSecondaryColorPatternSize = 90f;

        [Tooltip("World-space width, in meters, of the fine brightness variation layered over the broad sand colors.")]
        [Range(15f, 40f)] public float DuneDetailColorPatternSize = 24f;

        [Tooltip("World-space offset of the macro color pattern. Change this to explore a different layout.")]
        public Vector2 DuneMacroColorNoiseOffset = new Vector2(1200f, -800f);

        [Tooltip("World-space offset of the secondary brightness pattern.")]
        public Vector2 DuneSecondaryBrightnessNoiseOffset = new Vector2(-370f, 910f);

        [Tooltip("Independent world-space offset of the secondary saturation pattern.")]
        public Vector2 DuneSecondarySaturationNoiseOffset = new Vector2(1420f, 480f);

        [Tooltip("Independent world-space offset of the fine brightness pattern.")]
        public Vector2 DuneDetailBrightnessNoiseOffset = new Vector2(280f, -1640f);

        [Tooltip("Macro-noise value below which darker sand regions dominate.")]
        [Range(0f, 1f)] public float DuneMacroDarkThreshold = 0.44f;

        [Tooltip("Macro-noise value above which light golden sand regions dominate.")]
        [Range(0f, 1f)] public float DuneMacroLightThreshold = 0.56f;

        [Tooltip("Softness of transitions between dark, mid, and light macro regions.")]
        [Range(0.01f, 0.25f)] public float DuneMacroColorTransitionSoftness = 0.1f;

        [Tooltip("How strongly the three-color macro palette modifies the existing dune texture.")]
        [Range(0f, 0.5f)] public float DuneMacroColorBlendStrength = 0.4f;

        [Tooltip("Lowest secondary brightness multiplier. Keep within 0.82 to 1.08 for natural sand.")]
        [Range(0.82f, 1.08f)] public float DuneBrightnessMultiplierMinimum = 0.92f;

        [Tooltip("Highest secondary brightness multiplier. Keep within 0.82 to 1.08 for natural sand.")]
        [Range(0.82f, 1.08f)] public float DuneBrightnessMultiplierMaximum = 1.08f;

        [Tooltip("Maximum fine-scale brightness change. Values around two to three percent avoid a speckled appearance.")]
        [Range(0f, 0.05f)] public float DuneDetailBrightnessVariation = 0.025f;

        [Tooltip("Lowest secondary saturation multiplier. Keep within 0.92 to 1.06 for natural sand.")]
        [Range(0.92f, 1.06f)] public float DuneSaturationMultiplierMinimum = 0.92f;

        [Tooltip("Highest secondary saturation multiplier. Keep within 0.92 to 1.06 for natural sand.")]
        [Range(0.92f, 1.06f)] public float DuneSaturationMultiplierMaximum = 1.06f;

        [Tooltip("Additional saturation multiplier applied only to dark macro regions.")]
        [Range(0.9f, 1f)] public float DuneDarkRegionSaturationMultiplier = 0.94f;

        [Tooltip("Base smoothness of the streamed dune terrain.")]
        [Range(0f, 1f)] public float DuneSurfaceSmoothness = 0.14f;

        [Tooltip("Maximum smoothness change introduced by a decorrelated secondary noise sample.")]
        [Range(0f, 0.05f)] public float DuneSmoothnessVariation = 0.025f;

        [Tooltip("Metallic response of the streamed dune terrain.")]
        [Range(0f, 1f)] public float DuneSurfaceMetallic = 0f;

        [Tooltip("Minimum directional-light contribution retained inside shadows on the dune terrain.")]
        [Range(0f, 1f)] public float DuneMinimumShadowAttenuation = 0.72f;

        [Tooltip("Vertices along one edge of each generated terrain chunk. Higher values are smoother but cost more.")]
        [Range(8, 96)] public int DuneMeshResolution = 32;

        [Tooltip("World-space width and length of each streamed terrain chunk.")]
        [Min(24f)] public float DuneChunkSize = 80f;

        [HideInInspector] public DuneGenerationPreset SelectedDunePreset = DuneGenerationPreset.ClassicDesert;

        public void EnsureInitialized()
        {
            RetroCrtScanlines ??= new RetroCrtScanlineTuning();
            PlayerTuning ??= new DroneTuning();
            PlayerTuning.EnsureInitialized();
            CompassHud ??= new CompassHudTuning();
            LaunchHud ??= new LaunchHudTuning();
            BottomHud ??= new BottomHudTuning();
            MapHud ??= new MapHudTuning();
            DroneVisuals ??= new DroneVisualTuning();
            FlightSwooshes ??= new FlightSwooshTuning();
            BoostRingTrail ??= new BoostRingTrailTuning();
            WindFields ??= new WindFieldSystemTuning();
            WindFields.EnsureInitialized();
            DustDevils ??= new DustDevilTuning();
            Clouds ??= new CloudTuning();
            Clouds.EnsureInitialized();
            Weather ??= new DesertWeatherTuning();
            Weather.EnsureInitialized();
            EnvironmentalHazards ??= new EnvironmentalHazardTuning();
            EnvironmentalHazards.EnsureInitialized();
            Audio ??= new AudioTuning();
            Audio.EnsureInitialized();
            MusicReactiveSky ??= new MusicReactiveSkyTuning();
            Deliveries ??= new DeliveryTuning();
            Deliveries.EnsureInitialized();
            Contracts ??= new CourierContractTuning();
            DeliveryMessages ??= new DeliveryMessageTuning();
            DeliveryMessages.EnsureInitialized();
            WorldHub ??= new WorldHubTuning();
            Landmarks ??= new LandmarkSystemTuning();
            Buildings ??= new ProceduralBuildingSystemTuning();
            Geoglyphs ??= new GeoglyphSystemTuning();
            Geoglyphs.EnsureInitialized();
            DesertAtlas ??= new DesertAtlasTuning();
            DesertAtlas.EnsureInitialized();
            Photography ??= new PhotographyTuning();
            Photography.EnsureInitialized();
            RouteEncounters ??= new RouteEncounterTuning();
            DynamicCouriers ??= new DynamicCourierTuning();
            Pyramids ??= new PyramidTuning();
            Obelisks ??= new PyramidTuning();
            DarkPyramids ??= new PyramidTuning();
            Pyramid2 ??= new PyramidTuning();
            Cacti ??= new CactusTuning();
            DesertShrubs ??= new DesertShrubTuning();
            DesertShrubs.EnsureInitialized();
            Performance ??= new RuntimePerformanceTuning();
            WorldStreaming ??= new WorldStreamingTuning();
            RendererFrustumCulling ??= new RendererFrustumCullingTuning();
            SpatialGpuInstancing ??= new SpatialGpuInstancingTuning();
            HealthSettings ??= new PlayerHealthTuning();
            GameOverScreen ??= new GameOverScreenTuning();
            EnergyLauncher ??= new EnergyLauncherTuning();
            FlyingEnemies ??= new FlyingEnemyTuning();
            StormPyramids ??= new StormPyramidTuning();
            PlayerStrikeOrbs ??= new PlayerStrikeOrbTuning();
            VesperKites ??= new VesperKiteTuning();
            GroundExploders ??= new GroundExploderTuning();
            Rings ??= new RingTuning();
            PermanentUpgrades ??= new DronePermanentUpgradeTuning();
            PermanentUpgrades.EnsureInitialized();
            DuneGeneration ??= new DuneFieldSettings();
        }

        public void ApplyDunePreset(DuneGenerationPreset preset)
        {
            EnsureInitialized();
            int preservedSeed = DuneGeneration.WorldSeed;
            DuneFieldSettings previousGeneration = DuneGeneration;
            DuneGeneration = new DuneFieldSettings { WorldSeed = preservedSeed };
            DuneGeneration.CopyRollingElevationFrom(previousGeneration);
            SelectedDunePreset = preset;
            DuneChunkSize = 80f;
            DuneMeshResolution = 32;

            switch (preset)
            {
                case DuneGenerationPreset.GentleCinematic:
                    DuneGeneration.MajorScale = 360f;
                    DuneGeneration.MajorAmplitude = 2.4f;
                    DuneGeneration.BroadBowlStrength = 0.22f;
                    DuneGeneration.DuneScale = 72f;
                    DuneGeneration.DuneAmplitude = 2.8f;
                    DuneGeneration.DuneWarp = 0.28f;
                    DuneGeneration.RidgeHarmonicWeight = 0.08f;
                    DuneGeneration.CrestVariationStrength = 0.12f;
                    DuneGeneration.SecondaryScale = 145f;
                    DuneGeneration.SecondaryAmplitude = 1.1f;
                    DuneGeneration.DetailAmplitude = 0.12f;
                    DuneMeshResolution = 28;
                    break;

                case DuneGenerationPreset.GrandErg:
                    DuneGeneration.MajorScale = 520f;
                    DuneGeneration.MajorAmplitude = 7.2f;
                    DuneGeneration.MajorOctaves = 5;
                    DuneGeneration.BroadBowlStrength = 0.42f;
                    DuneGeneration.DuneScale = 96f;
                    DuneGeneration.DuneAmplitude = 10.5f;
                    DuneGeneration.DuneWarp = 0.48f;
                    DuneGeneration.RidgeHarmonicWeight = 0.21f;
                    DuneGeneration.SecondaryScale = 190f;
                    DuneGeneration.SecondaryAmplitude = 3.8f;
                    DuneGeneration.DetailAmplitude = 0.22f;
                    DuneGeneration.HeightMultiplier = 1.12f;
                    DuneMeshResolution = 40;
                    break;

                case DuneGenerationPreset.SharpRidges:
                    DuneGeneration.MajorScale = 240f;
                    DuneGeneration.MajorAmplitude = 3.8f;
                    DuneGeneration.DuneScale = 38f;
                    DuneGeneration.DuneAmplitude = 8.4f;
                    DuneGeneration.DuneWarp = 0.38f;
                    DuneGeneration.PrimaryRidgeWeight = 0.78f;
                    DuneGeneration.RidgeHarmonicWeight = 0.34f;
                    DuneGeneration.RidgeHarmonicFrequency = 2.15f;
                    DuneGeneration.CrestVariationStrength = 0.1f;
                    DuneGeneration.SecondaryAmplitude = 1.6f;
                    DuneGeneration.DetailScale = 12f;
                    DuneGeneration.DetailAmplitude = 0.48f;
                    DuneGeneration.NormalSampleDistance = 0.42f;
                    DuneMeshResolution = 48;
                    break;

                case DuneGenerationPreset.WindCarved:
                    DuneGeneration.WindDirection = new Vector2(1f, 0.14f);
                    DuneGeneration.MajorScale = 310f;
                    DuneGeneration.MajorAmplitude = 4.6f;
                    DuneGeneration.DuneScale = 61f;
                    DuneGeneration.DuneAmplitude = 6.4f;
                    DuneGeneration.DuneWarp = 1.35f;
                    DuneGeneration.WarpOctaves = 5;
                    DuneGeneration.CrestVariationStrength = 0.42f;
                    DuneGeneration.SecondaryScale = 132f;
                    DuneGeneration.SecondaryAmplitude = 2.9f;
                    DuneGeneration.DetailScale = 15f;
                    DuneGeneration.DetailAmplitude = 0.3f;
                    DuneMeshResolution = 40;
                    break;

                case DuneGenerationPreset.RoundedWindDunes:
                    DuneGeneration.WindDirection = new Vector2(0.96f, 0.28f);
                    DuneGeneration.MajorScale = 340f;
                    DuneGeneration.MajorAmplitude = 4.1f;
                    DuneGeneration.BroadBowlStrength = 0.38f;
                    DuneGeneration.DuneScale = 68f;
                    DuneGeneration.DuneAmplitude = 5.6f;
                    DuneGeneration.DuneWarp = 1.05f;
                    DuneGeneration.WarpOctaves = 4;
                    DuneGeneration.PrimaryRidgeWeight = 0.54f;
                    DuneGeneration.RidgeHarmonicWeight = 0.06f;
                    DuneGeneration.CrestVariationStrength = 0.3f;
                    DuneGeneration.SecondaryScale = 124f;
                    DuneGeneration.SecondaryAmplitude = 3.1f;
                    DuneGeneration.DetailScale = 21f;
                    DuneGeneration.DetailAmplitude = 0.16f;
                    DuneGeneration.NormalSampleDistance = 0.9f;
                    DuneMeshResolution = 36;
                    break;

                case DuneGenerationPreset.WindRibbonDunes:
                    DuneGeneration.WindDirection = new Vector2(0.82f, 0.57f);
                    DuneGeneration.MajorScale = 390f;
                    DuneGeneration.MajorAmplitude = 4.8f;
                    DuneGeneration.BroadBowlStrength = 0.3f;
                    DuneGeneration.DuneScale = 82f;
                    DuneGeneration.DuneAmplitude = 6.3f;
                    DuneGeneration.DuneWarp = 1.48f;
                    DuneGeneration.WarpOctaves = 5;
                    DuneGeneration.PrimaryRidgeWeight = 0.58f;
                    DuneGeneration.RidgeHarmonicWeight = 0.09f;
                    DuneGeneration.RidgeHarmonicFrequency = 1.7f;
                    DuneGeneration.CrestVariationStrength = 0.5f;
                    DuneGeneration.SecondaryScale = 155f;
                    DuneGeneration.SecondaryAmplitude = 3.4f;
                    DuneGeneration.SecondaryOctaves = 4;
                    DuneGeneration.DetailScale = 24f;
                    DuneGeneration.DetailAmplitude = 0.14f;
                    DuneGeneration.NormalSampleDistance = 1f;
                    DuneMeshResolution = 40;
                    break;

                case DuneGenerationPreset.GrandWindSwells:
                    DuneGeneration.WindDirection = new Vector2(0.9f, 0.43f);
                    DuneGeneration.MajorScale = 610f;
                    DuneGeneration.MajorAmplitude = 7.8f;
                    DuneGeneration.MajorOctaves = 5;
                    DuneGeneration.BroadBowlStrength = 0.48f;
                    DuneGeneration.DuneScale = 116f;
                    DuneGeneration.DuneAmplitude = 9.2f;
                    DuneGeneration.DuneWarp = 1.12f;
                    DuneGeneration.WarpOctaves = 4;
                    DuneGeneration.PrimaryRidgeWeight = 0.56f;
                    DuneGeneration.RidgeHarmonicWeight = 0.05f;
                    DuneGeneration.CrestVariationStrength = 0.34f;
                    DuneGeneration.SecondaryScale = 225f;
                    DuneGeneration.SecondaryAmplitude = 4.2f;
                    DuneGeneration.DetailScale = 28f;
                    DuneGeneration.DetailAmplitude = 0.12f;
                    DuneGeneration.HeightMultiplier = 1.08f;
                    DuneGeneration.NormalSampleDistance = 1.15f;
                    DuneMeshResolution = 40;
                    break;

                case DuneGenerationPreset.RollingSandSea:
                    DuneGeneration.MajorScale = 215f;
                    DuneGeneration.MajorAmplitude = 6.8f;
                    DuneGeneration.BroadBowlStrength = 0.55f;
                    DuneGeneration.DuneScale = 86f;
                    DuneGeneration.DuneAmplitude = 3.2f;
                    DuneGeneration.DuneWarp = 0.58f;
                    DuneGeneration.RidgeHarmonicWeight = 0.06f;
                    DuneGeneration.CrestVariationStrength = 0.3f;
                    DuneGeneration.SecondaryScale = 72f;
                    DuneGeneration.SecondaryAmplitude = 4.6f;
                    DuneGeneration.SecondaryOctaves = 4;
                    DuneGeneration.DetailAmplitude = 0.18f;
                    DuneMeshResolution = 36;
                    break;

                case DuneGenerationPreset.FineRipples:
                    DuneGeneration.MajorScale = 330f;
                    DuneGeneration.MajorAmplitude = 1.8f;
                    DuneGeneration.BroadBowlStrength = 0.18f;
                    DuneGeneration.DuneScale = 24f;
                    DuneGeneration.DuneAmplitude = 2.9f;
                    DuneGeneration.DuneWarp = 0.82f;
                    DuneGeneration.RidgeHarmonicWeight = 0.24f;
                    DuneGeneration.RidgeHarmonicFrequency = 3f;
                    DuneGeneration.SecondaryScale = 58f;
                    DuneGeneration.SecondaryAmplitude = 1.3f;
                    DuneGeneration.DetailScale = 7.5f;
                    DuneGeneration.DetailAmplitude = 0.72f;
                    DuneGeneration.DetailOctaves = 3;
                    DuneGeneration.NormalSampleDistance = 0.28f;
                    DuneMeshResolution = 64;
                    break;

                case DuneGenerationPreset.ExtremeDunes:
                    DuneGeneration.MajorScale = 155f;
                    DuneGeneration.MajorAmplitude = 10f;
                    DuneGeneration.MajorOctaves = 6;
                    DuneGeneration.BroadBowlStrength = 0.62f;
                    DuneGeneration.DuneScale = 34f;
                    DuneGeneration.DuneAmplitude = 12f;
                    DuneGeneration.DuneWarp = 1.65f;
                    DuneGeneration.WarpOctaves = 5;
                    DuneGeneration.PrimaryRidgeWeight = 0.8f;
                    DuneGeneration.RidgeHarmonicWeight = 0.38f;
                    DuneGeneration.CrestVariationStrength = 0.48f;
                    DuneGeneration.SecondaryScale = 64f;
                    DuneGeneration.SecondaryAmplitude = 6.2f;
                    DuneGeneration.SecondaryOctaves = 5;
                    DuneGeneration.DetailScale = 10f;
                    DuneGeneration.DetailAmplitude = 1.1f;
                    DuneGeneration.HeightMultiplier = 1.25f;
                    DuneGeneration.NormalSampleDistance = 0.35f;
                    DuneMeshResolution = 48;
                    break;
            }
        }

        private void OnEnable()
        {
            EnsureInitialized();
        }
    }
}
