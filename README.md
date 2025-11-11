# 6DOF ArUco-Based Laparoscopic Forceps Tracking for Unity (Meta Quest 3)# Mixed Reality (MR) Surgical Training App for Meta Quest



<div align="center"><div align="center">

<img src="./media/meta-quest.svg" alt="Meta Quest" width="500"/>

**Real-time marker-based surgical tool tracking for Mixed Reality training applications**</div>



[![Unity](https://img.shields.io/badge/Unity-2022.3_LTS-black?logo=unity)](https://unity.com/)This is a Unity-based mixed reality application for surgical training. This project demonstrates advanced articulated tool manipulation, trigger-based physics, and smooth animation systems specifically designed for Meta Quest devices in medical training scenarios.

[![Meta Quest 3](https://img.shields.io/badge/Meta_Quest-3-blue)](https://www.meta.com/quest/)

[![OpenCV](https://img.shields.io/badge/OpenCV-ArUco-green?logo=opencv)](https://opencv.org/)## 🎯 Project Overview

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

### Key Features

</div>



---### Laparoscopic Forceps



## 🎯 Project OverviewThe laparoscopic forceps module represents one of core functionalities of this training application.



This project implements **6-degree-of-freedom (6DOF) tracking** for laparoscopic forceps using **ArUco marker detection** on Meta Quest 3's passthrough cameras. The system enables precise, real-time tracking and interaction with virtual objects in mixed reality surgical training scenarios.<div align="center">

<img src="./media/laparoscopic-forceps.jpg" alt="Laparoscopic Forceps" width="300"/>

<div align="center"></div>

<img src="./media/laparoscopic-forceps.jpg" alt="Laparoscopic Forceps" width="400"/>

</div>### further surgical tools ...



### Key Features

## 📋 Requirements

- ✅ **Computer vision-based 6DOF tracking** using ArUco markers (no controllers needed)### Development Environment

- ✅ **Marker visibility-based clamp control** (OR logic: any marker visible → close, all hidden → open)- **Unity Hub:** 3.12.1

- ✅ **Size-adaptive object grasping** with geometric angle calculation- **Unity Editor:** 2022.3 LTS

- ✅ **Multi-attach-point system** for different object sizes

- ✅ **One Euro Filter smoothing** for stable tracking### Packages

- ✅ **Anti-regrab cooldown** to prevent object teleportation-	AR Foundation (4.2.7+)

-	Meta XR SDK (57.0+)

----	XR Interaction Toolkit (2.6.4+)

-	XR Plugin Management (4.4.0+)

## 🔬 Technical Implementation-	Oculus XR Plugin (3.2.3+)



### Core Components### Platform Support

- **Meta Quest 2**

#### 1. **ArUco Marker Tracking** (`RigidCubeAxesMinimal.cs`)- **Meta Quest 3**

- Tracks **6 ArUco markers** (IDs 0-5) on a rigid cube attached to forceps- **Meta Quest Pro**

- Uses Meta Quest 3's **passthrough camera** (1280×960 @ 30Hz)

- **One Euro Filter** for smooth pose estimation with adaptive parameters## 🛠️ Installation

- **Board-based pose estimation** using OpenCV's `solvePnP`--



#### 2. **Marker Visibility Control** (Method 13)## 📄 License

- Monitors **3 visibility markers** (IDs 9, 6, 10) for clamp control

- **OR logic**: ANY marker visible → CLOSE, ALL hidden → OPENThis project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

- Frame confirmation (3 frames) prevents jitter

#### 3. **Geometric Angle Calculation** (`ForcepsController.cs`)
- Calculates optimal clamp angle based on object size and attach point
- Maps geometric angles (30°-70°) to rotation angles (-85° to -50°)

```csharp
// Geometric calculation
Vector3 middle = (upperClamp.position + lowerClamp.position) * 0.5f;
Vector3 clampLine = (upperClamp.position - lowerClamp.position).normalized;
Vector3 attachDir = (attachPoint.position - middle).normalized;
float geometricAngle = Vector3.Angle(attachDir, clampLine);
float targetAngle = Mathf.Lerp(-85f, -50f, normalizedAngle);
```

#### 4. **Multi-Attach-Point System** (`CustomXRDirectInteractor.cs`)
- Extended `XRDirectInteractor` with multiple attach transforms
- Automatically selects closest attach point:
  - **Small ball (0.01m)** → outer point → -50° (more open)
  - **Medium ball (0.015m)** → middle point → -67.5°
  - **Large ball (0.02m)** → inner point → -85° (more closed)

---

## 📊 System Architecture

```
Meta Quest 3 Passthrough Camera (30Hz, 1280×960)
            ↓
    ArUcoMarkerTracking.cs (OpenCV Detection)
            ↓
    MarkerCornerExtractor.cs (3D World Corners)
            ↓
    RigidCubeAxesMinimal.cs
    ├─ 6DOF Pose Estimation (markers 0-5)
    ├─ One Euro Filter Smoothing
    └─ Visibility Detection (markers 9,6,10)
            ↓
    ForcepsController.cs
    ├─ ArUco Grab/Release State Machine
    ├─ Geometric Angle Calculation
    └─ Clamp Freeze Mechanism
            ↓
    CustomXRDirectInteractor.cs (XR Toolkit)
            ↓
    XRGrabInteractable Objects
```

---

## 🛠️ Setup Instructions

### Requirements

**Development Environment:**
- Unity Hub 3.12.1+
- Unity Editor 2022.3 LTS

**Packages:**
- Meta XR SDK 57.0+
- OpenCV for Unity 2.5.9+
- XR Interaction Toolkit 2.6.4+
- XR Plugin Management 4.4.0+
- Oculus XR Plugin 3.2.3+

**Platform Support:**
- Meta Quest 3 (primary)
- Meta Quest 2 / Pro (lower resolution)

### ArUco Marker Setup

1. **Print 9 ArUco markers** (DICT_4X4_50):
   - IDs 0-5: Cube face markers (6DOF tracking)
   - IDs 9, 6, 10: Visibility control markers

2. **Marker specifications:**
   - Size: 65-100mm per marker
   - Material: Rigid (foam board/cardboard)
   - High contrast black/white

3. **Assembly:**
   - Attach markers 0-5 to rigid cube
   - Attach visibility markers 9, 6, 10 to clamp handles

### Project Setup

```bash
# Clone the repository
git clone https://github.com/YOUR_USERNAME/YOUR_REPO_NAME.git

# Open in Unity Hub (2022.3 LTS)
# Import OpenCV for Unity from Asset Store
# Configure XR Plugin Management → Oculus
# Build Settings → Android → Switch Platform
```

### Configuration

**RigidCubeAxesMinimal:**
```
Marker Length: 0.065m          (adjust to your markers)
Position Min Cutoff: 0.05      (smoothing)
Position Beta: 0.0             (responsiveness)
Visibility Marker IDs: [9,6,10]
```

**ForcepsController:**
```
Ball Size Mapping:
  Small: 0.01m, Medium: 0.015m, Large: 0.02m
```

---

## 🎮 Usage

1. Start application on Quest 3
2. Point cameras at ArUco cube on forceps
3. Wait for tracking lock (cube model appears)
4. Position ball between clamps
5. **Hide markers** (cover 9,6,10) → clamps **CLOSE**
6. **Show markers** → clamps **OPEN**
7. Different ball sizes auto-select attach points

---

## 📈 Performance

| Metric | Value |
|--------|-------|
| **Tracking Rate** | 30 Hz (API limit) |
| **Latency** | 40-60ms |
| **Position Accuracy** | ±5mm (6 markers) |
| **Rotation Accuracy** | ±2° |
| **GPU Overhead** | ~1-2% |

### Known Limitations

- 30Hz update rate causes tip jitter (lever arm effect)
- 1280×960 resolution limits long-range accuracy
- Motion blur degrades fast movement tracking
- Lighting sensitivity

### Future Improvements

- **IMU fusion** (200-500Hz) for rotational stability
- **Kalman filtering** for predictive tracking
- **Larger markers** (80-100mm) for better range
- More simultaneous markers

---

## 📁 Project Structure

```
Assets/Scripts/
├── ArUcoMarkerTracking.cs        # OpenCV wrapper
├── MarkerCornerExtractor.cs      # 3D corner extraction
├── RigidCubeAxesMinimal.cs       # Main tracking + visibility
├── RigidBodyPoseEstimator.cs     # 6DOF pose estimation
├── OneEuroFilters.cs             # Smoothing filters
├── ForcepsController.cs          # Grab/release + geometry
├── CustomXRDirectInteractor.cs   # Multi-attach XR
├── UpperClamp.cs / LowerClamp.cs # Trigger detection
└── DiceCADModel.cs               # Cube CAD definition
```

---

## 🧪 Testing Results

### Tracking Accuracy
- ✅ ±5mm accuracy within 1.5m range
- ✅ Stable with 4+ markers visible

### Clamp Angle Validation
| Ball Size | Attach Point | Target Angle | Status |
|-----------|-------------|--------------|--------|
| Small | Outer | -50° | ✅ |
| Medium | Middle | -67.5° | ✅ |
| Large | Inner | -85° | ✅ |

### Visibility Logic
| Markers | Clamp State | Status |
|---------|-------------|--------|
| None | OPEN | ✅ |
| Any 1-3 | CLOSE | ✅ |

---

## 🤝 Contributing

Contributions welcome! Key areas:
- IMU sensor fusion
- Advanced filtering (Kalman)
- Performance optimization
- Multi-tool tracking

---

## 📚 References

- [Meta Passthrough API](https://developer.oculus.com/documentation/unity/unity-passthrough/)
- [OpenCV ArUco](https://docs.opencv.org/4.x/d5/dae/tutorial_aruco_detection.html)
- [One Euro Filter](https://hal.inria.fr/hal-00670496/document)
- [Unity XR Interaction Toolkit](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@2.6/)

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## 👤 Author

Developed as part of MR surgical training research. This repository focuses on the **ArUco-based 6DOF tracking and marker visibility control system**.

---

## 🙏 Acknowledgments

- Team members for the MR surgical training framework
- OpenCV community for ArUco algorithms
- Meta for Quest 3 Passthrough Camera API

---

<div align="center">
<sub>Built with ❤️ for advancing surgical training through Mixed Reality</sub>
</div>
