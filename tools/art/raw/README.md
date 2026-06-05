# Raw art staging

Drop raw AI-generated / hand-drawn images here, **named by their final target
filename**, then run the processor — it cuts backgrounds (sprites/icons), square-pads,
resizes to spec, and writes engine-ready PNGs into `assets/art/`.

```powershell
tools/art/Process-Art.ps1 -Category tiles      # or units / cities / resources / all
```

After processing: open the Godot project once (regenerates `.import` files), run the
**run-checks** skill, then F5 to eyeball it. Full prompts + specs live in
[docs/ART_ASSETS.md](../../../docs/ART_ASSETS.md).

## Folders → manifest section → engine destination

| Stage here | Manifest section | Processed into | Background |
| --- | --- | --- | --- |
| `tiles/` | Terrain tiles (12) | `assets/art/tiles/` | opaque, seamless |
| `units/` | Units (19) | `assets/art/units/` | transparent (rembg) |
| `cities/` | Cities (2) | `assets/art/cities/` | transparent (rembg) |
| `resources/` | Resource icons (18) | `assets/art/resources/` | transparent (rembg) |

## Required filenames

Names must match the resolver **exactly** (lowercase enum/id). Get them wrong and the
registry won't pick the art up.

- **tiles/**: `grassland plains desert tundra snow forest mountain ocean coast savanna
  jungle wetlands` (`.png`)
- **units/**: `scout warrior archer spearman horseman swordsman catapult settler worker
  palace_guard pioneer legionary mercenary drone ranger galley frigate transport
  galleon` (`.png`)
- **cities/**: `city` `capital` (`.png`)
- **resources/**: `horses iron wheat fish cattle sheep deer stone banana gems goldore
  silver silk spices dyes cotton incense ivory` (`.png`)
- **ui/** (optional, processed manually — not via this script): `banner.png` overrides
  the synthesized owner-colour banner shown beside full-colour unit/city art.

## Notes

- **Units/cities look mud-tinted until you stage full-colour art.** The renderer now
  detects real PNGs and switches from whole-sprite tinting to an owner-colour *banner*
  (see docs/ART_ASSETS.md). Placeholders still tint whole — so a half-finished mix is
  fine; each real PNG flips itself over as you add it.
- This `raw/` tree is a scratch area — the processed output under `assets/art/` is what
  ships. Keep or delete raws as you like; only `.gitkeep`/this README are tracked.
