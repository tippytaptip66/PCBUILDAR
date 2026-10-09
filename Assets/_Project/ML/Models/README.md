# ML models go here

- `your_model.onnx`: exported from Ultralytics or PyTorch (opset 7-25; 17 recommended)
- `labels.txt`: one class per line, **in training order** (same order as `names:` in `data.yaml`).

Class names do **not** have to match this app's: `Scripts/Data/MlLabelMap.cs` maps common outside names
(`ram_stick`, `graphics card`, `hard drive`, `rear_io`, …) onto components, a class with no component is
ignored, and anything unusual can be added to that component's **Ml Aliases** list in the Inspector.

`labels_pcparts_roboflow.txt` holds the 12 classes of the Roboflow Universe datasets the training notebook
pools (james-manalili/pc-parts-5uy7m, cas-pc-project/pc-parts-detection-yajmi and
mynhungs-workspace/pc-detection-hu8q2, all CC BY 4.0; the third one's 7 class names are renamed onto the 12).
None of them can hand you a model to put in the app — Roboflow serves trained models only through its cloud
API — so the notebook trains on their images and writes this file from the merged class list, which is what
guarantees the order matches.

Then assign the model and the labels file on **BuildAR_ARScanner > Detectors > InferenceComponentDetector**.
Full walkthrough: `Assets/_Project/BUILDAR_GUIDE.md`, section 3.
