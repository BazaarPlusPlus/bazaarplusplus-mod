# AutoBazaar High-Winrate Agent Design

**Status:** Draft for implementation planning
**Date:** 2026-05-17
**Owner:** BazaarPlusPlus mod

## 1. Problem

AutoBazaar now has an HTTP control surface that can expose the live Bazaar state and execute legal actions, but a high-winrate agent needs more than "pick an available action." The agent has to answer three different questions at the same time:

1. What actions are legal and safe right now?
2. What build direction is statistically strong for this hero, day, and current inventory?
3. Which legal action best moves the run toward winning, while respecting current health, economy, board space, and tempo?

The project already has valuable data:

- Local run logging in SQLite (`runs`, `run_events`, `battles`, `battle_snapshots`).
- Uploaded V3 run bundles containing run projections, battle projections, replay payloads, and card-set snapshots.
- Existing ten-win/final-build data used by `CardSetBuildDataRepository`.
- Full game static data available in the mod/runtime: card templates, tags, attributes, abilities, heroes, encounters, and state rules.

The missing piece is a coherent data-processing and decision architecture that turns those sources into agent-ready priors and uses them without losing determinism, debuggability, or safety.

## 2. Goals

- Build an AutoBazaar agent that optimizes for high winrate, not just fast progression.
- Use historical uploaded runs and battles to produce build, card, and synergy priors.
- Keep every online decision constrained by deterministic validity and safety rules.
- Make decisions explainable: each action should be traceable to candidate scores, build goals, and tactical constraints.
- Log enough decision/outcome data to later train or calibrate a true shop policy.
- Support "Fashion Builds" and recent winning build queries from uploaded data.
- Keep the first shippable version simple enough to test: deterministic scorer first, optional agent reranker second.

## 3. Non-Goals

- Not training a full reinforcement-learning model in the first implementation.
- Not letting an LLM freely emit arbitrary game commands. All execution must go through `availableActions` and validator checks.
- Not depending on live R2 artifact reads during every agent decision. Runtime decisions need cached or precomputed analytics.
- Not treating final winning builds as direct proof of best shop actions. Final builds are priors, not ground-truth action labels.
- Not rewriting the existing History/Ghost perspective model. Analytics extraction may read those records, but UI projection semantics remain unchanged.

## 4. Core Decision

Use a hybrid strategy:

1. **Deterministic guardrails** reject illegal or strategically unsafe actions.
2. **Data priors** describe strong builds, card value, and synergies for the current hero and run phase.
3. **Deterministic baseline scorer** ranks legal actions using those priors and tactical state.
4. **Optional agent reranker** chooses among the top candidates, with explanations, while staying inside the guarded action set.
5. **Decision logging** records context, candidates, chosen action, explanation, execution result, and later outcome.

This gives us a robust first version and a path to stronger models. The deterministic scorer is always available as a fallback when analytics, network, or agent inference fails.

## 5. System Architecture

```text
Uploaded runs / local SQLite / static data
        |
        v
Analytics extraction pipeline
        |
        +--> normalized card/build records
        +--> outcome labels
        +--> build priors
        +--> card stats
        +--> synergy graph
        |
        v
Agent analytics cache / API
        |
        v
AutoBazaar live context  ---->  feature builder  ----> guarded candidate actions
        |                                                   |
        |                                                   v
        |                                         deterministic scorer
        |                                                   |
        |                                                   v
        |                                      optional agent reranker
        |                                                   |
        v                                                   v
POST /v1/decision  <---------------------------- selected legal action
        |
        v
decision log + post-action state diff + battle/run outcome
```

### 5.1 Offline/Server Analytics Layer

Responsibilities:

- Extract uploaded run artifacts and local SQLite battle snapshots.
- Normalize cards, boards, skills, and build signatures.
- Label outcomes at run and battle levels.
- Produce aggregate priors for builds, cards, synergies, and phase-specific decisions.
- Publish compact analytics JSON or D1-backed query endpoints for the local agent.

This layer can run in server jobs, local tools, or both. The online agent should consume the output, not do heavyweight artifact processing during play.

### 5.2 Local Agent Runtime Layer

Responsibilities:

- Poll AutoBazaar `/v1/context`.
- Fetch or load analytics priors.
- Build action features for every `availableActions` entry.
- Apply guardrails and score candidates.
- Optionally ask an agent/LLM to rerank top candidates.
- Execute only the final selected action through AutoBazaar HTTP.
- Write a decision trace.

