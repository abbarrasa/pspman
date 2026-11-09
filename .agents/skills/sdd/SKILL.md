---
name: sdd
description: Guide Spec-Driven Development (SDD) when creating, reviewing, or changing docs/constitution.md or feature specifications under specs/, or planning, implementing, and validating their tasks.
---

# Spec-Driven Development (SDD)

Turn an approved statement of user needs into a traceable, tested implementation. Follow this workflow:

Constitution → Specification → Clarification → Plan → Tasks → Implementation → Validation (including code review) → Change (when needed).

## Operating rules

- Treat invocation arguments as task context. Identify the requested phase and feature directory from the request and repository; ask when the target is ambiguous.
- Read repository instructions, `docs/constitution.md`, and the selected feature's existing documents before editing. Inspect relevant code and test conventions before planning implementation.
- Never advance to the next phase without explicit user approval. Prepare and review the current phase's deliverable before requesting approval. Existing explicit approval remains valid for its approved scope and revision.
- The specification defines scope. Do not invent requirements or implement unspecified behavior. Stop and ask when a missing decision affects scope, observable behavior, or correctness. Choose implementation details within the approved plan.
- Keep each feature in `specs/NNN-feature-name/`, containing `spec.md`, `plan.md`, and `tasks.md`. Choose an unused number and preserve existing identifiers.
- Changes flow through specification → plan → tasks → code. Reapprove materially affected documents before dependent work resumes. Never silently change the constitution to accommodate a feature.
- At each phase boundary and completed task, update the repository's `MEMORY.md` with the feature, phase, document revisions, approval evidence, decisions, blockers, validation summary, and next authorized step. Link to authoritative documents instead of copying them. Create it if absent; never invent approval history.
- At each phase close, present the deliverable, unresolved issues, and exact approval needed, then stop. A phase with blocking issues is not ready for approval.

## 1. Constitution

Review `docs/constitution.md` for project principles, architectural constraints, quality requirements, and required checks. Identify conflicts or missing rules that materially affect the feature.

If absent, draft a concise constitution grounded in known project constraints, label unresolved decisions, and request approval. Do not invent commitments. If present, confirm its applicability and request approval to proceed; propose amendments separately when necessary.

## 2. Specification — what and why

Write `spec.md` using the template below. Describe observable behavior and value, without choosing a stack, architecture, functions, or implementation file paths. Record externally imposed compatibility constraints as requirements when relevant.

```markdown
# Spec NNN — <Feature name>
Status: draft | approved | implemented
Revision: <revision identifier>

## Context and objective
<Problem, value, and intended outcome in one paragraph.>

## Users and actors
<Who interacts with the feature, including external systems where applicable.>

## User stories
- US-1: As a <role>, I want <action> so that <benefit>.

## Definitions
<Include only terms that could be interpreted differently.>

## Functional requirements
- FR-1: When <event>, the system shall <observable response>.

## Non-functional requirements
- NFR-1: <Applicable measurable constraint and verification conditions.>

## Edge cases
<Expected outcomes for relevant empty, duplicate, invalid, boundary,
failure, permission, and concurrent scenarios; reference requirement IDs.>

## Out of scope
<Explicit exclusions for this iteration.>

## Acceptance criteria
- AC-1 (FR-1): Given <context>, when <action>, then <observable outcome>.

## Completion criteria
<Required coverage, passing checks, and any manual demonstration or evidence.>

## Open questions
- [NEEDS CLARIFICATION] <Decision needed and affected requirements.>
```

Omit optional sections that do not apply. Keep requirement and acceptance IDs stable; do not reuse retired IDs. Every requirement needs an observable acceptance condition, either inline or through linked acceptance criteria.

Use EARS patterns for functional requirements:

- Ubiquitous: The system shall <response>.
- Event driven: When <event>, the system shall <response>.
- State driven: While <state>, the system shall <response>.
- Unwanted behavior: If <unwanted condition>, then the system shall <response>.
- Optional feature: Where <feature is included>, the system shall <response>.

Combine patterns only when necessary. Keep each requirement focused and verifiable. Replace subjective words such as “fast” or “intuitive” with measurable outcomes and explicit conditions; ask for missing thresholds rather than inventing them.

Request approval to begin clarification; this does not yet approve the specification for implementation planning.

## 3. Clarification

Review `spec.md` as a QA engineer. First return a numbered findings list covering:

1. Ambiguities and requirements that cannot be verified.
2. Contradictions between requirements or acceptance criteria.
3. Missing edge cases and expected failure behavior.
4. Conflicts with `docs/constitution.md`.

For each finding, identify the affected section or IDs and the decision needed. Detect issues only in this first pass; do not propose solutions or silently resolve them. If none are found, state that explicitly.

Then collect the user's decisions, update the specification, and review it again. Keep unresolved questions visible; do not proceed with questions that constrain subsequent work. Request explicit approval of the clarified revision before planning. Mark it `approved` only after approval.

## 4. Plan — how

