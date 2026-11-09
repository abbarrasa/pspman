---
name: sdd-change
description: Add or revise requirements in an existing Spec-Driven Development (SDD) specification, show the specification diff and downstream impact, and obtain approval before updating plans, tasks, or code.
---

# SDD Requirement Change

Update the specification first. Follow the `sdd` skill's change process and approval rules; report a missing `sdd` dependency before proceeding.

This workflow authorizes edits only to the selected `spec.md` and the repository's `MEMORY.md`. Do not modify the constitution, plan, tasks, implementation, tests, dependencies, or configuration.

## 1. Identify the change and baseline

- Resolve the existing `specs/NNN-feature-name/` directory and requested behavior from the user's request and invocation arguments. Do not depend on positional placeholders such as `$1`. Ask if the target or intended behavior is ambiguous; do not create a new feature specification as a substitute.
- Read applicable repository instructions, `MEMORY.md`, `docs/constitution.md`, and the target's `spec.md`, `plan.md`, and `tasks.md`. Report missing documents instead of inventing their contents. A missing specification blocks this workflow; other missing sources must be disclosed and assessed before proceeding.
- Record the starting specification revision, status, relevant approval evidence, and current file contents. Preserve existing uncommitted edits. Use the starting contents as the baseline for the change-specific diff, rather than attributing all differences from Git HEAD to this request.
- Inspect relevant code or tests only when necessary to assess impact. Distinguish confirmed effects from assumptions; do not claim a complete implementation assessment without evidence.

## 2. Check scope and clarify

Determine whether the request adds a requirement, changes an existing one, or is already covered. Avoid duplicate requirements and unrelated improvements.

Check for conflicts with existing requirements, acceptance criteria, exclusions, and the constitution. Identify effects on external contracts, existing users or data, permissions, and compatibility when applicable. Do not invent thresholds, migration behavior, or other product decisions.

If a missing decision affects scope, observable behavior, or correctness, present the affected requirement IDs and focused questions, then stop the affected update until the user answers. Record unresolved questions as `[NEEDS CLARIFICATION]` where useful; never present a blocked draft as ready for approval. A constitution conflict requires an explicit resolution or a separately approved amendment, not an implicit override.

## 3. Update the specification

Make the smallest coherent change to `spec.md`:

- Add a new stable requirement ID for new behavior; retain the existing ID when revising the same requirement. Follow the specification's naming scheme, preserve unrelated IDs, and never reuse retired IDs.
- Express functional requirements in EARS, using the appropriate pattern:
  - The system shall <response>.
  - When <event>, the system shall <response>.
  - While <state>, the system shall <response>.
  - If <unwanted condition>, then the system shall <response>.
  - Where <optional feature is included>, the system shall <response>.
- Make each requirement independently verifiable. For non-functional requirements, state measurable constraints and verification conditions; use EARS only where it fits.
- Update affected acceptance criteria, edge cases and expected outcomes, and explicit exclusions. Adjust stories, definitions, objectives, or completion criteria only when the requested change requires it.
- Keep implementation details in the plan. Do not introduce architecture, file paths, or technology choices into behavioral requirements; retain genuine externally imposed constraints.
- Advance the revision using the repository convention and set the changed specification to `draft`, even if the previous revision was `approved` or `implemented`. Preserve the historical approval record; it does not approve the new revision.

Review the resulting specification for ambiguity, contradictions, missing failure behavior, and constitution conflicts. If the request is already fully covered, report the relevant IDs and avoid an unnecessary revision or approval invalidation.

## 4. Assess downstream impact without editing

Identify the changes that would be needed after specification approval:

| Artifact | Affected section or ID | Required follow-up and reason | Approval or evidence affected |
| --- | --- | --- | --- |
| `plan.md` | Decision, component, interface, or test strategy | Describe the needed revision | Identify stale scope |
| `tasks.md` | Existing task ID or proposed new work | Describe additions, revisions, dependencies, or reopened work | Identify completed work needing reassessment |
| Tests, code review, and validation | Requirement and test IDs where known | Describe new or changed checks and regression needs | Identify review and validation evidence that no longer covers the changed requirements |

Include relevant effects on other specifications and completed implementation when supported by evidence. Mark unknowns explicitly. Preserve unaffected approvals and results; do not assert that all historical work is invalid. An impact report is not an approved implementation plan.

## 5. Present the diff and stop

Update `MEMORY.md` with the target, draft revision, changed requirement IDs, actual decisions, pending approval, downstream impact, blockers, and next step. Link to authoritative documents rather than duplicating them. Record no approval that has not occurred.

Present:

1. A concise summary of the requested behavior and affected requirement IDs.
2. The complete change-specific unified diff of `spec.md` against the captured baseline, including revision and status changes.
3. The downstream impact report and any unresolved questions.
4. An explicit request to approve the displayed specification revision, if it is ready for approval.

**Stop.** Do not edit `plan.md` or `tasks.md`, write tests, or implement code. Approval of the specification alone does not authorize those actions.

After explicit approval, mark only the displayed revision `approved` and update the approval evidence in `MEMORY.md`. If the specification has changed since presentation, show the additional diff and obtain approval of the revised content first. Continue to plan revision only when explicitly authorized, then follow `sdd` approvals for tasks and implementation. Keep status short of `implemented` until validation is complete and accepted.
