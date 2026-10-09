# PCBuildAR: Learn, Identify, Assemble — project guide

Unity 6.5 · AR Foundation 6.5 + Google ARCore (Android) · UI Toolkit · Inference Engine 2.6 (ML)

---

## 1. Quick start

1. Let Unity finish compiling, then run **BuildAR ▸ Setup ▸ Run Full Setup**. It:
   - creates the folders below
   - adds the **AR Background Renderer Feature** to your URP renderers (without it the camera feed is black)
   - builds placeholder 3D models (primitive shapes with hotspots and explode parts)
   - generates sample content: 11 components, 13 lessons, 16 quiz questions, a 10-step assembly guide
   - builds 4 scenes and puts them in Build Settings, with Boot first
2. Open `Assets/_Project/Scenes/BuildAR_Boot.unity` and press **Play**. You can also press Play in any BuildAR scene; `DevBootstrap` loads the managers for you.
   - In the Editor, the AR scanner uses the **SimulatedComponentDetector**: a fake GPU is "seen" after about 2.5 s. To switch parts, right-click the component header and choose **Next Label**.
3. Run **BuildAR ▸ Setup ▸ Apply Android AR Player Settings** (sets Portrait, IL2CPP, ARM64). Then check **Project Settings ▸ XR Plug-in Management ▸ Android ▸ ARCore**, and build.

**App icon and brand:** `Art/Branding/Resources/BuildAR_AppIcon.png` is applied automatically when imported: it becomes the default and Android (adaptive, round, legacy) icon and the splash logo on a `#051332` background. To change it, replace the PNG, or run **BuildAR ▸ Setup ▸ Apply App Icon + Splash**. Its gradient (`#B2F1FF → #5456B6 → #051332`) is drawn by `GradientBackground` on the Welcome screen, the Home "current step" card, the Progress cards and the quiz summary.

Setup never overwrites existing assets or prefabs. To regenerate one, delete it first. For scenes, it asks before rebuilding.

### Scenes
| Scene | Contains |
|---|---|
| `BuildAR_Boot` | GameManager, ProgressManager, AudioManager, ComponentDatabase (persist) |
| `BuildAR_App` | Welcome, Home, Learn, Component Detail, Progress, Quiz screens (UI Toolkit) + `ModelViewerStage` |
| `BuildAR_ARScanner` | AR Session, XR Origin, detectors, scanner UI |
| `BuildAR_VirtualAssembly` | Case prefab with SnapSlots/CablePorts, assembly camera, tray + instructions UI |

---

## 2. Where to put 3D models

```
Assets/_Project/
├─ Art/
│  ├─ Models/
│  │  ├─ Components/
│  │  │  ├─ CPU/            ← cpu.fbx + its textures + materials
│  │  │  ├─ Motherboard/
│  │  │  ├─ RAM/
│  │  │  ├─ Storage/
│  │  │  ├─ GraphicsCard/
│  │  │  ├─ PowerSupply/
│  │  │  ├─ Cooling/
│  │  │  ├─ Case/
│  │  │  ├─ Cables/
│  │  │  └─ InputOutput/
│  │  └─ Assembly/          ← the PC case model used in Virtual Assembly
│  ├─ Materials/  Textures/  Fonts/
├─ Prefabs/
│  ├─ Components/           ← YOUR prefab per component (wraps the raw model)
│  ├─ Assembly/             ← case prefab with SnapSlots + CablePorts
│  └─ Placeholders/         ← generated stand-ins (safe to delete once replaced)
└─ Data/Resources/Components/  ← ComponentDefinitionSO assets (model3DPrefab field points to your prefab)
```

**Raw model files** (`.fbx`, `.blend`, `.glb`) go in `Art/Models/...`. **Do not** reference them directly. Always wrap each one in a prefab in `Prefabs/Components/`.

### The quick way: drop the files in and run the importer
Put each `.fbx` (or `.obj`) in its category folder and run **BuildAR ▸ Setup ▸ Import Component Models** — or
**Import All Models**, which also re-imports the assembly case afterwards, so you can't forget it. For every file it:

- applies mobile-friendly import settings (no animation/cameras/lights, medium mesh compression, GPU-only meshes — a part with fans stays
  readable so they can be cut out, and the prefab uses the GPU-only cut-up copy);
- gives the part its **real colours**: textures packed inside the `.fbx` (as Hyper3D/Rodin and most photogrammetry
  exports do) are unpacked into a `Textures` folder beside the model — colour, normal map, and metallic + roughness
  packed the way URP reads them — and the model is put on one **URP/Lit** material in `Materials`. Big parts get
  2048 px textures, small ones 1024. A model without embedded textures keeps its own materials, converted to URP/Lit
  so nothing renders pink;
- **turns the model to fit its slot** from its shape: the longest side runs across the board, RAM and graphics
  cards stand on their edge connector, a tower cooler stands up. Shape can't tell top from bottom, so if a part
  comes out upside down, set its component's **Model Rotation Euler** (usually `z: 180`) and run the importer
  again — the Hyper3D RAM sticks and SSD have this set;
