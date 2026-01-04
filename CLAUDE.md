# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

## Interaction Expectations
- Prefer concise explanations over long essays.
- When suggesting changes, explain *why* briefly.
- Do not repeat information already stated in this file.
- Assume the user understands Revit API fundamentals.

## Project Overview

SmartTags is a Revit add-in that assists with intelligent tag placement and configuration. The add-in supports both Revit 2024 (.NET Framework 4.8) and Revit 2026 (.NET 8.0-windows) through multi-targeting.

### Core Features (Implemented)
- **Active Selection Tagging Mode**: Click-to-tag workflow with category filtering and duplicate detection
- **Direction-Based Tag Type Override**: Automatic tag type selection based on placement direction (Left/Right/Up/Down)
- **Intelligent Collision Detection**: Automatic tag positioning to avoid overlapping with elements and other tags
  - Spatial indexing (uniform grid) for performance in dense views
  - Smart fallback with least-overlap selection when no collision-free position exists
  - Deterministic placement (never random)
- **Dynamic Safe Distance Calculation**: Element-size-aware minimum offset to prevent tags on host elements
- **Retag/Normalize Workflow**: Batch update existing tags with user confirmation option
  - Excludes each tag from self-collision while still avoiding other tags
- **Direction Parameter Auto-Loading**: Automatic detection and application of direction-based tag types
- **Skip Already Tagged**: Option to avoid duplicate tags in Active Selection mode
- **Separated Tag Type & Orientation Logic**: Clear separation between tag type selection and rotation/orientation

> ⚠️ IMPORTANT  
> All features listed above are **complete and production-critical**.  
> Claude must **not re-implement, refactor, or redesign** these features unless the user explicitly requests changes to them.

---

## Architecture Constraints

### Multi-Threading Requirements
- **Command → ExternalEvent → Service separation is mandatory**
- No Revit API calls from UI thread
- All model changes must occur inside ExternalEvent handlers
- Use `ISelectionFilter` for interactive selection workflows
- Do not “modernize” ExternalEvent execution with async/await.  
  Use async only for non-Revit computation; all Revit API work stays synchronous inside ExternalEvent.

### Multi-Targeting
- Revit 2024: net48 (C# 7.3)
- Revit 2026: net8.0-windows (C# latest)
- Use `#if` preprocessor directives where Revit API differences exist
- Test both frameworks after significant changes
- Do not replace existing ExternalEvent polling/completion patterns unless explicitly requested.
- Async/await is only allowed for non-Revit logic (e.g. pure computation, data prep).
- Revit API calls must remain strictly synchronous within ExternalEvent execution.

### Code Quality Standards
- Keep methods small and focused
- Maintain separation of concerns
- No emojis in code or comments
- Preserve existing functionality unless explicitly removing
- Clear, descriptive variable and method names
- All tag placement workflows must use the same core placement pipeline.

### Key Service Classes
- **TagCollisionDetector** (`Services/TagCollisionDetector.cs`): Collision detection with spatial indexing
  - `SpatialIndex2D`: Uniform grid for broad-phase filtering in view-plane coordinates
  - `CollectObstaclesExcludingTags()`: Exclude specific tags from obstacle collection
  - `FindValidPosition()`: Radial search with collision avoidance
  - `SelectLeastOverlapCandidate()`: Deterministic fallback when no collision-free position exists
  - `GetPerformanceDiagnostics()`: Performance logging for optimization verification
- **TagTypeOrientationResolver** (`Services/TagTypeOrientationResolver.cs`): Separates tag type selection from orientation
  - `ResolveTagType()`: Determines tag type based on direction override
  - `ResolveOrientation()`: Calculates rotation angle independently
  - `ResolveOffsetDirection()`: Determines placement offset vector
- **TagAdjustmentService** (`Services/TagAdjustmentService.cs`): Retag/Normalize workflow logic
  - Creates collision detector per tag to prevent self-collision
  - Uses `IsSignificantChange()` to filter out unnecessary updates

## Development & Testing Environment

- The plugin is loaded during development using **Ricaun App Loader**.
- Do not assume standard Revit add-in startup behavior (e.g. static initializers running only once).
- Code must tolerate:
  - Multiple load/unload cycles per Revit session
  - Re-execution without Revit restart
- Avoid relying on:
  - Static state that is not explicitly reset
  - AppDomain-level assumptions
- Startup, command registration, and UI initialization must remain compatible with App Loader behavior.

---

## Change Scope Rule

- Only modify code that is directly required to fulfill the current task.
- Do **not** refactor existing logic “for clarity” or “best practices” unless explicitly requested.
- If a change risks affecting:
  - Active Selection
  - Collision detection
  - Direction override logic
  - Retag/Normalize workflows  
  the change must be **minimal and localized**.
- Prefer extending existing services over introducing new parallel logic.

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

> NOTE  
> Items in this section are **design targets only**.  
> Claude must not implement these unless the user explicitly asks for them.

#### 1. Active Selection Compact Window
**Status**: Attempted but caused Revit crashes due to WPF window creation issues

**Goal**: Show a small floating window during Active Selection mode instead of keeping the full main window visible

**Challenges**:
- Creating child windows in Revit add-in context is unstable
- `AllowsTransparency` and custom window chrome cause crashes
- Setting `Owner` property doesn't prevent crashes
- Minimizing/hiding main window also caused issues

**Implementation Guidance (to avoid Revit crashes)**:
- Prefer **single-window “Compact Mode”** (toggle visibility/layout inside the existing main window) instead of creating a new `Window`.
- Avoid WPF features that commonly crash Revit:
  - `AllowsTransparency=true`
  - custom window chrome (`WindowStyle=None`, custom titlebars) for secondary windows
  - aggressive Owner/Topmost tricks during interactive selection
- If a floating UI is required, prefer:
  1) Compact mode inside the main window
  2) WPF `Popup`/overlay anchored to the main window
  3) Revit-native UI (DockablePane)
  Separate top-level windows are last resort.
