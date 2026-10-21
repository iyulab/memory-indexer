# Quality Benchmarks

[BENCHMARKS.md](BENCHMARKS.md) measures speed. This page says which public benchmark measures the quality of what Memory
Indexer is responsible for — storing what a conversation said and finding it again for a new turn — and how we run it.

## The benchmark: LongMemEval (retrieval only)

[LongMemEval](https://arxiv.org/abs/2410.10813) (ICLR 2025, [repository](https://github.com/xiaowu0162/LongMemEval),
[cleaned data](https://huggingface.co/datasets/xiaowu0162/longmemeval-cleaned), MIT) has 500 questions over long chat
histories, covering information extraction, multi-session reasoning, knowledge updates, temporal reasoning and abstention.
Each question labels the sessions and turns that hold the answer, so a memory system can be scored on **what it retrieves**,
separately from the model that answers.

| Choice | Value |
|---|---|
| Data | the cleaned release, variant `_S` (about 115k tokens, ~40 sessions per question) |
| Run | ingest every session of a question's history, then recall for the question |
| Metric | Recall@5, Recall@10 and NDCG@10 at session and turn level against `answer_session_ids` / `has_answer` |
| Broken out by | question type — knowledge-update and temporal-reasoning are the closest public measure of conflict resolution and temporal validity |
| Not used | the GPT-4o-judged answer score: it measures the answering model as much as the memory |

A stored memory must keep the id of the session and turn it came from, so a recalled fact can be matched to the gold labels: `RememberAsync(…, sourceRef:)` stores the caller's turn id and a recalled memory hands it back as `MemoryUnit.SourceRef` (a merged or summarized memory lists its originals in `DerivedFrom`). The history's own times go in as `RememberAsync(…, occurredAt:)`, and a question asked on a past day is recalled with `RecallAsync(…, asOf:)`.

## Considered and not chosen

- **LoCoMo** ([paper](https://arxiv.org/abs/2402.17753)): its data is licensed CC BY-NC 4.0, and a public
  [audit](https://penfieldlabs.substack.com/p/we-audited-locomo-64-of-the-answer) found errors in its answer key.
- **MemoryAgentBench** ([paper](https://arxiv.org/html/2507.05257v2)): its conflict-resolution tasks are scored on the final
  answer only, so an LLM is in every number.

## What no public benchmark isolates

Conflict resolution (which of two facts is current) and temporal validity (`GetValidAtAsync` — what was true at a time) have
no benchmark that scores the store without an answering model. The LongMemEval question types above are the proxy; the
in-repository tests in [EVALUATION.md](EVALUATION.md) cover the rest.

## Status

First measurement (2026-10-16): LongMemEval_S cleaned, the first 5 questions of each type (30, abstention left out),
14,990 chat turns ingested turn by turn with `RememberAsync(user, session, content, role)` and recalled with
`RecallAsync(…, limit: 10)` from a new session; in-memory store, `WithEmbeddingGenerator()` over LMSupply's bge-m3 GGUF
(llama-server, file pooling and context).

| version, registration | memories stored | session R@5 | session R@10 | turn R@5 | turn R@10 | turn NDCG@10 |
|---|---|---|---|---|---|---|
| 0.35.0, default | 1,888 (12.6 %) | 0.556 | 0.642 | 0.056 | 0.062 | 0.059 |
| 0.35.0, deduplication off | 15,511 | 0.931 | 0.950 | 0.686 | 0.788 | 0.659 |
| 0.36.0, default | 15,467 | 0.931 | 0.950 | 0.686 | 0.788 | 0.659 |

By question type (0.36.0): knowledge-update session R@5 1.00 / turn R@10 0.93; multi-session 0.68 / 0.59;
single-session-preference 1.00 / 0.47; temporal-reasoning 0.90 / 0.84. 0.35.0's default deduplication skipped, merged or
overwrote most turns by similarity (see the 0.36.0 changelog).

With the history's own times (0.39.0, every temporal-reasoning (127) and knowledge-update (72) question, paired bootstrap 95 %): passing each session's date as `occurredAt` and the question date as `asOf` **lowers** temporal-reasoning (any-evidence R@5 0.976 → 0.835), because LongMemEval dates some evidence sessions after the question and `asOf` correctly leaves them out. Measured with `asOf` = the later of the question date and the last session (recency from the question, nothing filtered), temporal-reasoning NDCG@10 rises 0.679 → 0.697 [+0.004, +0.033] and knowledge-update session R@5 falls 0.049 [−0.083, −0.021] — the superseded fact's session now ranks below the update, which the metric counts as a miss. The recency defaults were kept.
