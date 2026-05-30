# NWO — New World Order
## Turn-Based Strategy Game (Civ5-inspired, Alpha-Centauri-flavoured)

---

## Vision

NWO is a turn-based 4X strategy game that takes Civilization V's mechanics and bends them
into a **faster, gamier spin-off** about **ideological warfare on a colony world**. It is
*not* a Civ5 clone and *not* a grand-strategy sim: it's **easy to learn, hard to master**,
built around decisive combat and strong faction identity rather than deep micro.

The setting is the Civ → **Alpha Centauri** bridge (full canon in [LORE.md](LORE.md)):
after a science victory, the ark *Exodus* settles the planet *Cradle*; generations later
the colony fractures — **the Sundering** — into nations that are Civ5's **social-policy
trees made flesh**. Each fights to impose its **New World Order** on the new world. The
title means exactly that: the political order being contested *on the literal new world*.

> **Status:** the Civ5-style **MVP core is built and playable** (Phases 0–6; see
> [ROADMAP.md](ROADMAP.md)). The spin-off identity below — factions, fast-warfare reframe,
> objective victory — is the **next direction (Phase 8)** and is flagged `[planned]`.

---

## What makes it a spin-off, not a clone (the four levers)

1. **Less expansion micro.** Replace infinite settler-spam with a **capped / contested
   settlement** model — few cities and **pre-placed sites** factions fight over. The game
   becomes *fighting over the map*, not *carpeting it*. `[planned]`
2. **Decisive, faster combat + a short clock.** A turn cap well below today's 500, and
   more lethal units, so battles and matches resolve quickly. `[planned]`
3. **Objective victory tied to lore.** *Establish the New World Order* — hold a majority
   of the planet's **key sites** (rival capitals, the crashed ark, terraform nexus) for N
   turns — alongside the existing Domination/Score wins. `[planned]`
4. **Light-but-real economy & diplomacy.** Gold as leverage (rush-buy, **hire Reavers**)
   and simple alliances / non-aggression — meaningful, but never the main event. `[planned]`

Faction asymmetry (a fixed cast of ideological nations, player-chosen 2–8 per match) is the
depth-and-replay engine — see [FACTIONS.md](FACTIONS.md).

---

## Pillars

| Pillar | Description |
|--------|-------------|
| Identity | Hard-asymmetric factions; each is a distinct, one-sentence playstyle |
| Tempo | Short, decisive games — warfare resolves fast, matches don't drag |
| Clarity | Every action, faction, and consequence is readable on screen at a glance |
| Performance | 60 fps on mid-range hardware for maps up to 80×50 hex tiles |

---

## Scope

### In scope (built — the Civ5 core)
A single-player prototype proving the loop works, **already shipped**: procedural hex map;
found & grow cities; build/move/fight units; tech tree & economy; a competent reactive AI;
Domination + Score victories; save/load; HUD. Full detail in [ROADMAP.md](ROADMAP.md).

### In scope (next — the spin-off identity, `[planned]`)
- **Factions:** a fixed asymmetric cast (v1: 6 + the Reavers), player-chosen count per
  match — [FACTIONS.md](FACTIONS.md).
- **Fast-warfare reframe:** the four levers above (capped settlement, decisive combat,
  objective victory, short clock).
- **Light diplomacy/economy:** alliances, non-aggression, gold-as-leverage.
- **Tech-regression arc:** salvaged-primitive → recovered colony tech → new planetary tech
  (existing antiquity units are the first tier; later tiers go sci-fi) — [LORE.md](LORE.md).

### Out of scope (for now)
- **Religion, ideology, and culture-victory systems.** The Piety and Aesthetics factions
  are **shelved/planned** until light morale/influence layers exist — [FACTIONS.md](FACTIONS.md).
- Multiplayer; espionage; full 50+ tech tree; map editor; mod support.

---

## References

- **Sid Meier's Alpha Centauri** (Firaxis, 1999) — the ideological-factions-on-a-colony
  north star.
- **Civilization V** (2K Games, 2010) — the mechanical base we spin off from.
- Freeciv — open-source reference implementation.
