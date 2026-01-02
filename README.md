# SmartTags

Revit tag placement helper that automates tagging workflows and adds collision-aware placement controls.

## Current Plan

1. Review the current tag placement flow to find the best insertion points for a collision-avoidance pass (covers tag all/selected, placement direction, leader length/angle, view scale).
2. Define the collision model: treat all visible elements and existing tags in the active view as obstacles, compute their view-plane bounds, and provide a configurable gap (default 1 mm, convertible to internal units).
3. Design a candidate-search strategy that starts at the intended offset and fans out laterally around the anchor to find the closest valid head position, falling back to the existing behavior when needed.
4. Implement collision checks and the candidate search inside the placement handler, using 2D projections and respecting the configured gap.
5. Add UI/persistence for enabling collision detection and tuning the gap, then validate across both Tag All and Tag Selected scenarios.
