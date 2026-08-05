# Music Visualizer Inspection — Exodia / 825 HP

This report records the Phase 1 inspection completed before the orchestration pass. It describes the project as found on 2026-08-05 and is intentionally implementation-facing.

## Existing reactive stack

- `DuneVectorMusicReactiveSky` owns the single FMOD FFT DSP, reads three bands, smooths them, derives bass/high pulses, writes the existing `DuneVectorY2KSky` volume parameters, and applies the existing capped bloom response.
- `DuneVectorY2KSky` plus `DuneVectorY2KSky.shader` already implement resonance fronts, melodic currents, bass shock rings, percussive filaments, treble star bursts, and their authored response parameters. These remain the visual implementation to extend through a conductor adapter.
- `MusicReactiveSkyTuning` is embedded in `DuneVectorRuntimeSettings`; its serialized values live in `Assets/DuneVector/ScriptableObjects/Dune Vector Runtime Settings.asset`. The existing values must remain compatible and the preview beat rate is currently 167 BPM.
- `DuneVectorBootstrap.BuildMusicReactiveSky` creates one runtime component and passes the audio manager, shared sky override, bloom override, gameplay camera, and runtime settings.

## FMOD ownership and lifecycle

- `DuneVectorAudioManager` is the authoritative background-music owner. It creates and retains one `EventInstance` from `AudioTuning.BackgroundMusicEvent`, pauses it when muted, exposes its channel group to the FFT system, and releases it in `OnDestroy`.
- The owner currently exposes no beat/marker callback bridge and no authoritative seek API for music. Timeline callbacks, state snapshots, generation tracking, and development seek controls must therefore be added at this ownership boundary.
- Audio/video preferences are JSON persisted to `DuneVectorAudio.dat`; this is the existing `.dat` persistence path to extend for Visualizer FOV.
- No editable FMOD Studio project is present in the repository. Only built `Master.bank` and `Master.strings.bank` files exist, so Unity integration can be completed but named timeline markers cannot truthfully be authored here.

## URP and camera architecture

- Graphics Settings and every quality level reference `Dune Vector URP Pipeline.asset`.
- That pipeline has one renderer at index 0: `Dune Vector URP Renderer.asset`. The dynamically built gameplay camera does not override the renderer index, so it uses renderer 0 for all supported qualities.
- The pipeline asset has depth and opaque textures disabled globally. The gameplay camera explicitly requests both color and depth. The glitch only needs camera color, so it must not introduce a new global depth requirement.
- URP package version is 17.4.0. The renderer asset has native render pass disabled and currently contains Beautify and a Full Screen Pass for retro CRT scanlines. The new glitch must use the URP 17 RenderGraph `RecordRenderGraph` path and skip recording while inactive.
- The gameplay camera is created by `DuneVectorBootstrap`; `DroneCameraController` owns normal camera pose and FOV. Music contributions must be additive at a defined late presentation point and must never replace `BaseFieldOfView`.
- HUD systems are immediate-mode `OnGUI` components, not world geometry or camera-space canvases. They render after the camera, which naturally keeps text outside a renderer feature applied to the world camera color.

## Foreground ownership

- The streamed world and roads are procedurally created and use shared materials assembled by `DuneVectorVisuals`; there is no dedicated road-response Shader Graph or material contract.
- Procedural structures are spawned and streamed by `DuneVectorProceduralBuildingDirector`, with imported prefab renderers retaining shared materials. A global shader response is the least invasive SRP-Batcher-friendly integration.
- The drone presentation is created by `DuneVectorVisuals.CreateDroneVisual`. There is no music-thruster response contract. A cached hierarchy responder can target authored renderer/material properties without per-frame lookup.
- The project contains conventional ParticleSystem and prefab effects but no project-owned VFX Graph assets. Foreground streaks should therefore use one prewarmed shared particle system unless a VFX Graph package/asset is deliberately introduced later.
- No music-specific object pool exists. The visualizer needs fixed-size pressure-front storage and a single precreated local reaction light; pool exhaustion must drop cosmetics and increment diagnostics.

## Debug and settings integration

- The project uses immediate-mode development HUDs and an immediate-mode pause menu. A development-only visualizer panel can follow this architecture and remain outside the world render.
- Video controls and defaults are owned by `DuneVectorAudioManager` and `DuneVectorPauseMenu`. Visualizer FOV belongs in the same `.dat` schema, must migrate missing data to Off, and must be applied immediately.
- The runtime assembly already references FMOD, Input System, Core RP, and URP. No additional runtime assembly is required.

## Phase boundaries

1. Inspection and findings (this document).
2. Timeline bridge, analysis snapshot, immutable conductor/runtime state, track-profile data model, section/cue evaluation, seek handling, and debug state.
3. Explicit adapters for the existing sky and bloom stack.
4. Perspective pressure-front pool and authored reactor multi-front mode.
5. Foreground globals, road/structure/drone response, reaction light, and streak system.
6. Additive camera sink plus persisted Visualizer FOV option.
7. Conditional URP RenderGraph world glitch and HUD-safe snare integration.
8. Exodia marker, section, and cue authoring in the runtime-settings asset.
9. Safety tuning, validation, profiling instrumentation, and acceptance checks available without external authoring tools.

## Known external/manual dependencies

- FMOD Studio must receive the supplied 168 BPM / 4/4 tempo marker and the 15 stable `EXODIA825_Bxxx` markers. The built banks cannot be edited safely from Unity.
- Representative CPU/GPU captures, full-song synchronization observation, Frame Debugger, RenderGraph Viewer, and SRP Batcher checks require an interactive player/editor profiling session. Automated compilation and static validations can be performed here; measured runtime values must not be invented.
