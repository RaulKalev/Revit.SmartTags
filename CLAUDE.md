# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

## Project Overview

SmartTags is a Revit add-in that assists with intelligent tag placement and configuration. The add-in supports both Revit 2024 (.NET Framework 4.8) and Revit 2026 (.NET 8.0-windows) through multi-targeting.

### Core Features (Implemented)
- **Active Selection Tagging Mode**: Click-to-tag workflow with category filtering and duplicate detection
- **Direction-Based Tag Type Override**: Automatic tag type selection based on placement direction (Left/Right/Up/Down)
- **Intelligent Collision Detection**: Automatic tag positioning to avoid overlapping with elements and other tags
- **Dynamic Safe Distance Calculation**: Element-size-aware minimum offset to prevent tags on host elements
- **Retag/Normalize Workflow**: Batch update existing tags with user confirmation option
- **Direction Parameter Auto-Loading**: Automatic detection and application of direction-based tag types
- **Skip Already Tagged**: Option to avoid duplicate tags in Active Selection mode

---

## Architecture Constraints

### Multi-Threading Requirements
- **Command → ExternalEvent → Service separation is mandatory**
- No Revit API calls from UI thread
- All model changes must occur inside ExternalEvent handlers
- Use `ISelectionFilter` for interactive selection workflows

