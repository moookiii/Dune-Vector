using System;
using System.Runtime.InteropServices;
using System.Threading;
using FMOD.Studio;
using Unity.Profiling;
using UnityEngine;

namespace DuneVector
{
    public enum MusicTimelineEventType : byte
    {
        Started,
        Restarted,
        Stopped,
        Beat,
        Marker,
        Destroyed,
    }

    public enum MusicVisualSectionId : byte
    {
        Opening,
        Breakdown,
        Rebuild,
        ImpactRise,
        FirstBassEntry,
        Expansion,
        HighFrequencyLift,
        MainCharge,
        PrimaryDrop,
        CompositionShift,
        SecondCharge,
        SecondPush,
        HighFrequencyClimax,
        FinalCadence,
        FinalRelease,
    }

    public enum MusicVisualCueType : byte
    {
        MinorKick,
        MajorKick,
        MinorSnare,
        AccentSnare,
        TrebleTick,
        TrebleBurst,
        ReactorAnticipation,
        ReactorDischarge,
        CompositionShift,
        FinalRelease,
    }

    [Flags]
    public enum MusicVisualEffectGroups : ushort
    {
        None = 0,
        Sky = 1 << 0,
        PressureFront = 1 << 1,
        Road = 1 << 2,
        Structures = 1 << 3,
        Drone = 1 << 4,
        Streaks = 1 << 5,
        Camera = 1 << 6,
        Glitch = 1 << 7,
        HudBorder = 1 << 8,
        Bloom = 1 << 9,
        All = ushort.MaxValue,
    }

    [Serializable]
    public struct MusicAnalysisFrame
    {
        public float RawBass;
        public float RawMid;
        public float RawHigh;
        public float NormalizedBass;
        public float NormalizedMid;
        public float NormalizedHigh;
        public float SmoothedBass;
        public float SmoothedMid;
        public float SmoothedHigh;
        public float BassTransient;
        public float HighTransient;
        public float TotalEnergy;
        public float LowHighBalance;
        public uint Sequence;
        public int TimelinePositionMilliseconds;
    }

    [Serializable]
    public struct MusicTimelineState
    {
        public int TrackId;
        public bool IsValid;
        public int TimelinePositionMilliseconds;
        public int TrackBar;
        public int Beat;
        public float Tempo;
        public int Numerator;
        public int Denominator;
        public bool IsPlaying;
        public bool IsPaused;
        public int CurrentMarkerId;
        public int CurrentSectionIndex;
        public uint PlaybackGeneration;
        public uint SeekGeneration;
        public bool BeatCallbacksRecent;
        public bool MarkerCallbacksReceived;
        public bool DiscontinuousJump;
        public int QueueOccupancy;
        public int QueueOverflowCount;
    }

    [Serializable]
    public struct MusicVisualContinuousMultipliers
    {
        [Min(0f)] public float Bass;
        [Min(0f)] public float Mid;
        [Min(0f)] public float High;
        [Min(0f)] public float Energy;
        [Min(0f)] public float Pressure;
        [Min(0f)] public float Foreground;
        [Min(0f)] public float Bloom;

        public static MusicVisualContinuousMultipliers Identity => new MusicVisualContinuousMultipliers
        {
            Bass = 1f,
            Mid = 1f,
            High = 1f,
            Energy = 1f,
            Pressure = 1f,
            Foreground = 1f,
            Bloom = 1f,
        };
    }

    [Serializable]
    public struct MusicVisualSectionDefinition
    {
        [Min(0)] public int StartBar;
        [Min(0)] public int StartTimelineMilliseconds;
        public MusicVisualSectionId Section;
        [Range(0, 5)] public int VisualTier;
        [Min(0f)] public float TransitionBeats;
        public MusicVisualContinuousMultipliers Multipliers;
        public MusicVisualEffectGroups Permissions;
        [Min(0)] public int CompositionMode;
        public uint VariationSeed;
    }

