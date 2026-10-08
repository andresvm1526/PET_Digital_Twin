# PET Digital Twin — Unity Scene Setup Guide

## Prerequisites

| Tool | Version |
|------|---------|
| Unity Editor | 2022.3 LTS or newer |
| TextMeshPro | Included free (install via Package Manager) |
| .NET | 4.x (set in **Player Settings → Other Settings → Api Compatibility Level**) |

> [!IMPORTANT]
> No paid assets required. All 3D geometry is procedural (Unity primitives).

---

## 1 — Create the Unity Project

1. Open **Unity Hub**.
2. Click **New Project → 3D (Core)**.
3. Name it `PET_DigitalTwin`.
4. **Location**: point to `PET_Digital_Twin/unity/PET_DigitalTwin/`.
5. Click **Create Project**.

> Unity will generate its own `Assets/`, `Library/`, `ProjectSettings/` folders inside that path.

---

## 2 — Install TextMeshPro

1. **Window → Package Manager**.
2. Search **TextMeshPro** → Install.
3. When prompted, click **Import TMP Essentials** (free assets).

---

## 3 — Copy Scripts

All C# scripts are already in `Assets/Scripts/`. Unity will compile them automatically on the next Editor focus.

Folder structure:

```
Assets/
  Scripts/
    Core/
      MachineState.cs
      PlantManager.cs
      MaterialFactory.cs
    API/
      DigitalTwinApiClient.cs
    Animation/
      InjectionMachineAnimation.cs
      BlowMoldingAnimation.cs
    Camera/
      CameraController.cs
    UI/
      DashboardController.cs
      BillboardLabel.cs
    MachineController.cs
    ProductMover.cs
    PETDigitalTwin.asmdef
```

---

## 4 — API Connection Config

`Assets/Resources/Config/api_config.json` is already present and contains only Unity connection settings. Do not copy `data/input_pet.json`: that file is the SimPy process input and remains the canonical source on the Python side.

---

## 5 — Create the Main Scene

1. **File → New Scene → Basic (Built-in)** → Save as `Assets/Scenes/MainScene.unity`.

---

## 6 — Scene GameObjects

### 6.1 — Managers Empty Object

1. Create empty: `GameObject → Create Empty`, name it `Managers`.
2. Add components:
   - `PlantManager`
   - `DigitalTwinApiClient`
3. In **PlantManager Inspector**:
   - **Api Client**: drag the same `Managers` object.
   - **Dashboard**: will wire in step 7.
   - **Offline Banner**: will wire in step 7.
4. In **DigitalTwinApiClient Inspector**:
   - Host: `127.0.0.1`
   - Port: `8000`
   - Poll Interval Ms: `200`

> [!NOTE]
> `PlantManager.Start()` procedurally builds all 9 machine GameObjects, the floor, and the connector pipes. You do **not** need to create them manually.

### 6.2 — Camera

1. Select the existing **Main Camera**.
2. Add component: `CameraController`.
3. Confirm default values:
   - Overview Position: `(3, 22, -18)`
   - Overview Rotation: `(52, 0, 0)`

### 6.3 — Directional Light

Set **Rotation**: `(50, -30, 0)` for industrial lighting feel.

---

## 7 — Build the HUD (Canvas)

### 7.1 — Create Canvas

`GameObject → UI → Canvas`

- Render Mode: **Screen Space – Overlay**
- Canvas Scaler: **Scale With Screen Size**, Reference: `1920 × 1080`

### 7.2 — Offline Banner

1. Under Canvas: `UI → Panel`, name it `OfflineBanner`.
2. Anchor: **top-center**, size `600 × 60`.
3. Background colour: `(0.8, 0.1, 0.1, 0.9)` (red).
4. Child `UI → Text – TextMeshPro`, name `OfflineLabel`.
   - Text: `⚠  BACKEND OFFLINE`
   - Font Size: `28`, Bold, centre-aligned.
5. Wire **PlantManager → Offline Banner** field to `OfflineBanner`.

### 7.3 — KPI Panel

1. Create `UI → Panel`, name `KPIPanel`.
   - Anchor: **top-left**, size `340 × 280`.
2. Add `Vertical Layout Group` component.
3. Add 7 `Text – TextMeshPro` children:
   - `SimTimeLabel`
   - `TotalProductionLabel`
   - `GoodProductsLabel`
   - `ScrapLabel`
   - `ThroughputLabel`
   - `WIPLabel`
   - `DemandCompletionLabel`
4. Create empty `DashboardManager` object (or add to `Managers`).
5. Add `DashboardController` component.
6. Wire all KPI labels in Inspector.

### 7.4 — Machine Table

1. Create `UI → Panel`, name `MachineTable`.
   - Anchor: **bottom-right**, size `500 × 340`.
2. Add `Vertical Layout Group`.
3. Create a **Row Prefab**:
   - `UI → Panel`, name `TableRowPrefab`.
   - Add `Horizontal Layout Group`.
   - Add 4 `Text – TextMeshPro` children: `MachineId`, `State`, `Progress`, `Queue`.
   - Set equal `Preferred Width` on each.
