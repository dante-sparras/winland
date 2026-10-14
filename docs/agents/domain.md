# Domain Docs

Read these before exploring. A missing file is skipped silently.

- `CONTEXT.md` at the repo root: the glossary.
- `docs/adr/README.md`: decisions by area. Read the ADRs it lists for the area you are about to work in, and keep the index in step when an ADR is added, rewritten, or deleted.

This repo is one context, with its own `CONTEXT.md` and `docs/adr/`. `/domain-modeling` creates and edits these files when terms or decisions get resolved.

## Vocabulary

Name a domain concept (in an issue title, a refactor proposal, a hypothesis, a test name) with the term `CONTEXT.md` defines, never a synonym it avoids. A concept the glossary lacks is either language the project does not use (reconsider) or a gap (note it for `/domain-modeling`).

## ADR conflicts

An output that contradicts an ADR says so:

> _Contradicts ADR 0003 (a Workspace is a number) — but worth reopening because…_