    [Serializable]
    public struct MusicVisualAuthoredCue
    {
        [Min(0)] public int Bar;
        [Min(0f)] public float Beat;
        [Min(-1)] public int ExactTimelineMilliseconds;
        public MusicVisualCueType Cue;
        [Range(0f, 1f)] public float Strength;
        [Min(0f)] public float DurationBeats;
        [Min(0)] public int VariationIndex;
        public MusicVisualEffectGroups AllowedEffects;
        public uint Seed;
    }

    [Serializable]
    public struct MusicVisualMarkerDefinition
    {
        [Min(0)] public int Bar;
        [Min(0)] public int TimelineMilliseconds;
        public string StableName;

        public int StableId => MusicMarkerHash.Compute(StableName);
    }

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

    public struct MusicReactiveRuntimeState
    {
        public MusicAnalysisFrame Analysis;
        public MusicTimelineState Timeline;
        public MusicVisualSectionId Section;
        public int SectionIndex;
        public int VisualTier;
        public MusicVisualContinuousMultipliers Multipliers;
        public MusicVisualEffectGroups Permissions;
        public float Bass;
        public float Mid;
        public float High;
        public float Energy;
        public float Pressure;
        public float Foreground;
        public float Bloom;
        public bool SuppressTransientEvents;
    }

    public struct MusicVisualDispatchCommand
    {
        public MusicVisualCueType Type;
        public float Strength;
        public float DurationBeats;
        public MusicVisualEffectGroups AllowedEffects;
        public int CueIndex;
        public uint DeterministicSeed;
        public bool IsAuthored;
    }

    public interface IMusicReactiveSink
    {
        void ApplyContinuous(in MusicReactiveRuntimeState state);
        void Dispatch(in MusicVisualDispatchCommand command, in MusicReactiveRuntimeState state);
        void ResetMusicResponse();
    }

