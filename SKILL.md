# SKILL.md - SmartTags Development Guide

This file provides practical, context-specific guidance for AI assistants working on the SmartTags Revit add-in.

---

## Quick Start

## Guardrails (Production-Critical)
The following features are complete and must not be re-implemented or refactored unless explicitly requested:
- Active Selection Tagging Mode
- Direction-Based Tag Type Override (Left/Right/Up/Down)
- Collision Detection + Safe Distance
- Retag/Normalize workflow

### Understanding the Codebase
```
SmartTags/
├── Commands/           # Revit IExternalCommand implementations (entry points)
├── ExternalEvents/    # IExternalEventHandler implementations (async Revit API operations)
├── Services/          # Business logic and utilities
├── UI/                # WPF windows and controls
├── Models/            # Data transfer objects and enums
└── SmartTags.csproj   # Multi-targeting project file (net48 + net8.0-windows)
```

### Key Architectural Pattern: Command → ExternalEvent → Service

**Why**: Revit API requires all model modifications on the main thread, but UI operations are async.

**Flow**:
```
User clicks button (UI Thread)
    ↓
Command validates input (UI Thread)
    ↓
ExternalEvent.Raise() (UI Thread → Revit Thread)
    ↓
ExternalEventHandler.Execute() (Revit Thread)
    ↓
Service performs Revit API operations (Revit Thread)
    ↓
Handler sets result properties (Revit Thread)
    ↓
UI reads result state without blocking Revit (UI Thread)
**UI completion patterns (do not block Revit)**:
- ExternalEvent sets result flags + UI checks status via DispatcherTimer
- ExternalEvent posts a callback action to UI (captured SynchronizationContext)
- ExternalEvent writes to a shared result object; UI reads it after completion signal


```

---

## Common Development Tasks

### Task 1: Adding a New Tag Placement Feature

**Steps**:
1. Create handler in `ExternalEvents/` implementing `IExternalEventHandler`
2. Create ExternalEvent in window constructor
3. Add UI controls in `TagPlacementWindow.xaml`
4. Wire button click to configure handler and raise event
5. Implement business logic in handler's `Execute()` method

**Example Pattern**:
```csharp
// In TagPlacementWindow.xaml.cs constructor:
private readonly MyFeatureHandler _myFeatureHandler;
private readonly ExternalEvent _myFeatureEvent;

public TagPlacementWindow(UIApplication app)
{
    _myFeatureHandler = new MyFeatureHandler();
    _myFeatureEvent = ExternalEvent.Create(_myFeatureHandler);
}

// Button click handler:
private void MyFeatureButton_Click(object sender, RoutedEventArgs e)
{
    if (!ValidateSettings()) return;

    // Configure handler with UI settings
    _myFeatureHandler.SomeSetting = GetSettingFromUI();

    // Raise event to execute on Revit thread
    _myFeatureEvent.Raise();
}
```

### Task 2: Adding Multi-Framework Support for New Code

**Pattern for Revit API differences**:
```csharp
#if REVIT2024
    var elementId = new ElementId(intValue);
#else
    var elementId = new ElementId((long)intValue);
#endif
```

**Testing both frameworks**:
```bash
dotnet build SmartTags.csproj -f net48
dotnet build SmartTags.csproj -f net8.0-windows
```

### Task 3: Implementing Interactive Selection

**Always use ISelectionFilter**:
```csharp
// Create filter class
public class CategorySelectionFilter : ISelectionFilter
{
    private readonly ElementId _categoryId;

    public CategorySelectionFilter(ElementId categoryId)
    {
        _categoryId = categoryId;
    }

    public bool AllowElement(Element elem)
    {
        return elem?.Category?.Id == _categoryId;
    }

    public bool AllowReference(Reference reference, XYZ position)
    {
        return false; // Only allow element selection
    }
}

// Use in handler
var filter = new CategorySelectionFilter(targetCategoryId);
var reference = uiDoc.Selection.PickObject(
    ObjectType.Element,
    filter,
    "Click element to tag");
```

### Task 4: Safe Element Access with Bounding Boxes

**Pattern**:
```csharp
private bool TryGetAnchorPoint(Element element, View view, out XYZ anchor)
{
    anchor = null;

    // Try bounding box first (works for most elements)
    var bbox = element.get_BoundingBox(view);
    if (bbox != null)
    {
        anchor = (bbox.Min + bbox.Max) * 0.5;
        return true;
    }

    // Fallback to LocationPoint
    if (element.Location is LocationPoint point)
    {
        anchor = point.Point;
        return true;
    }

    // Fallback to LocationCurve
    if (element.Location is LocationCurve curve)
    {
        var c = curve.Curve;
        if (c != null)
        {
            anchor = (c.GetEndPoint(0) + c.GetEndPoint(1)) * 0.5;
            return true;
        }
    }

    return false;
}
```

