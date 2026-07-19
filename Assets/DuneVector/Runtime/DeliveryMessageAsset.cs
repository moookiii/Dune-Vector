using System;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

namespace DuneVector
{
    [CreateAssetMenu(
        fileName = "Delivery Message",
        menuName = "Dune Vector/Narrative/Delivery Message",
        order = 20)]
    public sealed class DeliveryMessageAsset : ScriptableObject
    {
        [Tooltip("Optional stable narrative identifier used by tooling and future campaign logic.")]
        public string MessageId;

        [Tooltip("Optional authored campaign index. Sequence order remains authoritative when this is negative.")]
        public int ProgressionIndex = -1;

        [TextArea(8, 30)]
        [Tooltip("Plain text message. Two consecutive newline characters create a hard page break.")]
        public string Text;

        public IReadOnlyList<string> BuildPages()
        {
            string normalized = (Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] authoredPages = normalized.Split(new[] { "\n\n" }, StringSplitOptions.None);
            List<string> pages = new List<string>(authoredPages.Length);
            for (int index = 0; index < authoredPages.Length; index++)
            {
                string page = authoredPages[index].Trim();
                if (page.Length > 0)
                {
                    pages.Add(page);
                }
            }

            if (pages.Count == 0)
            {
                pages.Add(string.Empty);
            }
            return pages;
        }
    }

    [Serializable]
    public sealed class DeliveryMessageTuning
    {
        [Header("Progression")]
        [Tooltip("Messages play in this authored list order, one per completed delivery.")]
        public List<DeliveryMessageAsset> Sequence = new List<DeliveryMessageAsset>();

        [Tooltip("Explicitly allows the authored sequence to replay from the beginning after its final entry.")]
        public bool LoopSequence;

        [Header("Typewriter")]
        [Min(0.01f)] public float CharactersPerSecond = 34f;
        [Min(0f)] public float PageAdvanceInputDelay = 0.12f;
        [Min(0f)] public float PageFadeOutDuration = 0.1f;
        [Min(0f)] public float EmptyPageBeatDuration = 0.16f;
        [Min(0f)] public float PageStartBrightnessDuration = 0.18f;

        [Header("Reading Layout")]
        [Min(320f)] public float ReferenceWidth = 1920f;
        [Min(240f)] public float ReferenceHeight = 1080f;
        [Range(0.5f, 2f)] public float MinimumScale = 0.72f;
        [Range(0.5f, 2f)] public float MaximumScale = 1.25f;
        [Min(320f)] public float ReadingAreaWidth = 900f;
        [Min(160f)] public float ReadingAreaHeight = 360f;
        public float ReadingAreaVerticalOffset = 78f;
        [Min(0f)] public float ScreenMargin = 48f;
        [Min(0f)] public float HorizontalPadding = 48f;
        [Min(0f)] public float TextTopPadding = 64f;
        [Min(0f)] public float TextBottomPadding = 64f;
        [Min(0f)] public float RuleOffset = 18f;
        [Min(0.5f)] public float RuleThickness = 1f;
        [Range(0f, 1f)] public float BottomRuleOpacity = 0.28f;
        [Min(0f)] public float CornerDetailLength = 14f;
        [Min(0f)] public float HeaderRuleGap = 3f;
        [Min(8f)] public float IndicatorWidth = 60f;

        [Header("Typography")]
        [Tooltip("Optional packaged narrative font. PreferredFontName is used when this is unassigned.")]
        public Font NarrativeFont;
        [Tooltip("Preferred installed font. The active GUI font is used if it is unavailable.")]
        public string PreferredFontName = "Segoe UI";
        [Min(10)] public int NarrativeFontSize = 28;
        [Min(0f)] public float NarrativeLineSpacing = 12f;
        [Tooltip("Extra vertical room reserved for font ascenders and descenders so glyphs are never clipped.")]
        [Min(0f)] public float NarrativeLineClipPadding = 8f;
        [Min(8)] public int HeaderFontSize = 10;
        [Min(8)] public int IndicatorFontSize = 18;
        [Min(8)] public int HintFontSize = 11;
        public string TransmissionHeader = "PRIVATE COURIER CHANNEL  /  RECEIVED";
        public string ContinueIndicator = "▾";
        public string FinalIndicator = "◇";
        public string FirstUseInputHint = "SPACE / ENTER / CLICK\nSkip while typing  •  Continue when complete";
        [Min(0f)] public float TextGlowOffset = 1f;
        [Range(0f, 1f)] public float TextGlowOpacity = 0.14f;

