# dreamloader - MIN: Analysis and Visualizer Direction

## Source and alignment

- Source: `dreamloader - MIN.mp3`
- Unity/FM0D event: `event:/Dreamloader_MIN`
- Source SHA-256: `B6C31AC6BE5CAC1428AD02D361FA4273EC8A3025861AD5924245E9CEBB54C2A1`
- Encoded format: MP3, 44.1 kHz, stereo, approximately 192 kb/s
- Container duration: 140.0395 seconds
- FMOD decoded duration: 140.0947 seconds
- Tempo: 180 BPM, with a very clear 90 BPM half-time interpretation
- Meter: 4/4
- First-grid offset: approximately 0.050 seconds
- Beat duration: 0.3333 seconds
- Bar duration: 1.3333 seconds

The onset autocorrelation has matching peaks at roughly 90 and 180 BPM. The 180 BPM grid is the useful visual grid because the post-1:57 material exposes the fast pulse directly, while 90 BPM remains useful for large structural accents and enclosing pressure fronts.

## Mastering and dynamics

- Integrated loudness: approximately -7.5 LUFS
- True peak: approximately -0.86 dBTP
- Loudness range: approximately 1.1 LU
- Mean sample level: approximately -8.8 dBFS

This is a deliberately dense, highly limited master. Macro contrast comes much more from arrangement, spectral density, dropouts, and onset rate than from large changes in overall level. A visualizer driven only by amplitude would therefore look too uniform. The authored profile instead uses section state, transient cues, beat-grid flare patterns, and deliberate glitch permissions.

## Spectral and rhythmic character

The low end is nearly continuous and strongly periodic. The upper bass and low-mid region supplies the track's forward pressure, while the 1-4 kHz region carries the most readable melodic and lyric information. High-frequency energy reaches well into the upper treble during the full sections, giving the song a bright, compressed, almost overexposed edge.

The arrangement repeatedly alternates between full-spectrum passages and reduced-density breathers. Two especially useful near-dropout transitions occur around 0:52 and 1:35.7. Those moments create stronger perceived impacts than the small loudness range suggests, so the profile pre-rolls reactor cues into the following returns instead of waiting for an amplitude detector.

The last section is not dramatically louder in average RMS than the preceding passage. What changes is density: more high-band activity, a much more exposed 180 BPM pulse, rapid subdivisions, and fewer perceptual gaps. That is why the post-1:57 choreography increases event frequency, flare velocity, camera accents, and alternating color-direction patterns instead of merely multiplying bloom.

## Arrangement map

| Time | Bars | Musical role | Visual treatment |
|---|---:|---|---|
| 0:00.00-0:10.72 | 0-7 | Opening pulse and spectral reveal | Restrained sky current, low pressure, sparse drone rays, no lyric glitch |
| 0:10.72-0:21.38 | 8-15 | First high-frequency / vocal lift | Treble availability opens, diagonal lyric rays, phrase glitches switch on |
| 0:21.38-0:32.05 | 16-23 | Expansion | Stronger bass fronts, road and structure response, brighter lyric glitches |
| 0:32.05-0:42.72 | 24-31 | Breakdown | Pressure and bloom contract; glitch permission switches off |
| 0:42.72-0:53.38 | 32-39 | Rebuild and vocal return | Glitch and HUD edge accents return; energy ramps into the dropout |
| 0:53.38-1:14.72 | 40-55 | First major bass entry | Reactor arrival, enclosing halo, mirrored horizontal drone flares, strong world pulses |
| 1:14.72-1:25.38 | 56-63 | Reduced-density break | Fewer fronts and particles, thinner current, glitch off |
| 1:25.38-1:36.05 | 64-71 | Second charge | Vertical rise, vocal glitch accents, increasing filament and treble response |
| 1:36.05-1:46.72 | 72-79 | Primary return | Reactor hit, road/structure pulses, fast vertical drone rays |
| 1:46.72-1:57.00 | 80-87 | Main charge | Increasing proximity, travel speed, bloom, and phrase-level glitches |
| 1:57.00-2:08.05 | 88-95 | High-frequency climax | Immediate reactor discharge; fast alternating flashes and beat accents from the drone |
| 2:08.05-2:18.72 | 96-103 | Final push | Maximum tier, thicker and faster currents, strongest alternating glitch/ray barrage |
| 2:18.72-end | 104+ | Release | One last multi-arc reactor flash, then rapid visual release to near-zero pressure |

## Lyric and glitch strategy

The profile treats the vocal-forward windows as explicit permission zones rather than leaving glitch enabled for the entire song. Glitch and HUD-border permissions turn off in the instrumental breathers and turn back on with the vocal/high-frequency returns. Within those windows, authored phrase cues create short slices rather than a constant distortion layer, preserving gameplay readability.

Composition changes receive separate anticipation and impact cues. The pre-impact sky/filament cue begins before the boundary, the impact drives a reactor or composition event, and a delayed treble burst adds the visible slice. This creates a three-part visual sentence: warning, hit, digital after-image.

Glitch displacement is capped at 0.0038 UV. Ordinary lyric phrases use approximately 0.0018-0.0023, while major returns and the final climax reach approximately 0.0035-0.0037. The center of the screen retains the existing protected region so the effect reads as expressive without burying the drone or aiming area.

The August 10 gameplay capture begins the track 7.487 seconds after video time zero. Its requested 1:01-1:22 and 1:43.5-1:54 glitch-line passages therefore align to the authored 180 BPM grid at 0:53.383-1:14.717 and 1:36.050-1:46.717. The first passage uses 14 slices per authored cue. The second begins at 20 slices and steps down to 2 by its final beat so the line density visibly fades out without changing the scene hue.

## Post-1:57 climax choreography

At 1:57.000 the profile switches to visual tier 5 and fires a three-arc reactor discharge. From there to 2:18.717:

- authored accents occur on the 180 BPM grid;
- alternating accent types create left/right camera roll and color variation;
- glitch pulses occur every other fast beat instead of remaining continuously opaque;
- two interleaved drone-anchored flare patterns alternate diagonal and horizontal directions;
- the patterns are offset by half a beat, producing a new ray burst about every 0.167 seconds;
- each pattern emits mirrored moving rays plus short held rays, so the drone appears to radiate flashing light;
- drone-anchored rays are limited to half of the available distance from the drone to the screen edge;
- flare travel speed rises to 1.55-1.75 times the song baseline;
- short 0.10-0.23 second lifetimes keep the barrage crisp at 180 BPM;
- down accents also drive road and structure response, while the rapid in-between accents emphasize bloom, filaments, treble particles, and camera motion;
- the final pre-release event emits four reactor arcs and a 30-line authored drone flash before the visual release.

The result is intentionally fast rather than merely bright: the viewer should perceive many discrete on-beat events pouring out of the drone, with the world, sky, camera, and HUD answering at different rhythmic scales.

## Authored asset summary

- 13 section states
- 248 sorted authored cues
- 5 repeating flare patterns
- Independent profile and playlist entry; the original `dreamloader - space.info` profile and event are not modified
- FMOD bank contains the new `event:/Dreamloader_MIN` event and its streaming audio