- **scales the model to the part's real size** (a GPU exported in centimetres becomes 0.28 m wide), capped so a
  badly proportioned model can't stick out of the case, and puts a board-mounted part's contact side (pads, edge
  connector, cooler base) exactly where the placeholder's was, so it lands on the board;
- **spins the fans and adds RGB** where the component asks for it (see below);
- saves `Prefabs/Components/<component id>.prefab` and assigns it to that component's **Model 3D Prefab**.

**Spinning fans and RGB lights.** A component's **Model Fans** list tells the importer where each fan sits on the
model file. Generated models bake a fan into their one-piece mesh as a lump that can't turn cleanly, so the importer
cuts that cylinder out and builds a real fan in its place under a `Fan n` child: a `Rotor` with swept, frosted blades
that spins (`SpinningPart`, capped per frame so blades never strobe backwards on a slow phone) and picks up the RGB
colour, a closed `Well` behind it (or a whole square `Frame` with motor struts, for a fan that stands on its own —
**Frame** ticked), and a glowing `RGB Ring`. **Inset** mounts a fan behind a wall that is kept (the case's rear fan
sits behind its grille). **Model Light Bars** adds glowing bars. All of it is given in the model file's own space —
0–1 across the mesh's bounds, sizes as fractions of its largest side — so it survives any Model Rotation Euler. The
cut-up mesh and fan meshes are saved beside the model as `<name>_fx.asset`. The Hyper3D GPU (three fans + edge light
bar), CPU cooler (one fan) and PC Case (rear exhaust fan) have this set (the RAM sticks are plain, with no RGB); the PSU fan
isn't spun (it's a grille facing the floor). If a regenerated model moves its fans, re-measure them, or empty the
list. Every LED shares one colour clock (`RgbGlow`), so the whole build cycles through the rainbow together.
Import Assembly Case Model gives the assembly case the PC Case component's fans when it's the same model file, and
adds an RGB strip along the top of the opening and a weak colour-cycling spot light washing in from above it (its
`Lighting` child — weak on purpose: real lights fade with the square of the distance, so one inside the case would
burn out whatever is beside it), plus `CaseGlow`, which adds a Bloom volume and turns post-processing on for the
current camera so the LEDs glow (tonemapping and vignette stay off, so the AR camera feed keeps its colours, and
Stop NaN is on so one bad pixel can't bloom into a blotch).

**I/O panels.** A component's **Model Panels** list (see `ModelIoPanel`) builds I/O onto a case model, laid on its
surface where a line along the panel's facing meets it: **Rear Ports** fills the case's rear I/O cut-out with a
shield of real-size ports (Wi-Fi antennas, BIOS button, HDMI, DisplayPort, USB-C, red/blue/black USB-A, Ethernet
with link LEDs, colour-coded audio jacks, optical out), and **Top Buttons** puts the front I/O on top: power button
with an RGB ring and power symbol, reset, power/drive LEDs, headset jack, USB-C and two USB-A, on a bezel with an RGB
edge light. The PC Case component has both, and the assembly case gets them as the same model. The panels are
modelled from primitives in `IoParts` and merged into `Art/Models/Generated/IO_Panels.asset` (one renderer each);
the built-in motherboard's rear edge carries the same port column under a full-height I/O cover with an RGB line.
In the 3D viewer the panels have hotspots (power button, front USB and audio, rear ports).

**Power on (Practice).** In Virtual Assembly the PC starts switched off: fans stand still, RGB LEDs and the case's
spot light are dark, and the panels' indicator LEDs (power, drive, network) are off (`IndicatorLeds`, added at
runtime to renderers wearing an `M_IO_LED_*` / `M_LED_*` material). Once every part is installed and every cable
connected, the last step is to switch it on: the power button's ring pulses cyan, and tapping it (`PowerButton`, on
the top panel's `Power Ring`; a tap within about 34 dp counts, on release, so a drag that starts there still turns
the view) calls `AssemblyManager.PressPower`. The fans spin up over 1.5 s, the lights fade in, and the Build
complete card follows. Pressing it again shuts the PC down (fans coast to a stop); pressing it before the build is
finished only says how many steps are left. "Show me" on that step tilts the view down onto the case's top. The
power state isn't saved: each visit starts off. Outside Virtual Assembly (3D viewer, scanner) there's no
`AssemblyManager`, so `AssemblyManager.HasPower` is always true and fans and LEDs run as before.

**Case detail.** A generated case's colour map is flat and smudged, so Import Assembly Case Model paints real-case
detail into its textures (`BakeCaseDetail`), working out what each texel is from where it lands on the fitted case:
the smudges evened out under a fine powder-coat grain, perforated dust-filter mesh on the front and top panels,
vent slots and a raised BuildAR logo on the PSU shroud, rubber cable grommets with tie-down slots on the tray
beside the board (where the cables come through), and a filtered intake vent in the floor under the PSU's
downward-facing fan — colour, normal map and smoothness together, so the holes catch the light. The PC Case component,
the same model, is given the same textures. Run **Import All Models** after regenerating the case so both update.

It matches a file to a component by name first (`gpu.fbx`, `rtx4070.fbx` → Graphics Card), then by the folder it
sits in. Anything it can't place is listed in the Console for you to assign by hand. Re-running it is safe: it
replaces the prefab and leaves everything else on the component alone.