---

## Domain-Specific Knowledge

### Revit Coordinate Systems

**View-Based Coordinates**:
- `view.RightDirection`: Horizontal axis in view plane
- `view.UpDirection`: Vertical axis in view plane
- `view.ViewDirection`: Normal to view plane (out of screen)

**Converting Direction to Vector**:
```csharp
private XYZ GetDirectionVector(View view, PlacementDirection direction)
{
    var right = view.RightDirection;
    var up = view.UpDirection;

    switch (direction)
    {
        case PlacementDirection.Up: return up;
        case PlacementDirection.Down: return up.Negate();
        case PlacementDirection.Left: return right.Negate();
        case PlacementDirection.Right:
        default: return right;
    }
}
```

### Revit Units

**Critical**: Revit internal units are **FEET**, not millimeters!

**Conversion**:
```csharp
// Millimeters to feet
double feet = millimeters / 304.8;

// Feet to millimeters
double mm = feet * 304.8;

// Degrees to radians (Revit uses radians)
double radians = degrees * (Math.PI / 180.0);
```

### Tag Creation Best Practices

**Issue**: Tags without leaders ignore `TagHeadPosition`

**Solution**: Create with leader, position, then disable:
```csharp
bool shouldDisableLeader = !hasLeader;
bool createWithLeader = hasLeader || shouldDisableLeader;

var tag = IndependentTag.Create(doc, viewId, reference,
    createWithLeader, TagMode.TM_ADDBY_CATEGORY,
    orientation, headPosition);

tag.TagHeadPosition = headPosition; // Set position

if (shouldDisableLeader)
{
    tag.HasLeader = false; // Now disable if not wanted
}
```

### Collision Detection Algorithm

**Correctness constraints**:
- Collision computations must be in **view-plane coordinates** and remain correct for **rotated views**.
- Bounding boxes often require transforms; do not assume world XYZ aligns with the view.

**When no valid position exists**:
1) If leader enabled: increase leader length in steps and retry.
2) Escape-spiral search around intended point.
3) Choose candidate with **least overlap area** + log a warning (never random/last-candidate).

**Two-Pass Approach**:
1. **Estimated bounds**: Use fixed size estimate for initial placement
2. **Actual bounds**: After tag creation, get real bounds and adjust if needed

**Spatial Search**:
```csharp
// Radial search from anchor point
for (double radius = initialRadius; radius <= maxRadius; radius += step)
{
    for (int i = 0; i < angularSamples; i++)
    {
        double angle = (2π * i) / angularSamples;
        var candidate = anchor +
            right * (radius * cos(angle)) +
            up * (radius * sin(angle));

        if (!HasCollision(candidate))
        {
            return candidate; // Found valid position
        }
    }
}
```

**Key Insight**: Must regenerate document after tag creation to get accurate bounding box.

---

## Testing Strategies

## Ricaun App Loader (Development)
During development the add-in may be loaded/unloaded multiple times per Revit session.
- Unsubscribe ALL static/event handlers on shutdown/exit of modes (Idling, ViewActivated, DocumentChanged, etc.)
- Avoid static caches that assume one-time initialization
- Ensure selection modes exit cleanly on reload

### Manual Testing Workflow

**Setup**:
1. Build project for target Revit version
2. Copy DLL to `%AppData%\Autodesk\Revit\Addins\{Version}\SmartTags\`
3. Copy `.addin` manifest to `%AppData%\Autodesk\Revit\Addins\{Version}\`
4. Launch Revit

**Debugging**:
- Attach Visual Studio debugger to `Revit.exe` process
- Set breakpoints in ExternalEventHandler `Execute()` methods
- Use `System.Diagnostics.Debugger.Launch()` for early breakpoints

### Common Test Cases

**1. Active Selection Mode**
```
Test: Click 5 elements rapidly
Expected: All 5 get tagged without errors
Common failure: Threading issues, event not pending
```

**2. Collision Detection**
```
Test: Tag 20 elements in tight cluster
Expected: No overlapping tags, all readable
Common failure: Tags on top of elements when no valid position
```

**3. Direction Override**
```
Test: Place same element type in all 4 directions
Expected: Different tag types used based on direction
Common failure: Wrong type selected, rotation issues
```

**4. Multi-Framework**
```
Test: Same operation in Revit 2024 and 2026
Expected: Identical behavior
Common failure: ElementId API differences
```

---

## Troubleshooting Guide

### Issue: "Object reference not set to an instance of an object"

**Common Causes**:
1. Accessing UI elements before `InitializeComponent()`
2. Null view or document reference
3. Element deleted between selection and operation

**Debug Pattern**:
```csharp
if (element == null)
{
    System.Diagnostics.Debug.WriteLine("Element is null!");
    return;
}

