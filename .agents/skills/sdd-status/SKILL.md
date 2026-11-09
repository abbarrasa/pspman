---
name: sdd-status
description: Report an existing SDD specification's current phase, approval state, task progress, blockers, and exact next step without modifying files or executing the recommended action.
---

# SDD Status

Determine where a specification stands in Spec-Driven Development and explain how to continue. Follow the `sdd` workflow and approval gates. If that skill is unavailable, disclose the limitation and avoid claiming verified workflow compliance.

**Read-only:** do not create, edit, or repair any files, including `MEMORY.md`. Do not run tests, implement tasks, change approval state, or execute the recommended next step. A status inquiry is not a phase transition and does not require a memory update.

## 1. Locate and inspect

- Resolve the existing `specs/NNN-feature-name/` directory from the request and invocation context; do not depend on `$1`. Ask which specification to inspect if the target is ambiguous. Do not create a missing directory.
- Read applicable repository instructions, the target's `spec.md`, `plan.md`, and `tasks.md` where present, and the relevant entries in `MEMORY.md`. Consult `docs/constitution.md` when its approval, constraints, or checks affect the next step.
- Read referenced approval or validation records when necessary to establish status. Use available explicit user approvals tied to the target scope and revision. Do not infer approval from file existence, timestamps, checked tasks, or a status label alone.
- Record missing sources and mismatched revision references. Distinguish “not documented” from “not done”: absence of evidence does not prove an action never occurred.

## 2. Determine phase and state

Use the current artifacts and revision-specific evidence, not merely the last phase named in memory. Report the observed work stage and any earlier gate that remains unresolved. Do not hide later work merely because an earlier approval cannot be verified.

| Evidence | Phase or interpretation | Next eligible action |
| --- | --- | --- |
| Constitution absent or awaiting approval | Constitution prerequisite unresolved | Propose or review the constitution, or obtain approval of an existing proposal |
| No `spec.md` after constitution approval | Specification not started | Draft the specification when that phase is authorized |
| Specification draft exists; clarification has not been completed | Specification or Clarification, according to recorded activity | Obtain approval to begin clarification, or resolve recorded clarification findings |
| Clarification complete; current specification not yet approved | Clarification awaiting specification approval | Present the clarified revision for approval |
| Current specification approved; plan absent or draft | Plan pending or in progress | Draft or review the plan within the authorized phase |
| Current plan approved; tasks absent or draft | Tasks pending or in progress | Prepare or review the task list within the authorized phase |
| Tasks approved; work remains | Implementation ready, in progress, or blocked | Request authorization for, or continue, the next eligible task |
| All tasks marked complete; validation absent | Implementation reported complete; Validation pending | Check completion evidence and obtain any required approval to begin validation |
| Validation started and has failures or lacks required review or check evidence | Validation not fulfilled or not fully verified | Present blockers and identify the corrective task or missing review or check |
| Current requirements and completion criteria verified; code review passed; acceptance pending | Validation awaiting acceptance | Request acceptance of the reported validation |
| Current validation accepted | Complete | No required next step for this revision |
| Requirements changed after approval or implementation | Change in progress | Resolve and approve the changed specification, then revise affected plan and tasks in order |

Apply the `sdd` approval gates to every suggested action. Existing explicit authorization remains valid for its scope; do not request it again without a revision or scope change. If the table cannot distinguish adjacent stages, report that uncertainty and the specific evidence needed.

Check for open clarification questions, constitution conflicts, stale plan or task references, missing dependencies, and review or validation evidence for an older revision or change scope. Keep previously accepted validation for its historical scope. Treat memory as supporting context; when it contradicts artifacts, report the discrepancy rather than silently choosing whichever suggests greater progress. Do not declare a specification complete based only on `Status: implemented` or checked boxes.

## 3. Count tasks consistently

- Count each unique task ID in the active task list once. Exclude nested checklist steps and historical or explicitly retired tasks from the total; disclose exclusions when material.
- Report `x of y marked complete; y − x pending`. Distinguish blocked or in-progress tasks when recorded, without double-counting them.
- A checked task records reported completion, not proof that tests passed or requirements were validated. Note missing completion evidence or explicitly reopened work.
- If task IDs are duplicated, contradictory, or absent, report the counting limitation. For a legacy list without IDs, count unambiguous top-level tasks only and state that basis.
- If no task list exists, report “Tasks not yet defined,” not `0 of 0 complete`. An existing empty list is zero defined tasks, not evidence of feature completion.

## 4. Return a concise, actionable report

Use a few lines, normally these four items, and link to the specific source sections supporting material conclusions:

1. **Phase and state:** target, observed phase, revision, and approval or verification state. Distinguish documented facts from inference.
2. **Tasks:** marked complete / total, pending count, and any material counting or evidence limitation.
3. **Blocker:** the earliest unresolved prerequisite or inconsistency; say “None identified” only when supported by the inspected sources.
4. **Next step:** one concrete action, its target document or task ID, and a copyable instruction the user can send to the developer or agent.

Choose the next step in dependency order. When approval is missing, first review or present the exact deliverable for approval; do not invite blind approval of an unseen revision. When the user has already reviewed it, identify the exact revision to approve. When evidence is insufficient, request the specific confirmation or record needed to determine status.

For an implementation step, choose a task whose dependencies are satisfied and respect task-by-task authorization. For a completed specification, state that no further action is required; do not invent maintenance work. Use a real command or skill invocation only if its availability and syntax are known; otherwise provide a plain-language prompt. **End after reporting; do not perform the action.**
