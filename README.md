# SmartTags

SmartTags is a Revit add-in that replaces the default tag placement workflow with a focused UI for category/tag selection, leader controls, orientation + rotation overrides, placement direction presets, and collision-aware behavior.

## Features

- Select an element category and matching tag family/type from the current model.
- Configure leader usage, length, type (Attached/Free end), orientation, and angle when placing tags.
- Pick a placement direction (Up/Down/Left/Right) with a dedicated card for leader and direction controls.
- Save/load user preferences (theme, window size, last selections, leader length/angle, direction).
- Tag all elements of the chosen category in the active view or just the current selection with the same settings.
- Detect element rotation and apply it to both tag rotation and placement offsets.
- Include future collision detection controls (planned) to avoid overlapping host geometry.

## Supported Revit Versions

SmartTags is compiled against .NET Framework 4.8 with the Revit 2024+ API assemblies, so it is intended for Revit 2024 and later versions (builds may also run in newer releases as long as the API remains compatible).

## Installation

1. Build the solution (`SmartTags.sln`) with Visual Studio using the Revit 2024+ reference assemblies.
2. Copy `SmartTags.dll` and its dependencies from `bin\Debug\net48` to your Revit add-ins folder (e.g., `%APPDATA%\Autodesk\Revit\Addins\2024`).
3. Create an `.addin` manifest that loads `SmartTags.dll` and place it next to the DLL.
4. Launch Revit; the SmartTags ribbon command opens the custom placement window.
5. Use the `Tag all`/`Tag Selected` buttons to place tags according to your saved settings.

## License

SmartTags is available under the MIT License. See `LICENSE` for details.

## Current Plan

1. Review the current tag placement flow to find insertion points for a collision-avoidance pass (tag all/selected, placement direction, leader length/angle, view scale).
2. Define the collision model: treat all visible elements and existing tags as obstacles, compute their view-plane bounds, and provide a configurable gap (default 1 mm, convertible to internal units).
3. Design a candidate-search strategy that starts at the intended offset and fans out laterally around the anchor to locate the closest valid head position, falling back to the existing behavior if necessary.
4. Implement collision checks and the candidate search inside the placement handler, using 2D projections and respecting the configured gap.
5. Add UI/persistence controls for enabling collision detection and tuning the gap, then validate across both `Tag all` and `Tag Selected` scenarios.
