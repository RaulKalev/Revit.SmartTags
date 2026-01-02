# SmartTags

SmartTags is a Revit add-in that replaces the default tag placement workflow with a focused UI for category/tag selection, leader controls, orientation + rotation overrides, placement direction presets, and intelligent collision-aware tag placement.

## Features

- **Smart Tag Placement**: Select an element category and matching tag family/type from the current model.
- **Leader Controls**: Configure leader usage, length, type (Attached/Free end), orientation, and angle when placing tags.
- **Placement Direction**: Pick a placement direction (Up/Down/Left/Right) with a dedicated card for leader and direction controls.
- **User Preferences**: Save/load preferences including theme, window size, last selections, leader length/angle, and direction.
- **Batch Operations**: Tag all elements of the chosen category in the active view or just the current selection with the same settings.
- **Element Rotation Detection**: Automatically detect element rotation and apply it to both tag rotation and placement offsets.
- **Collision Detection**: Intelligent collision avoidance system that prevents tags from overlapping with visible elements, existing tags, and text annotations:
  - Automatic tag size detection using actual bounding boxes
  - Post-creation validation and repositioning for optimal placement
  - Configurable gap buffer (default 1mm) between tags and obstacles
  - Radial search algorithm finds the closest collision-free position
  - Respects minimum offset when leaders are disabled
  - Works with both "Tag All" and "Tag Selected" workflows

## Supported Revit Versions

SmartTags uses multi-targeting to support multiple Revit versions:
- **Revit 2024**: .NET Framework 4.8 (`net48`)
- **Revit 2026**: .NET 8.0 Windows (`net8.0-windows`)

The add-in should also work with newer Revit versions as long as the API remains compatible.

## Installation

1. Build the solution (`SmartTags.sln`) with Visual Studio or the .NET CLI (`dotnet build SmartTags.sln`)
2. Copy the appropriate build output:
   - For Revit 2024: Copy from `bin\Debug\net48\` to `%APPDATA%\Autodesk\Revit\Addins\2024`
   - For Revit 2026: Copy from `bin\Debug\net8.0-windows\` to `%APPDATA%\Autodesk\Revit\Addins\2026`
3. Create an `.addin` manifest that loads `SmartTags.dll` and place it in the same addins folder
4. Launch Revit; the SmartTags ribbon command opens the custom placement window
5. Configure collision detection settings in the "Collision Detection" section:
   - Enable/disable collision avoidance
   - Set gap buffer between tags and obstacles (default: 1mm)
   - Configure minimum offset when leaders are disabled (default: 300mm)
6. Use the `Tag all`/`Tag Selected` buttons to place tags according to your saved settings

## How Collision Detection Works

The collision detection system uses a sophisticated two-pass approach:

1. **Initial Placement**: Tags are placed at the intended position with an estimated size check
2. **Validation Pass**: After creation, the actual tag bounding box is measured
3. **Repositioning**: If a collision is detected with the actual size, the tag is automatically repositioned to the nearest collision-free location
4. **Tracking**: Each tag's actual bounds are tracked to prevent subsequent tags from overlapping

This ensures that tags with varying text lengths are accurately placed without overlaps, while minimizing wasted space.

## License

SmartTags is available under the MIT License. See `LICENSE` for details.
