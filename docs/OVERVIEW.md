# NWO — New World Order
## Turn-Based Strategy Game (Civilization 5-inspired)

---

## Vision

A turn-based 4X strategy game (eXplore, eXpand, eXploit, eXterminate) where one or more civilizations compete on a procedurally generated hex-grid map. The player manages cities, units, and a technology tree to achieve a victory condition before opponents do.

---

## MVP Goals

The MVP is a **single-player, one-civilization prototype** that proves the core loop works:

1. A playable hex-grid map is generated
2. The player can found and grow cities
3. The player can build and move military units
4. A basic AI opponent exists (reactive, not strategic)
5. One win condition is reachable (e.g., Domination — capture all enemy capitals)

The MVP does **not** need: diplomacy, religion, culture victories, multiplayer, or a polished UI.

---

## Pillars

| Pillar | Description |
|--------|-------------|
| Clarity | Every action and its consequence must be readable on screen |
| Depth | Simple rules that combine into complex decisions |
| Performance | 60 fps on mid-range hardware for maps up to 80×50 hex tiles |

---

## Out of Scope (MVP)

- Multiplayer
- More than 2 civilizations (player + 1 AI)
- Religion / Culture / Science victory conditions
- Full tech tree (only 5–8 techs in MVP)
- Diplomacy system
- Espionage
- City-states
- Naval units (land units only in MVP)

---

## References

- Civilization V (2K Games, 2010) — primary inspiration
- Freeciv — open-source reference implementation
