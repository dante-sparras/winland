# Prompts

Every prompt here goes after the packet. `{...}` marks what you fill in.

## Proposer prompts

### `synthesize`

```text
Work on this task on your own and give your best complete answer. Read whatever files you need, but change nothing: no edits, commits, or installs.

End with your answer in full, then a short list of the assumptions you made and anything you were unsure of.
```

### `pick`

```text
Make the code change this task needs. Work only in your own worktree, if you have one; otherwise change nothing and return a unified diff.

Run the project's checks ({test, typecheck, and lint commands}) on your change if you can.

End with:
1. The full change as a unified diff against {base commit}.
2. The result of each check you ran, or why you couldn't run it.
3. In two or three sentences, why this change fixes the task, and what else you considered.
```

### `critique`, first wave

```text
Work on this task on your own. Read whatever files you need, and run read-only commands to test your ideas, but change nothing: no edits, commits, or installs.

End with your single best {root cause | design}, the evidence for it, and the observation that would prove it wrong.
```

### `critique`, second wave

```text
Below is one agent's answer to this task. Attack it: find what it gets wrong, what it can't explain, and the evidence against it. Read files and run read-only commands to check its claims; change nothing.

End with a verdict (holds, holds with fixes, or fails), the evidence for that verdict, and any fix it needs.

Answer to critique:
{proposal}
```

## Aggregator prompts

Each one starts from the aggregate-and-synthesize prompt of Wang et al. (2024), changed only to say "models" instead of "open-source models", and adds a rider for its mode. Pass the original task as its own message, separate from this prompt.

### `synthesize`

```text
You have been provided with a set of responses from various models to the latest user query. Your task is to synthesize these responses into a single, high-quality response. It is crucial to critically evaluate the information provided in these responses, recognizing that some of it may be biased or incorrect. Your response should not simply replicate the given answers but should offer a refined, accurate, and comprehensive reply to the instruction. Ensure your response is well-structured, coherent, and adheres to the highest standards of accuracy and reliability.

When responses conflict, settle the conflict with the check results below where one applies. Otherwise choose one position, say why, and name what you rejected; do not merge incompatible positions into one. A concrete point raised by only one response stays in if it holds up. End with any disagreement you could not settle.

Check results:
{results, or "none"}

Responses from models:
1. [{model}] {response}
2. [{model}] {response}
...
```

### `pick`

```text
You have been provided with a set of candidate code changes from various models for the latest user query, with the result of the project's checks on each. Your task is to choose the single best candidate. It is crucial to critically evaluate each one, recognizing that some may be incorrect even when their checks pass.

Judge each whole candidate on: whether it fixes the task at its cause rather than a symptom, whether it changes the right place, what it could break, and how well it fits the surrounding code. A candidate whose checks fail is out unless every candidate fails.

Choose one candidate as written. You may name a small fix to apply on top of it, but never combine parts of different candidates. End with the chosen candidate's number, your reasons, and why each other candidate lost.

Candidates:
1. [{model}] checks: {results}
{diff and explanation}
2. ...
```

### `critique`

```text
You have been provided with a set of responses from various models to the latest user query, each followed by an independent critique. Your task is to synthesize these into a single, high-quality response. It is crucial to critically evaluate both the responses and the critiques, recognizing that either may be biased or incorrect.

Keep the {root cause | design} that best survives its critique and the evidence. Do not merge two different {root causes | designs} into one. If none survives, say so, and say what evidence would settle it. End with why each other response lost.

Responses and critiques:
1. [{model}] {response}
   Critique [{critic model}]: {critique}
2. ...
```
