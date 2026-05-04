# Fast AO Baker Usage Guide

## 1. Tool Overview
Fast AO Baker is a Unity Editor tool designed to compute and save "Ambient Occlusion (AO)" maps as textures. 
Using GPU acceleration, it quickly creates masks that represent soft shadows in corners.

## 2. Basic Workflow
1.  **Register Models**: Drag and drop the objects you want to bake into the `TARGET` section.
2.  **Adjust Settings**: Change settings like shadow density or resolution if needed (defaults usually work fine).
3.  **Run Bake**: Click the `Bake Now` button at the bottom.
4.  **Check Result**: Once finished, textures will be generated in the same folder as the model's main texture (or your specified folder).

## 3. Feature Descriptions
### TARGET
*   **Add Target**: Objects registered here will get their own baked textures. Requires a MeshFilter or SkinnedMeshRenderer.

### OCCLUDER MESHES
*   **Add Occluder**: Register additional objects that should cast shadows onto your targets (e.g., surrounding environment or props).

### BASIC AO SETTINGS
*   **Self Occlusion**: Calculates shadows that the object casts on itself.
*   **Mutual Occlusion**: Calculates shadows cast between all registered target objects.
*   **Low Resource Mode**: Turn this on if Unity crashes or your PC freezes during baking.

### ADVANCED SETTINGS
*   **Shadow Quality (Ray Count)**: Affects how clean the shadows look. Higher is better.
*   **Shadow Spread (Max Distance)**: Controls how far away an object can be to cast a shadow.

### SHADOW SMOOTHING
*   Settings to smooth out grainy shadows. Defaults are usually sufficient for a clean look.


### OUTPUT SETTINGS
*   **Output Resolution**: The size of the saved image.
*   **Output Folder**: If left empty, a `BakedAO` folder will be created near the source model.
*   **UV Channel**: Select which UV set of the mesh to use for baking. "Auto" will automatically select the first valid channel. You can specify a channel (e.g., UV1 for lightmaps) if needed. Only UV channels actually present on the selected meshes will be shown.
*   **Overwrite Existing**: Whether to replace old files or create new ones.

### FINISHING TOUCHES
*   **Edge Filling (Dilation)**: Expands shadows outward to prevent seams from appearing on the model.
*   **Shadow Color**: Customizes the color of the dark areas.
*   **Gaussian Blur**: Blurs the result for a softer, more diffused appearance.

## 4. Q&A
**Q: Unity freezes during baking.**
A: Try enabling `Low Resource Mode`. For extremely high-poly models, it may still take some time.

**Q: Shadows are completely black or white.**
A: Adjust the `Max Distance` value. It needs to be calibrated based on the physical size of your model.

**Q: Strange lines appear at the texture seams.**
A: Increase the `Dilation` (Edge Filling) value to fill the gaps between UV islands.
