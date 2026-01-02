# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

# ACTIVE FEATURE IMPLEMENTATION INSTRUCTIONS
## Interactive Active Selection Mode + Direction-Based Tag Type Overrides + ComboBox Fix

You are Claude Code working in the SmartTags Revit add-in repository.

Follow this file exactly.

### Execution mode
Operate in DIRECT IMPLEMENTATION MODE:
- Do not ask for confirmation before creating or modifying files.
- Do not ask design questions unless blocked by ambiguity.
- Implement only the active features described below.
- Make incremental, safe changes. Ensure both target frameworks build after each feature.
- Do not remove existing functionality unless explicitly instructed.

---

## FEATURE 1 — Active Selection Tagging Mode

### Goal
Add an “Active Selection” mode: user clicks elements in the model and SmartTags places a tag for each click.

### UI requirements
In `TagPlacementWindow` add a new card in the right column (appropriate location near other placement actions). The card must include:
- A toggle button (or equivalent) to start/stop **Active Selection Mode**
- A checkbox:
  - Label: **“Skip if already tagged”**
  - Behavior:
    - If checked: if the clicked element already has a tag of the relevant tag category/type (see implementation notes), skip creating a new one.
    - If unchecked: allow creating another tag even if one exists.

### Behavior requirements
- User activates Active Selection Mode from the UI.
- While active:
  - User can click elements in the active view.
  - Only elements of the currently selected **target category** are allowed to be clicked.
  - When a valid element is clicked, create a tag for it immediately using current settings:
    - Leader toggle (enabled/disabled)
    - Leader length setting
    - Leader type/orientation settings already present
    - Direction settings already present
    - Rotation/orientation policy already implemented
    - Collision detection engine already implemented
  - The mode remains active after a tag is placed so the user can continue clicking.
- Exiting:
  - User can exit by pressing **ESC twice** (standard Revit cancel behavior).
  - If the active view changes, or required selections become invalid (no category/tag type), the mode exits safely without crashing.

### Implementation constraints
- Do not use Revit API calls from the WPF thread.
- All model modifications must occur in an ExternalEvent handler.
- Selection filtering must be implemented via `ISelectionFilter` so only elements matching the chosen category are selectable.
- The mode must not spam modal dialogs while active.
- Provide clear UI feedback (e.g., toggle state, label, or status text).

### “Already tagged” detection (skip option)
Implement a reasonable, performant “already tagged” check:
- Must run in Revit API context (ExternalEvent).
- Must check for existing tags in the active view that reference the element.
- Keep it conservative and safe: if unsure, treat as “tag exists” only when clearly confirmed.

---

## FEATURE 2 — Direction-Based Tag Type Override (Left / Right / Up / Down)

### Goal
Add configuration so SmartTags can automatically choose different tag types depending on placement direction, based on a tag *type name mapping*.

This feature is intended for tag families that contain multiple types like:
- “Device Tag - Left”
- “Device Tag - Right”
- “Device Tag - Up”
- “Device Tag - Down”

### UI requirements (below Tag Direction selection)
Add:
1) A TextBox + Check button above the direction ComboBoxes:
- Label: **“Direction keyword source”** (or similar)
- TextBox: user enters a keyword/mapping token (e.g., `Left`, `Right`, `Up`, `Down` usage rules)
- Button: **“Check”**
  - On click, verify that direction variants exist for the selected category/tag family types and report the result to the user (non-intrusive message area or dialog, but do not break flow).

2) Four labeled ComboBoxes:
- Label + ComboBox rows:
  - **Left tag**  [ComboBox]
  - **Right tag** [ComboBox]
  - **Up tag**    [ComboBox]
  - **Down tag**  [ComboBox]

### Population & filtering requirements
- The ComboBoxes must populate based on the **currently selected category** and the available compatible tag types.
- When the user provides a keyword/token in the TextBox and presses **Check**:
  - The system must attempt to resolve candidate tag types for each direction **based on type names**.
  - The direction ComboBoxes should be **filtered** to show only the types that match the expected direction naming rule.
  - If no matches exist for a direction, show it clearly in the Check result (e.g., “Right: no matching tag type found”).

### Selection & application requirements
- When placing a tag:
  - If direction is Left/Right/Up/Down and a matching override type is selected (or resolved), use that tag type.
  - Otherwise fall back to the default tag type currently selected.
- This direction override must apply consistently in:
  - Tag All
  - Tag Selected
  - Active Selection Mode (Feature 1)
  - Any other tag placement workflows that already exist

### Rules (explicit)
- The override source is the **Tag Type Name** (not a parameter on the tag).
- Do not hardcode family names.
- Centralize the resolution logic in a service so it’s not duplicated across handlers.

---

## FEATURE 3 — Fix ComboBox selection bug

### Problem statement
Currently, existing ComboBoxes do not reliably apply a selection when the user clicks an entry with the mouse, but selection works when scrolling with the mouse wheel.

### Goal
Fix ComboBoxes in `TagPlacementWindow` so:
- Mouse click selection updates the underlying bound properties immediately.
- Keyboard navigation works.
- Mouse wheel behavior continues to work.

### Constraints
- Claude must investigate the cause and fix it without rewriting the whole architecture.
- The fix must be consistent across all relevant ComboBoxes in the window (including new ones added in Feature 2).

---

## Global constraints (must follow)
- Command → ExternalEvent → Service separation is mandatory.
- No Revit API calls from UI thread.
- All model changes must occur inside ExternalEvent handlers.
- Preserve multi-targeting:
  - Revit 2024: net48
  - Revit 2026: net8.0-windows
- Use `#if` where Revit API differences exist.
- Do not remove existing functionality.
- Keep code readable and small-method oriented.
- No emojis in code or comments.

---

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
