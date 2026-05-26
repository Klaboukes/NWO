# Claude Code Instructions

## Git & GitHub

- Auto-commit and push to `origin/main` when a roadmap phase is marked complete.
- Also commit and push on explicit user request ("commit", "push", etc.).
- Use the git identity already configured in this repo (`Klaboukes` / `barthoukes@gmail.com`).
- Follow the standard commit format: concise subject line, blank line, short body if needed, trailing `Co-Authored-By` line.

## UI/UX — Camera & Game Progression Flow

These rules govern how the camera behaves and how the end-turn queue advances. All future edits must respect this flow.

### Camera centering
- Selecting a unit or city (player click) centers the camera on it immediately and smoothly (exponential lerp).
- Manual keyboard/mouse-drag panning cancels any in-progress camera tween.

### Unit movement
- While a unit animates, the camera follows it each frame (`_cameraTarget = _animPos`).
- When animation ends, the camera rests exactly on the destination tile.

### End-turn queue progression
- When animation ends, **game state advances immediately**: `PruneAndShowEndTurnQueue()` is called right away, selecting the next unit/city in the queue (or triggering `ProcessTurn` if the queue is empty).
- **Camera centering to the next item is deferred** by `PostAnimCenterDelay` (0.5 s), so the player can see the destination before the camera jumps away. This is implemented via `_postAnimCenterDelay` + `_deferredCenterPos` — the position is stored in `DeferOrCenter()` and applied once the timer expires in `_Process`.
- If the player presses Space or F during the delay, `AdvanceEndTurnQueue` clears the timer and deferred position so the next item centers immediately.
- Pressing End Turn for the first time (no animation pending) shows and centers on the first queue item immediately — no delay.