**File formats.** `.fbx` is the safe choice — it's self-contained and imports anywhere. `.obj`, `.dae`, `.glb`
and `.gltf` also work (glTF needs `com.unity.cloud.gltfast`). **`.blend` works only on a computer that has
Blender installed**, because Unity converts it by running Blender in the background; on any machine without it,
the model silently imports as nothing. If you build on one machine and it's only you, `.blend` is fine —
otherwise export FBX.

Exporting FBX from Blender: *File ▸ Export ▸ FBX*, with **Limit to: Selected Objects**, **Apply Modifiers** on,
**Forward −Z / Up Y** (the defaults), and **Apply Scalings: FBX All**. Keep detail meshes as separate objects —
the Explode view moves direct children apart, so a fully joined mesh can't come apart.

Read the checklist below anyway — the importer fixes scale and materials, but it can't fix a model that's a single
welded mesh (the Explode view needs separate children) or one with 500k triangles.

### Model checklist (for each component)
| Rule | Why |
|---|---|
| **1 unit = 1 metre, real size.** A CPU is about 0.04 m, a RAM stick about 0.133 m, a GPU about 0.28 m | Needed for AR placement, snap slots, and camera framing |
| **Pivot at the geometric centre** (in Blender: *Origin to Geometry*; or use an empty parent in the prefab) | Virtual Assembly snaps the pivot onto the SnapSlot |
| **Split into separate child meshes**, e.g. GPU → Shroud, Fans, PCB, Backplate, Bracket | The Explode view moves direct children apart |
| Mobile budget: about 5–30k triangles per part, 1–2 textures at ≤ 2048 px (ASTC) | Keeps phones at 60 fps |
| Format: **FBX** (built in). For glTF/GLB, install `com.unity.cloud.gltfast` | — |
| URP materials: use *Universal Render Pipeline/Lit*. Use *Edit ▸ Rendering ▸ Materials ▸ Convert* for imported ones | Avoids pink materials |

Where to get models: build your own in Blender, use **Fab**, the **Unity Asset Store**, or **Sketchfab** (download a CC-BY or CC0 licence and credit the author). Photogrammetry of real parts also works well.

### Build the component prefab
1. Drag the FBX into the scene. Create an empty parent named after the part (e.g. `GPU`), and put the model under it. Its direct children are the parts that explode.
2. On the root, add **ExplodableModel** and set `distance` (for example 0.03–0.06 m). Optionally add **ExplodePart** to a child to set its direction or distance.
3. For every feature to teach, add an empty child **under the part it belongs to** with **ModelHotspot**:
   - Place it on the surface: pins, heat spreader, notch, locking clip, port.
   - Point its **blue Z axis out of the surface**, so the marker dims when that side faces away.
   - Fill in `title` and `description`. Hotspots appear as tappable dots in the 3D viewer and as rows in the Parts tab.
4. Save it as `Prefabs/Components/GPU.prefab`.
5. Select the component asset (`Data/Resources/Components/gpu.asset`) and set **Model 3D Prefab** to your prefab.

Compare with the generated `Prefabs/Placeholders/PH_GPU.prefab`, which is built exactly this way.

### Replace the assembly case — the quick way
Put your case model in `Art/Models/Assembly/` and run **BuildAR ▸ Setup ▸ Import Assembly Case Model**. It swaps
the **Visuals** child of `PH_AssemblyCase` and fits the case around the slot layout — the built-in motherboard and
the slots and ports on it keep their shape, so parts still snap where they should:

- keeps the model's own up axis (Z-up exports stay upright) and turns it so its **open side faces the learner**
  (local −Z). The open side is read from the mesh — it's the big side with the least panel in it;
- **takes the near side panel off**: faces lying flat against the open side (panel, frame, glass) are removed,
  what's left is cut clean just inside the opening, and every face gets a back face so the model's thin shell
  walls look solid from inside. The result is saved as `<name>_open.asset` beside the model;
- finds the **motherboard tray** by looking into the case, and scales and moves the case so the built-in
  motherboard sits in the tray's rear-top corner with 1 cm to spare — next to the rear I/O cut-out and the slot
  covers, as in a real case. Real cases keep the tray a few centimetres inside the far wall;
