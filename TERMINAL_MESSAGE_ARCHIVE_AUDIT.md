# Past-Message Terminal Audit

## Requested feature boundary

Add a second physical terminal in the courier hub, positioned opposite and facing the existing contract terminal. It should let the player browse previously received delivery messages through a UI that matches the existing delivery-message presentation.

This document is an audit only. It does not implement the terminal, interaction, archive, or UI.

## Current architecture

- `DuneVectorCourierGame.BuildHub()` creates the hub and its physical contract terminal entirely at runtime. There is no scene-authored hub terminal to duplicate.
- The existing terminal is stored in one `_terminal` transform, positioned at positive local forward using `WorldHubTuning.TerminalForwardOffset`.
- `UpdateHub()` measures distance to only that transform and maps `E` to the contract UI. `DrawHubHUD()` also knows about only that terminal and prompt.
- The contract terminal UI is immediate-mode GUI owned by `DuneVectorCourierGame`. It uses the terminal palette and layout fields in `WorldHubTuning`.
- Delivery-message playback is a separate immediate-mode GUI component, `DuneVectorDeliveryMessagePresenter`. It owns typewriter pacing, page transitions, transmission artifacts, typography, message palette, first-use guidance, and FMOD typing audio.
- Delivery message content is authored in `DeliveryMessageAsset` instances. `DeliveryMessageTuning.Sequence` is the authoritative runtime order; `ProgressionIndex` is informational when sequence order differs.
- Courier progress is saved as JSON to `DuneVectorCourierProgress.dat`. The relevant fields are `NextDeliveryMessageIndex`, `PendingDeliveryMessageIndex`, and `DeliveryMessageInputHintAcknowledged`.

## Findings

### 1. The second terminal needs its own interaction identity

Reusing `_terminalOpen` or `_terminal` for both terminals would make proximity prompts and close behavior ambiguous. The hub needs a second transform reference and an explicit UI/interaction mode such as `None`, `Contracts`, or `MessageArchive`. Only one terminal mode should be open at a time.

Recommended physical placement is the mirrored local position (`Vector3.back`) with a 180-degree local yaw so its screen faces the hub center and the original terminal. The archive terminal should reuse a shared terminal-geometry builder rather than duplicate the pedestal, screen, header, and mast construction.

### 2. Physical terminal dimensions are currently hardcoded

The existing pedestal, screen, header, mast positions, scales, and tilt are literal values inside `BuildHub()`. Adding a mirrored terminal will make those literals shared designer-facing presentation values. Before or during implementation, move all such geometry and placement controls into `WorldHubTuning`, then author them in `Assets/DuneVector/ScriptableObjects/Dune Vector Runtime Settings.asset` as required by the project rules.

The archive terminal's offset, interaction radius if distinct, labels, list layout, pagination, empty-state copy, and colors must also be fields on `DuneVectorRuntimeSettings` (within `WorldHubTuning` or the existing `DeliveryMessageTuning`) and authored in the same runtime settings asset. No separate tuning ScriptableObject is needed.

### 3. “Past” can currently be inferred, but it is not explicitly recorded

With the current non-looping configuration, resolved sequence indexes lower than `NextDeliveryMessageIndex` are messages the player has completed. `PendingDeliveryMessageIndex` must not be included until its first presentation completes.

This inference is sufficient for the requested feature as currently configured, but it has limits:

- Reordering or replacing `DeliveryMessageTuning.Sequence` can make old saves point at different content.
- Enabling `LoopSequence` makes one absolute progress index map repeatedly onto the same authored assets, so a unique receipt history cannot be reconstructed.
- Corrupt or missing sequence entries create gaps.
- Legacy saves deliberately set `NextDeliveryMessageIndex` from completed deliveries, even though those players may never have viewed the corresponding message assets.

For the current scope, use the existing indexes and `.dat` file rather than introducing a second save file. If exact historical receipts must survive sequence edits later, version the existing save data and add stable received message IDs to that same `.dat` file.