var bounds = element.get_BoundingBox(view);
if (bounds == null)
{
    System.Diagnostics.Debug.WriteLine($"No bounds for {element.Id}");
    // Use fallback method
}
```

### Issue: "Cannot edit the document while it is in read-only mode"

**Cause**: Attempting Revit API modifications outside transaction

**Fix**: All model changes need Transaction:
```csharp
using (var transaction = new Transaction(doc, "Operation Name"))
{
    transaction.Start();

    try
    {
        // Revit API modifications here
        tag.TagHeadPosition = newPosition;

        transaction.Commit();
    }
    catch
    {
        transaction.RollBack();
        throw;
    }
}
```

### Issue: Tags appear in wrong location

**Causes**:
1. Forgot to call `doc.Regenerate()` after tag creation
2. Using wrong coordinate system (model vs view)
3. Scale factor not applied for view scale

**Fix**:
```csharp
var scaleFactor = Math.Max(1, view.Scale);
var adjustedLength = leaderLength * scaleFactor;

// Create tag
var tag = IndependentTag.Create(...);

// MUST regenerate before getting accurate bounds
doc.Regenerate();

// Now bounds are accurate
var bbox = tag.get_BoundingBox(view);
```

### Issue: ExternalEvent never executes

**Common Causes**:
1. Event already pending
2. Revit in modal dialog
3. Event raised from non-UI thread

**Debug**:
```csharp
// Check if pending
if (_externalEvent.IsPending)
{
    MessageBox.Show("Event already pending!");
    return;
}

// Raise event
_externalEvent.Raise();

**Preferred debugging patterns (avoid blocking UI thread)**:
- Disable the triggering button while an event is in-flight.
- Use a `DispatcherTimer` to poll a handler `Completed` flag.
- Log start/end timestamps inside `Execute()` to confirm execution order.
- If Revit is in a modal state (dialogs), ExternalEvents will not run until it returns to normal UI.

```

---

## Code Patterns & Anti-Patterns

### ✅ DO: Use Try-Pattern Methods

```csharp
// Good: Explicit success/failure handling
if (TryGetAnchorPoint(element, view, out var anchor))
{
    // Use anchor
}
else
{
    // Handle failure gracefully
    continue; // Skip this element
}
```

### ❌ DON'T: Silent Failures

```csharp
// Bad: Swallowing all exceptions
try
{
    tag.TagHeadPosition = newPosition;
}
catch
{
    // User has no idea what went wrong!
}

// Better: At minimum, log the error
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"Failed to set position: {ex.Message}");
}
```

### ✅ DO: Validate Before ExternalEvent

```csharp
// Good: Validate on UI thread before raising event
private void TagAllButton_Click(object sender, RoutedEventArgs e)
{
    if (!ValidateTagSettings())
    {
        return; // Show error to user, don't raise event
    }

    _tagPlacementHandler.Configure(...);
    _tagPlacementExternalEvent.Raise();
}
```

### ❌ DON'T: Complex Logic in Event Handlers

```csharp
// Bad: Business logic mixed with event handling
public void Execute(UIApplication app)
{
    // 200 lines of tag placement logic here
}

// Better: Delegate to service
public void Execute(UIApplication app)
{
    var service = new TagPlacementService();
    var result = service.PlaceTag(settings);

    Success = result.Success;
    Message = result.Message;
}
```

### ✅ DO: Use Descriptive Transaction Names

```csharp
// Good: Clear undo stack entry
using (var t = new Transaction(doc, "SmartTags: Active Selection"))
{
    // ...
}

// Bad: Generic name
using (var t = new Transaction(doc, "Update"))
{
    // ...
}
```

---

## Performance Considerations

### Minimize Document Regeneration

```csharp
// Bad: Regenerate for every tag
foreach (var element in elements)
{
    var tag = CreateTag(...);
    doc.Regenerate(); // SLOW!
}

// Better: Regenerate once after all tags
foreach (var element in elements)
{
    var tag = CreateTag(...);
}
doc.Regenerate(); // Once at end

// Best: Only regenerate when bounds needed
foreach (var element in elements)
{
    var tag = CreateTag(...);

    if (needsCollisionCheck)
    {
        doc.Regenerate(); // Only when necessary
        // Do collision check
    }
}
```

### Cache Expensive Lookups

```csharp
// Bad: Query collector every iteration
foreach (var elementId in elementIds)
{
    var allTags = new FilteredElementCollector(doc, view.Id)
        .OfClass(typeof(IndependentTag))
        .ToList(); // EXPENSIVE!
}

