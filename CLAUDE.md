# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

# ACTIVE FEATURE IMPLEMENTATION INSTRUCTIONS
## Retag / Normalize Existing Tags Workflow (SmartTags)

You are Claude Code working in an existing Revit add-in project named **SmartTags**.

### GOAL
Implement a **Retag / Update existing tags** workflow so SmartTags becomes a maintenance tool, not just a placement tool.

Two execution modes must be supported:
1) **Fully Automatic**
   - Apply all adjustments immediately.
   - Notify the user with a summary of results.
2) **User Confirmation**
   - Step through each proposed adjustment one-by-one.
   - Focus the view on the affected location.
   - Ask the user to **Accept / Reject / Cancel**.
   - Changes are applied immediately.
   - If rejected, that specific change must be reverted to its previous state.
   - Cancel stops the process and reverts the current candidate only.

---

### UI REQUIREMENTS
- Add a new **card in the right column, at the bottom** of `TagPlacementWindow`.
- Card contents:
  - Two **mutually exclusive** checkboxes:
    - “Fully automatic”
    - “User confirmation”
  - Buttons:
    - “Retag Selected”
    - “Normalize View”
- Default mode: **Fully automatic** (unless user preferences say otherwise).
- Persist the selected mode in user preferences.

---

### FUNCTIONAL REQUIREMENTS

#### A) Retag Selected
- Use the current element selection in the active view.
- Find all **SmartTags-managed tags** that reference those elements.
- For each tag:
  - Compute a new optimal placement using current SmartTags settings:
    - Placement direction
    - Leader settings
    - Rotation / orientation policy
    - Collision detection engine
  - If the computed placement is unchanged within tolerance → skip.
- Execution:
  - **Fully automatic**
    - Apply all changes in a single transaction.
    - Show summary: adjusted / unchanged / skipped / failed.
  - **User confirmation**
    - For each proposed adjustment:
      - Focus the view to the tag location.
      - Prompt: *Apply this adjustment?*  
        `[Accept] [Reject] [Cancel]`
      - Accept → keep change
      - Reject → revert that tag to its previous state
      - Cancel → stop; revert current candidate only

#### B) Normalize View
- Collect **all SmartTags-managed tags in the active view**.
- Run the same logic as Retag Selected.
- Support both execution modes.

---

### SMARTTAGS MARKER (MANDATORY)
- SmartTags must reliably identify tags it manages.
- Use **Extensible Storage on the tag element**.
- Do **not** modify tag families or require shared parameters.
- Stored metadata must include:
  - Schema GUID
  - Plugin name + version
  - Creation timestamp
  - Referenced element id (if available)
  - `managed = true`
- All new tags placed by SmartTags must write this marker.
- Existing tags without this marker are considered unmanaged and skipped.

---

### ARCHITECTURE CONSTRAINTS
- Follow **Command → ExternalEvent → Service** separation strictly.
- No Revit API calls from WPF/UI thread.
- All model changes must occur in ExternalEvent handlers.
- Confirmation workflow must use an **ExternalEvent-driven state machine**.
- UI dialogs must be parented to Revit’s main window using existing Win32 helpers.
- Respect existing Material Design theming and window patterns.

---

### IMPLEMENTATION ORDER (MANDATORY)

1) **Data model**
- Create `TagAdjustmentProposal`:
  - TagId
  - ReferencedElementId
  - OldStateSnapshot
  - NewStateProposal
  - Reason / notes
- Create `TagStateSnapshot` capturing everything needed to revert:
  - TagHeadPosition
  - Leader state that is modified
  - Rotation/orientation that is modified
  - Any parameters that are modified

2) **Extensible Storage**
- Implement `SmartTagMarkerStorage`:
  - EnsureSchema()
  - SetManagedTag()
  - IsManagedTag()
  - TryGetMetadata()
- Integrate marker writing into existing tag placement workflows.

3) **Tag discovery**
- Implement service method:
  - `FindTagsReferencingElements(Document doc, View view, ICollection<ElementId> elementIds)`
- Filter:
  - Active view only
  - Managed tags only
- Handle Revit 2024 / 2026 API differences using `#if`.

4) **Adjustment computation**
- Compute proposed placements **without modifying the model**.
- Reuse the existing collision detection engine.
- Skip unchanged placements using tolerance (~0.5 mm).

5) **Automatic application**
- ExternalEvent handler:
  - `RetagApplyHandler`
- One transaction per operation:
  - “SmartTags: Retag”
  - “SmartTags: Normalize View”
- Apply all proposals, collect results, return summary to UI.

6) **Confirmation workflow**
- Implement `RetagConfirmationController` (UI-level coordinator).
- Use two ExternalEvent handlers:
  - `ApplySingleProposalHandler`
  - `RevertSingleProposalHandler`
- Flow:
  - Apply proposal → focus view → ask user
  - Reject → revert snapshot
  - Cancel → revert current and stop
- Never leave the model in an unknown or partial state.

7) **UI + Preferences**
- Add card to bottom-right of `TagPlacementWindow`.
- Enforce checkbox exclusivity in code.
- Persist selected execution mode in preferences.

8) **Safety & Edge Cases**
- Handle deleted tags or elements gracefully.
- Skip invalid references.
- Avoid infinite loops.
- Cache obstacles per run where possible.

---

### CODING RULES
- Keep methods small and readable.
- Do not remove existing functionality.
- Preserve multi-targeting for Revit 2024 / 2026.
- Use explicit `System.Windows.Visibility.Visible/Hidden` if applicable.
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