    internal static class MusicMarkerHash
    {
        public static int Compute(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < value.Length; i++)
                {
                    hash = (hash ^ value[i]) * 16777619;
                }
                return (int)hash;
            }
        }

        public static int ComputeUtf8(IntPtr value)
        {
            if (value == IntPtr.Zero)
            {
                return 0;
            }
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; ; i++)
                {
                    byte current = Marshal.ReadByte(value, i);
                    if (current == 0)
                    {
                        break;
                    }
                    hash = (hash ^ current) * 16777619;
                }
                return (int)hash;
            }
        }
    }

    internal sealed class MusicTimelineCallbackBridge : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct TimelineEvent
        {
            public MusicTimelineEventType Type;
            public int Position;
            public int Bar;
            public int Beat;
            public float Tempo;
            public int Numerator;
            public int Denominator;
            public int MarkerId;
            public uint Generation;
        }

        private readonly TimelineEvent[] _events;
        private readonly EVENT_CALLBACK _callback;
        private GCHandle _selfHandle;
        private EventInstance _instance;
        private int _readIndex;
        private int _writeIndex;
        private int _overflowCount;
        private uint _generation;
        private bool _attached;

        public int Occupancy
        {
            get
            {
                int write = Volatile.Read(ref _writeIndex);
                int read = Volatile.Read(ref _readIndex);
                return write >= read ? write - read : _events.Length - read + write;
            }
        }

        public int OverflowCount => Volatile.Read(ref _overflowCount);

        public MusicTimelineCallbackBridge(int requestedCapacity)
        {
            int capacity = Mathf.NextPowerOfTwo(Mathf.Max(8, requestedCapacity));
            _events = new TimelineEvent[capacity];
            _callback = EventCallback;
        }

        public bool Attach(EventInstance instance, uint generation)
        {
            Dispose();
            if (!instance.isValid())
            {
                return false;
            }

            _instance = instance;
            _generation = generation;
            _selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);
            IntPtr userdata = GCHandle.ToIntPtr(_selfHandle);
            FMOD.RESULT userResult = _instance.setUserData(userdata);
            EVENT_CALLBACK_TYPE mask = EVENT_CALLBACK_TYPE.STARTED
                | EVENT_CALLBACK_TYPE.RESTARTED
                | EVENT_CALLBACK_TYPE.STOPPED
                | EVENT_CALLBACK_TYPE.DESTROYED
                | EVENT_CALLBACK_TYPE.TIMELINE_BEAT
                | EVENT_CALLBACK_TYPE.TIMELINE_MARKER;
            FMOD.RESULT callbackResult = _instance.setCallback(_callback, mask);
            _attached = userResult == FMOD.RESULT.OK && callbackResult == FMOD.RESULT.OK;
            if (!_attached)
            {
                Dispose();
            }
            return _attached;
        }

        public void SetGeneration(uint generation)
        {
            _generation = generation;
        }

        public void Consume(ref MusicTimelineState state)
        {
            state.DiscontinuousJump = false;
            while (TryDequeue(out TimelineEvent timelineEvent))
            {
                state.PlaybackGeneration = timelineEvent.Generation;
                switch (timelineEvent.Type)
                {
                    case MusicTimelineEventType.Started:
                    case MusicTimelineEventType.Restarted:
                        state.IsPlaying = true;
                        break;
                    case MusicTimelineEventType.Stopped:
                    case MusicTimelineEventType.Destroyed:
                        state.IsPlaying = false;
                        break;
                    case MusicTimelineEventType.Beat:
                        state.TimelinePositionMilliseconds = timelineEvent.Position;
                        state.TrackBar = Math.Max(0, timelineEvent.Bar - 1);
                        state.Beat = Math.Max(0, timelineEvent.Beat - 1);
                        state.Tempo = timelineEvent.Tempo;
                        state.Numerator = timelineEvent.Numerator;
                        state.Denominator = timelineEvent.Denominator;
                        state.BeatCallbacksRecent = true;
                        break;
                    case MusicTimelineEventType.Marker:
                        state.CurrentMarkerId = timelineEvent.MarkerId;
                        state.MarkerCallbacksReceived = true;
                        break;
                }
            }
            state.QueueOccupancy = Occupancy;
            state.QueueOverflowCount = OverflowCount;
        }

        private bool TryDequeue(out TimelineEvent timelineEvent)
        {
            int read = Volatile.Read(ref _readIndex);
            if (read == Volatile.Read(ref _writeIndex))
            {
                timelineEvent = default;
                return false;
            }
            timelineEvent = _events[read];
            Volatile.Write(ref _readIndex, (read + 1) & (_events.Length - 1));
            return true;
        }

        private void Enqueue(in TimelineEvent timelineEvent)
        {
            int write = Volatile.Read(ref _writeIndex);
            int next = (write + 1) & (_events.Length - 1);
            if (next == Volatile.Read(ref _readIndex))
            {
                Interlocked.Increment(ref _overflowCount);
                return;
            }
            _events[write] = timelineEvent;
            Volatile.Write(ref _writeIndex, next);
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT EventCallback(EVENT_CALLBACK_TYPE type, IntPtr eventInstancePointer, IntPtr parameters)
        {
            EventInstance instance = new EventInstance { handle = eventInstancePointer };
            if (instance.getUserData(out IntPtr userdata) != FMOD.RESULT.OK || userdata == IntPtr.Zero)
            {
                return FMOD.RESULT.OK;
            }

            GCHandle handle = GCHandle.FromIntPtr(userdata);
            if (!(handle.Target is MusicTimelineCallbackBridge bridge))
            {
                return FMOD.RESULT.OK;
            }

            TimelineEvent timelineEvent = new TimelineEvent { Generation = bridge._generation };
            switch (type)
            {
                case EVENT_CALLBACK_TYPE.STARTED:
                    timelineEvent.Type = MusicTimelineEventType.Started;
                    break;
                case EVENT_CALLBACK_TYPE.RESTARTED:
                    timelineEvent.Type = MusicTimelineEventType.Restarted;
                    break;
                case EVENT_CALLBACK_TYPE.STOPPED:
                    timelineEvent.Type = MusicTimelineEventType.Stopped;
                    break;
                case EVENT_CALLBACK_TYPE.DESTROYED:
                    timelineEvent.Type = MusicTimelineEventType.Destroyed;
                    break;
                case EVENT_CALLBACK_TYPE.TIMELINE_BEAT:
                    TIMELINE_BEAT_PROPERTIES beat = Marshal.PtrToStructure<TIMELINE_BEAT_PROPERTIES>(parameters);
                    timelineEvent.Type = MusicTimelineEventType.Beat;
                    timelineEvent.Position = beat.position;
                    timelineEvent.Bar = beat.bar;
                    timelineEvent.Beat = beat.beat;
                    timelineEvent.Tempo = beat.tempo;
                    timelineEvent.Numerator = beat.timesignatureupper;
                    timelineEvent.Denominator = beat.timesignaturelower;
                    break;
                case EVENT_CALLBACK_TYPE.TIMELINE_MARKER:
                    TIMELINE_MARKER_PROPERTIES marker = Marshal.PtrToStructure<TIMELINE_MARKER_PROPERTIES>(parameters);
                    IntPtr namePointer = Marshal.ReadIntPtr(parameters);
                    timelineEvent.Type = MusicTimelineEventType.Marker;
                    timelineEvent.Position = marker.position;
                    timelineEvent.MarkerId = MusicMarkerHash.ComputeUtf8(namePointer);
                    break;
                default:
                    return FMOD.RESULT.OK;
            }
            bridge.Enqueue(in timelineEvent);
            return FMOD.RESULT.OK;
        }

        public void Dispose()
        {
            if (_instance.isValid() && _selfHandle.IsAllocated)
            {
                _instance.setCallback(null, 0);
                _instance.setUserData(IntPtr.Zero);
            }
            _attached = false;
            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }
            _instance.clearHandle();
            Volatile.Write(ref _readIndex, 0);
            Volatile.Write(ref _writeIndex, 0);
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorMusicReactiveConductor : MonoBehaviour
    {
        private static readonly ProfilerMarker TimelineConsumeMarker = new ProfilerMarker("MusicVisualizer.TimelineConsume");
        private static readonly ProfilerMarker ConductorUpdateMarker = new ProfilerMarker("MusicVisualizer.ConductorUpdate");
        private static readonly ProfilerMarker CueEvaluationMarker = new ProfilerMarker("MusicVisualizer.CueEvaluation");
        private static readonly ProfilerMarker DispatchMarker = new ProfilerMarker("MusicVisualizer.Dispatch");

        private DuneVectorAudioManager _audio;
        private DuneVectorMusicReactiveSky _analysisSource;
        private MusicReactiveSkyTuning _settings;
        private MusicVisualTrackProfile _profile;
        private readonly IMusicReactiveSink[] _sinks = new IMusicReactiveSink[16];
        private int _sinkCount;
        private int _cueCursor;
        private int _previousTimelinePosition;
        private uint _observedSeekGeneration;
        private MusicReactiveRuntimeState _state;

        public MusicReactiveRuntimeState RuntimeState => _state;
        public int NextCueIndex => _cueCursor;

        public void Initialize(DuneVectorAudioManager audio, DuneVectorMusicReactiveSky analysisSource, MusicReactiveSkyTuning settings)
        {
            _audio = audio;
            _analysisSource = analysisSource;
            _settings = settings;
            _profile = settings != null ? settings.TrackProfile : null;
            _cueCursor = 0;
            _previousTimelinePosition = 0;
            _observedSeekGeneration = 0;
            if (_profile == null)
            {
                Debug.LogWarning("Music visualizer track profile is unavailable; authored cues remain disabled.", this);
            }
        }

        public bool RegisterSink(IMusicReactiveSink sink)
        {
            if (sink == null || _sinkCount >= _sinks.Length)
            {
                return false;
            }
            for (int i = 0; i < _sinkCount; i++)
            {
                if (ReferenceEquals(_sinks[i], sink))
                {
                    return true;
                }
            }
            _sinks[_sinkCount++] = sink;
            return true;
        }

        private void Update()
        {
            if (_audio == null || _analysisSource == null || _settings == null)
            {
                return;
            }

            MusicTimelineState timeline;
            using (TimelineConsumeMarker.Auto())
            {
                timeline = _audio.TimelineState;
            }
            using (ConductorUpdateMarker.Auto())
            {
                MusicAnalysisFrame analysis = _analysisSource.LatestAnalysisFrame;
                AssembleRuntimeState(in timeline, in analysis);
            }
            using (CueEvaluationMarker.Auto())
            {
                EvaluateAuthoredCues();
            }
            using (DispatchMarker.Auto())
            {
                for (int i = 0; i < _sinkCount; i++)
                {
                    _sinks[i].ApplyContinuous(in _state);
                }
            }
            _previousTimelinePosition = timeline.TimelinePositionMilliseconds;
        }

        private void AssembleRuntimeState(in MusicTimelineState timeline, in MusicAnalysisFrame analysis)
        {
            int sectionIndex = _profile != null
                ? _profile.ResolveSectionIndex(timeline.TimelinePositionMilliseconds, timeline.TrackBar)
                : -1;
            MusicVisualSectionDefinition section = sectionIndex >= 0
                ? _profile.Sections[sectionIndex]
                : new MusicVisualSectionDefinition
                {
                    Multipliers = MusicVisualContinuousMultipliers.Identity,
                    Permissions = MusicVisualEffectGroups.All,
                };

            _state.Analysis = analysis;
            _state.Timeline = timeline;
            _state.SectionIndex = sectionIndex;
            _state.Section = section.Section;
            _state.VisualTier = section.VisualTier;
            _state.Multipliers = section.Multipliers;
            _state.Permissions = section.Permissions;
            _state.Bass = Mathf.Clamp01(analysis.SmoothedBass * section.Multipliers.Bass);
            _state.Mid = Mathf.Clamp01(analysis.SmoothedMid * section.Multipliers.Mid);
            _state.High = Mathf.Clamp01(analysis.SmoothedHigh * section.Multipliers.High);
            _state.Energy = Mathf.Clamp01(analysis.TotalEnergy * section.Multipliers.Energy);
            _state.Pressure = Mathf.Clamp01(_state.Bass * section.Multipliers.Pressure);
            _state.Foreground = Mathf.Clamp01(_state.Energy * section.Multipliers.Foreground);
            _state.Bloom = Mathf.Clamp01(_state.Energy * section.Multipliers.Bloom);
            _state.SuppressTransientEvents = !timeline.IsValid || !timeline.IsPlaying || timeline.IsPaused;

            if (timeline.SeekGeneration != _observedSeekGeneration || timeline.DiscontinuousJump)
            {
                _observedSeekGeneration = timeline.SeekGeneration;
                ReconstructCueCursor(timeline.TimelinePositionMilliseconds);
                ResetSinks();
            }
        }

        private void EvaluateAuthoredCues()
        {
            if (_profile == null || _state.SuppressTransientEvents)
            {
                return;
            }
            int current = _state.Timeline.TimelinePositionMilliseconds;
            while (_cueCursor < _profile.AuthoredCues.Length)
            {
                int cueTime = _profile.ResolveCuePositionMilliseconds(_cueCursor);
                if (cueTime > current)
                {
                    break;
                }
                if (cueTime > _previousTimelinePosition)
                {
                    DispatchCue(_cueCursor);
                }
                _cueCursor++;
            }
        }

        private void DispatchCue(int cueIndex)
        {
            MusicVisualAuthoredCue cue = _profile.AuthoredCues[cueIndex];
            MusicVisualDispatchCommand command = new MusicVisualDispatchCommand
            {
                Type = cue.Cue,
                Strength = cue.Strength,
                DurationBeats = cue.DurationBeats,
                AllowedEffects = cue.AllowedEffects,
                CueIndex = cueIndex,
                DeterministicSeed = cue.Seed ^ _state.Timeline.PlaybackGeneration ^ (uint)_profile.StableTrackHash,
                IsAuthored = true,
            };
            for (int i = 0; i < _sinkCount; i++)
            {
                _sinks[i].Dispatch(in command, in _state);
            }
        }

        private void ReconstructCueCursor(int timelineMilliseconds)
        {
            _cueCursor = 0;
            if (_profile == null)
            {
                return;
            }
            while (_cueCursor < _profile.AuthoredCues.Length
                && _profile.ResolveCuePositionMilliseconds(_cueCursor) <= timelineMilliseconds)
            {
                _cueCursor++;
            }
        }

        private void ResetSinks()
        {
            for (int i = 0; i < _sinkCount; i++)
            {
                _sinks[i].ResetMusicResponse();
            }
        }

        private void OnDisable()
        {
            ResetSinks();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            if (_settings == null || !_settings.ShowDevelopmentDebugPanel)
            {
                return;
            }

            GUILayout.BeginArea(_settings.DevelopmentDebugPanelRect, GUI.skin.box);
            GUILayout.Label("MUSIC VISUALIZER / AUTHORITATIVE STATE");
            MusicTimelineState timeline = _state.Timeline;
            MusicAnalysisFrame analysis = _state.Analysis;
            GUILayout.Label($"Track: {(_profile != null ? _profile.StableTrackId : "PROFILE MISSING")}");
            GUILayout.Label($"FMOD: valid={timeline.IsValid} playing={timeline.IsPlaying} paused={timeline.IsPaused}");
            GUILayout.Label($"Timeline: {timeline.TimelinePositionMilliseconds} ms  generation={timeline.PlaybackGeneration}/{timeline.SeekGeneration}");
            GUILayout.Label($"Bar/beat: {timeline.TrackBar}:{timeline.Beat}  {timeline.Tempo:0.###} BPM  {timeline.Numerator}/{timeline.Denominator}");
            GUILayout.Label($"Marker: {timeline.CurrentMarkerId}  beat callbacks={timeline.BeatCallbacksRecent} marker callbacks={timeline.MarkerCallbacksReceived}");
            GUILayout.Label($"Queue: {timeline.QueueOccupancy}  overflow={timeline.QueueOverflowCount}");
            GUILayout.Space(4f);
            GUILayout.Label($"Section: {_state.Section}  tier={_state.VisualTier}  cue={_cueCursor}");
            GUILayout.Label($"FFT raw: {analysis.RawBass:0.000} / {analysis.RawMid:0.000} / {analysis.RawHigh:0.000}");
            GUILayout.Label($"FFT smooth: {analysis.SmoothedBass:0.000} / {analysis.SmoothedMid:0.000} / {analysis.SmoothedHigh:0.000}");
            GUILayout.Label($"Transient: bass={analysis.BassTransient:0.000} high={analysis.HighTransient:0.000} energy={analysis.TotalEnergy:0.000}");
            GUILayout.Label($"Discontinuous jump: {timeline.DiscontinuousJump}  suppressed={_state.SuppressTransientEvents}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Previous Section"))
            {
                SeekRelativeSection(-1);
            }
            if (GUILayout.Button("Next Section"))
            {
                SeekRelativeSection(1);
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Restart Authoritative Track"))
            {
                _audio.RestartMusicTimeline();
            }
            GUILayout.EndArea();
        }

        private void SeekRelativeSection(int direction)
        {
            if (_profile == null || _profile.Sections.Length == 0)
            {
                return;
            }
            int current = Mathf.Clamp(_state.SectionIndex, 0, _profile.Sections.Length - 1);
            int target = Mathf.Clamp(current + direction, 0, _profile.Sections.Length - 1);
            _audio.SeekMusicTimeline(_profile.Sections[target].StartTimelineMilliseconds);
        }
#endif
    }
}
