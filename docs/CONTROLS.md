# Controls & Camera — Civ 5 Conventions

Players coming from Civ 5 must find the controls intuitively familiar. When adding or
changing UI/UX, match these conventions before inventing new ones.

---

## Mouse

- **Left-click** on own unit or city: select it (centers camera smoothly).
- **Left-click** on an empty/unselectable tile: deselect.
- **Left-click drag** on the map: pan the camera (grab-pan). A click that doesn't
  move past a small threshold still selects/deselects as above; only a drag pans.
- **Right-click** on a reachable tile while a unit is selected: execute the move.
- **Middle-mouse drag**: pan the camera.
- **Mouse hover** while a unit is selected: shows the path preview to the hovered tile — no button held.
- **Scroll wheel**: zoom (vertical). **Horizontal scroll** (touchpad two-finger sideways): pan left/right.
- **Touchpad**: tap = click, click-drag = grab-pan (same events as a mouse). Two-finger
  pan gesture pans; pinch gesture zooms (macOS-reliable; on Windows two-finger scroll
  arrives as wheel events and is handled there instead).

---

## Keyboard

- **WASD / arrow keys**: pan camera.
- **Enter**: end turn (or advance the end-turn queue if a prompt is active).
- **Tab**: cycle to next unit needing attention.
- **C**: cycle between your own cities (centers on each).
- **Space**: skip the current end-turn-queue item.
- **B**: Build city (when a settler is selected).
- **F**: Fortify selected unit (Found city if the unit is a settler — `B` is preferred).
- **H**: Fortify the selected unit until it heals to full HP, then auto-wake ("sleep until healed").
- **Esc**: cancel / deselect / clear notification.

**Space** skips a unit for the current turn (it leaves the end-turn queue and won't
re-block End Turn) without spending its action, so a skipped unit still heals.

---

## Camera Behaviour

- Selecting a unit or city (player click) centers the camera on it immediately and
  smoothly (exponential lerp).
- Manual keyboard/mouse-drag panning cancels any in-progress camera tween.
- While a unit animates, the camera follows it each frame (`_cameraTarget = _animPos`).
- When animation ends, the camera rests exactly on the destination tile.

---

## End-Turn Queue Progression

All future edits must respect this flow.

- When animation ends, **game state advances immediately**: `PruneAndShowEndTurnQueue()`
  is called right away, selecting the next unit/city in the queue (or triggering
  `ProcessTurn` if the queue is empty).
- **Camera centering to the next item is deferred** by `PostAnimCenterDelay` (0.5 s), so
  the player can see the destination before the camera jumps away. Implemented via
  `_postAnimCenterDelay` + `_deferredCenterPos` — the position is stored in
  `DeferOrCenter()` and applied once the timer expires in `_Process`.
- If the player presses Space or F during the delay, `AdvanceEndTurnQueue` clears the
  timer and deferred position so the next item centers immediately.
- Pressing End Turn for the first time (no animation pending) shows and centers on the
  first queue item immediately — no delay.