4. Save to `Assets/Prefabs/TableRowPrefab.prefab`.
5. Wire in `DashboardController`:
   - **Table Row Container**: `MachineTable`
   - **Table Row Prefab**: the prefab above

### 7.5 — Machine Detail Panel

1. Create `UI → Panel`, name `DetailPanel`.
   - Anchor: **right-center**, size `340 × 420`.
   - Deactivate by default (checked in DashboardController.Start).
2. Add TMP labels for each field (see `DashboardController` public fields).
3. Wire all in `DashboardController` Inspector.

---

## 8 — Queue Item Prefabs

PlantManager needs three small prefabs for the queue visualisation:

| Prefab | Primitive | Scale | Colour |
|--------|-----------|-------|--------|
| `PelletPrefab` | Sphere | `(0.08, 0.08, 0.08)` | Tan |
| `PreformPrefab` | Cylinder | `(0.06, 0.15, 0.06)` | Light blue |
| `BottlePrefab` | Cylinder | `(0.12, 0.30, 0.12)` | Cyan tint |

1. Create each primitive, adjust scale and material colour.
2. **Remove Rigidbody** (no physics).
3. Save to `Assets/Prefabs/`.
4. Wire in **PlantManager Inspector**:
   - Queue Item Pellet Prefab
   - Queue Item Preform Prefab
   - Queue Item Bottle Prefab

---

## 9 — Conveyor Waypoints (CV-101)

1. In the Hierarchy, find `CV-101` (auto-created by PlantManager).
2. Create 3–4 empty GameObjects as children: `WP_0`, `WP_1`, `WP_2`.
3. Space them along the belt's local X axis.
4. Add `ProductMover` component to `CV-101`.
5. Drag waypoints into **Waypoints** array in Inspector.
6. Wire **Product Prefab** → `PreformPrefab`.

---

## 10 — Play Mode Test (Backend Offline)

1. Press **Play**.
2. You should see:
   - Red `⚠  BACKEND OFFLINE` banner.
   - All 9 machines built procedurally on the floor.
   - Floating labels above each machine.
   - No console errors (failure is silent after 3 consecutive failures).

---

## 11 — Play Mode Test (Backend Online)

1. Start the Python API: `uvicorn api.server:app --reload --port 8000`
2. Press **Play** in Unity.
3. The offline banner disappears.
4. Machines change colour as states arrive.
5. IMM-101 starts the mold-cycle animation when `state = RUNNING`.
6. SBM-101 starts the blow-molding animation when `state = RUNNING`.

---

## 12 — Keyboard Controls

| Key | Action |
|-----|--------|
| F1 | Overview camera |
| F2 | Free camera |
| WASD | Move (free mode) |
| Right-mouse drag | Look (free mode) |
| Scroll wheel | Zoom (free mode) |
| Shift | Sprint (3× speed) |
| Click machine | Follow camera + show detail |
| Esc | Return to overview |

---

## 13 — Replacing Primitives with FBX Models

The project is pre-structured for this:

1. Import your FBX into `Assets/Models/`.
2. In `PlantManager.BuildMachines()`, locate the `MachineDefinition` for the target machine.
3. Replace the `CreateMachineGO()` primitive creation with `Instantiate(yourFBXPrefab)`.
4. Ensure the FBX root has no `MachineController` component — `PlantManager` adds it via `AddComponent<MachineController>()`.
5. Animation components (`InjectionMachineAnimation`, `BlowMoldingAnimation`) drive child transforms by name. Rename your FBX sub-meshes to match:
   - IMM-101: `MoldTop`, `MoldBottom`
   - SBM-101: `MoldLeft`, `MoldRight`

> [!TIP]
> You can also create Unity Prefabs from the FBX files and assign them to the `queueItemPrefab` fields on MachineController for realistic queue item visuals.

---

## 14 — API Contract Reference

All data comes from Python via:

| Endpoint | Payload |
|----------|---------|
| `GET /status` | `{ "status": string, "simulation_time": float, "time_unit": "seconds", ... }` |
| `GET /machines` | `[ MachineState, ... ]` |
| `GET /kpis` | `KpiSnapshot` |
| `GET /simulation/time` | `{ "simulation_time": float, "unit": "seconds" }` |

Full schema: `contracts/machine_state.schema.json`

---

## 15 — File Map

```
unity/PET_DigitalTwin/Assets/
  Scripts/
    Core/
      MachineState.cs          ← data model (mirrors schema)
      PlantManager.cs          ← builds scene, routes API data
      MaterialFactory.cs       ← colour palette
    API/
      DigitalTwinApiClient.cs  ← REST poller (5 Hz)
    Animation/
      InjectionMachineAnimation.cs
      BlowMoldingAnimation.cs
    Camera/
      CameraController.cs
    UI/
      DashboardController.cs
      BillboardLabel.cs
    MachineController.cs       ← per-machine state/colour/label/queue
    ProductMover.cs            ← conveyor product animation
  Prefabs/                     ← create manually (guide §8)
  Materials/                   ← auto-generated by MaterialFactory
  Scenes/
    MainScene.unity            ← create manually (guide §5)
  Resources/
    Config/
      api_config.json          ← Unity-only host, port and polling interval
```
