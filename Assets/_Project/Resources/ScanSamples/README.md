# Sample pictures for the scanner

Drop `.jpg` or `.png` photos of PC parts in this folder and they ship inside the app. They appear under the
**picture button** in the AR Scanner's top bar, and the trained model runs on them exactly as it does on the
camera — handy when the part isn't to hand, and for a demo that doesn't depend on lighting.

Name each file after what it shows (`gpu.jpg`, `motherboard.jpg`); the name is what the list displays.

Keep them reasonably small — 1024 px on the long edge is plenty, and every file here adds to the download size.

Learners can also add their own on the phone, with no storage permission needed, by copying files over USB into:

```
Android/data/com.pcbuildar.app/files/photos/
```

`Pictures/PCBuildAR/` and `DCIM/PCBuildAR/` are checked too, when Android allows it.
