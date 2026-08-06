using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using FMOD.Studio;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Serialization;

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

    public enum MusicVisualFrontKind : byte
    {
        None,
        Ordinary,
        Strong,
        Reactor,
        Split,
    }

    public enum MusicScreenFlareDirectionMode : byte
    {
        Default,
        Vertical,
        Horizontal,
        Diagonal,
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
        Filaments = 1 << 10,
        TrebleParticles = 1 << 11,
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
        [Min(0f)] public float CurrentIntensity;
        [Min(0f)] public float CurrentThickness;
        [Min(0f)] public float CurrentProximity;
        [Min(0f)] public float CurrentTravel;
        [Min(0f)] public float FilamentAvailability;
        [Min(0f)] public float TrebleAvailability;
        [Min(0f)] public float RoadResponse;
        [Min(0f)] public float StructureResponse;

        public static MusicVisualContinuousMultipliers Identity => new MusicVisualContinuousMultipliers
        {
            Bass = 1f,
            Mid = 1f,
            High = 1f,
            Energy = 1f,
            Pressure = 1f,
            Foreground = 1f,
            Bloom = 1f,
            CurrentIntensity = 1f,
            CurrentThickness = 1f,
            CurrentProximity = 1f,
            CurrentTravel = 1f,
            FilamentAvailability = 1f,
            TrebleAvailability = 1f,
            RoadResponse = 1f,
            StructureResponse = 1f,
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
        public MusicVisualFrontKind FrontKind;
        [Range(0, 4)] public int FrontArcCount;
        [Min(0f)] public float FrontLeadBeats;
        [Min(0f)] public float FrontTravelBeats;
        [Min(0f)] public float FrontStrengthMultiplier;
        [Range(0f, 1f)] public float FrontEdgeBreakup;
        [ColorUsage(false, true)] public Color FrontColor;
        [FormerlySerializedAs("FragmentCount")]
        [Min(0)] public int ScreenFlareLineCount;
        [FormerlySerializedAs("FragmentLifetimeSeconds")]
        public Vector2 ScreenFlareLineLifetimeSeconds;
        [FormerlySerializedAs("FragmentHorizontalBias")]
        [Range(-1f, 1f)] public float ScreenFlareHorizontalBias;
        public MusicScreenFlareDirectionMode ScreenFlareDirectionMode;
        [Min(0f)] public float ScreenFlareWidthScale;
        [Min(0f)] public float ScreenFlareSpeedScale;
        [Min(0)] public int ScreenFlareHeldLineCount;
        public Vector2 ScreenFlareHeldLineLifetimeSeconds;
        [Min(0f)] public float ScreenFlareHeldWidthScale;
        [Min(0f)] public float ScreenFlareHeldSpeedScale;
        [Min(0f)] public float RoadResponse;
        [Min(0f)] public float SecondaryRoadResponse;
        [Min(0f)] public float SecondaryRoadDelayBeats;
        [Min(0f)] public float StructureResponse;
        [Min(0f)] public float DroneThrusterBoost;
        [Min(0f)] public float DroneTrailWidthBoost;
        [Min(0f)] public float FovDegrees;
        [Min(0f)] public float PositionImpulseMeters;
        public float RollDegrees;
        [Min(0f)] public float GlitchUvDisplacement;
        [Range(0, 2)] public int GlitchSliceCount;
        [Min(0f)] public float FilamentIntensity;
        [Range(0, 3)] public int FilamentStrikeCount;
        [Min(0)] public int TrebleParticleCount;
        [Min(0f)] public float TrebleBrightness;
    }

    [Serializable]
    public struct MusicVisualAuthoredFlarePattern
    {
        [Min(0)] public int StartTimelineMilliseconds;
        [Min(0)] public int EndTimelineMilliseconds;
        [Min(0.125f)] public float IntervalBeats;
        [Min(0)] public int MinimumLineCount;
        [Min(0)] public int MaximumLineCount;
        public Vector2 LineLifetimeSeconds;
        public MusicScreenFlareDirectionMode DirectionMode;
        [Min(0f)] public float WidthScale;
        [Min(0f)] public float SpeedScale;
        [Range(0f, 0.5f)] public float InitialViewportRadius;
        public bool EmitMirroredPair;
        [Min(0)] public int HeldLineCount;
        public Vector2 HeldLineLifetimeSeconds;
        [Min(0f)] public float HeldWidthScale;
        [Min(0f)] public float HeldSpeedScale;
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
        public bool IsPreRoll;
        public MusicVisualFrontKind FrontKind;
        public int FrontArcCount;
        public float FrontTravelSeconds;
        public float FrontStrengthMultiplier;
        public float FrontEdgeBreakup;
        public Color FrontColor;
        public int ScreenFlareLineCount;
        public Vector2 ScreenFlareLineLifetimeSeconds;
        public float ScreenFlareHorizontalBias;
        public MusicScreenFlareDirectionMode ScreenFlareDirectionMode;
        public float ScreenFlareWidthScale;
        public float ScreenFlareSpeedScale;
        public float ScreenFlareInitialViewportRadius;
        public bool ScreenFlareEmitMirroredPair;
        public int ScreenFlareHeldLineCount;
        public Vector2 ScreenFlareHeldLineLifetimeSeconds;
        public float ScreenFlareHeldWidthScale;
        public float ScreenFlareHeldSpeedScale;
        public float RoadResponse;
        public float SecondaryRoadResponse;
        public float SecondaryRoadDelaySeconds;
        public float StructureResponse;
        public float DroneThrusterBoost;
        public float DroneTrailWidthBoost;
        public float FovDegrees;
        public float PositionImpulseMeters;
        public float RollDegrees;
        public float GlitchUvDisplacement;
        public int GlitchSliceCount;
        public float FilamentIntensity;
        public int FilamentStrikeCount;
        public int TrebleParticleCount;
        public float TrebleBrightness;
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
        private bool _bassTransientArmed = true;
        private bool _highTransientArmed = true;
        private int _lastKickTimeline = int.MinValue;
        private int _lastHighTimeline = int.MinValue;
        private int _rateLimitBar = -1;
        private int _kicksThisBar;
        private int _highEventsThisBar;
        private bool[] _observedMarkers;
        private int _lastObservedMarkerId;
        private uint _validatedPlaybackGeneration;
        private DuneVectorPerspectivePressureFronts _pressureFronts;
        private DuneVectorMusicForegroundResponse _foreground;
        private DuneVectorMusicCameraEffects _cameraEffects;
        private DuneVectorMusicWorldGlitchSink _glitch;
        private bool _rendererValidationScheduled;
        private bool _releaseLocked;

        public MusicReactiveRuntimeState RuntimeState => _state;
        public int NextCueIndex => _cueCursor;

        public void Initialize(DuneVectorAudioManager audio, DuneVectorMusicReactiveSky analysisSource, MusicReactiveSkyTuning settings)
        {
            if (_audio != null)
            {
                _audio.ActiveMusicTrackChanged -= HandleActiveMusicTrackChanged;
                _audio.MusicVisualizerModeChanged -= HandleMusicVisualizerModeChanged;
            }
            _audio = audio;
            _analysisSource = analysisSource;
            _settings = settings;
            if (_audio != null)
            {
                _audio.ActiveMusicTrackChanged += HandleActiveMusicTrackChanged;
                _audio.MusicVisualizerModeChanged += HandleMusicVisualizerModeChanged;
            }
            _profile = _audio != null
                ? _audio.ActiveMusicTrackProfile
                : settings != null ? settings.TrackProfile : null;
            _cueCursor = 0;
            _releaseLocked = false;
            _previousTimelinePosition = 0;
            _observedSeekGeneration = 0;
            int initialTimeline = _audio != null ? _audio.TimelineState.TimelinePositionMilliseconds : 0;
            _previousTimelinePosition = initialTimeline;
            if (_profile == null)
            {
                Debug.LogWarning("Music visualizer track profile is unavailable; authored cues remain disabled.", this);
            }
            else
            {
                _observedMarkers = new bool[_profile.Markers.Length];
                ReconstructCueCursor(initialTimeline);
                ValidateProfileAuthoring();
            }
        }

        private void HandleActiveMusicTrackChanged(MusicPlaylistTrack track)
        {
            _profile = track != null ? track.VisualizerProfile : null;
            _cueCursor = 0;
            _releaseLocked = false;
            _lastObservedMarkerId = 0;
            _validatedPlaybackGeneration = 0;
            _bassTransientArmed = true;
            _highTransientArmed = true;
            _lastKickTimeline = int.MinValue;
            _lastHighTimeline = int.MinValue;
            _rateLimitBar = -1;
            _kicksThisBar = 0;
            _highEventsThisBar = 0;

            MusicTimelineState timeline = _audio != null ? _audio.TimelineState : default;
            _previousTimelinePosition = timeline.TimelinePositionMilliseconds;
            _observedSeekGeneration = timeline.SeekGeneration;
            _observedMarkers = _profile != null
                ? new bool[_profile.Markers.Length]
                : null;
            if (_profile != null)
            {
                ReconstructCueCursor(timeline.TimelinePositionMilliseconds);
                ValidateProfileAuthoring();
            }
            ResetSinks();
        }

        private void HandleMusicVisualizerModeChanged(MusicVisualizerMode mode)
        {
            if (mode != MusicVisualizerMode.NoFlash)
            {
                return;
            }
            _pressureFronts?.ResetMusicResponse();
            _foreground?.ClearFlashingResponse();
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
            if (sink is DuneVectorPerspectivePressureFronts pressureFronts)
            {
                _pressureFronts = pressureFronts;
            }
            else if (sink is DuneVectorMusicForegroundResponse foreground)
            {
                _foreground = foreground;
            }
            else if (sink is DuneVectorMusicCameraEffects cameraEffects)
            {
                _cameraEffects = cameraEffects;
            }
            else if (sink is DuneVectorMusicWorldGlitchSink glitch)
            {
                _glitch = glitch;
            }
            return true;
        }

        public void ValidateRuntimeIntegration()
        {
            if (_pressureFronts == null)
            {
                Debug.LogWarning("Music visualizer pressure-front pool is unavailable.", this);
            }
            if (_foreground == null)
            {
                Debug.LogWarning("Music visualizer foreground responder is unavailable.", this);
            }
            if (_cameraEffects == null)
            {
                Debug.LogWarning("Music visualizer camera-effects sink is unavailable.", this);
            }
            if (_glitch == null)
            {
                Debug.LogWarning("Music visualizer world-glitch sink is unavailable.", this);
            }
            else if (!DuneVectorMusicGlitchRuntime.FeatureAvailable && !_rendererValidationScheduled)
            {
                _rendererValidationScheduled = true;
                StartCoroutine(ValidateRendererFeatureAfterFirstFrame());
            }
        }

        private IEnumerator ValidateRendererFeatureAfterFirstFrame()
        {
            yield return new WaitForEndOfFrame();
            _rendererValidationScheduled = false;
            if (!DuneVectorMusicGlitchRuntime.FeatureAvailable)
            {
                Debug.LogWarning("Gameplay camera uses Renderer Data without the visualizer glitch feature.", this);
            }
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
            ObserveTimelineValidation(in timeline);
            using (ConductorUpdateMarker.Auto())
            {
                MusicAnalysisFrame analysis = _analysisSource.LatestAnalysisFrame;
                AssembleRuntimeState(in timeline, in analysis);
            }
            using (CueEvaluationMarker.Auto())
            {
                EvaluateRuntimeTransients();
                EvaluateAuthoredPreRolls();
                EvaluateAuthoredCues();
                EvaluateAuthoredFlarePatterns();
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
            MusicVisualContinuousMultipliers multipliers = section.Multipliers;
            NormalizeExtendedMultipliers(ref multipliers);
            if (_profile != null && sectionIndex > 0 && section.TransitionBeats > 0f)
            {
                MusicVisualSectionDefinition previousSection = _profile.Sections[sectionIndex - 1];
                MusicVisualContinuousMultipliers previousMultipliers = previousSection.Multipliers;
                NormalizeExtendedMultipliers(ref previousMultipliers);
                float beatMilliseconds = 60000f / Mathf.Max(1f, _profile.BeatsPerMinute);
                float transitionMilliseconds = section.TransitionBeats * beatMilliseconds;
                float transition = Mathf.Clamp01(
                    (timeline.TimelinePositionMilliseconds - section.StartTimelineMilliseconds)
                    / Mathf.Max(1f, transitionMilliseconds));
                multipliers = LerpMultipliers(previousMultipliers, multipliers, transition);
            }

            _state.Analysis = analysis;
            _state.Timeline = timeline;
            _state.SectionIndex = sectionIndex;
            _state.Section = section.Section;
            _state.VisualTier = section.VisualTier;
            _state.Multipliers = multipliers;
            _state.Permissions = section.Permissions;
            if (_audio.VisualizerMode == MusicVisualizerMode.NoFlash)
            {
                _state.Permissions &= ~(
                    MusicVisualEffectGroups.PressureFront
                    | MusicVisualEffectGroups.Structures
                    | MusicVisualEffectGroups.Streaks
                    | MusicVisualEffectGroups.Glitch
                    | MusicVisualEffectGroups.HudBorder
                    | MusicVisualEffectGroups.Bloom
                    | MusicVisualEffectGroups.TrebleParticles);
            }
            _state.Bass = Mathf.Clamp01(analysis.SmoothedBass * multipliers.Bass);
            _state.Mid = Mathf.Clamp01(analysis.SmoothedMid * multipliers.Mid);
            _state.High = Mathf.Clamp01(analysis.SmoothedHigh * multipliers.High);
            _state.Energy = Mathf.Clamp01(analysis.TotalEnergy * multipliers.Energy);
            _state.Pressure = Mathf.Clamp01(_state.Bass * multipliers.Pressure);
            _state.Foreground = Mathf.Clamp01(_state.Energy * multipliers.Foreground);
            _state.Bloom = Mathf.Clamp01(_state.Energy * multipliers.Bloom);
            _state.SuppressTransientEvents = !timeline.IsValid || !timeline.IsPlaying || timeline.IsPaused;

            if (timeline.SeekGeneration != _observedSeekGeneration || timeline.DiscontinuousJump)
            {
                _observedSeekGeneration = timeline.SeekGeneration;
                ReconstructCueCursor(timeline.TimelinePositionMilliseconds);
                ResetSinks();
            }
        }

        private static MusicVisualContinuousMultipliers LerpMultipliers(
            in MusicVisualContinuousMultipliers from,
            in MusicVisualContinuousMultipliers to,
            float t)
        {
            return new MusicVisualContinuousMultipliers
            {
                Bass = Mathf.Lerp(from.Bass, to.Bass, t),
                Mid = Mathf.Lerp(from.Mid, to.Mid, t),
                High = Mathf.Lerp(from.High, to.High, t),
                Energy = Mathf.Lerp(from.Energy, to.Energy, t),
                Pressure = Mathf.Lerp(from.Pressure, to.Pressure, t),
                Foreground = Mathf.Lerp(from.Foreground, to.Foreground, t),
                Bloom = Mathf.Lerp(from.Bloom, to.Bloom, t),
                CurrentIntensity = Mathf.Lerp(from.CurrentIntensity, to.CurrentIntensity, t),
                CurrentThickness = Mathf.Lerp(from.CurrentThickness, to.CurrentThickness, t),
                CurrentProximity = Mathf.Lerp(from.CurrentProximity, to.CurrentProximity, t),
                CurrentTravel = Mathf.Lerp(from.CurrentTravel, to.CurrentTravel, t),
                FilamentAvailability = Mathf.Lerp(from.FilamentAvailability, to.FilamentAvailability, t),
                TrebleAvailability = Mathf.Lerp(from.TrebleAvailability, to.TrebleAvailability, t),
                RoadResponse = Mathf.Lerp(from.RoadResponse, to.RoadResponse, t),
                StructureResponse = Mathf.Lerp(from.StructureResponse, to.StructureResponse, t),
            };
        }

        private static void NormalizeExtendedMultipliers(ref MusicVisualContinuousMultipliers multipliers)
        {
            if (multipliers.CurrentIntensity != 0f
                || multipliers.CurrentThickness != 0f
                || multipliers.CurrentProximity != 0f
                || multipliers.CurrentTravel != 0f
                || multipliers.FilamentAvailability != 0f
                || multipliers.TrebleAvailability != 0f
                || multipliers.RoadResponse != 0f
                || multipliers.StructureResponse != 0f)
            {
                return;
            }
            multipliers.CurrentIntensity = 1f;
            multipliers.CurrentThickness = 1f;
            multipliers.CurrentProximity = 1f;
            multipliers.CurrentTravel = 1f;
            multipliers.FilamentAvailability = 1f;
            multipliers.TrebleAvailability = 1f;
            multipliers.RoadResponse = 1f;
            multipliers.StructureResponse = 1f;
        }

        private void EvaluateAuthoredPreRolls()
        {
            if (_profile == null || _state.SuppressTransientEvents)
            {
                return;
            }
            int current = _state.Timeline.TimelinePositionMilliseconds;
            float beatMilliseconds = 60000f / Mathf.Max(1f, _profile.BeatsPerMinute);
            for (int cueIndex = 0; cueIndex < _profile.AuthoredCues.Length; cueIndex++)
            {
                MusicVisualAuthoredCue cue = _profile.AuthoredCues[cueIndex];
                if (cue.FrontKind == MusicVisualFrontKind.None || cue.FrontLeadBeats <= 0f)
                {
                    continue;
                }
                int arrival = _profile.ResolveCuePositionMilliseconds(cueIndex);
                int dispatchTime = arrival - Mathf.RoundToInt(cue.FrontLeadBeats * beatMilliseconds);
                if (dispatchTime <= current && dispatchTime > _previousTimelinePosition)
                {
                    DispatchCue(cueIndex, true);
                }
            }
        }

        private void ValidateProfileAuthoring()
        {
            if (!string.Equals(_audio.MusicEventPath, _profile.FmodEventPath, StringComparison.Ordinal))
            {
                Debug.LogWarning("Music visualizer event reference is invalid for the active track profile.", this);
            }
            if (!_audio.MusicTimelineCallbackRegistered)
            {
                Debug.LogWarning("FMOD beat callbacks are not registered.", this);
            }
            float durationDifference = Mathf.Abs(
                _audio.MusicEventLengthMilliseconds * 0.001f - _profile.ExpectedDurationSeconds);
            if (_audio.MusicEventLengthMilliseconds > 0
                && _profile.ExpectedDurationSeconds > 0f
                && durationDifference > _settings.DurationValidationToleranceSeconds)
            {
                Debug.LogWarning("Music visualizer FMOD event duration does not match the active track profile.", this);
            }
        }

        private void OnDestroy()
        {
            if (_audio != null)
            {
                _audio.ActiveMusicTrackChanged -= HandleActiveMusicTrackChanged;
                _audio.MusicVisualizerModeChanged -= HandleMusicVisualizerModeChanged;
            }
        }

        private void ObserveTimelineValidation(in MusicTimelineState timeline)
        {
            if (_profile == null || _observedMarkers == null)
            {
                return;
            }
            if (_validatedPlaybackGeneration == 0)
            {
                _validatedPlaybackGeneration = timeline.PlaybackGeneration;
            }
            else if (timeline.PlaybackGeneration != 0
                && timeline.PlaybackGeneration != _validatedPlaybackGeneration)
            {
                ReportMissingMarkers();
                Array.Clear(_observedMarkers, 0, _observedMarkers.Length);
                _lastObservedMarkerId = 0;
                _validatedPlaybackGeneration = timeline.PlaybackGeneration;
            }

            int markerId = timeline.CurrentMarkerId;
            if (markerId == 0 || markerId == _lastObservedMarkerId)
            {
                return;
            }
            _lastObservedMarkerId = markerId;
            for (int i = 0; i < _profile.Markers.Length; i++)
            {
                if (_profile.Markers[i].StableId == markerId)
                {
                    _observedMarkers[i] = true;
                    break;
                }
            }
        }

        private void ReportMissingMarkers()
        {
            int missingCount = 0;
            for (int i = 0; i < _observedMarkers.Length; i++)
            {
                if (!_observedMarkers[i])
                {
                    missingCount++;
                }
            }
            if (missingCount == 0)
            {
                return;
            }

            StringBuilder report = new StringBuilder(256);
            report.Append("Music visualizer FMOD marker validation: ");
            report.Append(missingCount);
            report.Append(" required marker(s) were not observed: ");
            for (int i = 0; i < _observedMarkers.Length; i++)
            {
                if (_observedMarkers[i])
                {
                    continue;
                }
                MusicVisualMarkerDefinition marker = _profile.Markers[i];
                report.Append(marker.StableName);
                report.Append('@');
                report.Append(marker.TimelineMilliseconds);
                report.Append("ms ");
            }
            Debug.LogWarning(report.ToString(), this);
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

        private void EvaluateAuthoredFlarePatterns()
        {
            if (_profile == null
                || _state.SuppressTransientEvents
                || _state.Timeline.DiscontinuousJump
                || _profile.AuthoredFlarePatterns == null)
            {
                return;
            }

            int current = _state.Timeline.TimelinePositionMilliseconds;
            float beatMilliseconds = 60000f / Mathf.Max(1f, _profile.BeatsPerMinute);
            for (int patternIndex = 0; patternIndex < _profile.AuthoredFlarePatterns.Length; patternIndex++)
            {
                MusicVisualAuthoredFlarePattern pattern = _profile.AuthoredFlarePatterns[patternIndex];
                int interval = Mathf.Max(1, Mathf.RoundToInt(pattern.IntervalBeats * beatMilliseconds));
                int firstStep = Mathf.Max(0, Mathf.FloorToInt(
                    (_previousTimelinePosition - pattern.StartTimelineMilliseconds) / (float)interval) + 1);
                for (int step = firstStep; ; step++)
                {
                    int cueTime = pattern.StartTimelineMilliseconds + step * interval;
                    if (cueTime >= pattern.EndTimelineMilliseconds || cueTime > current)
                    {
                        break;
                    }
                    if (cueTime <= _previousTimelinePosition)
                    {
                        continue;
                    }

                    uint seed = pattern.Seed ^ ((uint)step * 2654435761u)
                        ^ _state.Timeline.PlaybackGeneration ^ (uint)_profile.StableTrackHash;
                    int minimum = Mathf.Max(0, pattern.MinimumLineCount);
                    int maximum = Mathf.Max(minimum, pattern.MaximumLineCount);
                    int lineCount = minimum + (int)(seed % (uint)(maximum - minimum + 1));
                    float speedScale = pattern.SpeedScale;
                    if (pattern.DirectionMode != MusicScreenFlareDirectionMode.Default)
                    {
                        lineCount *= Mathf.Max(1, _profile.AuthoredDirectionalFlareLineCountMultiplier);
                        speedScale *= _profile.AuthoredDirectionalFlareTravelScale > 0f
                            ? _profile.AuthoredDirectionalFlareTravelScale
                            : 1f;
                    }
                    MusicVisualDispatchCommand command = new MusicVisualDispatchCommand
                    {
                        Type = MusicVisualCueType.TrebleBurst,
                        Strength = 1f,
                        AllowedEffects = MusicVisualEffectGroups.Streaks & _state.Permissions,
                        CueIndex = -1,
                        DeterministicSeed = seed,
                        IsAuthored = true,
                        ScreenFlareLineCount = lineCount,
                        ScreenFlareLineLifetimeSeconds = pattern.LineLifetimeSeconds,
                        ScreenFlareDirectionMode = pattern.DirectionMode,
                        ScreenFlareWidthScale = pattern.WidthScale,
                        ScreenFlareSpeedScale = speedScale,
                        ScreenFlareInitialViewportRadius = pattern.InitialViewportRadius,
                        ScreenFlareEmitMirroredPair = pattern.EmitMirroredPair,
                        ScreenFlareHeldLineCount = pattern.HeldLineCount,
                        ScreenFlareHeldLineLifetimeSeconds = pattern.HeldLineLifetimeSeconds,
                        ScreenFlareHeldWidthScale = pattern.HeldWidthScale,
                        ScreenFlareHeldSpeedScale = pattern.HeldSpeedScale,
                    };
                    for (int sinkIndex = 0; sinkIndex < _sinkCount; sinkIndex++)
                    {
                        _sinks[sinkIndex].Dispatch(in command, in _state);
                    }
                }
            }
        }

        private void EvaluateRuntimeTransients()
        {
            if (_state.SuppressTransientEvents || _releaseLocked)
            {
                return;
            }

            int currentBar = _state.Timeline.TrackBar;
            if (currentBar != _rateLimitBar)
            {
                _rateLimitBar = currentBar;
                _kicksThisBar = 0;
                _highEventsThisBar = 0;
            }

            float bassTransient = _state.Analysis.BassTransient;
            if (!_bassTransientArmed && bassTransient <= _settings.KickHysteresisRelease)
            {
                _bassTransientArmed = true;
            }
            int timeline = _state.Timeline.TimelinePositionMilliseconds;
            if (_bassTransientArmed
                && bassTransient >= _settings.MinorKickThreshold
                && (long)timeline - _lastKickTimeline >= _settings.KickCooldownMilliseconds
                && _kicksThisBar < _settings.MaximumKicksPerBar)
            {
                MusicVisualCueType type = bassTransient >= _settings.MajorKickThreshold
                    ? MusicVisualCueType.MajorKick
                    : MusicVisualCueType.MinorKick;
                DispatchRuntime(type, bassTransient, MusicVisualEffectGroups.Sky
                    | MusicVisualEffectGroups.Road
                    | MusicVisualEffectGroups.Drone);
                _bassTransientArmed = false;
                _lastKickTimeline = timeline;
                _kicksThisBar++;
            }

            float highTransient = _state.Analysis.HighTransient;
            if (!_highTransientArmed && highTransient <= _settings.SnareHysteresisRelease)
            {
                _highTransientArmed = true;
            }
            if (_highTransientArmed
                && highTransient >= _settings.MinorSnareThreshold
                && (long)timeline - _lastHighTimeline >= _settings.SnareCooldownMilliseconds
                && _highEventsThisBar < _settings.MaximumSnaresPerBar)
            {
                bool snareBeat = _state.Timeline.BeatCallbacksRecent
                    && (_state.Timeline.Beat == 1 || _state.Timeline.Beat == 3);
                MusicVisualCueType type = snareBeat
                    ? (highTransient >= _settings.AccentSnareThreshold
                        ? MusicVisualCueType.AccentSnare
                        : MusicVisualCueType.MinorSnare)
                    : (highTransient >= _settings.AccentSnareThreshold
                        ? MusicVisualCueType.TrebleBurst
                        : MusicVisualCueType.TrebleTick);
                DispatchRuntime(type, highTransient, MusicVisualEffectGroups.Sky
                    | MusicVisualEffectGroups.Streaks
                    | MusicVisualEffectGroups.TrebleParticles);
                _highTransientArmed = false;
                _lastHighTimeline = timeline;
                _highEventsThisBar++;
            }
        }

        private void DispatchRuntime(MusicVisualCueType type, float strength, MusicVisualEffectGroups effects)
        {
            MusicVisualDispatchCommand command = new MusicVisualDispatchCommand
            {
                Type = type,
                Strength = Mathf.Clamp01(strength),
                AllowedEffects = effects & _state.Permissions,
                CueIndex = -1,
                DeterministicSeed = (uint)_state.Timeline.TimelinePositionMilliseconds
                    ^ _state.Timeline.PlaybackGeneration
                    ^ (uint)_state.Timeline.TrackId,
                IsAuthored = false,
            };
            for (int i = 0; i < _sinkCount; i++)
            {
                _sinks[i].Dispatch(in command, in _state);
            }
        }

        private void DispatchCue(int cueIndex, bool isPreRoll = false)
        {
            MusicVisualAuthoredCue cue = _profile.AuthoredCues[cueIndex];
            if (!isPreRoll && cue.Cue == MusicVisualCueType.FinalRelease)
            {
                _releaseLocked = true;
            }
            float beatSeconds = 60f / Mathf.Max(1f, _profile.BeatsPerMinute);
            MusicVisualEffectGroups allowed = cue.AllowedEffects & _state.Permissions;
            if (isPreRoll)
            {
                allowed &= MusicVisualEffectGroups.PressureFront;
            }
            else if (cue.FrontLeadBeats > 0f)
            {
                allowed &= ~MusicVisualEffectGroups.PressureFront;
            }
            MusicVisualDispatchCommand command = new MusicVisualDispatchCommand
            {
                Type = cue.Cue,
                Strength = cue.Strength,
                DurationBeats = cue.DurationBeats,
                AllowedEffects = allowed,
                CueIndex = cueIndex,
                DeterministicSeed = cue.Seed ^ _state.Timeline.PlaybackGeneration ^ (uint)_profile.StableTrackHash,
                IsAuthored = true,
                IsPreRoll = isPreRoll,
                FrontKind = cue.FrontKind,
                FrontArcCount = cue.FrontArcCount,
                FrontTravelSeconds = cue.FrontTravelBeats * beatSeconds,
                FrontStrengthMultiplier = cue.FrontStrengthMultiplier,
                FrontEdgeBreakup = cue.FrontEdgeBreakup,
                FrontColor = cue.FrontColor,
                ScreenFlareLineCount = cue.ScreenFlareLineCount,
                ScreenFlareLineLifetimeSeconds = cue.ScreenFlareLineLifetimeSeconds,
                ScreenFlareHorizontalBias = cue.ScreenFlareHorizontalBias,
                ScreenFlareDirectionMode = cue.ScreenFlareDirectionMode,
                ScreenFlareWidthScale = cue.ScreenFlareWidthScale,
                ScreenFlareSpeedScale = cue.ScreenFlareSpeedScale,
                ScreenFlareHeldLineCount = cue.ScreenFlareHeldLineCount,
                ScreenFlareHeldLineLifetimeSeconds = cue.ScreenFlareHeldLineLifetimeSeconds,
                ScreenFlareHeldWidthScale = cue.ScreenFlareHeldWidthScale,
                ScreenFlareHeldSpeedScale = cue.ScreenFlareHeldSpeedScale,
                RoadResponse = cue.RoadResponse,
                SecondaryRoadResponse = cue.SecondaryRoadResponse,
                SecondaryRoadDelaySeconds = cue.SecondaryRoadDelayBeats * beatSeconds,
                StructureResponse = cue.StructureResponse,
                DroneThrusterBoost = cue.DroneThrusterBoost,
                DroneTrailWidthBoost = cue.DroneTrailWidthBoost,
                FovDegrees = cue.FovDegrees,
                PositionImpulseMeters = cue.PositionImpulseMeters,
                RollDegrees = cue.RollDegrees,
                GlitchUvDisplacement = cue.GlitchUvDisplacement,
                GlitchSliceCount = cue.GlitchSliceCount,
                FilamentIntensity = cue.FilamentIntensity,
                FilamentStrikeCount = cue.FilamentStrikeCount,
                TrebleParticleCount = cue.TrebleParticleCount,
                TrebleBrightness = cue.TrebleBrightness,
            };
            for (int i = 0; i < _sinkCount; i++)
            {
                _sinks[i].Dispatch(in command, in _state);
            }
        }

        private void ReconstructCueCursor(int timelineMilliseconds)
        {
            _cueCursor = 0;
            _releaseLocked = false;
            if (_profile == null)
            {
                return;
            }
            while (_cueCursor < _profile.AuthoredCues.Length
                && _profile.ResolveCuePositionMilliseconds(_cueCursor) <= timelineMilliseconds)
            {
                if (_profile.AuthoredCues[_cueCursor].Cue == MusicVisualCueType.FinalRelease)
                {
                    _releaseLocked = true;
                }
                _cueCursor++;
            }
        }

        private void ResetSinks()
        {
            _bassTransientArmed = true;
            _highTransientArmed = true;
            _lastKickTimeline = int.MinValue;
            _lastHighTimeline = int.MinValue;
            _rateLimitBar = -1;
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
            GUILayout.Label($"Thresholds: kick={_settings.MinorKickThreshold:0.00}/{_settings.MajorKickThreshold:0.00} snare={_settings.MinorSnareThreshold:0.00}/{_settings.AccentSnareThreshold:0.00} armed={_bassTransientArmed}/{_highTransientArmed}");
            GUILayout.Label($"Discontinuous jump: {timeline.DiscontinuousJump}  suppressed={_state.SuppressTransientEvents}");
            GUILayout.Label($"Fronts: ordinary={(_pressureFronts != null ? _pressureFronts.ActiveOrdinaryCount : 0)} reactor={(_pressureFronts != null ? _pressureFronts.ActiveReactorCount : 0)} dropped={(_pressureFronts != null ? _pressureFronts.DroppedFrontCount : 0)}");
            GUILayout.Label($"Foreground: road={(_foreground != null ? _foreground.ActiveRoadPulseCount : 0)} streaks={(_foreground != null ? _foreground.LiveStreakCount : 0)} light={(_foreground != null && _foreground.ReactionLightActive)}");
            GUILayout.Label($"Glitch: {(_glitch != null ? _glitch.Intensity : 0f):0.000} feature={DuneVectorMusicGlitchRuntime.FeatureAvailable} HUD=OnGUI/post-world");
            GUILayout.Label($"Camera: FOV option={(_audio != null && _audio.VisualizerFovEnabled)} request={(_cameraEffects != null ? _cameraEffects.RequestedFovOffset : 0f):0.000} applied={(_cameraEffects != null ? _cameraEffects.AppliedFovOffset : 0f):0.000}");
            GUILayout.Label($"Camera: roll={(_cameraEffects != null ? _cameraEffects.AppliedRoll : 0f):0.000} position={(_cameraEffects != null ? _cameraEffects.AppliedPosition : 0f):0.000}");
            GUILayout.Label("Rendering: renderer index=0 color=Camera depth=not used by glitch");

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