        [Header("Newest Character Emission")]
        [ColorUsage(false, true)] public Color NewestCharacterEmissionColor = new Color(0.72f, 1.15f, 1.08f, 1f);
        [Min(0.01f)] public float NewestCharacterFlashDuration = 0.18f;
        [Range(0f, 4f)] public float NewestCharacterFlashIntensity = 1.2f;
        [Min(0f)] public float NewestCharacterGlowRadius = 2f;
        [Range(0f, 1f)] public float NewestCharacterGlowOpacity = 0.52f;
        [Range(4, 16)] public int NewestCharacterGlowSamples = 8;

        [Header("Transmission Palette")]
        [ColorUsage(false)] public Color BackdropColor = new Color(0.003f, 0.006f, 0.009f, 0.992f);
        [ColorUsage(false)] public Color ReadingAreaColor = new Color(0.035f, 0.055f, 0.062f, 0.16f);
        [ColorUsage(false)] public Color BorderColor = new Color(0.44f, 0.62f, 0.65f, 0.32f);
        [ColorUsage(false)] public Color NarrativeTextColor = new Color(0.82f, 0.88f, 0.87f, 1f);
        [ColorUsage(false)] public Color PageStartTextColor = new Color(0.94f, 1f, 0.98f, 1f);
        [ColorUsage(false)] public Color SecondaryTextColor = new Color(0.48f, 0.61f, 0.62f, 0.8f);
        [ColorUsage(false)] public Color SignalColor = new Color(0.45f, 0.76f, 0.76f, 0.48f);
        [ColorUsage(false)] public Color GrainColor = new Color(0.7f, 0.8f, 0.79f, 0.035f);
        [ColorUsage(false)] public Color ChromaticWarmColor = new Color(0.72f, 0.2f, 0.18f, 0.025f);
        [ColorUsage(false)] public Color ChromaticCoolColor = new Color(0.18f, 0.55f, 0.72f, 0.03f);

        [Header("Subtle Motion")]
        [Min(0f)] public float IndicatorPulseSpeed = 2.1f;
        [Range(0f, 1f)] public float IndicatorMinimumAlpha = 0.32f;
        [Min(0f)] public float TransmissionFlickerSpeed = 7f;
        [Range(0f, 0.2f)] public float TransmissionFlickerAmount = 0.025f;
        [Min(0f)] public float ScanLineSpeed = 18f;
        [Min(0.5f)] public float ScanLineThickness = 1f;
        [Min(0f)] public float ChromaticSeparation = 1f;
        [Range(0, 160)] public int GrainPointCount = 42;
        [Min(0.1f)] public float GrainRefreshRate = 10f;
        [Min(0.5f)] public float GrainPointSize = 1f;
        [Range(4, 64)] public int TypingSignalSegments = 28;
        [Min(0f)] public float TypingSignalWidth = 96f;
        [Min(0f)] public float TypingSignalHeight = 4f;
        [Min(0f)] public float TypingSignalSpeed = 9f;
        [Min(0.5f)] public float TypingSignalSegmentThickness = 1f;

        [Header("First-use Hint")]
        [Min(0f)] public float FirstUseHintHoldDuration = 3.5f;
        [Min(0.01f)] public float FirstUseHintFadeDuration = 1.2f;
        [Min(0f)] public float FirstUseHintBottomMargin = 54f;
        [Min(8f)] public float FirstUseHintHeight = 48f;

        [Header("FMOD")]
        [Tooltip("Looping FMOD event used only while characters are actively appearing.")]
        public EventReference TypingLoopEvent;

        public bool TryResolve(int absoluteSequenceIndex, out DeliveryMessageAsset message)
        {
            message = null;
            if (Sequence == null || Sequence.Count == 0 || absoluteSequenceIndex < 0)
            {
                return false;
            }

            int index = LoopSequence
                ? absoluteSequenceIndex % Sequence.Count
                : absoluteSequenceIndex;
            if (index < 0 || index >= Sequence.Count)
            {
                return false;
            }

            message = Sequence[index];
            return message != null;
        }

        public void EnsureInitialized()
        {
            Sequence ??= new List<DeliveryMessageAsset>();
        }
    }
}
