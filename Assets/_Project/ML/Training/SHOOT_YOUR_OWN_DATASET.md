# Shooting your own dataset

The two Roboflow Universe datasets the notebook pools were photographed by other people, with other
cameras, of other hardware. The scanner's accuracy **in your app** depends on how the model copes with
your phone, your parts and your room — which neither dataset measures.

A few hundred photos of your own, pooled in alongside them, is the single largest accuracy gain
available to this project. Budget an afternoon: about 2 hours shooting, 2 hours labelling.

---

## 1. Use these class names

Exactly these twelve, spelled exactly this way:

```
cpu   cpu_cooler   disk_drive   front_panel   gpu   gpu_slot
motherboard   optical_drive   psu   ram_slot   ram_stick   rear_io
```

They match both pooled datasets and `ML/Models/labels_pcparts_roboflow.txt`. Invent a different name and
it becomes a thirteenth class: the labels file changes, the model changes shape, and `MlLabelMap.cs`
needs a new entry. Stick to the list and nothing in Unity has to change at all.

Not every class is worth your time. What the app does with each:

| Class | Shows in the app as | Shoot it? |
|---|---|---|
| `cpu` | CPU | Yes |
| `cpu_cooler` | CPU Cooler | Yes |
| `gpu` | Graphics Card | Yes |
| `motherboard` | Motherboard | Yes |
| `ram_stick` | Memory | Yes |
| `psu` | Power Supply | Yes |
| `disk_drive` | Storage | Yes |
| `rear_io` | I/O Panel | Yes |
| `front_panel` | Case | If you have a case |
| `gpu_slot`, `ram_slot` | Motherboard | Only on a bare board (see §4) |
| `optical_drive` | *ignored* | Skip |

> Cables aren't in the twelve, so the model can't detect them, even though the app has a cable component.
> Adding a `cable` class is possible but it changes the class count — a separate job, not this one.

## 2. How many

Aim for **20–30 usable photos per class you own**, so 150–250 in total. More helps, but variety helps far
more than volume: 30 genuinely different shots beat 100 near-identical ones.

Include **10–20 photos with no parts in them at all** — an empty desk, a keyboard, a mug, a charger, your
bed. Upload these with no boxes drawn. YOLO uses them to learn what *isn't* a component, and they are the
direct cure for the scanner firing at nothing, which you've already hit once.

## 3. How to shoot

**Use the phone you run the app on.** Its sensor, lens and colour processing are what the model will face.
A DSLR shot is a photo of the wrong world.

Vary these deliberately — each photo should differ from the last in at least two ways:

- **Distance** — part filling the frame, part plus some desk, part across the table. Match how far away
  you actually hold things when scanning.
- **Angle** — straight down, 30°, 60°, from the side, upside down, one end towards the camera.
- **Background** — desk, carpet, bed, tiled floor, cluttered table, plain paper. This matters more than
  people expect: shoot every CPU on the same wooden desk and the model quietly learns that wooden desks
  are CPUs.
- **Light** — daylight by a window, ceiling light at night, a lamp off to one side, partial shadow.
- **In hand** — a good share of shots should be the part held in someone's fingers, because that is how
  the app is used. Datasets of parts lying flat on a table don't teach this.
- **Together** — several parts in one frame, parts partly behind each other, a part half out of frame.
  Label every one of them.

Keep them mostly sharp. A couple of slightly soft frames are realistic and fine; the app now discards
badly blurred ones before they reach the model anyway.

Avoid burst-firing twenty frames of one pose. Near-duplicates inflate the count and teach nothing, and if
they land on both sides of the train/validation split they'll flatter your mAP with a result you can't
reproduce on the phone.

## 4. Labelling in Roboflow

1. Sign up free at roboflow.com, **Create New Project** → **Object Detection**.
   The free plan is built around *public* projects — check the current terms as you sign up, and note a
   public project means your photos are visible on Universe.
2. Add the twelve class names above.
3. Upload your photos and draw boxes.

Rules that decide whether this helps or hurts:

- **Tight boxes.** Edge of the part, not its shadow, not a margin of desk.
- **Label every instance**, including ones partly cut off — if you can tell what it is, box it. An
  unlabelled part in a corner teaches the model that parts are background.
- **A stick in a slot is a `ram_stick`.** The slot class is for the empty socket on a bare board. Same for
  `gpu` versus `gpu_slot`. These two pairs are the ones your confusion matrix will show trouble on, and
  inconsistent labelling here is what creates it.
- **A cooler mounted on a board is both** `cpu_cooler` and `motherboard` — two boxes, overlapping. That's
  correct and expected.
- **Decide once, apply throughout.** Any rule applied consistently beats a better rule applied half the
  time.

Then **Generate** a version:

- Preprocessing: **Auto-Orient ON**, **Resize → Stretch to 640×640**.
- Augmentation: **all off**. The training command already augments, and doing it twice bakes fixed copies
  into your image budget while making the training-time augmentation harder to reason about.
- Train/valid/test split: the 70/20/10 default is fine.

## 5. Add it to the training run

Open `PCBuildAR_train_pcparts.ipynb` and append your project to `SOURCES`:

```python
SOURCES = [
    ("james-manalili", "pc-parts-5uy7m"),
    ("cas-pc-project", "pc-parts-detection-yajmi"),
    ("mynhungs-workspace", "pc-detection-hu8q2"),
    ("your-workspace", "your-project-slug"),      # <- yours
]
```

Both strings come straight out of your project's URL:
`universe.roboflow.com/`**`your-workspace`**`/`**`your-project-slug`**

Your own API key already has access, public or not. Run the notebook top to bottom.

## 6. Check it actually helped

- The **comparison cell** validates the old and new models on the same images, one source dataset at a
  time. If the new one isn't clearly ahead, your photos didn't help — usually inconsistent labelling, or too
  many near-duplicates.
- The **confusion matrix** from the val cell: look at the `ram_slot` / `ram_stick` and `gpu` / `gpu_slot`
  rows. That's where §4's consistency rule pays off or doesn't.
- Then the only test that counts: build to the phone and scan the actual parts. Use **Scan a picture ▸
  See what the model sees** (development builds only) if results look strange — it saves the exact frame the model was given, which
  settles rotation, focus and exposure questions in one look.

## 7. For your paper

Say what you did plainly: you fine-tuned YOLO11s on pooled public datasets plus your own captured and
annotated images, and evaluated all variants on common held-out splits. Report the before/after mAP from
the comparison cell. A modest, honestly measured gain reads far better than an unexplained 98%.

Credit the public datasets (all CC BY 4.0): *PC parts Dataset, James Manalili, Roboflow Universe*;
*PC Parts Detection Dataset, CAS PC Project, Roboflow Universe*; *PC-DETECTION Dataset, MyNhungs Workspace,
Roboflow Universe.*
