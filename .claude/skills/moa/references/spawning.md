# Spawning proposers

Use the first host below whose tools you have. Proposers in one wave always launch together, in a single message, so they run in parallel.

## Cross-provider delegation (T3 Code)

Available when you have `delegate_task` and `orchestrator_capabilities` (they may be deferred; load them with tool search).

1. Call `orchestrator_capabilities` once to see the providers and models. Choose proposers from it as step 3 of the skill says. Use only models it lists, and say so when a model the user named isn't there. The effort option's id differs by provider: `reasoningEffort` for Grok and Codex, `effort` for Claude. Read it from each model's `options`.
2. Launch one `delegate_task` per proposer, all in one message: `mode: "async"`, `role: "research"` (`"review"` for a review, `"implementation"` for `pick`), `target: { providerInstanceId, model, options: { <effort option>: <effort> } }`, and in `pick` mode `isolation: "worktree"`. The `task` is the packet plus the proposer prompt; the child sees nothing else.
3. End your turn. Each completion wakes you; read it with `task_status`. Aggregate once every proposer has completed or failed.
4. The report links each proposer's thread, as its `link` field gives it.

## Claude Code

Use the `Agent` tool, one call per proposer, all in one message, with `run_in_background: false`. Set `model` to choose a model; leave it out for your own model. In `pick` mode set `isolation: "worktree"`. The `prompt` is the packet plus the proposer prompt.

## Codex

Use `spawn_agent` once per proposer, then wait for all of them. Each gets the packet plus the proposer prompt.

## Grok Build

Use `spawn_subagent` once per proposer, all in one message. The `prompt` is the packet plus the proposer prompt. Keep the default `run_in_background: true`, then collect each result with `get_command_or_subagent_output`. In `pick` mode set `isolation: "worktree"`; the result includes the worktree path.

Set `model` to put a proposer on another model. The argument is hidden when subagent model inheritance is on and every model in the picker is xAI's; then run Self-MoA on your own model. Only the top-level session can spawn, so inside a Grok subagent, follow "No subagents".

## No subagents

Tell the user that MoA needs subagents and this host has none, and stop. Answering the task yourself, several times in a row, isn't MoA: the later answers see the earlier ones.
