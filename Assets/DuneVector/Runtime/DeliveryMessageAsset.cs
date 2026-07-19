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
