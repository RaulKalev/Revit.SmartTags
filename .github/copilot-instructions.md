# SmartTags - AI Coding Agent Instructions

## Project Overview
SmartTags is a Revit add-in for intelligent tag placement with collision detection, direction-based tag type selection, and batch operations. The plugin targets **Revit 2024** (.NET Framework 4.8) and **Revit 2026** (.NET 8.0-windows) through multi-targeting.

## Critical Architecture Constraints

### Multi-Threading & Revit API
- **Mandatory Pattern**: Command → ExternalEvent → Service separation
- All Revit API modifications MUST occur inside `IExternalEventHandler.Execute()`
- UI thread cannot make Revit API calls directly
- Do NOT modernize with async/await patterns - ExternalEvent execution must remain synchronous
- Use `ISelectionFilter` for interactive selection workflows (see [ActiveSelectionTagHandler.cs](../ExternalEvents/ActiveSelectionTagHandler.cs))

### Multi-Targeting Requirements
- Revit 2024: `net48` (C# 7.3 language features only)
- Revit 2026: `net8.0-windows` (modern C#)
- Use `#if NET48` / `#if NET8_0_OR_GREATER` preprocessor directives for version-specific API differences
- Test both frameworks after significant changes: `dotnet build -f net48` and `dotnet build -f net8.0-windows`

### Development Environment (Ricaun App Loader)
- **No Revit restarts needed** - Plugin loads dynamically via Ricaun App Loader during development
- After building, the add-in automatically reloads in the same Revit session
- Code must tolerate multiple load/unload cycles per session
- Avoid static state that isn't explicitly reset between loads
- Do NOT assume standard add-in startup behavior (static initializers run only once)

## Core Service Architecture

### TagCollisionDetector ([Services/TagCollisionDetector.cs](../Services/TagCollisionDetector.cs))
**Purpose**: Spatial collision detection with performance optimization
- `SpatialIndex2D`: Uniform grid for O(1) broad-phase filtering
- `CollectObstaclesExcludingTags()`: Exclude specific tags to prevent self-collision (critical for Retag workflow)
- `FindValidPosition()`: Radial search with deterministic fallback (never random)
- `SelectLeastOverlapCandidate()`: When no collision-free position exists, choose minimal overlap
- Performance: Spatial indexing reduces collision checks by ~90% in dense views

### TagTypeOrientationResolver ([Services/TagTypeOrientationResolver.cs](../Services/TagTypeOrientationResolver.cs))
**Purpose**: Separates tag type selection from rotation logic
- `ResolveTagType()`: Determines tag type based on direction override settings (Left/Right/Up/Down)
- `ResolveOrientation()`: Calculates rotation angle independently from tag type
- `ResolveOffsetDirection()`: Determines placement offset vector
- **Key Distinction**: Direction types vs. tag rotation are separate concerns

### TagAdjustmentService ([Services/TagAdjustmentService.cs](../Services/TagAdjustmentService.cs))
**Purpose**: Retag/Normalize workflow with collision avoidance
- Creates per-tag collision detector instances to prevent self-collision
- Uses `IsSignificantChange()` to filter unnecessary updates
- Supports user confirmation mode and auto-apply mode

### AnchorPointService ([Services/AnchorPointService.cs](../Services/AnchorPointService.cs))
**Purpose**: Determines tag placement anchor points on elements
- Supports Center, Top, Bottom, Left, Right anchor strategies
- Handles element-specific geometry (walls, ducts, pipes, structural elements)

## ExternalEvent Handlers

All model-modifying operations use this pattern:
```csharp
public class MyHandler : IExternalEventHandler
{
    // Properties set by UI thread
    public ElementId ElementToProcess { get; set; }
    
    // Execute runs on Revit's main thread - safe for API calls
    public void Execute(UIApplication app)
    {
        using (var transaction = new Transaction(doc, "Operation"))
        {
            transaction.Start();
            // ... Revit API operations ...
            transaction.Commit();
        }
    }
    
    public string GetName() => "MyHandler";
}
```

**Key Handlers**:
- `ActiveSelectionTagHandler`: Click-to-tag workflow with duplicate detection
- `TagPlacementHandler`: Batch "Tag All" / "Tag Selected" operations
- `RetagApplyHandler` + `RetagConfirmationHandler`: Normalize existing tags with preview

## UI Structure

### TagPlacementWindow ([UI/TagPlacementWindow.xaml.cs](../UI/TagPlacementWindow.xaml.cs))
- Material Design WPF with custom title bar ([UI/TitleBar.xaml](../UI/TitleBar.xaml))
- Singleton pattern - surfaces existing window instead of creating duplicates (see [Commands/SmartTagsCommand.cs](../Commands/SmartTagsCommand.cs))
- Persists preferences to `C:\ProgramData\RK Tools\SmartTags\config.json`
- Active Selection mode uses `CategorySelectionFilter` for interactive element picking

### Build Commands
```bash
# Build all targets (net48 + net8.0-windows)
dotnet build SmartTags.sln

# Build specific framework
dotnet build SmartTags.csproj -f net48
dotnet build SmartTags.csproj -f net8.0-windows

# Output locations for manual deployment:
# Revit 2024: bin\Debug\net48\SmartTags.dll
# Revit 2026: bin\Debug\net8.0-windows\SmartTags.dll
```

## Project-Specific Conventions

### Naming Patterns
- ExternalEvent handlers: `*Handler.cs` in [ExternalEvents/](../ExternalEvents/)
- Services: `*Service.cs` in [Services/](../Services/)
- Models: Data classes in [Models/](../Models/) (DTOs, view models, proposals)
- UI: WPF windows/controls in [UI/](../UI/)

### Direction System
- `PlacementDirection` enum: `Left`, `Right`, `Up`, `Down` (view-plane coordinates, NOT world axes)
- Direction keyword: User-configurable string to identify direction-specific tag types by name (e.g., "LEFT", "RIGHT")
- Direction types are OPTIONAL - tag placement works without them

### Collision Detection Flow
1. **Pre-placement**: Estimate tag size (~600mm × 200mm conservative)
2. **Post-creation**: Measure actual bounding box
3. **Validation**: Check for collisions with actual size
4. **Repositioning**: If collision detected, find nearest valid position via radial search

### Critical Feature List (DO NOT RE-IMPLEMENT)
These features are production-critical and complete unless explicitly requested:
- Active Selection mode with duplicate detection
- Direction-based tag type override system
- Spatial-indexed collision detection with deterministic fallback
- Dynamic safe distance calculation
- Retag/Normalize workflow with self-collision exclusion
- Direction parameter auto-loading from tag type names

## Change Scope Rules
- Only modify code directly required for the current task
- Do NOT refactor for "clarity" or "best practices" unless explicitly requested
- When changes risk affecting collision detection, direction overrides, or retag workflows: **minimize and localize changes**
- Prefer extending existing services over creating parallel logic

## Common Integration Points
- **Revit API**: All handlers use `UIApplication.ActiveUIDocument.Document`
- **Material Design**: Uses MaterialDesignThemes.Wpf for UI styling
- **Costura.Fody**: Embeds dependencies at build time (configured in [FodyWeavers.xml](../FodyWeavers.xml))
- **ricaun.Revit.UI**: Ribbon button creation and AppLoader support

## Testing Approach
- Manual testing in Revit (no automated test framework)
- Use Ricaun App Loader for rapid iteration without Revit restarts
- Test both `net48` and `net8.0-windows` builds in respective Revit versions
- Verify collision detection in dense views (100+ elements)

## References
- Source documentation: [CLAUDE.md](../CLAUDE.md) (comprehensive developer guide)
- User documentation: [README.md](../README.md)
- Revit API: Autodesk.Revit.DB, Autodesk.Revit.UI
