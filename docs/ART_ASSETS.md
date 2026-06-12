# Art Asset Production Kit

How to replace NWO's procedurally-generated **painterly v2** art (Phase 7 V7.5)
with hand-authored or AI-generated assets. This is the production reference:
every filename a registry expects, its target spec, and a tuned generation
prompt. The current committed art is itself baked from the v2 generators (the
`generate-terrain-art` skill), so every asset already exists on disk — replacing
one is overwriting a PNG.

> The render pipeline is **drop-in by design.** Each texture registry prefers a
> real PNG at the path below over its synthesized fallback, with **no code
> change**. Unit/city bodies are never owner-tinted — the owner colour rides a
> separate banner sprite — so full-colour art of any palette just works, and
> `WorldRenderer` derives sprite scale from the texture so **any square size**
> renders correctly. Pipeline contract: the `add-art-asset` skill and
> [ROADMAP.md](ROADMAP.md) Phase 7.

## Target style

**Painterly / illustrated** — Civ 5-style hand-painted look: rich, textured
terrain and detailed unit illustrations, not flat pixels. The procedural v2 art
sets the baseline: one sun from the upper-left, hue-shifted shadows (cool/
saturated) and highlights (warm/desaturated), soft dark rims on sprites.

- Lock a **style bible first**: generate and approve *one* hero terrain tile and
  *one* hero unit in the final style **before** batch-producing the rest. Every
  other asset references these (a style-reference image, an IP-Adapter, or a trained
  LoRA). Cross-asset consistency — not single-image quality — is the hard part.
- Filtering is **Linear** in-engine (painterly v2); author at or above the target
  resolution and let the importer downscale.

### Free toolchain (no paid account; tuned for a 4 GB GPU)

This pipeline is tool-agnostic — any image generator works as long as you can pull
three levers (next table). A fully-free setup that suits a low-VRAM card:

- **Primary generator — Leonardo.ai free tier** (browser, server-side SDXL/Flux, so
  the 4 GB card is irrelevant). Has Style Reference, a Tiling toggle, and background
  removal — all three levers in one place. ~150 fast tokens/day.