This layer can be an external process at first. It does not need to live inside the Unity mod as long as the HTTP endpoint is stable.

## 6. Data Sources

### 6.1 Static Game Data

Source examples:

- Card templates and runtime card objects.
- Hero ids/names.
- Card type, size, tier, tags, hidden tags, attributes.
- Enchantments.
- Active abilities: trigger type, action type, active/works-in phases, priority.
- Encounter/combat metadata.

Use cases:

- Understand what a card does.
- Group cards into archetypes.
- Compute synergy features.
- Explain decisions in card/domain terms rather than raw ids.

### 6.2 Live AutoBazaar Context

Required fields:

- Run state: `PlayerHero`, `Day`, `Hour`, `Wins`, `Losses`, `CurrentEncounterId`, `CurrentEncounterType`.
- Player state: gold, income, health, max health, prestige, level.
- Inventory: board items, stash/chest items, skills.
- Selection offers: items, skills, encounters, price, fit, affordability, target section/sockets.
- Legal actions: action kind, card/action ids, target sockets, cost/cooldown constraints.
- Schema version and tick id for staleness handling.

This is the online input for every decision.

### 6.3 Historical Run Data

From local SQLite and uploaded projections:

- `run_id`
- hero
- started/ended timestamps
- final day/hour
- final wins/losses
- final rank/rating
- run status
- battle ids in run order

Use cases:

- Build run-level outcome labels.
- Filter by hero, recency, rank/rating, and final performance.
- Identify high-win and ten-win archetypes.

### 6.4 Historical Battle Data

From local `battles`, `battle_snapshots`, uploaded artifact battles:

- battle id, run id, recorded time
- day/hour
- encounter/combat kind
- player/opponent hero/rank/rating/level
- result, winner/loser combatant id
- player hand, player skills, opponent hand, opponent skills

Use cases:

- Phase-specific board strength signals.
- "Winning builds on day X" queries.
- Opponent/meta distribution.
- Immediate combat outcome labels.

### 6.5 AutoBazaar Decision Logs

New agent-owned data:

- context snapshot before action
- candidate actions
- guardrail rejections
- score breakdowns
- chosen action
- agent explanation
- POST result
- context diff after execution
- next battle result
- final run result

Use cases:

- Debug strategy mistakes.
- Tune scorer weights.
- Build action-level training data later.

## 7. Canonical Data Model

### 7.1 Card Record

Every card-like object should normalize to:

```text
templateId: string
instanceId: string?
kind: item | skill | encounter | unknown
type: string?
displayName: string?
tier: string?
size: string?
enchant: string?
slot: int?
section: hand | stash | skill | selection | opponent | unknown
tags: string[]
hiddenTags: string[]
attributes: map<string,int>
activeAbilities: AbilityRecord[]
```

Ability record:

```text
id: string
internalName: string?
internalDescription: string?
trigger: string?
action: string?
activeIn: string?
worksIn: string?
priority: string?
```

### 7.2 Build Signature

Use two signatures:

**Strict signature**

```text
hero
items: templateId + tier + enchant + slot
skills: templateId + tier
```

Purpose: exact final build display, duplicate detection, replay/debugging.

**Loose signature**

```text
hero
items: templateId + tier bucket + enchant bucket
skills: templateId
```

Purpose: analytics aggregation. Slot and minor tier differences should not fragment samples too aggressively.

### 7.3 Build Archetype

An archetype is a cluster of related build signatures. First implementation can use simple deterministic clustering:

- group by hero
- group by top N core cards
- include skills separately
- merge builds whose Jaccard similarity on core card ids exceeds a threshold

Later versions can use embeddings or graph clustering, but deterministic clustering is enough to start.

### 7.4 Action Feature Record

Each legal action gets a feature vector:

```text
actionKind
cardTemplateId
targetSocketId
cost
sellValue
isAffordable
fitsBoard
matchesTargetBuild
targetBuildCoverageDelta
synergyGain
immediatePowerDelta
economyDelta
spaceDelta
healthPressureAdjustment
tempoValue
flexibilityValue
riskPenalty
sampleSupport
```

The feature record must also carry a human-readable explanation source, not just numbers.

## 8. Analytics Processing

### 8.1 Extraction

Inputs:

- Uploaded V3 run bundles.
- Local SQLite rows when running local experiments.
- Existing final build JSON from `CardSetBuildDataRepository`.

