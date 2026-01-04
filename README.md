# SmartTags

SmartTags is a Revit add-in that replaces the default tag placement workflow with a streamlined UI for intelligent tag placement with collision detection, direction-based tag type selection, and batch operations.

## Features

- **Smart Tag Placement**: Select an element category and matching tag family/type from the current model
- **Unified Placement Controls**: Compact 4-column layout combining direction, anchor point, leader settings, and orientation in one card
- **Active Selection Mode**: Click-to-tag workflow with duplicate detection and category filtering
- **Direction-Based Tag Types**: Automatically select different tag types based on placement direction (Left/Right/Up/Down)
- **Anchor Point Selection**: Choose from 9 anchor positions (corners, edges, center) for precise tag placement
- **Leader Controls**: Configure leader usage, length (with mm suffix), type (Attached/Free end)
- **Orientation & Rotation**: Set tag orientation and rotation angle (with ° suffix), with optional element rotation detection
- **User Preferences**: Persistent settings including window position, theme, card expansion states, and all placement parameters
- **Batch Operations**: Tag all elements or selected elements with the same settings
- **Collision Detection**: Intelligent collision avoidance system with spatial indexing:
  - Automatic tag size detection using actual bounding boxes
  - Post-creation validation and repositioning for optimal placement
  - Configurable gap buffer (default 1mm) between tags and obstacles
  - Radial search algorithm finds the closest collision-free position
  - Deterministic fallback with least-overlap selection when no collision-free position exists
  - Works with all tagging workflows (batch, selection, active selection)
- **Smooth Animations**: Card expansion/collapse animations (0.2s) with window auto-resize
- **Window Position Memory**: Window reopens at the last saved position

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
