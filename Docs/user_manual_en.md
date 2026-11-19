# Fast AO Baker User Guide

**Fast AO Baker** is a GPU-accelerated ambient occlusion (AO) baking tool for the Unity Editor.  
Combining Compute Shaders with BVH (Bounding Volume Hierarchy) acceleration and edge-preserving SVGF / À-trous denoising, it generates smooth, high-precision shadow maps comparable to dedicated texturing software—directly inside Unity.

---

## UI Overview

From top to bottom, the window consists of:

1. **Top Bar** — tool name, version display, update checker, language switcher, manual, menu
2. **Target Card** — GameObjects to bake (Targets) and additional shadow casters (Occluders)
3. **Basic AO Settings Card** — resolution, UV channel, self/mutual occlusion, low-resource mode, and "Advanced" settings
4. **Shadow Smoothing Card** — edge-preserving SVGF denoising toggle and smoothing parameters
5. **Finishing Touches Card** — edge filling (dilation), shadow color, and Gaussian blur
6. **Output Dock** — destination folder and overwrite settings
7. **Bake / Cancel Buttons** and **Status Bar**

When resized, the middle card area scrolls while the Top Bar and bottom controls (Output dock, Bake button, Status bar) remain fixed in view.

---

## Getting Started

Open the window from the Unity top menu: **`dennokoworks` > `Fast AO Baker`**.

---

## Basic Workflow