Extraction steps:

1. Read run projection.
2. Read battle projections in chronological order.
3. Read artifact battle snapshots when available.
4. Project card sets into canonical card records.
5. Emit normalized records:
   - `RunRecord`
   - `BattleRecord`
   - `CardSetRecord`
   - `BuildSignatureRecord`

### 8.2 Outcome Labeling

Run labels:

- `finalWins`
- `finalLosses`
- `isTenWin`
- `isHighWin`: configurable, initially `finalWins >= 7`
- `survivedLate`: e.g. reached day 8+
- `rankBucket`
- `ratingBucket`

Battle labels:

- `battleWon`
- `battleLost`
- `day`
- `hour`
- `phase`: early, mid, late
- `isFinalBattle`
- `isEliminationWin`: final battle and winner survives from the relevant perspective

Build labels:

- `source`: final run, battle pre-combat snapshot, opponent snapshot, uploaded final-build feed
- `performanceWeight`: based on final wins, battle win, recency, rank/rating confidence
- `sampleWeight`: downweights duplicate uploads or suspicious outliers

### 8.3 Recency and Fashion Weighting

"Fashion Builds" means current/recent meta, not necessarily all-time strongest. Use a scoring formula such as:

```text
fashionScore =
  recencyWeight
* popularityWeight
* performanceWeight
* confidenceWeight
```

Where:

- `recencyWeight`: exponential decay by uploaded/recorded date.
- `popularityWeight`: log-scaled sample count.
- `performanceWeight`: average final wins, ten-win rate, or battle win proxy.
- `confidenceWeight`: sample-size smoothing to avoid one-off builds dominating.

### 8.4 Build Prior Generation

For each hero and phase bucket:

- top archetypes
- representative strict builds
- core cards
- optional cards
- common skills
- common enchantments
- average final wins
- ten-win rate proxy
- sample count
- fashion score

For each current inventory query:

- compute owned card ids
- compute build distance:
  - missing core cards
  - matching cards
  - conflicting cards
  - board size pressure
  - skill overlap
- rank candidate archetypes by relevance and performance.

### 8.5 Card Stats

For each `hero + phase + templateId`:

- appearance rate
- high-win appearance rate
- ten-win appearance rate
- average final wins among runs containing the card
- average final wins among runs not containing the card
- smoothed lift
- common pairings
- common enchants
- common skills
- sample count

The first version should report stats with confidence markers. Low-sample results must not dominate scoring.

### 8.6 Synergy Graph

Build a weighted graph:

```text
node = card templateId or skill templateId
edge = co-occurrence + performance lift
```

Edge features:

- co-occurrence count
- expected co-occurrence baseline
- lift
- high-win lift
- phase bucket
- hero bucket

Use cases:

- Recommend cards that complete an existing pair.
- Penalize isolated cards that rarely appear in winning builds with current inventory.
- Explain "this offer supports your current core."

## 9. Online Decision Flow

### 9.1 Loop

1. GET AutoBazaar context.
2. If disabled, stale, busy, or cooldown active, wait.
3. Build a normalized live state.
4. Fetch relevant priors:
   - build priors for hero/day/wins/losses/current inventory
   - card stats for offer cards
   - synergy recommendations for owned cards
5. Convert `availableActions` into candidate feature records.
6. Apply deterministic guardrails.
7. Score remaining candidates.
8. Optional: ask agent reranker to pick among top K.
9. POST selected action.
10. Record decision trace.
11. Observe next context and attach state diff.

### 9.2 Guardrails

Guardrails are deterministic and run before scoring:

- Reject actions not present in `availableActions`.
- Reject stale tick/decision mismatches.
- Reject unaffordable offers.
- Reject items that cannot fit.
- Reject sell actions against protected/core cards.
- Reject reroll when gold is below reserved threshold unless no useful action exists.
- Reject exit when there are still high-value free actions.
- Reject board moves that worsen fit unless they are part of a known target-selection/upgrade flow.
- Reject high-risk greed lines under severe health pressure.

The exact thresholds are config/tuning data, but the guardrail categories are fixed.

### 9.3 Baseline Scorer

Initial action score:

```text
score =
  buildPriorAlignment
+ synergyGain
+ immediatePowerGain
+ economyValue
+ tempoValue
+ survivalAdjustment
+ flexibilityValue
- opportunityCost
- spacePenalty
- deadEndPenalty
- protectedAssetPenalty
- lowConfidencePenalty
```