Read the approved specification, constitution, and relevant implementation. Write `plan.md`; do not write implementation code.

Include:

- The specification revision being implemented.
- Files to create or modify and each file's responsibility.
- Components, interfaces, data flows, and data changes where relevant.
- Pure domain logic functions where appropriate, with inputs and outputs; pseudocode for nontrivial algorithms.
- UI states and interactions where applicable, including loading, empty, error, and accessibility behavior required by the spec.
- Significant technical decisions, rationale, and a rejected alternative with its tradeoff.
- Test strategy: appropriate unit, integration, end-to-end, and manual checks; fixtures, environments, and regression checks as needed.
- A traceability table mapping every FR and NFR to planned work and verification. Explain how acceptance and completion criteria will be demonstrated.

Do not add product behavior through technical decisions. Surface feasibility issues or specification gaps for clarification. Request approval of the plan revision before creating tasks.

## 5. Tasks

Generate `tasks.md` from the approved specification and plan. Record their revisions. Order tasks by dependency and use this structure:

```markdown
- [ ] **T1. <Concrete outcome>.** FR-1, NFR-1
  - Depends on: <task IDs or none>.
  - Work: <bounded implementation and verification scope>.
  - Done when: <observable, reproducible check>.
```

Aim for 20–30 minutes per task and about ten tasks or fewer. Treat these as decomposition targets, not guarantees. If more are needed, propose splitting the specification and wait for the user's decision before changing its scope.

Each task should leave the product coherent and add a verifiable increment toward the feature. Include tests with the behavior they verify. Map every requirement to at least one task; for necessary enabling work, name the requirements it supports instead of inventing new ones.

Request approval of the task list and authorization to start the first task. Plan approval alone does not authorize implementation.

## 6. Implementation — one task at a time

Before starting, verify that the relevant document revisions are approved and the user has authorized this task. Follow the constitution, plan, and dependencies.

1. Write or update the behavioral tests first. Run them and confirm they fail because the required behavior is missing, not because the environment or test is broken. If existing tests already demonstrate the behavior, record that evidence.
2. Implement the smallest change that satisfies the task. Refactor within the approved scope while preserving behavior.
3. Run the task's checks and relevant regression checks required by the plan and constitution. For requirements unsuitable for automated testing, execute the planned reproducible manual checks and record evidence.
4. Review the diff against the task and requirements. Report failures or blocked checks honestly; do not claim an unexecuted check passed.
5. Mark the task complete only when its “Done when” conditions pass. Record covered requirement IDs, commands or manual procedures, actual results, and any remaining limitations in `tasks.md`; update `MEMORY.md`.
6. Present the result and **stop**. Do not start another task until the user explicitly authorizes it, even if several tasks appear straightforward.

If implementation reveals a requirement gap or requires a material plan change, stop the affected work and follow the change process. After the last task, request approval to begin validation.

## 7. Validation — assess without repairing

After the user authorizes validation, apply the installed `sdd-code-review` skill
to the approved specification and the intended merge scope. Prefer a fresh
review session when practical. You may use a separate report when its approved
specification, change scope, and evidence match the current validation. If the
companion skill is missing, report the installation gap and leave validation
incomplete. Install `sdd-code-review` with its dependencies before completing
validation; it is a separate extension of this base workflow.

Use the review's requirement-by-requirement report to cover every FR and NFR,
all completion criteria, relevant regression results, and constitution
compliance. Execute any agreed checks still needed for this revision and
reassess the review verdict with that evidence. Report missing coverage,
failed checks, and unavailable environments. Record the review scope, verdict,
and evidence reference in `MEMORY.md` with the validation result. Keep the review
itself read-only.

Give a clear verdict supported by evidence. Report **not fulfilled** for confirmed
blockers or unmet criteria and **not fully verified** when required review or
verification evidence is incomplete. Report **fulfilled** only with a current
review verdict of **PASS** and all requirements and completion criteria passing.
Do not repair defects during this phase. Present findings and request approval
for any corrective work. Mark the specification `implemented` only when the user
accepts fulfilled validation for the reviewed scope. Previously accepted
validation remains historical evidence for its scope.

## 8. Change and corrective work

- For a requirement change, describe the requested change and its impact on scope, requirement IDs, acceptance criteria, dependencies, tasks, tests, and completed work.
- Update `spec.md` first; repeat clarification as needed and obtain approval. Then revise and obtain approval for the affected plan and tasks before changing code. Preserve unaffected approvals and completed work when still valid.
- For an implementation defect under unchanged requirements, retain the approved specification, add or reopen a corrective task, revise the plan only if necessary, and obtain authorization before fixing it.
- Identify which previous approvals, review findings, and validation results are stale. Return an affected specification to `draft` when its approved requirements change; return an accepted implementation to `approved` when corrective implementation is needed.
- Implement authorized corrective tasks one at a time, then review the updated scope and repeat affected validation and required regression checks. Keep `MEMORY.md` aligned with the current state.
