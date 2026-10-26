---
name: feature
description: Plan a new PSPMan feature before implementation. Use whenever the user requests a new feature, including when invoked as $feature.
---

# Feature planning

Treat the text following `$feature` as the requested feature. Before editing any file, read `AGENTS.md` and `MEMORY.md`, then inspect the relevant code.

Present a concise plan covering:
1. The implementation approach and how it follows `AGENTS.md`.
2. The files to create or modify and the purpose of each change.
3. Relevant edge cases and decisions the user must make before implementation.
4. Proposed updates to `AGENTS.md` and `MEMORY.md`; say “none” where appropriate.

State any assumptions. Ask only about decisions that cannot be resolved from the repository or the request. Do not edit files, including `MEMORY.md`, until the user approves the plan. After approval, implement the agreed scope and follow the repository’s verification and memory instructions.
