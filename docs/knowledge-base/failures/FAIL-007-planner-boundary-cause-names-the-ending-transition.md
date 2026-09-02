# [FAIL-007] The planner's `BoundaryCause` names the transition that ENDS a non-final segment

| Field | Value |
|-------|-------|
| **ID** | FAIL-007 |
| **Category** | failure |
| **Status** | resolved (test-side; the convention is by design and persisted in manifests) |
| **Sprint** | S137 |
| **Date** | 2026-09-02 |
| **Domains** | SharedKernel (Segmentation), Payroll, Test |
| **Tags** | period-planner, boundary-cause, segment-manifest, convention, test-authoring, adr-016, adr-040 |

## Summary
`PeriodPlanner.Plan` gives segment 0 and every MIDDLE segment the cause of the boundary that
introduces the NEXT segment; only the FINAL segment carries the cause of the boundary that started
it (see the "2 + 3. Build contiguous segments" comment block in `PeriodPlanner.cs`). Two-segment
plans hide this — both segments get the same cause — so the convention is easy to invert the first
time a THREE-segment plan is asserted.

## How it surfaced
TASK-13702's hire-and-leave test (NOT_EMPLOYED / EMPLOYED / NOT_EMPLOYED) asserted
`EmploymentStarted` on the employed middle segment and failed: by the convention the middle
segment carries `EmploymentEnded` (the transition that ends it). The test was corrected; the
planner was NOT changed — manifests already persist this shape, and a "fix" would silently
re-label every stored manifest on replay.

## Lesson
For plans with 3 or more segments, assert causes per the convention (leading edge for the last
segment, "what ends me" for the others). Do not "fix" the planner to match intuition. When a reader
needs the leading-edge cause of a middle segment, derive it from the PREVIOUS segment's cause.