Feature intent:

- `buildPriorAlignment`: action moves current inventory closer to high-performing archetypes.
- `synergyGain`: card pairs well with existing cards/skills.
- `immediatePowerGain`: likely improves next fight.
- `economyValue`: preserves or increases future buying power.
- `tempoValue`: prioritizes timely upgrades/enchantments.
- `survivalAdjustment`: favors immediate strength at low health or high losses.
- `flexibilityValue`: keeps multiple strong archetype paths open.
- `opportunityCost`: cost compared with expected future value.
- `spacePenalty`: board/stash constraints.
- `deadEndPenalty`: card rarely appears in successful builds from this state.
- `protectedAssetPenalty`: selling/moving core cards.
- `lowConfidencePenalty`: analytics sample size too small.

The scorer must emit a breakdown, not just a total number.

### 9.4 Agent Reranker

The agent reranker is optional and bounded:

- Input: current state summary, top K scored candidates, build priors, scorer breakdowns.
- Output: selected candidate id, confidence, short reason, and fallback candidate.
- Constraint: cannot invent an action; candidate id must match top K.
- Failure behavior: use deterministic top score.

The reranker is useful when scorer features conflict, such as:

- strong final-build card vs immediate survival need
- economy greed vs low health
- two possible archetype pivots
- sell/keep decisions where long-term synergy matters

## 10. Data APIs

Separate analytics APIs from AutoBazaar control APIs.

### 10.1 Analytics API

These can live in ModCFServerV3, a metrics service, or a local generated JSON cache.

#### `GET /agent/build-priors`

Query:

```text
hero
day
wins
losses
owned_template_ids
skill_template_ids
limit
lookback_days
```

Response:

```json
{
  "schema_version": 1,
  "builds": [
    {
      "archetype_id": "string",
      "hero": "Vanessa",
      "phase": "mid",
      "fashion_score": 0.82,
      "performance_score": 0.76,
      "sample_count": 142,
      "average_final_wins": 8.1,
      "ten_win_rate": 0.21,
      "matched_cards": ["..."],
      "missing_core_cards": ["..."],
      "core_cards": ["..."],
      "common_skills": ["..."],
      "representative_builds": []
    }
  ]
}
```

#### `GET /agent/card-stats`

Query:

```text
hero
day
template_id
owned_template_ids
```

Response includes phase-specific appearance, high-win rate, lift, common pairings, common enchants, and sample count.

#### `GET /agent/synergy`

Query:

```text
hero
day
owned_template_ids
skill_template_ids
```

Response returns recommended complements, synergy edges, and confidence.

#### `GET /agent/recent-winning-builds`

Query:

```text
hero
day
lookback_days
min_final_wins
limit
```

This powers "Fashion Builds" and "this day winning builds" browsing.

### 10.2 Local Agent Evaluation API

This can be local-only.

#### `POST /agent/evaluate-actions`

Request:

```json
{
  "context": {},
  "available_actions": [],
  "analytics": {}
}
```

Response:

```json
{
  "chosen_action_id": "string",
  "fallback_action_id": "string",
  "candidates": [
    {
      "action_id": "string",
      "score": 12.4,
      "breakdown": {},
      "reason": "string"
    }
  ]
}
```

This endpoint is useful for testing and replaying decisions without running the game.

## 11. Storage

### 11.1 Analytics Tables or Files

Minimum aggregate datasets:

- `build_archetypes`
- `build_representatives`
- `card_phase_stats`
- `synergy_edges`
- `fashion_builds`
- `analytics_metadata`

These can be D1 tables or generated JSON files. For the mod, a cached JSON file under the BazaarPlusPlus data directory is enough for first integration.

### 11.2 Decision Trace Store

Local append-only records:

```text
decision_id
run_id
tick_id
created_at_utc
context_json
analytics_version
candidates_json
guardrail_rejections_json
chosen_action_json
agent_reason
post_status
post_error
post_context_diff_json
next_battle_id
next_battle_result
final_run_wins
final_run_losses
```

This store is the foundation for later supervised/ranking training.

## 12. Testing Strategy

### 12.1 Analytics Tests

- Card normalization preserves template id, tier, enchant, tags, attributes.
- Strict and loose signatures are stable and deterministic.
- Outcome labeling handles local and ghost/uploader perspectives correctly.
- Fashion score is monotonic with recency, performance, and sample confidence.
- Low-sample builds are downweighted.
- Synergy graph ignores duplicate card-set rows from the same run when configured.