// Good: Collect once
var allTags = new FilteredElementCollector(doc, view.Id)
    .OfClass(typeof(IndependentTag))
    .ToList();

foreach (var elementId in elementIds)
{
    var existingTag = allTags.FirstOrDefault(t => ...);
}
```

---

## WPF/XAML Patterns

### ComboBox Binding Pattern (Preferred)

Use TwoWay binding to a VM property. Avoid `SelectionChanged` for business logic.

```xml
<ComboBox
    ItemsSource="{Binding TagCategories}"
    SelectedItem="{Binding SelectedTagCategory, Mode=TwoWay}"
    DisplayMemberPath="DisplayName"
    IsSynchronizedWithCurrentItem="False" />

```

```csharp
// Code-behind
public ObservableCollection<TagCategoryOption> TagCategories { get; }

private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    var option = e.AddedItems.Count > 0
        ? e.AddedItems[0] as TagCategoryOption
        : null;

    if (option != null)
    {
        // Handle selection
    }
}
```

### Mutual Exclusive CheckBoxes

```csharp
private void PlacementDirection_Checked(object sender, RoutedEventArgs e)
{
    if (!(sender is CheckBox checkBox) || checkBox.IsChecked != true)
    {
        return;
    }

    // Uncheck all others
    PlacementUpCheckBox.IsChecked = false;
    PlacementDownCheckBox.IsChecked = false;
    PlacementLeftCheckBox.IsChecked = false;
    PlacementRightCheckBox.IsChecked = false;

    // Re-check the one that was clicked
    checkBox.IsChecked = true;
}
```

---

## Debugging Techniques

### Visual Debugging with Model Lines

```csharp
// Draw debug line in Revit to visualize vectors
private void DebugDrawLine(Document doc, XYZ start, XYZ end)
{
    using (var t = new Transaction(doc, "Debug Line"))
    {
        t.Start();

        var line = Line.CreateBound(start, end);
        var sketchPlane = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(
            XYZ.BasisZ, XYZ.Zero));

        doc.Create.NewModelCurve(line, sketchPlane);

        t.Commit();
    }
}
```

### Logging Helper

```csharp
private static void Log(string message)
{
    var logPath = @"C:\Temp\SmartTags.log";
    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
    File.AppendAllText(logPath, $"[{timestamp}] {message}\n");
}

// Usage
Log($"Placing tag at {position.X}, {position.Y}, {position.Z}");
```

---

## Git Commit Guidelines

### Good Commit Messages

```
✅ Add collision detection for batch tag operations
✅ Fix leader line positioning when view scale > 100
✅ Remove deprecated ElementId.IntegerValue usage
✅ Update direction override to handle rotated elements

❌ Fixed bug
❌ Updates
❌ WIP
❌ asdf
```

### Commit Message Template

```
{Verb} {what} {optional: why/context}

{Optional: detailed explanation}
{Optional: breaking changes}
{Optional: issue references}

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>
```

---

## API Reference Quick Links

### Most Used Revit APIs

- [IndependentTag](https://www.revitapidocs.com/2024/b294f7f7-9bbc-8ad5-7b16-3d6a83f77c97.htm)
- [FilteredElementCollector](https://www.revitapidocs.com/2024/87a8f548-d7f8-be96-433f-7aa4ba8db089.htm)
- [Transaction](https://www.revitapidocs.com/2024/1f3be462-0cff-0c5a-9c92-df92539ff32c.htm)
- [ExternalEvent](https://www.revitapidocs.com/2024/cb24e156-3d5e-89e1-8e0b-70c0558efb37.htm)
- [View](https://www.revitapidocs.com/2024/610a2e8a-7c7d-6ab9-20c8-0c3385b0ecdf.htm)

### Material Design Components

- [Button](http://materialdesigninxaml.net/buttons)
- [ComboBox](http://materialdesigninxaml.net/combo-boxes)
- [TextBox](http://materialdesigninxaml.net/text-fields)

---

## Additional Resources

### Learning Revit API

- [The Building Coder Blog](https://thebuildingcoder.typepad.com/)
- [Revit API Forum](https://forums.autodesk.com/t5/revit-api-forum/bd-p/160)
- [RevitAPIToolbox](https://github.com/BoostYourBIM/RevitAPIToolbox)

### WPF Best Practices

- [WPF Tutorial](https://wpf-tutorial.com/)
- [Material Design In XAML](http://materialdesigninxaml.net/)

---

**Remember**: When in doubt, look at existing patterns in the codebase. The project has consistent patterns that should be followed for maintainability.