- **Offline overflow — Stable Diffusion *Forge* + an SD 1.5 painterly checkpoint**,
  run locally with `--lowvram` + Tiled VAE. 512px native is a fine match for our
  small targets; quality is below server SDXL, so use it for batch overflow, not the
  hero images. (SDXL on 4 GB is borderline/slow; Flux won't fit.)
- **LoRA training (optional) — in the cloud, free**: Tensor.art or Civitai, trained
  on the two hero images. Don't train locally — 4 GB is too tight.
- **Touch-up — Krita + AI Diffusion plugin** (SD 1.5 inpainting at 512 runs fine on
  4 GB) for fixing a banner area, hands, or a stray background.

The three levers, per tool:

| Lever | Why | Leonardo.ai | Local SD (Forge/ComfyUI) |
| --- | --- | --- | --- |
| **Style consistency** | hold one look across ~50 assets | Style Reference / Image Guidance (feed a hero) | IP-Adapter, or a trained LoRA |
| **Seamless** (terrain only) | tiles must wrap | Tiling toggle | asymmetric/seamless tiling (ComfyUI node / Forge ext) |
| **Transparency** (sprites/icons) | cut-out billboards | built-in background removal | none — generate on a flat plain background |

> Transparency note: most generators don't output clean alpha. Generate units /
> cities / icons on a **plain flat background**; `tools/art/Process-Art.ps1` strips it
> via `rembg`. So the transparency lever is optional — the pipeline handles it.

## Global specs

| Property | Terrain tiles | Units & cities | Resource icons |
| --- | --- | --- | --- |
| Path | `assets/art/tiles/` | `assets/art/units/`, `assets/art/cities/` | `assets/art/resources/` |
| Size | 256×256 (512 then downscale OK) | 256×256 (any square works) | 64×64 (or clean multiple) |
| Format | RGB8/RGBA8 PNG | RGBA8 PNG | RGBA8 PNG |
| Background | Opaque, **seamlessly tileable**, top-down | **Transparent** | **Transparent** |
| View angle | Flat/top-down (the 3D prism does the 45° tilt) | Front-facing billboard (slight low-angle) | Front-facing icon |
| Outline | none needed | soft dark rim so it reads on any terrain | dark rim, reads on any terrain |

Naming is **exactly** the lowercase enum/id name: terrain = `TerrainType` lower
(`grassland.png`), units = the `data/units.json` id (`palace_guard.png`), resources
= `ResourceType` lower (`goldore.png`), cities = `city.png` / `capital.png`.

## Team colours — banners (landed in V7.5)

Unit/city bodies are **never owner-tinted**. Every unit and city always shows a
separate banner sprite (`assets/art/ui/banner.png`, via `BannerTextureRegistry` /
`WorldRenderer.EnsureBanner`) that gets `Modulate = owner.Color`. The banner
cloth is authored in whites/greys so the modulate dyes it cleanly.

Consequence for art production: author units/cities in **full colour**, any
palette — they render exactly as painted. Terrain and resource icons were never
owner-tinted.

## Generation workflow

1. Pick the style tool; generate + approve the two **hero** images (one terrain,
   one unit). Save them as the style anchor.
2. Batch-generate per category using the prompts below, prepending the shared
   **style prefix** and reusing the style anchor via the **style-consistency lever**
   (Style Reference / IP-Adapter / LoRA — see the levers table above). Turn the
   **seamless** lever on for terrain only.
3. Drop raw outputs into `tools/art/raw/<category>/` named by their target name.
4. Run `tools/art/Process-Art.ps1` (background removal → square-pad → resize to
   target → write to `assets/art/...`). See [Processing](#processing).
5. Open the project once (or run a headless import) so Godot regenerates `.import`
   siblings, then run the **run-checks** skill. The *look* still needs a human F5
   pass — headless can't judge visuals.

### Shared style prefix

Prepend to every prompt (tune once on the hero images, then keep fixed):

```text
painterly hand-painted game art, Civilization-5 style, rich textured brushwork,
warm naturalistic palette, soft directional lighting, high detail, no text,
no UI, no border frame
```

## Manifest — terrain tiles (12)

Top-down, **seamlessly tileable**, opaque, 256×256 (or 512 → downscale). The 3D
camera supplies the 45° tilt, so do **not** paint perspective into these.

| File | Prompt fragment |
| --- | --- |
| `grassland.png` | lush green grassland meadow texture, scattered wildflowers, seamless tile |
| `plains.png` | dry golden-green plains, short grass and patches of soil, seamless tile |
| `desert.png` | rippled sand dunes, warm ochre desert texture, seamless tile |
| `tundra.png` | cold tundra, mossy frost-bitten ground, sparse lichen, seamless tile |
| `snow.png` | smooth snowfield with subtle blue shadows and wind drifts, seamless tile |
| `forest.png` | dense forest canopy seen from above, varied treetops, seamless tile |
| `mountain.png` | rocky grey mountain peak, snow-capped ridges and scree, seamless tile |
| `ocean.png` | deep blue open ocean water, gentle swell, seamless tile |
| `coast.png` | shallow turquoise coastal water over sand, seamless tile |
| `savanna.png` | dry savanna grass, reddish soil, scattered acacia shrubs, seamless tile |
| `jungle.png` | thick tropical jungle canopy from above, vivid greens, seamless tile |
| `wetlands.png` | marsh wetlands, reeds and water pools, muddy banks, seamless tile |

## Manifest — units (19)

Transparent background, front-facing billboard, slight low-angle, dark rim.
Full-colour (team colour via banner — see above). Keep silhouette scale consistent
across the set (a footman fills a similar fraction of the 128px frame as a ship,
scaled sensibly).

| File | Prompt fragment |
| --- | --- |
| `scout.png` | lightly-armed scout with cloak and spyglass, exploring pose |
| `warrior.png` | early-era warrior with club/axe and small shield |
| `archer.png` | archer drawing a bow, leather armour |
| `spearman.png` | spearman with long spear and round shield |
| `horseman.png` | mounted light cavalry on a horse, lance raised |
| `swordsman.png` | armoured swordsman with sword and kite shield |
| `catapult.png` | wooden siege catapult, loaded throwing arm |
| `settler.png` | civilian settler caravan, covered cart and supplies |
| `worker.png` | labourer worker with pickaxe and tools |
| `palace_guard.png` | elite palace guard in ornate armour, halberd, regal |
| `pioneer.png` | rugged frontier pioneer with pack and survey gear |
| `legionary.png` | disciplined legionary, segmented armour, rectangular shield, gladius |
| `mercenary.png` | grizzled sellsword mercenary, mismatched armour, drawn blade |
| `drone.png` | small hovering recon drone, sleek sci-fi, glowing sensor |
| `ranger.png` | modern ranger / marksman with rifle, camouflage |
| `galley.png` | early wooden oared war galley, single sail, ram prow |
| `frigate.png` | multi-mast sailing frigate with cannon ports |
| `transport.png` | sturdy wooden transport ship, broad cargo deck |
| `galleon.png` | large ocean-going galleon, tall masts, deep hull |

## Manifest — cities (2)

Transparent background, billboard, dark rim, full-colour (banner-tinted).

| File | Prompt fragment |
| --- | --- |
| `city.png` | small fortified settlement, stone walls, a few buildings, banner pole |
| `capital.png` | grand capital city, palace and towers, prominent banner, larger |

## Manifest — resource icons (24)

64×64 (author 128 → downscale), transparent background, clean readable icon,
dark rim so it reads on any terrain. Single consistent icon family.

| File | Resource | Prompt fragment |
| --- | --- | --- |
| `horses.png` | Horses (strategic) | horse head icon |
| `iron.png` | Iron (strategic) | iron ore chunk / ingot icon |
| `wheat.png` | Wheat (bonus) | golden wheat sheaf icon |
| `fish.png` | Fish (bonus) | fish icon |
| `cattle.png` | Cattle (bonus) | cattle / cow head icon |
| `sheep.png` | Sheep (bonus) | sheep icon |
| `deer.png` | Deer (bonus) | deer / stag head icon |
| `stone.png` | Stone (bonus) | stacked stone blocks icon |
| `banana.png` | Banana (bonus) | banana bunch icon |
| `gems.png` | Gems (luxury) | cut gemstone icon |
| `goldore.png` | GoldOre (luxury) | gold ore nuggets icon |
| `silver.png` | Silver (luxury) | silver ore / bar icon |
| `silk.png` | Silk (luxury) | silk thread spool / bolt icon |
| `spices.png` | Spices (luxury) | pile of colourful spices icon |
| `dyes.png` | Dyes (luxury) | dye pots / pigment icon |
| `cotton.png` | Cotton (luxury) | cotton boll icon |
| `incense.png` | Incense (luxury) | smoking incense burner icon |
| `ivory.png` | Ivory (luxury) | ivory tusk icon |

> `None` is not an asset. There are 18 named resources; the 24 figure in the kit
> overview counts the bonus/strategic/luxury split loosely — author the 18 above.

## Processing

`tools/art/Process-Art.ps1` turns raw generations in `tools/art/raw/<category>/`
into engine-ready PNGs under `assets/art/`. Requires **ImageMagick** (`magick`) on
PATH; background removal additionally uses **rembg** (units/cities/resources only —
terrain stays opaque). Install rembg with the CLI extra (the plain `rembg` package
has no `rembg` command):

```powershell
pip install "rembg[cli]" onnxruntime
```

Both `magick` and rembg's `rembg.exe` must be on PATH. On Windows, pip installs
`rembg.exe` into the Python `Scripts` dir (e.g.
`%LOCALAPPDATA%\Python\pythoncore-3.x-64\Scripts`) — add that dir to PATH if pip warns
it isn't. The first rembg run downloads the ~176 MB u2net model to `~/.u2net`.

```powershell
# process one category
tools/art/Process-Art.ps1 -Category tiles
tools/art/Process-Art.ps1 -Category units
tools/art/Process-Art.ps1 -Category resources
tools/art/Process-Art.ps1 -Category cities

# or everything staged so far
tools/art/Process-Art.ps1 -Category all
```

The script square-pads (transparent for sprites/icons), resizes to the category
target, and strips backgrounds on sprites/icons. After it runs, regenerate Godot
`.import` files (open the editor once or run a headless import) and run
**run-checks**.

## Maintenance

If a registry path/name convention or the team-colour approach changes, update this
file alongside `add-art-asset/SKILL.md`, the texture registries, and `WorldRenderer`
/ `WorldOverlay`.