1. Select the objects you want to bake in the Hierarchy, then open the window (or drag & drop them onto the Target card).
2. Check the **Resolution** (default: 2048) in the Basic AO Settings card.
3. If necessary, open "Advanced" and adjust **Shadow Spread (Max Distance)** to match the physical scale of your model.
4. Click **`Bake`**.
5. Once complete, the generated textures are saved to the destination folder (or next to the model's texture) and automatically highlighted in the Project window.

---

## Features & Settings

### 1. Top Bar

| Element | Description |
|---|---|
| **Tool Name** | `Fast AO Baker`. Truncated automatically when the window is narrow. |
| **Version** | Current version (e.g. `v2.0.0`). Automatically checks GitHub Releases on startup.<br>• `Update available <version>`: A newer version is available (green text; click to open release page)<br>• `Checking…`: Checking for updates<br>• `Cannot fetch latest`: Check failed due to network or connection issues |
| **`↻` (Check for updates)** | Retries the version check manually. |
| **`EN` / `JA`** | Toggles the UI language between Japanese and English. The button displays the language it will **switch to** (shows `JA` while in English). Settings are saved. |
| **`?` (Manual)** | Opens this user guide (PDF) in the currently selected language. |
| **`⋮` (Menu)** | • **Reset settings to defaults…**: Resets baking settings to default values while keeping registered targets (shows confirmation dialog)<br>• **Check for updates**: Same as `↻` |

---

### 2. Target Cards (Targets / Occluders)

Specifies the GameObjects to bake and any additional objects that cast shadows.

#### Target (TARGET)
Each GameObject registered here will have an AO texture baked and exported for its UV layout.  
*Requires a `MeshFilter` or `SkinnedMeshRenderer`.*

- **Auto-registration**: If the target list is empty when the window opens, GameObjects selected in the Hierarchy are added automatically.
- **Drag & Drop**: Drag GameObjects from the Hierarchy over the card to highlight it in blue, then drop to add.
- **`Add Selected`**: Adds the GameObjects currently selected in the Hierarchy.
- **`Clear`**: Removes all registered targets.
- **Remove Row (`×`)**: Removes that specific entry from the list.
- **Mesh Info**: Displays the count of detected meshes and available UV sets.
- **Read/Write Warning & `Enable` Button**: Displays a warning if any mesh has `Read/Write Enabled` turned off. Click **`Enable`** to automatically update the model import settings and enable it immediately.

#### Occluder Meshes (OCCLUDER MESHES)
Registers surrounding objects (scenery, props, clothing pieces, etc.) that should cast shadows onto the targets.  
*Occluder meshes do not receive or output their own baked textures.*

---

### 3. Basic AO Settings Card

Controls core AO baking parameters and ray settings. Click **`↺`** in the top-right corner to reset this card's settings to defaults.

| Setting | Description | Effect of Changing |
|---|---|---|
| **Output Resolution**<br>`(Resolution)` | Resolution of the generated texture (128 / 256 / 512 / 1024 / 2048 / 4096). Default is 2048. | **▲ Increase**: Produces sharper, high-fidelity shadows with increased bake time and file size.<br>*Rendered natively at the chosen resolution without upscale blurring.* |
| **UV Channel**<br>`(UV Channel)` | The mesh UV channel to bake into (Auto / UV0 to UV7). Default is Auto. | **Auto**: Selects the first valid UV channel on the mesh.<br>**Explicit**: Use when baking into a specific set (e.g. UV1 for lightmaps). Meshes missing the chosen UV trigger a warning. |
| **Self Occlusion**<br>`(Self Occlusion)` | Calculates shadows cast by the mesh onto itself (wrinkles, crevices, geometry self-shadowing). Default is ON. | Fundamental setting for giving 3D models depth and volume. |
| **Mutual Occlusion**<br>`(Mutual Occlusion)` | Calculates shadows cast between registered targets and occluders. Default is ON. | Essential when baking shadows cast between layered clothing, accessories, or nearby objects. |
| **Low Resource Mode**<br>`(Low Resource Mode)` | Splits compute dispatches into smaller, yield-protected chunks. Default is OFF. | **ON**: Prevents GPU driver timeout (TDR), PC freezes, and Unity crashes during heavy bakes. Highly recommended for dense high-poly meshes or lower-spec GPUs. |

#### Advanced Settings (▶ Advanced)
Click **`▶ Advanced`** at the bottom of the card to unfold detailed settings. While folded, any settings modified from defaults appear as summary chips (e.g. `Spread 0.50m`).

| Setting | Description | Effect of Changing |
|---|---|---|
| **Shadow Quality**<br>`(Ray Count)` | Number of shadow sampling rays cast per texel (16 to 256, default 64). | **▲ Increase**: Produces smoother, more accurate raw shadows (takes longer).<br>Combined with SVGF denoising, the default value of 64 delivers excellent results. |
| **Shadow Spread**<br>`(Max Distance)` | Maximum distance shadow rays travel (0.01m to 5.0m, default 0.3m). | **▲ Increase**: Considers distant geometry, resulting in wider, softer ambient shadows.<br>**▼ Decrease**: Produces tight, localized shadows in narrow cracks.<br>*Calibrate this value based on your model's actual physical size.* |
| **Ray Bias**<br>`(Ray Bias)` | Offset distance above the surface to launch shadow rays (0.0001m to 0.05m, default 0.002m). | **▲ Increase**: Fixes black specks / acne caused by self-intersection.<br>**▼ Decrease**: Prevents shadows from detaching or floating away from contact points. |
| **Ignore Backfaces**<br>`(Ignore Backfaces)` | Ignores ray hits on polygon backfaces. Default is ON. | **ON**: Prevents erroneous dark spots from interior geometry on watertight meshes.<br>**OFF**: Recommended for thin, single-sided geometry such as hair ribbons or clothing cards. |

---

### 4. Shadow Smoothing Card (SHADOW SMOOTHING)

Applies an edge-preserving À-trous / SVGF filter to eliminate ray-tracing grain while preserving geometric silhouettes and creases.  
Toggle the header checkmark to enable/disable. Click **`↺`** to reset this card.

| Setting | Description | Effect of Changing |
|---|---|---|
| **Smoothness**<br>`(Iterations)` | Number of filter iterations (1 to 5, default 3). | **▲ Increase**: Stronger noise removal. Setting too high may slightly soften very fine crevice shadows. |
| **Shape Retention**<br>`(Sigma Pos)` | Sensitivity of edge preservation with respect to world position differences (0.1 to 5.0, default 1.0). | **▼ Decrease**: Retains sharp corners and stepped geometric transitions.<br>**▲ Increase**: Blurs smoothly across small spatial gaps. |
| **Curve Retention**<br>`(Sigma Nrm)` | Sensitivity of edge preservation with respect to surface normal differences (0.05 to 2.0, default 0.5). | **▲ Increase**: Keeps smooth curved surfaces looking clean and continuous.<br>**▼ Decrease**: Prevents blurring across sharp facet edges and hard angles. |
| **Shadow Contrast**<br>`(Sigma Lum)` | Sensitivity of edge preservation with respect to luminance differences (0.01 to 1.0, default 0.1). | **▼ Decrease**: Preserves high-contrast shadow boundaries.<br>**▲ Increase**: Blends varying shadow depths for a softer look. |

---

### 5. Finishing Touches Card (FINISHING TOUCHES)

Controls post-processing, edge expansion, and final tinting. Click **`↺`** to reset this card.

| Setting | Description | Effect of Changing |
|---|---|---|
| **Edge Filling**<br>`(Dilation)` | Distance (in pixels) to extend shadow colors outside UV island borders (0 to 64 px, default 16px). | Prevents dark seam lines caused by texture filtering or mipmaps at a distance. Uses a GPU Jump Flood Algorithm (JFA) for instant execution. |
| **Shadow Color**<br>`(Shadow Color)` | Multiplied tint color for the darkest shadow areas (default: Black `#000000`). | Useful for creating tinted shadow maps (warm/cool ambiance) or preparing textures for multiplicative blending. |
| **Gaussian Blur**<br>`(Gaussian Blur)` | Toggles a post-dilation Gaussian blur pass (default: OFF). | Softens the entire shadow texture for a diffused, ambient look. |
| **Blur Intensity**<br>`(Blur Passes)` | Number of blur iterations (1 to 10, default 1). | Increases the overall blur spread and softness. |

---

### 6. Output Dock (OUTPUT)

Configures destination folder and filename collisions.

- **Output Path**:
  - Left empty (or by clicking **`Auto`**), textures are saved in a `BakedAO` folder next to the model's main texture.
  - Clicking **`Browse…`** lets you choose any folder within the project (`Assets/...`).
- **Overwrite Existing**:
  - **ON**: Replaces existing textures with the same name.
  - **OFF**: Appends an incremental number (e.g. `TextureName 1.png`) if a file already exists.

---

### 7. Footer & Status Bar

- **`Bake` Button**: Starts the baking process. Displays progress percentage while active (e.g. `Baking… 60%`). Disabled if no targets are registered.
- **`Cancel` Button**: Safely halts the bake pipeline.
- **Status Bar (DennokoStatus)**:
  - Displays real-time status updates: "Ready", "Preparing meshes", "Building BVH", "Tracing AO", "Denoising", "Bake Completed", etc.
  - On completion, shows the saved file path and highlights the texture in the Project window.

---

## Frequently Asked Questions (FAQ)

#### Q: Unity freezes or crashes during the bake.
**A:** Dense high-polygon meshes can trigger GPU driver timeout detection (TDR).  
Turn on **"Low Resource Mode"** in the Basic AO Settings card.

#### Q: The shadow map is completely black or washed out white.
**A:** The **"Shadow Spread (Max Distance)"** in Advanced Settings may not match your model's scale.  
For human-scale characters or accessories, try `0.1m - 0.3m`. For buildings or rooms, try `1.0m - 3.0m`.

#### Q: Black specks or moiré acne appear on the mesh surface.
**A:** This is self-shadow acne caused by precision limits on ray origins.  
Slightly increase **"Ray Bias"** under Advanced Settings (e.g. from `0.002` to `0.005`).

#### Q: Dark seam lines appear along UV seams on the model.
**A:** Increase **"Edge Filling (Dilation)"** in Finishing Touches (e.g. `16` to `32`). Filling the margin around UV islands ensures mipmaps don't sample black background pixels.

#### Q: Can I bake into UV1 instead of UV0?
**A:** Yes. Select `UV1` in the **"UV Channel"** dropdown in the Basic AO Settings card.
