# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SmartTags is a Revit add-in that assists with tag placement and configuration. The add-in supports both Revit 2024 (.NET Framework 4.8) and Revit 2026 (.NET 8.0-windows) through multi-targeting.

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

### Output Location
The project uses a custom output path: `C:\Users\mibil\OneDrive\Desktop\DevDlls\SmartTags`
This is configured in the `<BaseOutputPath>` property in SmartTags.csproj.

### Revit API References
- Revit 2024 (net48): References DLLs from `E:\Autodesk\Revit 2024\`
- Revit 2026 (net8.0-windows): References DLLs from `E:\Revit 2026\`

## Architecture Overview

### Command-Event-Service Pattern
The add-in follows a strict separation between UI commands, external events, and business logic:

1. **Commands** (`Commands/`): Entry points triggered by Revit ribbon buttons
   - `SmartTagsCommand.cs`: Opens the Tag Placement window (singleton pattern)
   - Uses Win32 interop to properly parent windows to Revit's main window

2. **External Events** (`ExternalEvents/`): Handlers that execute Revit API operations
   - Legacy sheet-related handlers still exist but are not wired to the current tag UI
   - External events are required because Revit API operations must run on Revit's main thread

3. **Services** (`Services/`): Business logic separated from UI
   - `RevitService.cs`: Legacy sheet/schedule helpers (not used by the current tag UI)

### UI Architecture

WPF windows with Material Design theming:

- **TagPlacementWindow** (`UI/TagPlacementWindow.xaml`): Main interface
  - Category and tag type selection
  - Leader settings (length, line toggle, type)
  - Theme toggling

- **TitleBar** (`UI/TitleBar.xaml`): Reusable custom title bar component
  - Window dragging
  - Minimize/close buttons
  - Consistent styling across windows

Theme system uses dynamic resource dictionaries that can switch between light/dark modes at runtime.

### Dependency Management

The project uses Costura.Fody to embed dependencies into the output DLL, configured via `FodyWeavers.xml`. This ensures the add-in is distributed as a single DLL without dependency conflicts.

Key dependencies:
- MaterialDesignThemes/MaterialDesignColors: UI theming
- ricaun.Revit.UI: Revit ribbon/UI helpers
- netDxf, Newtonsoft.Json: Utility libraries

## Common Patterns

### Adding a New Command
1. Create command class in `Commands/` implementing `IExternalCommand`
2. Add `[Transaction(TransactionMode.Manual)]` attribute
3. Register in `App.cs` OnStartup via ribbon button
4. Create corresponding external event handler if Revit API operations needed
