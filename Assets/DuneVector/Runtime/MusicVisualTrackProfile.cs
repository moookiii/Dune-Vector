using System;
using UnityEngine;

namespace DuneVector
{
    [CreateAssetMenu(fileName = "Music Visual Track Profile", menuName = "Dune Vector/Music Visual Track Profile")]
    public sealed class MusicVisualTrackProfile : ScriptableObject
    {
        [Header("Identification")]
        public string StableTrackId;
        public string DisplayName;
        public string FmodEventPath;
        [Min(0f)] public float SourceDurationSeconds;
        [Min(1)] public int ProfileRevision = 1;
        public string AudioContentHash;

        [Header("Musical Alignment")]
        [Min(1f)] public float BeatsPerMinute = 168f;
        [Min(1)] public int TimeSignatureNumerator = 4;
        [Min(1)] public int TimeSignatureDenominator = 4;
        [Min(0f)] public float DownbeatOffsetSeconds;
        [Min(0f)] public float ExpectedDurationSeconds;
        public MusicVisualMarkerDefinition[] Markers = Array.Empty<MusicVisualMarkerDefinition>();

        [Header("Composition")]
        public MusicVisualSectionDefinition[] Sections = Array.Empty<MusicVisualSectionDefinition>();
        public MusicVisualAuthoredCue[] AuthoredCues = Array.Empty<MusicVisualAuthoredCue>();
        public MusicVisualAuthoredFlarePattern[] AuthoredFlarePatterns = Array.Empty<MusicVisualAuthoredFlarePattern>();

        [Header("Complete Song Visualizer Tuning")]
        [Tooltip("Music-reactive sky, analysis, transient, foreground, camera, bloom, and glitch settings activated with this song.")]
        public MusicReactiveSkyTuning ReactiveSkySettings = new MusicReactiveSkyTuning();

        public int StableTrackHash => MusicMarkerHash.Compute(StableTrackId);

        public int ResolveSectionIndex(int timelineMilliseconds, int trackBar)
        {
            int resolved = -1;
            for (int i = 0; i < Sections.Length; i++)
            {
                MusicVisualSectionDefinition section = Sections[i];
                if (timelineMilliseconds < section.StartTimelineMilliseconds && trackBar < section.StartBar)
                {
                    break;
                }
                resolved = i;
            }
            return resolved;
        }

        public int ResolveCuePositionMilliseconds(int cueIndex)
        {
            MusicVisualAuthoredCue cue = AuthoredCues[cueIndex];
            if (cue.ExactTimelineMilliseconds >= 0)
            {
                return cue.ExactTimelineMilliseconds;
            }
            double beatMilliseconds = 60000.0 / Math.Max(1.0, BeatsPerMinute);
            double beatsFromDownbeat = cue.Bar * TimeSignatureNumerator + cue.Beat;
            return Mathf.RoundToInt((float)(DownbeatOffsetSeconds * 1000.0 + beatsFromDownbeat * beatMilliseconds));
        }
    }
}
