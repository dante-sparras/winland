# Domain Docs

Read these before exploring. A missing file is skipped silently.

- `CONTEXT.md` at the repo root: the glossary.
- `docs/adr/README.md`: decisions by area. Read the ADRs it lists for the area you are about to work in, and keep the index in step when an ADR is added, rewritten, or deleted.

Each of the three repos is one context with its own `CONTEXT.md` and `docs/adr/` (ADR 0177): Winland's words are the window manager's, Winshell's the shell's, and Winoma's the desktop's as a person meets it. A word another repo defines keeps that definition here. `/domain-modeling` creates and edits these files when terms or decisions get resolved, in the repo whose behavior the term names.

## Vocabulary

Name a domain concept (in an issue title, a refactor proposal, a hypothesis, a test name) with the term `CONTEXT.md` defines, never a synonym it avoids. A concept the glossary lacks is either language the project does not use (reconsider) or a gap (note it for `/domain-modeling`).

## ADR conflicts

An output that contradicts an ADR says so:

> _Contradicts ADR 0003 (a Workspace is a number Winoma keeps) — but worth reopening because…_
