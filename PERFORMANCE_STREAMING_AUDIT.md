# Fast-Flight Streaming Performance Audit

## Implementation status

The optimization pass following this audit implemented:

- Collision-only emergency chunks at an independently authored resolution
- Separate collision, visual-terrain, and decorative-content stages
- Velocity-directed collision preloading
- A main-thread generation time budget
- Cached padded height grids for seam-safe terrain normals
- Reused terrain build buffers
- Distance-limited collider activation and streamed gameplay simulation
- Cached spatial-instancing LOD batches
- Streaming profiler markers
- Authored 165 FPS frame pacing with VSync disabled

The current authored active radius is 3 rather than 14, collision resolution is 24
rather than visual resolution 52, and full chunk content now advances one stage per
frame. A GTX 1080 player-build capture is still required to verify the 165 FPS
hardware target and tune GPU-bound effects.

## Executive finding

Fast flight exposes a synchronous chunk-generation path that bypasses the configured
`ChunksGeneratedPerFrame` budget. When the player enters a new chunk,
`ScheduleStreaming` calls `EnsureCollisionNeighborhood`, which immediately constructs
every missing chunk in a 3x3 neighborhood. A normal boundary crossing can therefore
construct three complete chunks in one frame; skipping far enough ahead can construct
all nine.

Those chunks are not collision-only. Their constructors synchronously build terrain,
upload a render mesh, assign a `MeshCollider`, spawn clouds, rings, cacti, pyramids,
enemies, and generate shrub placements and instance batches.

## Highest-value changes

### 1. Separate collision readiness from full chunk decoration

Create a staged chunk lifecycle:

1. Height/collision data
2. Terrain renderer
3. Gameplay-critical content
4. Decorative content (clouds, shrubs, cacti, pyramids)

Only the first stage should be permitted in the emergency neighborhood path. All
other stages should remain in a time-budgeted queue. Prefer a millisecond budget over
a chunk-count budget because chunk cost varies substantially.

Add velocity-directed preloading so the collision stages in front of the drone are
ready before a boundary crossing. Keep the prediction time and stage budgets on
`DuneVectorRuntimeSettings`.

Relevant code:

- `DesertWorldStreamer.ScheduleStreaming`
- `DesertWorldStreamer.EnsureCollisionNeighborhood`
- `DesertWorldStreamer.GenerateChunkImmediate`
- `DesertChunk` constructor

### 2. Generate terrain normals from a shared height grid

The authored runtime settings use mesh resolution 52, producing 2,809 vertices per
chunk. `BuildTerrainMesh` calls `SampleHeight` for the vertex and `SampleNormal` for
the normal. `SampleNormal` calls `SampleHeight` four more times.

That produces approximately 14,045 expensive multi-octave height evaluations per
chunk. Three synchronous chunks cost about 42,135 evaluations; nine cost about
126,405.

Build a one-vertex padded height grid and calculate normals from adjacent cached
heights. A 55x55 padded grid needs about 3,025 height evaluations, reducing this part
of generation by roughly 78% while preserving seam-safe central differences.

Relevant code:

- `DesertChunk.BuildTerrainMesh`
- `DuneHeightField.SampleHeight`
- `DuneHeightField.SampleNormal`

### 3. Move height generation off the main thread and stagger collider cooking

The height grid is deterministic, managed computation and can be generated in a
worker job. Unity object creation and mesh assignment must return to the main thread,
but those uploads can be separately budgeted.

`MeshCollider.sharedMesh` can trigger synchronous physics cooking. Do not assign
several new terrain colliders in one frame. Queue collider activation, or use a
coarser collision mesh whose resolution is independently authored on
`DuneVectorRuntimeSettings`.

### 4. Stop rebuilding all pyramid LOD batches every rendered frame

`DuneVectorSpatialInstancing.SubmitBatches` visits every spatial cell every frame.
For every cell with LOD sources, `PrepareLodBatches` clears and rebuilds all active
LOD instance lists, recalculates each source's world bounds, and recreates render
parameters. Static pyramids do not require this work at frame frequency.

Cache the selected LOD batches until the camera moves far enough or crosses a spatial
cell, and update only affected cells. A designer-authored refresh distance or
interval belongs on `DuneVectorRuntimeSettings`.

Relevant code:

- `DuneVectorSpatialInstancing.SubmitBatches`
- `DuneVectorSpatialInstancing.PrepareLodBatches`

### 5. Reduce persistent loaded-world cost

The original `PreloadRadius = 3` scheduling path considered radius 4 and
`UnloadRadius = 4`, allowing up to 81 retained chunks. `ActiveRadius = 14` did not
control that result because scheduling capped it at `PreloadRadius + 1`. The
implementation now gives `ActiveRadius` its direct meaning and authors it to 3.

Separate authored radii for:

- Terrain render data
- Terrain collision
- Gameplay simulation
- Decorative content

Disable or omit distant `MeshCollider` components, stop ticking rings and enemies
outside their simulation radius, and avoid keeping decorative GameObject hierarchies
for content represented by instance data.

## Secondary gains

- Pool chunk roots, mesh buffers, cloud objects, and common gameplay visuals instead
  of repeated `new`/`Destroy` churn during sustained travel.
- Cache `Camera.main` in spatial instancing rather than resolving it per frame.
- Use squared distances in `DesertShrubField.Draw` to avoid square roots. This is a
  small gain compared with chunk staging and cached normals.
- Add profiler markers around chunk stages, height-grid generation, mesh upload,
  collider assignment, cloud creation, scenery creation, and shrub generation.
  Existing spatial-instancing markers are useful, but the critical streaming path
  currently has no markers.

## Recommended implementation order

1. Add streaming profiler markers and capture a fast-flight trace.
2. Replace repeated normal sampling with a padded height grid.
3. Split collision terrain from full chunk construction and add a time budget.
4. Add velocity-directed preloading and stagger collider assignment.
5. Cache spatial-instancing LOD selections and introduce separate content radii.

The first three changes should address the visible frame-time spikes. The LOD and
content-radius changes are the best candidates for improving sustained FPS after the
streamed area fills.