### 12.2 Scorer Tests

- Unavailable/illegal actions never score above legal actions.
- Free high-value actions outrank exit.
- Under low health, immediate combat strength gains receive higher priority.
- Protected/core cards are not sold unless explicitly allowed by guardrail config.
- Build-aligned offer outranks unrelated offer when economy and fit are equal.
- Reroll behavior respects gold reserve thresholds.

### 12.3 Replay/Evaluation Tests

- Stored context + analytics + actions can reproduce the same deterministic ranking.
- Agent reranker cannot select an action outside the candidate list.
- Missing analytics falls back to baseline heuristics.
- Stale context or failed POST records a trace and does not silently continue as success.

### 12.4 Server/API Tests

- Analytics endpoints clamp limits and reject invalid query parameters.
- Responses include schema version and sample counts.
- Fashion build queries filter by hero/day/lookback correctly.
- Card-stat queries return confidence markers for low sample sizes.

## 13. Rollout Plan

### Phase 1: Analytics Vocabulary and Local Export

- Define canonical card/build/action data records.
- Add local tools/tests that read SQLite/artifact fixtures and generate aggregate JSON.
- Include existing final-build feed as one analytics source.
- No AutoBazaar behavior change.

### Phase 2: Build Prior Provider

- Add a local `BuildPriorProvider` that loads aggregate JSON.
- Expose query methods:
  - find relevant build priors
  - card stats
  - synergy complements
- Add unit tests around query ranking.

### Phase 3: Deterministic Scorer Agent

- Implement action feature extraction from AutoBazaar context.
- Implement guardrails.
- Implement baseline scorer and explanation breakdown.
- Run in dry-run mode first: log selected action but do not execute.

### Phase 4: Controlled Execution

- Enable execution with conservative settings.
- Keep fallback to `Wait` or deterministic top action.
- Log all decisions and post-action diffs.
- Compare against manual/naive baselines.

### Phase 5: Agent Reranker

- Add bounded reranker over top K candidates.
- Require candidate id selection only.
- Store prompt/input/output for offline audit.
- Keep deterministic scorer as fallback and comparison baseline.

### Phase 6: Learning From Decision Logs

- Join decision traces to next battle and final run outcomes.
- Tune scorer weights.
- Train a ranking model if the dataset becomes large enough.
- Promote learned model only if replay evaluation beats deterministic scorer.

## 14. Acceptance Criteria

The design is ready for implementation planning when:

1. We can generate a deterministic analytics artifact from known run/battle data.
2. The agent can evaluate a saved context without the game running.
3. Every selected action is present in `availableActions`.
4. Every scored action has a feature breakdown and explanation.
5. The system has a no-analytics fallback.
6. Decision traces can be replayed offline.
7. Fashion Builds and recent winning builds can be queried by hero/day/lookback.

## 15. Risks

### R1: Survivorship Bias

Final builds overrepresent what winners ended with, not what they correctly bought earlier. Mitigation: treat final builds as priors, not labels; use battle-phase snapshots and decision logs for action-level learning.

### R2: Sample Bias and Duplicate Uploads

Uploaded data may skew toward active mod users or repeated runs. Mitigation: sample-size smoothing, per-account/run de-duplication, recency windows, and confidence fields.

### R3: Artifact Availability

R2 artifacts can expire while SQL projections remain. Mitigation: precompute aggregate analytics before expiration and do not depend on artifact reads during online decisions.

### R4: Game Patch Drift

Card attributes, abilities, or state rules can change. Mitigation: include analytics version, game data version if available, and schema version in every cache/API response.

### R5: Agent Hallucination or Overreach

An LLM-style agent may invent invalid actions or overvalue narrative reasoning. Mitigation: candidate-id-only reranking, deterministic validation, and fallback scorer.

## 16. Open Implementation Choices

The design intentionally leaves these for implementation planning:

- Whether analytics aggregation first lives as a local tool or server job.
- Whether the first analytics artifact is JSON cache or D1 tables.
- Exact phase buckets for day/hour grouping.
- Exact scorer weights and guardrail thresholds.
- Whether the first reranker is LLM-based, heuristic-only, or skipped until trace data exists.

The recommended first implementation path is local JSON analytics + deterministic scorer + dry-run logging. That gives fast feedback while preserving a clean path to server-backed Fashion Builds and stronger agent decisions.
