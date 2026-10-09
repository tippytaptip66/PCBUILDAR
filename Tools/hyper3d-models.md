# Recreating the component models with Hyper3D (Rodin)

Hand-off for a Claude Code session that has the `hyper3d` MCP tools loaded.

## Method that works: reference image + short prompt
Text-only prompts gave unreliable parts (a four-fan GPU, an all-green CPU, a 20 cm tall graphics card). Every part
below was generated **from a reference image** in `Tools/hyper3d-references/` plus a one-line prompt, at
**Gen-2.5-High**. The references are clean three-quarter renders with real proportions, layouts and colours, so
Rodin copies the shape instead of guessing it.

1. `rodin_create_uploads` for the PNG, then HTTP `PUT` it to the returned URL.
2. `rodin_generate` with `reference_upload_ids`, the prompt below, `geometry_file_format: fbx`, `tier: Gen-2.5-High`.
3. Download the `base_basic_pbr.fbx` variant (it has the textures embedded; `base.fbx` has none).

## Output rules (all parts)
- Save as `Assets/_Project/Art/Models/Components/<Folder>/<component id>.fbx` — names must be kept exactly.
- Scale doesn't matter: the importer scales to real size and orients each part for its slot.
- Then run **BuildAR ▸ Setup ▸ Import All Models** in Unity (parts, then the assembly case).

## Parts
| id | Folder | Reference | Prompt | Model Rotation Euler |
|---|---|---|---|---|
| `cpu` | CPU | `cpu.png` | Desktop CPU processor: thin square green substrate with a raised brushed silver metal heat spreader lid covering most of the top, engraved text on the lid, small gold triangle in one corner. Photorealistic. | z 180 |
| `cpu_cooler` | Cooling | *(text only)* | Single-tower CPU air cooler: a tall rectangular stack of thin silver aluminium fins standing upright on a small square copper base plate, four copper heat pipes rising from the base up through the fins, one black 120 mm fan clipped to the front face of the fin stack. Photorealistic product photo. | y 180 |
| `gpu` | GraphicsCard | `gpu.png` | Desktop graphics card: long, low black shroud with three identical fans in a row, grey metal backplate, silver I/O bracket with display ports at one end, gold PCIe edge connector on the bottom edge, 8-pin power socket on the top edge. Photorealistic. | — |
| `ram_ddr5` | RAM | `ram_ddr5.png` | DDR5 desktop RAM stick: silver-grey aluminium heat spreader with a black label, green PCB edge, gold contact fingers along the bottom with a key notch near the middle. Photorealistic. | z 180 |
| `ram_ddr4` | RAM | `ram_ddr4.png` | DDR4 desktop RAM stick: black aluminium heat spreader with a red label, green PCB edge, gold contact fingers along the bottom with an off-centre key notch. Photorealistic. | z 180 |
| `ssd_m2` | Storage | `ssd_m2.png` | M.2 2280 NVMe SSD: thin black circuit board with black controller and NAND chips, gold M-key edge connector at one end, gold half-circle screw notch at the other end. Photorealistic. | z 180 |
| `psu` | PowerSupply | `psu.png` | ATX desktop power supply: black metal box, 120 mm fan with a wire grille on the top face, honeycomb vent with power switch and power inlet on one end, silver specification label on the side. Photorealistic. | x 180 (fan down) |
| `motherboard` | Motherboard | `motherboard.png` | ATX desktop motherboard: black circuit board, silver CPU socket, four DIMM slots beside it, two PCIe x16 slots, M.2 heatsink, dark grey VRM heatsinks and rear I/O cover, chipset heatsink, 24-pin power connector on the right edge. Photorealistic. | — |
| `cable_24pin` | Cables | `cable_24pin.png` | ATX 24-pin power cable: black braided sleeved cable with a black 2 x 12 pin plastic connector with a latch clip at each end. Photorealistic. | — |
| `io_panel` | InputOutput | `io_panel.png` | Motherboard rear I/O panel: black metal shield plate with blue and black USB-A ports, USB-C, silver Ethernet port, HDMI, DisplayPort, gold Wi-Fi antenna connectors and coloured audio jacks. Photorealistic. | z 180 |
| `pc_case` | Case | *(same file as the assembly case)* | — | — |

Assembly case: `Assets/_Project/Art/Models/Assembly/assembly_case.fbx`, Gen-2.5-High, text only — "Empty black ATX
mid-tower PC case seen from its open left side. The left side panel is completely removed, no glass. Inside: a flat
black motherboard tray covering the whole right-hand wall with small brass standoffs, a rectangular rear I/O shield
opening at the top of the back wall and seven horizontal expansion slot covers directly below it on the back wall, a
solid black PSU shroud along the bottom, cable cutouts with rubber grommets next to the tray. Matte black
powder-coated steel inside and out, front mesh panel. No fans, no drives, no cables, no components. Studio product
photo." Describing the tray, the rear I/O opening and the slot covers is what gets a usable interior.

## Known limits of generated models
- **Rodin returns a single welded mesh.** The Explode view moves the prefab's direct children apart, so a
  one-piece model won't come apart. Split it in Blender (Separate ▸ By Loose Parts) if that matters.
- **Up/down isn't consistent between generations.** The importer orients each part from its shape; the
  `Model Rotation Euler` column above is what these particular files need. **A regenerated part can need a
  different value** — check it in Practice Assembly (and the 3D viewer) and adjust the field on its component asset.
- The GPU file is laid out as a mirror image of the ideal, so with its bracket at the rear and fans down (both
  visible) the small 8-pin socket ends up on the board side. Not worth a mirrored prefab.
- The importer caps how far a part may stick out of the board (GPU 13 cm, cooler 15.5 cm) and puts each part's
  contact side where the placeholder's was.
- Import Assembly Case Model stands the case up, removes its near side panel, darkens it to painted steel, fits its
  motherboard tray around the built-in board (board in the tray's rear-top corner, next to the I/O cut-out and slot
  covers; the case comes out about ×1.12 bigger for that) and moves the PSU bay into its basement.
- **Fans and RGB are placed by measurement.** The GPU's three fans, the cooler's fan, the case's rear fan and the
  GPU's light bar are given on those component assets (Model Fans / Model Light Bars) in each file's own mesh
  space, measured from these exact files: fan centres by fitting the rims on a height map, depths from where the
  model's own fan stops and the fins, backplate or case wall start. The case's rear opening is a double wall — an
  outer grille about 1.5 % of the case height deep and an inner plate at about 10 % — so its fan is **Inset** 0.105
  and **Frame**d, mounted on the inside of that plate. **A regenerated GPU, cooler or case needs them
  measured again** (or the lists emptied), or the cut lands on the wrong part of the model. The PSU's fan (a grille
  facing the floor) is left as modelled.
- Generated colour maps for a black case are flat grey with baked-in smudges; the importer paints the case's detail
  itself (see the guide's *Case detail*), keyed to the fitted case's layout rather than to the texture.
- Each generation spends Hyper3D credits (High tier costs more than Medium).
