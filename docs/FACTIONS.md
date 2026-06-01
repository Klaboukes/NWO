# NWO — Factions

> Setting and the Sundering: [LORE.md](LORE.md). Design intent: [OVERVIEW.md](OVERVIEW.md).
> Current mechanics & numbers: [MECHANICS.md](MECHANICS.md).
>
> Status: **direction doc**. Faction asymmetry is **[planned]** (ROADMAP Phase 9). The
> table doubles as an **implementation index** — each hook names the system it touches.

---

## Concept: the social-policy trees as a fixed cast

NWO does **not** ship "pick 1 of 40 historical civs." It ships a **small fixed cast of
hard-asymmetric factions** — the Civ5 social-policy trees made into nations after the
Sundering. Think hero-shooter roster, not encyclopedia: few pieces, each with a sharp
identity. That is the engine for **easy to learn / hard to master** and for replay value.

- **Match setup is player-chosen:** the player picks **how many factions (2–8)** play and
  **which**, then the rest are AI. (Today's single hard-coded "Barbarians" AI is the
  starting point for this — see Phase 9.2.)
- **v1 roster = 6 factions + the Reavers.** Two trees (**Piety**, **Aesthetics**) are
  **shelved** because they need systems we don't have yet (morale, influence).

The architecture is ready for this: identity already lives on `Player` and mutable state
on `Civilization` (see [ARCHITECTURE.md](ARCHITECTURE.md)), and units/buildings/techs are
data in `data/*.json`. Most faction work is **data + a few flags**, not new subsystems.

---

## v1 roster (shipping cast)

| Tree | Faction (working name) | Agenda | Signature passive | Unique unit (reskin) | Touches |
|---|---|---|---|---|---|
| **Tradition** | **The Dominion** | Restore the old order; one unbreakable seat of power | Fortress capital: bonus city HP, defense & regen; strongest when tall | **Palace Guard** — Spearman/Swordsman variant, +Defense | `City.HP`/defense, Walls effect, `CityWorkforceService` |
| **Liberty** | **The Free Settlements** | Spread the colony wide and free | Cheaper/faster settling; new cities start with a defender | **Pioneer** — Settler that can defend (Minuteman militia) | settle rules, `MinCityDistance`, production |
| **Honor** | **The Iron Pact** | Strength decides who is right | +Combat strength; units gain veterancy XP fast; heal in enemy land | **Legionary** — heavy Warrior/Swordsman | `CombatResolver`, Barracks XP effect |
| **Commerce** | **The Syndicate** | Everything — and everyone — has a price | High gold income; cheap rush-buy; can **hire Reavers** | **Mercenary** — gold-bought unit | `CivEconomyService`, `BuyCost`/`TryBuyProduction` |
| **Rationalism** | **The Cognate** | Knowledge is the only true power | Faster research; reaches each tech tier sooner | **Drone** — early ranged unit | `CivEconomyService` science/research |
| **Exploration** | **The Voyagers** | The frontier belongs to those who reach it | +Sight & +Movement; reduced terrain costs; ambush from recon | **Ranger** — fast Scout/recon cavalry | `Unit` sight/move, `GameState.MovementCost`, fog |

### The Reavers (NPC raiders)
The Sundering's outcasts; replaces the placeholder **"Barbarians"** AI. Hold ruins, raid
the settled lands, and — uniquely — **can be hired** by gold (The Syndicate's specialty).
Future: Reaver camps that spawn raiders, and contested ruins worth fighting over.
*Touches:* `AIController` (today's reactive opponent), `GameFactory.NewGame`/`PickAISpawn`.

---

## Shelved (planned, lore exists — mechanics don't)

| Tree | Faction | Why shelved | Future hook |
|---|---|---|---|
| **Piety** | **The Devout** — cult of the sealed Ark | Needs a **morale/fervor** system (no religion in scope) | Fanaticism: fight at full strength while wounded; cheap zealots |
| **Aesthetics** | **The Conservatory** — heralds of Earth's heritage | Needs a light **influence/soft-power** layer (no culture victory in scope) | Influence pressure: demoralize/contest enemy cities without battle |

Reframing note: both avoid the dropped systems on purpose — Piety becomes **morale**, not
religion; Aesthetics becomes **influence**, not a culture victory. They return when those
light systems exist.

---

## Design guardrails for faction asymmetry

- **One sharp idea each.** A faction is a *playstyle*, not a stat sheet. If you can't name
  its plan in one sentence, it's not done.
- **Asymmetry via data + small hooks.** Prefer JSON (unique-unit variants, passive
  parameters) and a single touch-point in a static service over bespoke subsystems.
- **No dead trees.** Every shipped faction must have a viable path to a victory condition
  (see [OVERVIEW.md](OVERVIEW.md)); asymmetry changes *how* you win, not *whether* you can.
- **Readable on the board.** Owner tint + faction identity must be legible at a glance
  (ties into Phase 7 visuals).