- With Ricaun App Loader, ensure any selection-mode UI/event hooks are **fully unsubscribed** on exit (no static event leaks).

**Potential Solutions**:
- Use Revit's dockable pane API instead of WPF windows
- Keep main window visible but add compact mode toggle
- Use WPF popup/overlay instead of separate window
- Investigate WinForms implementation as alternative

#### 2. Enhanced Error Handling & User Feedback
**Current State**: Some operations fail silently or with generic error messages

**Improvements**:
- Detailed validation messages before tag placement
- Progress bars for batch operations
- Better error recovery (partial success reporting)
- Warning when tag type doesn't support selected orientation
- Preview mode showing where tags will be placed

### Medium Priority

#### 3. Tag Alignment & Distribution Tools
**Features**:
- Align selected tags (left, right, top, bottom, center)
- Distribute tags evenly along axis
- Match tag spacing from reference
- Snap tags to grid

#### 4. Configuration Presets & Templates
**Features**:
- Save/load tag placement settings as named presets
- Export/import configuration to share across projects
- Default presets for common tag types
- Per-category default settings

#### 5. Batch Tag Styling Operations
**Features**:
- Change tag type for multiple selected tags
- Bulk update leader settings (enable/disable, length)
- Apply rotation to selected tags
- Lock/unlock tag positions

#### 6. Tag Validation & Cleanup Tools
**Features**:
- Find orphaned tags (tagged element deleted)
- Find duplicate tags on same element
- Find tags outside view bounds
- Validate tag type compatibility with element category

### Low Priority

#### 7. Additional Tag Type Support
**Current**: Primarily tested with generic annotation tags

**Expand to**:
- Room tags with room name/number handling
- Door/window tags with schedule marks
- Section/elevation markers
- Area tags
- Keynote tags

#### 8. Tag Legend/Schedule Generation
**Features**:
- Generate tag legend showing all tag types used in view
- Create schedule of tagged vs untagged elements
- Tag count statistics per category
- Export tag placement report

#### 9. Advanced Collision Avoidance
**Enhancements**:
- Multi-pass collision resolution with priority weights
- Prefer certain directions over others
- Avoid placing tags over specific elements (annotation, detail items)
- Respect view-specific annotation crop boundaries
- Intelligent leader routing around obstacles

#### 10. Undo/Redo Support
**Features**:
- Track tag operations for undo
- Redo previously undone operations
- Undo history viewer
- Batch undo for multi-tag operations

#### 11. Tag Anchoring Options
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

### Collision Detection
- **Smart fallback now implemented**: When no collision-free position exists, selects position with least overlap (FIXED)
- **Normalize self-collision fixed**: Tags no longer treat themselves as obstacles during normalize (FIXED)
- Projection constraint was removed to fix left/right detection issues
- Spatial indexing provides significant performance improvement in dense views

### Direction Override & Rotation
- **Logic now separated** into distinct concerns via `TagTypeOrientationResolver` (IMPROVED)
  - Tag type selection (direction override)
  - Tag orientation (rotation / leader orientation)
- Tags only rotate when BOTH features enabled to prevent unexpected behavior
- Left/Right/Up/Down correctly interpreted in **view coordinates**, not world axes
- Future enhancement: Optional toggle for "Direction types control orientation (no rotation)" vs "Always rotate with element"

### Thread Safety
- All Revit API access must be on Revit's main thread via ExternalEvent
- Some operations use polling loops waiting for event completion
- Could be improved with async/await patterns where supported

### Regression Risks to Watch For
- Breaking collision avoidance when adjusting placement math
- Direction override failing silently when rotation detection is enabled
- Active Selection remaining active after view change
- Leader settings being partially applied (length without orientation)
- Duplicate tags created when skip-logic fails under multi-reference tags
- Normalize causing unnecessary tag movement (monitor `IsSignificantChange` tolerance)
- Spatial index correctness in rotated views (verify view-plane coordinate transforms)

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
   - Check Debug output for performance diagnostics (`GetPerformanceDiagnostics()`)
   - Verify fallback triggers in very dense layouts (should log warning)

4. **Retag Operations**
   - Tag view with existing tags
   - Click "Normalize" without changing settings
   - Verify tags don't move unnecessarily (should show "0 changes" or minimal changes)
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

### Documentation Rule
- Update README.md when:
  - User-facing behavior changes
  - New workflows or modes are introduced
  - Existing workflows are removed or altered
- README updates should happen **in the same commit** as the behavior change when possible.

---

## References

### Revit API Documentation
- [Revit API Developer's Guide](https://www.revitapidocs.com/)
- [IndependentTag Class](https://www.revitapidocs.com/2024/b294f7f7-9bbc-8ad5-7b16-3d6a83f77c97.htm)
- [ExternalEvent Pattern](https://thebuildingcoder.typepad.com/blog/2013/12/replacing-an-idling-event-handler-by-an-external-event.html)

### WPF Resources
- [Material Design In XAML Toolkit](http://materialdesigninxaml.net/)
- WPF custom window chrome and transparency best practices
