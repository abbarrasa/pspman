---
name: constitution
description: Create, review, or amend a project's constitution during the Constitution phase of Spec-Driven Development (SDD), proposing six concise, verifiable principles for explicit approval before writing.
---

# Project Constitution

Establish the project's non-negotiable engineering principles in `docs/constitution.md`. Follow the `sdd` skill's Constitution phase and approval rules. If it is not available, report the missing dependency before proceeding.

## 1. Inspect the project

Before proposing principles, read applicable `AGENTS.md` instructions, `MEMORY.md`, and the existing constitution. Inspect a representative selection of source code, dependency manifests, tests, CI configuration, and relevant specifications to understand the stack, boundaries, quality checks, and language conventions.

- Distinguish observed conventions from explicitly approved commitments. Existing code is evidence, not automatic justification for a principle.
- Report missing sources. For an empty project, use the user's stated goals and constraints; do not invent a stack or architecture.
- Ask focused questions only when an unresolved decision prevents a concrete, verifiable proposal, including unspecified language choices or necessary quality thresholds.
- Keep this phase read-only: do not create or modify the constitution, memory, code, or configuration before proposal approval.

## 2. Propose six principles

Present exactly six short principles, identified as `P1`–`P6`, in at most 15 source lines. Each principle must contain an enforceable rule and a concrete verification method. This limit applies to the proposed principles, not these skill instructions or a separate review explanation.

Cover these six concerns, adapting the wording to project evidence:

| ID | Concern | Required substance |
| --- | --- | --- |
| P1 | Stack simplicity | Prefer the existing stack and the simplest sufficient design. Require a recorded need and justification for new dependencies or technologies; verify through dependency and design review. |
| P2 | Specification authority | Implement only approved specified behavior. Update and approve affected specifications before changing requirements in code; verify requirement-to-code-and-validation traceability. |
| P3 | Domain independence | Keep domain rules and business logic independent of UI, persistence, and framework-specific APIs. Verify dependency direction and isolated unit tests without external services. |
| P4 | Separation of responsibilities | Separate presentation, application orchestration, and persistence behind explicit boundaries. Verify that UI and storage adapters do not own business rules and that boundary interactions are tested. |
| P5 | Testing and coverage | Use unit tests for business rules, integration tests for boundaries, and functional tests for specified user behavior. Verify requirement coverage, relevant failure paths, regression results, and agreed code-coverage checks. |
| P6 | Language policy | State the chosen languages for code identifiers and comments, documentation, and user-facing text, including localization when applicable. Verify through review and available automated checks. |

Use explicit obligations, not aspirations such as “clean,” “fast,” or “maximum possible coverage.” Keep principles project-wide; leave feature behavior in specifications and detailed implementation choices in plans. Do not require extra layers where the project has no corresponding responsibility.

For testing, distinguish requirement coverage from line and branch coverage. Propose measurable thresholds only when supported by existing policy or a reasoned proposal for user approval; do not assume arbitrary percentages or that coverage proves correctness. Make exclusions explicit and justified. If a test level does not apply, state the verifiable applicability rule rather than requiring irrelevant tests.

## 3. Review and obtain approval

For an existing constitution, compare the proposal with the approved text. Separately summarize changes, reasons, affected specifications or plans, and any existing violations. Preserve unrelated content and stable principle IDs; do not silently weaken commitments or renumber established principles merely to fit `P1`–`P6`. If the existing structure cannot accommodate exactly six principles without restructuring, identify the restructuring in the proposal and obtain approval for it.

Before requesting approval, check that:

- All six concerns are covered without contradictory obligations.
- Every principle has a practical verification method and no unresolved placeholders.
- Proposed rules respect repository instructions and expose any conflicts with current implementation.
- Language choices and quality commitments are explicit, not inferred solely from the conversation's language.

Present the exact proposed wording and ask for explicit approval. **Stop without writing files.** If the user requests changes, revise the proposal and obtain approval of that revision. Approval to propose principles does not authorize saving them.

## 4. Save the approved constitution

After explicit approval of the presented revision:

1. Create or update `docs/constitution.md` with the approved wording. Use a minimal heading, revision identifier, and approval reference or date; preserve unrelated existing content. Do not add unapproved obligations.
2. Re-read the saved document and verify that it matches the approved proposal and preserves applicable repository instructions. Approval does not mean existing code already complies.
3. Update `MEMORY.md`, creating it if absent, with the constitution revision, actual approval evidence, known compliance gaps, affected documents, and next step. Link to the constitution instead of duplicating it.
4. Report the saved location and any unresolved compliance work. **Stop before the Specification phase** unless the user has explicitly authorized that transition. Do not refactor code or alter specifications to enforce the constitution during this task.

Treat future amendments as new proposals requiring impact review and explicit approval before saving.