### Multi-Targeting
- Revit 2024: net48 (C# 7.3)
- Revit 2026: net8.0-windows (C# latest)
- Use `#if` preprocessor directives where Revit API differences exist
- Test both frameworks after significant changes

### Code Quality Standards
- Keep methods small and focused
- Maintain separation of concerns
- No emojis in code or comments
- Preserve existing functionality unless explicitly removing
- Clear, descriptive variable and method names

---

## Build & Development Commands

### Building the Project
```bash
# Build for all target frameworks (net48 and net8.0-windows)
dotnet build SmartTags.csproj

# Build for specific framework
dotnet build SmartTags.csproj -f net48
dotnet build SmartTags.csproj -f net8.0-windows

# Build the solution
dotnet build SmartTags.sln
```

---

## Potential Improvements & Future Work

### High Priority

#### 1. Active Selection Compact Window
**Status**: Attempted but caused Revit crashes due to WPF window creation issues

**Goal**: Show a small floating window during Active Selection mode instead of keeping the full main window visible

**Challenges**:
- Creating child windows in Revit add-in context is unstable
- `AllowsTransparency` and custom window chrome cause crashes
- Setting `Owner` property doesn't prevent crashes
- Minimizing/hiding main window also caused issues

**Potential Solutions**:
- Use Revit's dockable pane API instead of WPF windows
- Keep main window visible but add compact mode toggle
- Use WPF popup/overlay instead of separate window
- Investigate WinForms implementation as alternative

#### 2. Performance Optimization for Large Datasets
**Issue**: Collision detection can be slow with 100+ tags or dense element layouts

**Improvements Needed**:
- Spatial indexing (quadtree/R-tree) for obstacle lookup
- Parallel processing for batch tag operations
- Progressive rendering/feedback for long operations
- Caching of element bounding boxes

#### 3. Enhanced Error Handling & User Feedback
**Current State**: Some operations fail silently or with generic error messages

**Improvements**:
- Detailed validation messages before tag placement
- Progress bars for batch operations
- Better error recovery (partial success reporting)
- Warning when tag type doesn't support selected orientation
- Preview mode showing where tags will be placed

### Medium Priority

#### 4. Tag Alignment & Distribution Tools
**Features**:
- Align selected tags (left, right, top, bottom, center)
- Distribute tags evenly along axis
- Match tag spacing from reference
- Snap tags to grid

#### 5. Configuration Presets & Templates
**Features**:
- Save/load tag placement settings as named presets
- Export/import configuration to share across projects
- Default presets for common tag types
- Per-category default settings

#### 6. Batch Tag Styling Operations
**Features**:
- Change tag type for multiple selected tags
- Bulk update leader settings (enable/disable, length)
- Apply rotation to selected tags
- Lock/unlock tag positions

#### 7. Tag Validation & Cleanup Tools
**Features**:
- Find orphaned tags (tagged element deleted)
- Find duplicate tags on same element
- Find tags outside view bounds
- Validate tag type compatibility with element category

### Low Priority

#### 8. Additional Tag Type Support
**Current**: Primarily tested with generic annotation tags

**Expand to**:
- Room tags with room name/number handling
- Door/window tags with schedule marks
- Section/elevation markers
- Area tags
- Keynote tags

#### 9. Tag Legend/Schedule Generation
**Features**:
- Generate tag legend showing all tag types used in view
- Create schedule of tagged vs untagged elements
- Tag count statistics per category
- Export tag placement report

#### 10. Advanced Collision Avoidance
**Enhancements**:
- Multi-pass collision resolution with priority weights
- Prefer certain directions over others
- Avoid placing tags over specific elements (annotation, detail items)
- Respect view-specific annotation crop boundaries
- Intelligent leader routing around obstacles

#### 11. Undo/Redo Support
**Features**:
- Track tag operations for undo
- Redo previously undone operations
- Undo history viewer
- Batch undo for multi-tag operations

#### 12. Tag Anchoring Options
**Features**:
- Pin tags to specific element points (corners, edges, center)
- Maintain tag offset when element moves
- Update tag position when element rotates
- Support for tagged element parameter changes

---

## Known Issues & Technical Debt

### WPF ComboBox Behavior
- Mouse click selection was unreliable (FIXED in commit d049ccc)
- Mouse wheel scrolling worked but click didn't update bound properties
- Root cause was complex WPF event handling in custom styles

### Collision Detection Edge Cases
- Tags can still collide in very dense layouts when no valid position exists
- Projection constraint was removed to fix left/right detection issues
- May need smarter fallback strategies when all positions blocked

### Direction Override Rotation
- Element rotation detection + direction override interaction is complex
- Tags only rotate when BOTH features enabled to prevent unexpected behavior
- May need separate toggle for "always align with element" vs "use direction rotation"

### Thread Safety
- All Revit API access must be on Revit's main thread via ExternalEvent
- Some operations use polling loops waiting for event completion
- Could be improved with async/await patterns where supported

---

## Testing Notes

### Manual Test Scenarios
1. **Active Selection Mode**
   - Select category, click "Start Active Selection"
   - Click multiple elements, verify tags created
   - Press ESC twice, verify mode exits
   - Test "Skip if already tagged" checkbox

2. **Direction Override**
   - Select category with direction tag variants
   - Enter keyword, click "Check"
   - Place tags in all 4 directions, verify correct types used
   - Test with and without "Detect element rotation"

3. **Collision Detection**
   - Tag dense cluster of elements
   - Verify tags don't overlap elements or each other
   - Test with different gap settings
   - Verify safe distance for tags without leaders

4. **Retag Operations**
   - Change settings (direction, leader, angle)
   - Use "Retag Selected" or "Normalize View"
   - Test both automatic and user confirmation modes
   - Verify tags update correctly

### Framework-Specific Testing
- Test both net48 (Revit 2024) and net8.0-windows (Revit 2026)
- Verify ElementId API changes handled correctly
- Check deprecated API usage warnings

---

## Git Workflow

### Commit Message Format
Follow existing style: short imperative verb phrase describing the change

Examples:
- `Add Active Selection Tagging Mode`
- `Fix collision detection tracking`
- `Remove projection constraint from collision-repositioned tags`

### Branch Strategy
- `master` is the main development branch
- Create feature branches for major new features
- Commit directly to master for bug fixes and small changes

---

## References

### Revit API Documentation
- [Revit API Developer's Guide](https://www.revitapidocs.com/)
- [IndependentTag Class](https://www.revitapidocs.com/2024/b294f7f7-9bbc-8ad5-7b16-3d6a83f77c97.htm)
- [ExternalEvent Pattern](https://thebuildingcoder.typepad.com/blog/2013/12/replacing-an-idling-event-handler-by-an-external-event.html)

### WPF Resources
- [Material Design In XAML Toolkit](http://materialdesigninxaml.net/)
- WPF custom window chrome and transparency best practices
