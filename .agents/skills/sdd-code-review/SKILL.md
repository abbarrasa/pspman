---
name: sdd-code-review
description: Review a completed SDD implementation against its approved specification during validation or before a merge. Trace acceptance criteria to code and verification evidence and report actionable findings without repairing the implementation.
---

# SDD code review

Follow the `sdd` skill's validation and approval rules. If it is unavailable,
report the missing dependency before proceeding. This review does not authorize
advancing SDD phases or accepting validation on the user's behalf.

Review in a fresh session when practical. Use the approved artifacts and the
actual changes as evidence. Treat the implementer's summary and task checkboxes
as navigation aids.

## Establish the review baseline

1. Resolve the feature directory and change scope from the request. Read the
   applicable repository instructions, `docs/constitution.md`, the feature's
   `spec.md`, `plan.md` and `tasks.md`, and relevant approval records in
   `MEMORY.md` or other available sources. Identify the approved specification
   revision, acceptance and completion criteria, and required checks. A status
   label alone does not establish approval. Use the approved criteria if the
   change proposes new wording, and flag any material deviation that requires
   approval.
2. Record the PR's actual base and head commit IDs and inspect the PR diff. For a
   local review, resolve the intended base and target and state whether the scope
   includes staged, unstaged, or untracked files. Inspect included local changes
   and identify them in the report. Never guess the merge target or silently
   omit work from the agreed scope.
3. If the approved revision or review scope cannot be established, return
   **INCOMPLETE** and identify the missing input. If other relevant artifacts
   are missing or stale, describe what remains verifiable.

## Inspect the implementation and verify evidence

- Map every FR and NFR to its acceptance criteria or inline conditions. Trace
  the affected code through real entry points and callers. Look for introduced
  regressions, including authorization, sensitive data, external contracts,
  persistence and migrations, and failure or retry behavior where relevant.
- Check that tests or reproducible manual procedures cover the required
  behavior, including material negative and boundary cases. Run agreed checks
  when authorized and feasible in a suitable test environment. Use CI evidence
  only when it applies to the reviewed code and check configuration. A green
  run for an earlier revision or for HEAD alone when the review includes local
  changes is insufficient unless its applicability is demonstrated.
- Distinguish a successful check from one that exists, was skipped, or failed
  to run. Separate failures caused by the change from environment failures
  and pre-existing defects. Check the approved completion criteria and
  relevant constitution rules.
- Keep code, tests, SDD artifacts, and `MEMORY.md` read-only. Do not repair the
  change, change its approval status, commit, or merge. Return the report to
  the caller. Post it to a PR only when explicitly requested.

## Report the result

State the approved specification revision, reviewed base and target, and any
local scope included. Put actionable findings first, ordered by impact. For
each finding, give the file and line, triggering scenario, impact, expected
behavior, and affected requirement or project rule when applicable. Identify
which findings block acceptance.

| Requirement | Acceptance criterion | Code and verification evidence | Verdict |
| --- | --- | --- | --- |
| FR-1 / NFR-1 | AC-1 or inline condition | File and line, test or procedure, observed result | PASS / FAIL / NOT VERIFIED |

List checks with the command or CI run, result, and any limitation. Report
completion criteria that failed or remain unverified.

- **CHANGES REQUESTED** when a confirmed defect or unmet requirement blocks
  acceptance. Also list any outstanding verification gaps.
- **INCOMPLETE** when required evidence or review scope is missing, or a
  required check is blocked without a confirmed defect.
- **PASS** when all required criteria and checks are verified and no blocker
  remains. This verdict is evidence for user acceptance of SDD validation.

Recommend the next step for the caller. Use a corrective task for an
implementation defect under unchanged requirements, or the SDD change process
when requirements must change. Reassess findings and required checks on the
updated change scope before reusing a verdict.
