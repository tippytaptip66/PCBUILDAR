<div align="center">
  <h1>🛠️ PCBuildAR</h1>
  <p><strong>Demystifying PC Building with Augmented Reality & Machine Learning</strong></p>
</div>

<br/>

**PCBuildAR** is an interactive, educational Unity application designed to teach computer science students and tech enthusiasts how to identify, understand, and assemble PC hardware components. By bridging the gap between theoretical knowledge and practical experience, BuildAR lets you learn PC assembly without the fear of breaking expensive hardware.

---

## ✨ Features

### 🔍 Real-Time AR Hardware Scanning
Point your phone's camera at physical PC components, and BuildAR will identify them in real-time! 
* **Powered by On-Device ML**: Uses a YOLO-based ONNX model running locally via Unity Inference Engine.
* **Smart Bounding Boxes**: Automatically tracks and labels parts (Motherboards, GPUs, CPUs, RAM) instantly with Non-Maximum Suppression (NMS) for clean visuals.

### 🏗️ Virtual AR Assembly Room
Turn your physical desk into a virtual PC building workbench using AR Foundation (ARCore).
* **Life-Size Holograms**: Place a 1:1 scale virtual PC case in your room.
* **Hands-on Practice**: Drag, drop, and snap 3D components into their correct slots.
* **Cable Management**: Connect virtual cables and interact with the case (e.g., test the power button!).
* **Compatibility Engine**: Checks component compatibility in real-time as you build.

### 🧠 Interactive Lessons & Quizzes
A comprehensive built-in curriculum to solidify your hardware knowledge.
* **Component Dictionary**: Explore detailed 3D models and specifications for every part.
* **Spaced Repetition**: Adaptive quizzes that use proven spaced-repetition algorithms to ensure you memorize part functions efficiently.
* **Gamified Progress**: Earn rewards, unlock UI themes, and track your learning journey over time.

---

## 💻 Tech Stack

* **Game Engine**: Unity 3D
* **AR Framework**: AR Foundation (ARCore)
* **Machine Learning**: Unity Sentis / Inference Engine
* **AI Models**: YOLOv8 / YOLOv5 (Exported to ONNX)
* **UI/UX**: Unity UI Toolkit
* **Language**: C#

---

## 🚀 Getting Started

### Prerequisites
* Unity 2022.3 LTS (or later)
* Android device with ARCore support (for AR features)
* Visual Studio / Rider (for script editing)

### Installation
1. Clone the repository:
   ```bash
   git clone https://github.com/tippytaptip66/PCBuildAR.git
   ```
2. Open the `BuildAR` folder in Unity Hub.
3. Open the `BuildAR_App` scene located in `Assets/_Project/Scenes/` (if not loaded by default).
4. **Android Build:** Go to `File > Build Settings`, switch the platform to Android, and hit **Build and Run** to deploy it to your device!

*(Note: The machine learning models (`.onnx`) must be present in `Assets/_Project/ML/Models/` for the AR Scanner to function properly.)*

---

---

<div align="center">
  <i>Built as a Thesis Project.</i>
</div>