- moves the **PSU bay** (and its cables) into the case's basement and the **front-panel cable** to its front, then
  recentres everything so the origin is the case's floor centre, as the scene and AR placement expect. The PSU
  stands on the case's **measured floor** and against the rear wall's inside face (both found by looking through
  the mesh; the measured floor is lined up with the layout's floor surface, 4 mm up) — generated cases stand on feet and can have thick rear walls, so the model's outer bounds would sink
  the PSU through the floor and into the wall;
- gives each board header a **cable route** (`CablePorts/Route_*`, see `CablePort.routeExit`): the grommet beside
  the board nearest its height, or over the board's top edge for CPU power. Connected PSU cables then leave the
  PSU's panel, run behind the tray and come out through it, like a tidy real build; the front-panel lead runs low
  along the tray. Each cable is a flat loom of sleeved wires as wide as its connector (24-pin: 12 × 2). Before it's
  connected, a cable hangs from where it comes from (the PSU's panel, the board behind the tray, the front panel)
  to its loose plug (`CableBuilder.BuildSlack`); once plugged in, both give way to the routed cable
  (Restore Placeholder Assembly Case removes the routes again);
- has a **SATA SSD mount** (`Slot_sata_1`, a slot with *Mounted On Case* ticked, so it moves with the case's floor
  and rear like the PSU bay): a 2.5-inch drive stands on its long edge in a bracket on the basement floor beside the
  PSU, label to the open side, where it's in plain view (the M.2 drive sits between the cooler and graphics card, as on a real board,
  and gets hidden by them). Its two sockets are *direct* ports, so SATA power runs straight from the PSU's panel, and
  the red SATA data cable comes down from the board behind the tray (its `origin`) — steps 7, 11 and 12 of the
  practice build. Import Assembly Case Model (and so Import All Models) adds the drive, its component, quiz cards,
  mount and steps to a project set up before they existed (`BuildARSetup.UpgradeProject`);
- adds the **built-in motherboard** in its own `Motherboard` child — the CPU, RAM, SSD and GPU slots are laid out on
  it. Delete that child only if your model has its own board and you've moved the slots onto it;
- uses the textures inside the model, darkened to painted steel. A case without textures is painted with `M_Case`
  (matte black, `#121417`).

Re-running it is safe: the slots and ports are put back where they were authored first. **BuildAR ▸ Debug ▸
Report Assembly Case Layout** shows where everything ended up. If the open side still ends up facing away, rotate
the Visuals child 180° on Y.

### Replace the assembly case by hand
1. Open `Prefabs/Assembly/PH_AssemblyCase.prefab`. The origin is the case floor centre, and the open side faces **−Z**.
2. Replace the children of `Visuals` with your case and motherboard model.
3. Move each `Slots/Slot_*` so its transform is **exactly where the part's pivot should end up**, rotated so the part lines up. Move the matching `Highlight_*` glow box onto the slot surface.
4. Move the `CablePorts/Port_*` objects onto the real headers (24-pin, EPS, front panel) and PSU cable ends.
5. `slotId` and `connectorType` values must match the steps in `Data/Resources/AssemblyGuides/FirstBuild.asset`.

---

## 3. Scanning: recognising components and showing them in AR

The scanner has **three ways to recognise a part**, and uses the first one that is set up. They all end the same
way: the part's card slides up and its 3D model is spawned in the room by `ScannedModelPresenter` — on the real
part when the recogniser knows where it is, otherwise on the surface in front of you, otherwise floating at a
comfortable distance. One finger turns the model, two fingers resize it.

| Recogniser | Works on | Needs | Status on screen |
|---|---|---|---|
| `InferenceComponentDetector` | Any part it was trained on | A trained `.onnx` in `ML/Models` (section below) | normal |
| `TrackedImageComponentDetector` | The exact parts you photographed | Photos in `ML/ReferenceImages` | normal |
| `SimulatedComponentDetector` | Nothing — it invents detections | Nothing | **Demo mode · simulated scan** |

If none is set up the scanner says *"Recognition isn't set up yet"* and points the learner to Learn. There is no
manual parts list in the scanner: a card only opens for a part that was actually recognised, from the camera or
from a photo (**Scan a picture**, the picture button at the top right). In development builds that photo sheet
also holds the model-tuning tools: **Camera rotation** and **See what the model sees**.

### Fastest path: reference photos (no training)
1. Photograph each part **straight on, filling the frame, on a plain background, in even light**.
2. Save them to `Assets/_Project/ML/ReferenceImages/` named after the part's ML label:
   `cpu.jpg`, `motherboard.jpg`, `ram.jpg`, `ssd.jpg`, `gpu.jpg`, `psu.jpg`, `cooler.jpg`, `case.jpg`,
   `cable.jpg`, `io_panel.jpg`.
3. Run **BuildAR ▸ Setup ▸ Build Reference Image Library**, then **BuildAR ▸ Setup ▸ 4. Build Scenes**.

ARCore then recognises those parts and reports where they are, so the 3D model is anchored **on top of the real
part**. The trade-off: it recognises *the parts in your photos*, not any CPU in general — flat, well-textured
parts (motherboards, GPUs, boxed parts) track best. For "any GPU", train the model below.

### Training on a ready-made dataset (Roboflow Universe)
Public datasets save the labelling work. The training notebook pools three:

| Project | Images | Notes |
|---|---|---|
| [PC parts](https://universe.roboflow.com/james-manalili/pc-parts-5uy7m) — James Manalili | **1,369** | Sets the 12 class names. Also hosts trained YOLOv8s models (mAP@50 98.6%) |
| [PC Parts Detection](https://universe.roboflow.com/cas-pc-project/pc-parts-detection-yajmi) — CAS PC Project | 408 | Same 12 classes; extra variety |
| [PC-DETECTION](https://universe.roboflow.com/mynhungs-workspace/pc-detection-hu8q2) — MyNhungs Workspace | **5,297** | 7 classes, renamed onto the 12 by the notebook (`CPU_Fan`→`cpu_cooler`, `DISK`→`disk_drive`, `Mainboard`→`motherboard`, `RAM`→`ram_stick`). Many single-part photos, close to how the scanner is used; motherboards are only labelled in some of them |

All three are **CC BY 4.0**, so credit them in your report or app credits.

**Why not just deploy the hosted model?** Roboflow serves other people's trained models through its cloud API
only — the weights can't be downloaded, so they can't go inside the APK. Calling the API per camera frame means
an internet round-trip (a few hundred ms), an API key shipped inside the app, and usage limits; none of that
suits a live AR scanner. Training on the same images gives you a model you own that runs on-device, offline.

The class names aren't this app's, which is handled in code by `MlLabelMap`:

| Dataset class | Becomes | | Dataset class | Becomes |
|---|---|---|---|---|
| `cpu` | cpu | | `motherboard` | motherboard |
| `cpu_cooler` | cooler | | `optical_drive` | *ignored — no lesson for it* |
| `disk_drive` | ssd | | `psu` | psu |
| `front_panel` | case | | `ram_slot` | motherboard |
| `gpu` | gpu | | `ram_stick` | ram |
| `gpu_slot` | motherboard | | `rear_io` | io_panel |

**Ready-made notebook:** upload `ML/Training/PCBuildAR_train_pcparts.ipynb` to
[Colab](https://colab.research.google.com), set the runtime to **T4 GPU**, paste your Roboflow API key and run it
top to bottom. It downloads the dataset, trains, exports the ONNX, and writes `labels_pcparts_roboflow.txt`
**from the dataset's own `data.yaml`**, so the class order can't drift. Afterwards, **BuildAR ▸ Debug ▸ Check ML
Model** runs the model once in the Editor and reports the input size, output layout, class count, matching
labels file and what each class maps to.

The same steps by hand, if you'd rather:

```python
!pip install roboflow ultralytics
from roboflow import Roboflow
rf = Roboflow(api_key="YOUR_KEY")                      # Roboflow ▸ Settings ▸ API key
ds = rf.workspace("james-manalili").project("pc-parts-5uy7m").version(10).download("yolov8")

!yolo detect train data={ds.location}/data.yaml model=yolo11n.pt imgsz=640 epochs=100 batch=16
!yolo export model=runs/detect/train/weights/best.pt format=onnx imgsz=640 opset=17 simplify=True
```

Then follow *Step 4* below, with one change: set the detector's **Labels File** to
`ML/Models/labels_pcparts_roboflow.txt` (already in the project) and **check its order matches `names:` in the
downloaded `data.yaml`** — a different order silently mislabels everything.

**On accuracy.** With 1,369 images the numbers should be good — the owner's own YOLOv8s reports mAP@50 98.6%
on this data, so a weak result usually means a mistake in the pipeline rather than hard data. Watch the
confusion matrix for the look-alike pairs (`ram_slot` vs `ram_stick`, `gpu` vs `gpu_slot`). Whatever the score,
add 30–50 of your own phone photos of the exact parts you'll demo with: models generalise badly to lighting and
backgrounds they never saw.

### Training your own model

### Be realistic first
There is **no ready-made pretrained model that recognises CPUs, RAM, GPUs, etc.**. COCO-pretrained models only know *keyboard, mouse, laptop, tv, cell phone*. What you do is **fine-tune** a pretrained model (transfer learning) on your own photos of PC parts. That usually takes a few hundred images per class and about 1 hour on a free Colab GPU.

### Pick an approach
| | **Object detector** (recommended) | **Image classifier** (easiest) |
|---|---|---|
| Model | YOLO11n / YOLOv8n (Ultralytics) | MobileNetV3 / EfficientNet-Lite |
| Output | class + **bounding box** | class only |
| Labelling | draw a box on every image | put images in a folder per class |
| In the app | glowing box follows the part; AR labels placed inside the box | card works; labels use the scan frame |
| Supported here | ✅ auto-detected | ✅ auto-detected |

> **Licence note:** Ultralytics YOLO is AGPL-3.0. That's fine for a school or portfolio project. A closed-source commercial app needs an Ultralytics licence, or a permissively licensed model such as YOLOX (Apache-2.0).

### Step 1 — Collect & label data
> **Doing this for real?** `ML/Training/SHOOT_YOUR_OWN_DATASET.md` is the concrete procedure — which
> classes to shoot, how to vary the photos, the labelling rules that decide whether your images help or
> hurt, and how to pool them with the public datasets. The notes below are the short version.

- Use the **same twelve class names as the pooled Roboflow datasets** (`cpu, cpu_cooler, disk_drive, front_panel, gpu, gpu_slot, motherboard, optical_drive, psu, ram_slot, ram_stick, rear_io`) so everything merges without touching `labels_pcparts_roboflow.txt` or `MlLabelMap.cs`. A new name means a new class and a new labels file.
- Aim for **20–30 varied photos per class** you own, taken **with the phone that runs the app**: different angles, distances, lighting, backgrounds, parts held in hand, partly occluded parts, and parts inside a case.
- Label with **Roboflow**, **CVAT**, or **Label Studio**. Export in **YOLO format**.
- Keep about 15% for validation. Add some "background" photos (desks, cables, hands) with **no labels** to reduce false positives.

### Step 2 — Train (Google Colab, GPU runtime)
```bash
pip install ultralytics
yolo detect train data=pc_parts.yaml model=yolo11n.pt imgsz=640 epochs=100 batch=16
```
`pc_parts.yaml`:
```yaml
path: /content/pc_parts
train: images/train
val: images/val
names: [cpu, motherboard, ram, ssd, gpu, psu, cooler, case, cable, io_panel]
```
Check `runs/detect/train/results.png`. Aim for mAP50 > 0.8 before moving on to the phone.

### Step 3 — Export to ONNX
```bash
yolo export model=runs/detect/train/weights/best.pt format=onnx imgsz=640 opset=17 simplify=True
```
The installed Inference Engine (2.6) supports ONNX opsets **7–25**. Do **not** use `nms=True` unless you set the detector's Output Layout to *EndToEndNms*.
For a classifier (PyTorch), wrap the model so ImageNet mean/std normalisation happens **inside** the graph, then use `torch.onnx.export(..., opset_version=17)`.

### Step 4 — Put it in Unity
1. Copy `best.onnx` to **`Assets/_Project/ML/Models/`**. Unity imports it as a *Model Asset*. Click it to see its inputs and outputs.
2. Make sure the labels file lists the classes **in training order** (the same order as `names:` in `data.yaml`).
   The names don't have to be this app's: `MlLabelMap.cs` maps common outside names (`ram_stick`, `graphics card`,
   `hard drive`, `rear_io`…) onto components, a class with no component is ignored, and anything unusual can be
   added to that component's **Ml Aliases** list in the Inspector.
3. Open `BuildAR_ARScanner`, select **Detectors ▸ InferenceComponentDetector**, then:
   - **Model Asset** = your ONNX file, **Labels File** = `labels.txt`
   - **Input Size** = 640 (it must match `imgsz`), **Output Layout** = Auto
   - **Rotation** = Clockwise90 for a portrait Android app. If boxes appear rotated or mirrored on your phone, try the other values.
   - Start with **Score Threshold** 0.45 and **Inferences Per Second** 5.
   - Setup assigns the model automatically if it's already in `ML/Models` when you (re)build scenes.
4. Build to the phone. The ML detector only runs on device, because ARCore CPU camera images aren't available in the Editor. In the Editor, the simulated detector is used instead.

### How it works in code
```
ARCameraManager.TryAcquireLatestCpuImage   (≈5 fps)
  → centre square crop → rotate to portrait → Texture2D
  → TextureConverter.ToTensor → Worker.Schedule (GPUCompute, CPU fallback)
  → ReadbackAndCloneAsync → decode (YOLOv8/11, YOLOv5, end-to-end, classifier) + NMS
  → map box to screen (aspect-fill) → Detection { label, confidence, screenRect }
ARScannerController
  → label → ComponentDatabase.GetByMlLabel → needs 3 consistent frames → info card, glow box, AR labels
  → ScannedModelPresenter.Show(def, box, pose?) → model3DPrefab in the room
```
Files: `Scripts/AR/Detection/InferenceComponentDetector.cs`, `TrackedImageComponentDetector.cs`,
`SimulatedComponentDetector.cs`, `Scripts/AR/ScannedModelPresenter.cs`, `UI/ARScannerController.cs`.

### Scanning a photo instead of the camera
The **image** button in the scanner's top bar lists **Choose from my gallery**, the samples in
`Resources/ScanSamples`, and any photos in the app's `photos` folder.
- **Gallery:** on Android 13+ this opens the system photo picker; on older versions it opens the usual "choose a
  photo" chooser. No storage permission is needed. `GalleryPickerFragment.java` decodes the chosen photo and
  shrinks it to 1600 px on its longer side. It also turns it upright using the EXIF rotation and saves it as a
  JPEG, so HEIC and WebP photos work too. In the **Editor**, a file dialog stands in for the gallery. The
  model reads a photo without the camera, so a trained model (or the Roboflow key) can be tested in Play mode.
- **Minimise:** any photo loaded from a file is shrunk to `ScanPhotoLibrary.MaxPhotoSide` (1600 px) and turned
  upright from its EXIF data (`ScanPhotoLibrary.Load`).
- **Zoom out:** the photo is shown whole, below the top bar, and the model reads the **whole** photo. It is padded
  square with grey, the way YOLO letterboxes, instead of losing the sides to a centre crop (`StillFrame`).
- **Auto crop:** if the part covers less than **Close Look Area** (20%) of the photo, the model gets a second,
  closer look at a square crop around it. If nothing was found, it tries the middle of the photo instead (the middle
  square of a long photo, then a tighter square). If no look finds a known part, the "not a PC part" card explains
  why. Otherwise the view then zooms in on the part, framed above the info card. The **zoom** button in the top bar switches between the
  part and the whole photo. Closing the card goes back to the live camera.
```
IStillDetector.DetectStill(photo, region, done)   // region 0..1 of the photo; boxes come back 0..1 of the photo
  → StillFrame.Read: copy region → pad square (ONNX) / keep shape (Roboflow) → model
ARScannerController: whole photo → [closer look at a crop] → zoom the view to the part
```

### Tips
- **Performance:** `yolo11n` at 640 px runs at about 5–15 fps on mid-range phones with GPUCompute. If it's slow, export with `imgsz=416` (and set Input Size to 416), or lower Inferences Per Second.
- **Accuracy:** most errors come from training photos that don't look like real use. Add phone photos taken in the same conditions as your users.
- **AR label placement:** each component's `arLabels` use normalised positions inside the detection box: (0,0) is top-left and (1,1) is bottom-right.

---

## 4. Quizzes: the lesson quiz and Memorize

There are two kinds of quiz, with different jobs:

| | **Lesson quiz** (the test) | **Memorize** (practice) |
|---|---|---|
| Purpose | Proves the lesson was learned; passing it is required to complete the lesson | Keeps facts fresh over days and weeks; always optional |
| Where | A lesson's **Quiz** tab ▸ *Start quiz* | **Home ▸ Memorize**, or *Extra practice* on any part's **Quiz** tab |
| Questions | The lesson's own 20, shuffled, each asked **once** in its own format | Up to 10 per round; missed cards come back until they're right |
| Scoring | Score out of 20. **Pass mark 15 / 20 (75%)**. Best score kept; unlimited retakes | No score; cards level up on a schedule |
| Extras | None: no hearts, hints, reveal, flashcards or settings. XP for right answers; counts toward the streak | Hearts, hints, reveal, lightning rounds, speed runs, coins, shop |
| Leaving early | Not scored; next time starts from question 1 | XP already earned is kept |

**Completing a lesson** needs every tab opened (Overview, Parts, Compatibility, Installation) **and** the lesson quiz
passed at 75% or more. The results page lists every missed question with its answer and explanation. Once a lesson is
completed, its 20 questions also join the Memorize daily review.

**Editing the lesson quizzes.** All 260 questions live in `Tools/lesson-quizzes/lesson_quizzes.json` (outside
`Assets`). Edit it, then run `python Tools/lesson-quizzes/build_lesson_quizzes.py`: it checks every lesson has exactly
20 well-formed questions, writes them to `Data/LessonQuizzes/<lesson>/` and fills each lesson's **Quiz** list.
`--check` validates without writing. `{"ref": "cpu_1"}` reuses a card from `Data/QuizQuestions`. Asset GUIDs come from
the question ids, so rebuilding updates the same assets in place. Never reuse an old id for a different question:
Memorize progress is keyed by it. The constants are in `Scripts/Quiz/LessonQuizSession.cs` (`QuestionCount`,
`PassPercent`).

### Memorize

Modelled on Gizmo's Memorize mode. Open it from **Home ▸ Memorize** (daily review across all parts and completed
lessons) or a component's **Quiz** tab.

| Feature | How it works here |
|---|---|
| Spaced repetition | Each card has a level 0–5. Right when due → next review in 1, 3, 7, 16, 35 days. Wrong → back 2 levels and it returns later in the same round, tagged **Forgotten**. Practicing early doesn't raise the level. |
| Rounds | Up to 10 cards: due first, then new. Deck mastery ≥ 70% marks the component as mastered. |
| Question styles (cog ⚙) | Mixed · Multiple choice only · Typing preferred · Flashcards only, plus **Hide options initially** and **Lightning rounds**. |
| Card types | Multiple choice, True/False, Typing (typo-tolerant, any word order), Matching, Ordering. A Typing card can be asked as multiple choice using its `distractors`. |
| Hearts | 15. Wrong answer = −1. They refill to full 10 minutes after the last one is lost. Super Hearts only help if you own them *before* running out. Flashcards don't use hearts. |
| XP / coins | 10 XP + up to 10 for speed + up to 20 on a lightning question; ×2 at 4 in a row, ×3 at 8. +1 coin for each right-first-time answer. |
| Hints / Reveal | A hint removes a wrong option, shows the first letter, matches a pair or places the next item. Reveal shows the answer and costs a heart, like a wrong answer (the button shows the cost); the card comes back later in the round. |
| Streak | Answer 2+ questions a day. Tiers: Basic 2 → Gold 10 → … → Onyx 250. Your first round gives a free Streak Freeze. |
| Shop | Coins buy Hints (5), Super Hearts (15) and Streak Freezes (25). |
| Speed run | 2-minute challenge from the round summary. It's practice only, so mastery doesn't change. |

**Authoring cards:** *Create ▸ BuildAR ▸ Quiz Question*, then add it to a component's **Quiz Questions** list. Progress is keyed by the asset name, so keep names unique. **BuildAR ▸ Setup ▸ Regenerate Quiz Cards** rebuilds the 32 sample cards.

**Editor test keys (Play mode):** `F1` refill hearts · `F2` +50 coins · `F3` skip a day (makes cards due, shifts the streak).

---

## 5. Code map
| Folder | What |
|---|---|
| `Scripts/Data` | ScriptableObjects: ComponentDefinition, Lesson, QuizQuestion, AssemblyGuide, BadgeCatalog, CompatibilityRule |
| `Scripts/Managers` | GameManager (navigation), ProgressManager (save + badges; `ProgressManager.Quiz.cs` = hearts, XP, coins, streak, shop), ComponentDatabase, AudioManager, DevBootstrap |
| `Scripts/Quiz` | QuizSession (rounds), SpacedRepetition, AnswerMatcher, QuizRewards (numbers), QuizDeck |
| `Scripts/UI` | ScreenRouter, Theme (light/dark), LineIcon / ProgressRing / Spinner (custom UI Toolkit controls), UIUtil |
| `Scripts/Viewer` | ModelViewerStage (render-texture turntable), ExplodableModel, ExplodePart, ModelHotspot |
| `Scripts/AR` | Detection pipeline (ML, reference images, simulator), ScannedModelPresenter (model in the room) |
| `Scripts/Assembly` | AssemblyManager (step rules), AssemblyInteraction (drag / orbit / cables), SnapSlot, CablePort, CompatibilityChecker |
| `Scripts/Editor` | BuildARSetup (the Setup menu) |
| `Assets/UI` | UXML screens, `BuildAR.uss` design tokens, screen controllers |

**Fonts:** import Inter, Manrope, or Plus Jakarta Sans into `Art/Fonts`. Then choose *Create ▸ Text Core ▸ Font Asset*, and uncomment the `-unity-font-definition` line at the top of `BuildAR.uss`.

### Sound
Everything lives in `Assets/_Project/Resources/Audio/`, and AudioManager loads each file by name, so **replacing a
file is all it takes to change a sound** — no Inspector wiring. A clip dropped into the matching field on the
AudioManager (in `BuildAR_Boot`) overrides the file, and the volumes are on the same component.

| File | When it plays | Volume |
|---|---|---|
| `buttonsfx.mp3` | Every button press | *Click Volume* (0.55) |
| `confetti.wav` | With the confetti burst: lesson complete, round complete | *Confetti Volume* (0.6) |
| `bgmusic.wav` | Loops under the whole app, fading in over 1.5 s | *Music Volume* (0.12) |

`confetti.wav` and `bgmusic.wav` are synthesised, so they carry no licence — `Tools/synth-audio.ps1` (outside
`Assets`, so Unity ignores it) is the generator, and editing the chords or notes in it regenerates them:

```
powershell -File Tools\synth-audio.ps1 -OutDir Assets\_Project\Resources\Audio
```

The music is a seamless 24-second loop (C – Am – F – G pad with sparse bells). If you swap in a track of your own,
keep the loop join clean or you'll hear a click every pass.

`UIUtil.PrepareScreen` listens for clicks on the screen root and plays the tap sound for every `Button`, so new
buttons need no wiring. Anything tappable that isn't a Button uses `UIUtil.Tap(action)` instead of
`new Clickable(action)`. The confetti sound is played by `Confetti.Burst`, so it can't drift out of sync with the
animation. Learners can switch music off under **Sound** in the profile panel (`SaveData.musicOn`).

AudioManager also carries the app's only `AudioListener` and disables any other one a loaded scene brings in —
without it, the App and Boot scenes have no listener and nothing would be audible on the 2D screens.

### Guided tour (Ryan)
On the very first launch, right after the welcome slides and profile setup, Home opens with **Ryan**, a friendly
PC builder in a hard hat, who walks the learner through the screen in 12 short steps. It covers the progress
ring, the reward counters, the current build step, the four quick actions, the bottom bar, the scan button and
the profile/theme buttons. Each step dims the screen, puts a spotlight on one area and types out Ryan's line in a
speech bubble.
Tapping the words shows them all at once, and **Skip tour** ends it early.

- It starts by itself only once: `SaveData.guideTourPending` is set when onboarding finishes for the first time
  and cleared when the tour ends or is skipped. Learners who were already using the app don't see it.
- Learners can take it again from the profile panel (**Help ▸ Take the tour again**).
- The lines live in `Scripts/UI/GuideTour.cs` (`Steps`); `, {name}` becomes the learner's name, or is left out.
- Ryan is drawn from plain UI elements in the app's colours (the *guided tour* section of `BuildAR.uss`):
  white hard hat with the app badge, blue work jacket with a cyan reflective stripe. His name is
  `GuideTour.GuideName`. To use a picture instead, save a PNG of the character alone (transparent background,
  no speech bubble) as
  `Assets/_Project/Resources/Guide/guide_avatar.png`. It still bobs, but only the drawn version blinks, talks
  and waves.

### Dark mode
Tap the moon on Home, or use *Dark mode* in the profile panel. The choice is saved with the rest of the
progress (`SaveData.darkMode`).

`Theme.Apply` (called for you by `UIUtil.PrepareScreen`) puts the classes `theme` and, in dark mode, `theme-dark`
on each screen root. The `.theme.theme-dark` block at the end of `BuildAR.uss` re-declares the colour tokens
(`--bg`, `--surface`, `--text`, …), and everything styled with those tokens follows automatically — new screens
need no dark-mode code.

Two things to keep in mind when adding UI:
- Style with the tokens (`var(--surface)`, `var(--text)`, `var(--border)`, `var(--surface-2)`, `var(--invert-bg)` …)
  instead of hard-coded hex, and both themes work for free.
- `--icon-color` can't read a token, so an icon colour that must change between themes needs a
  `.theme-dark <selector> .line-icon { --icon-color: …; }` line in that same block.