### 4. The presenter is reusable visually but not archive-ready behaviorally

`DuneVectorDeliveryMessagePresenter.Open(message, completed)` assumes linear first-time playback. Reaching the final page invokes a completion callback; the game currently uses that callback to advance progression and begin the return-to-base sequence.

Archive playback should reuse the presenter's rendering, typewriter, audio, and authored message pages, but it must have a non-progression completion path that returns to the archive list. It also needs an explicit cancel/back path. An archive read must never call `CompletePendingDeliveryMessage()` or change courier progression.

A clean boundary is:

1. Archive terminal opens a message index/list UI styled from `DeliveryMessageTuning`.
2. Selecting an unlocked entry opens the existing presenter in a replay context.
3. Finishing or backing out returns to the archive list.
4. Closing the archive returns control to the hub without teleport or progression changes.

### 5. “Matches the message UI” should use the message system, not the contract cards

The contract terminal and delivery-message UI have different visual languages. The requested archive should inherit the delivery-message transmission palette, typography, framed reading area, artifacts, and indicators. The index/list screen can extend that same palette with message rows and a selection state; it should not copy the orange contract-card grid.

The existing full-screen message renderer has no archive list, scroll state, selected index, back label, or empty state. Those controls need tunable fields on the existing runtime settings object.

### 6. Authored content and configured content are out of sync

There are 25 `Delivery Message ###.asset` files with stable IDs and progression indexes 0 through 24, but `Dune Vector Runtime Settings.asset` currently assigns only 13 entries to `DeliveryMessageTuning.Sequence`. An archive sourced from the authoritative sequence can therefore expose only those 13 entries.

Before implementation is considered complete, confirm whether messages 014 through 025 should be appended to the configured sequence. They should not be silently added as part of the terminal implementation because changing the sequence affects live progression and save interpretation.

## Recommended implementation shape

- Refactor physical terminal construction into a shared helper and build contract/archive terminals at mirrored, tunable transforms.
- Replace the contract-only boolean with an explicit terminal mode while preserving `IsTerminalOpen` as the aggregate HUD/input suppression signal.
- Choose the nearest interactable terminal and show a terminal-specific prompt; resolve equal-distance behavior deterministically.
- Add an archive controller/list state to `DuneVectorCourierGame` or a narrowly scoped archive presenter component.
- Extend `DuneVectorDeliveryMessagePresenter` with replay-safe completion and cancel behavior, keeping first-time progression callbacks separate.
- Derive unlocked archive entries from completed absolute indexes under the current non-looping policy, excluding pending and unresolved entries.
- Keep all persistence in `DuneVectorCourierProgress.dat` and all designer-facing tuning in `DuneVectorRuntimeSettings` plus its existing asset.

## Required validation

- At hub startup, both terminals are present, face one another, and do not obstruct spawn, upgrade area, containment, or each other's interaction radius.
- The prompt names the nearest terminal and `E` opens only that terminal.
- `Escape` closes the contract UI; archive playback backs to the list first, then closes from the list.
- With zero completed messages, the archive shows a tuned empty state and cannot open pending/future messages.
- Completed messages are listed in authored sequence order and replay with the same presentation, page breaks, input, and audio as first-time delivery messages.
- Replaying a message leaves `NextDeliveryMessageIndex`, `PendingDeliveryMessageIndex`, delivery counts, gold, and save contents unchanged.
- A pending message is not archived until first-time playback completes.
- Missing/null sequence entries are skipped safely and do not shift which absolute indexes count as complete.
- Opening either terminal suppresses gameplay HUD and input consistently; closing restores both.
- Existing fresh-save, legacy-save, delivery-completion, return-to-hub, and contract-terminal flows remain intact.

## Decision needed before implementation

Decide whether the configured delivery sequence should remain at 13 messages or include the 12 additional authored assets. That choice affects what “past messages” means for existing saves and should be made explicitly before archive implementation.
