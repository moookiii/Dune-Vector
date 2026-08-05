# Exodia / 825 HP — FMOD Studio authoring still required

The Unity callback, fallback section resolver, cue profile, and missing-marker report are implemented. The repository contains built banks only, so the following edits must be made in the external FMOD Studio project and the banks rebuilt.

On the event that currently builds as `event:/Shadows on the Mesa`, align a 168 BPM, 4/4 tempo marker to the first downbeat at approximately `00:00.151786`, then add these named timeline markers:

| Marker | Timeline position |
|---|---:|
| `EXODIA825_B000` | `00:00.152` |
| `EXODIA825_B016` | `00:23.009` |
| `EXODIA825_B024` | `00:34.438` |
| `EXODIA825_B028` | `00:40.152` |
| `EXODIA825_B032` | `00:45.866` |
| `EXODIA825_B048` | `01:08.723` |
| `EXODIA825_B056` | `01:20.152` |
| `EXODIA825_B060` | `01:25.866` |
| `EXODIA825_B064` | `01:31.580` |
| `EXODIA825_B104` | `02:28.723` |
| `EXODIA825_B116` | `02:45.866` |
| `EXODIA825_B120` | `02:51.580` |
| `EXODIA825_B136` | `03:14.438` |
| `EXODIA825_B148` | `03:31.580` |
| `EXODIA825_B150` | `03:34.438` |

Do not add `EXODIA825_B152`; that theoretical grid point is beyond the source duration. Natural completion is handled through the FMOD stopped callback and polled playback state. Until banks containing the markers are installed, Unity evaluates the same boundaries from FMOD timeline milliseconds and zero-based track bars—never Unity time.
