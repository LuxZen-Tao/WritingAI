# WritingAI Project Audit Report

_Date: March 19, 2026_

## 1) Executive Summary

This project is a **strong prototype foundation for emergent NPC behavior in Unity** with clear systems-oriented design choices already in place.

### Overall assessment
- **Architecture direction:** Good (modular components, interfaces, clean concept boundaries)
- **Feature depth:** Medium-to-high for a prototype (needs, memory, exploration, doors/keys, inventory, comfort-light loop)
- **Readability:** Medium (main behavior file is feature-rich but now very large)
- **Scalability risk:** Moderate (complexity is concentrated in one `SimpleNPCBrain` class)
- **Production readiness:** Early-stage prototype; not yet hardened for team-scale extension

### Headline takeaway
You have already solved the hardest early challenge: proving that a layered, environment-driven NPC can exhibit believable behavior. The next phase should focus on **decomposition, correctness hardening, observability, and data-driven tuning**.

---

## 2) What Is Working Well (Key Strengths)

## 2.1 Strong systems-first design
The project correctly treats behavior as the result of interacting systems (needs + perception + memory + environment) rather than hardcoded scripts.

Why this is good:
- Supports combinatorial behavior growth as content expands
- Reduces brittleness vs. linear scripted AI
- Aligns with emergent simulation goals

## 2.2 Flexible interaction contract
The `Interactable` base class plus narrow interfaces (`INeedSatisfier`, `IPickupable`, `IKeyItem`) is a solid extensibility pattern.

Why this is good:
- New object classes can be introduced with minimal brain changes
- Behavior can branch by capability, not concrete class

## 2.3 Needs framework is adaptable
`NeedsManager` includes urgency bands and threshold abstractions rather than simple boolean urgent/not urgent.

Why this is good:
- Enables richer policy decisions (critical vs urgent vs low)
- Supports future utility/priority scoring models

## 2.4 Environmental comfort model is conceptually correct
Comfort depends on room/world lighting state, not direct reward from switch interaction.

Why this is good:
- Avoids “fake interactions” that immediately grant stat gains
- Produces believable causal loops (change world -> world affects NPC)

## 2.5 Meaningful memory subsystems
You track several memory forms:
- Interactable memory
- Comfort-zone memory
- Location memory
- Locked-door memory

Why this is good:
- Reduces purely reactive behavior
- Supports continuity and planning-like behavior without heavy planners

## 2.6 Practical door/key handling logic
Door systems include open/closed/locked, key matching, obstacle toggles, and fallback retrieval of keys when blocked.

Why this is good:
- Creates authentic route constraints
- Supports emergent detours and sub-goals

## 2.7 Thought logging utility
`NPCThoughtLogger` is excellent for behavior debugging and tuning, with categories and cooldown anti-spam controls.

Why this is good:
- Observability is often neglected in AI prototypes
- Makes behavior iteration much faster

---

## 3) Notable Working Features to Highlight

These are particularly compelling and worth showcasing in demos/readme snippets/videos:

1. **Urgency-driven action interruption and reprioritization**
   - NPC can switch priorities when a more urgent need appears.

2. **Inventory-first hunger resolution**
   - NPC checks carried food first before world search.

3. **Opportunistic behavior when no urgent need exists**
   - NPC can perform low-cost beneficial actions nearby.

4. **Memory-informed target selection**
   - NPC revisits remembered helpful objects/areas when currently unseen.

5. **Door-blocked routing with key retrieval fallback**
   - NPC can discover blocked path, seek key, and resume.

6. **Comfort from environmental state, not button reward**
   - Correct emergent loop design.

7. **World/daylight + room/artificial light layering**
   - Good base for time-of-day and weather later.

---

## 4) Behavior Flow Audit (Current Flow)

At a high level, the runtime flow is coherent:

1. Tick needs based on environment
2. Observe need shifts (for logging/transition insight)
3. Clean memory (expiry-based)
4. Passively perceive interactables/comfort zones
5. If urgent need exists -> enter/continue need-driven actions
6. Else idle-wander with periodic opportunistic checks
7. Explore / acquire target / move / interact
8. Re-evaluate and either continue solving or return to idle

This loop supports both:
- **Reactive adaptation** (new urgent need interrupts)
- **Continuity** (memory and door/key path handling)

---

## 5) Improvement Opportunities (Prioritized)

## Priority 0 — Correctness and reliability hardening

1. **Resolve logical/code anomalies in target observation path**
   - In `PassiveObserveVisibleInteractables`, key handling appears duplicated and includes a conflicting local declaration pattern that should be cleaned for correctness and maintainability.
   - Action: unify key-memory logic in one clear block and ensure key memories are tagged consistently.

2. **NeedType semantics cleanup**
   - `NeedType` includes `Key`, but keys are not a physiological/social need. This blurs model semantics.
   - Action: split into `NeedType` (true needs) and `GoalType`/`ResourceType` for acquisition sub-goals.

3. **State safety invariants**
   - Centralize invariant checks (e.g., only one of `currentTarget`, `currentMemoryTarget`, `currentComfortZoneTarget` should be active except explicit bridge cases).
   - Action: add assertion/debug guard utility.

## Priority 1 — Architecture decomposition

4. **Refactor `SimpleNPCBrain` into subsystems**
   Current class is very large and combines policy + memory + navigation + interaction + logging.

   Suggested split:
   - `NpcPerceptionService`
   - `NpcMemoryService`
   - `NpcActionPlanner` (or utility selector)
   - `NpcNavigationController`
   - `NpcInteractionController`

   Benefits:
   - Lower cognitive load
   - Easier testing and reuse
   - Safer feature additions

5. **Formalize state machine transitions**
   - Define transition table/graph explicitly.
   - Action: central transition gate with reason codes.

## Priority 2 — Data-driven tuning and balancing

6. **ScriptableObject-driven AI tuning profiles**
   - Move hardcoded behavior constants to profile assets.
   - Example: `NPCArchetypeProfile` (timid, bold, efficient hoarder).

7. **Need decay/recovery balancing toolkit**
   - Add quick simulation harness or editor tool to preview long-term need trajectories.

8. **Memory confidence/decay weighting**
   - Instead of binary existence, attach confidence score that decays.

## Priority 3 — Simulation quality

9. **Path cost awareness and route utility scoring**
   - Evaluate target utility against estimated travel cost, not only distance/visibility.

10. **Reservation/claim system for multi-NPC scaling**
   - Avoid two NPCs selecting same scarce target.

11. **Contextual failure recovery strategies**
   - Different fallback behavior for “target consumed,” “path blocked,” “room inaccessible,” etc.

---

## 6) Documentation and Developer Experience Improvements

1. Add a **high-level architecture diagram** (Needs ↔ Brain ↔ Perception ↔ Memory ↔ Interaction ↔ Environment).
2. Add a **state transition diagram** for AI state flow.
3. Add a **scene setup checklist** (required components, layers, colliders, navmesh, trigger setup).
4. Add **“known caveats” section** in README (prototype status + expected edge cases).
5. Add **telemetry quickstart** (how to use thought logs effectively).

---

## 7) Potential Avenues of Expansion

## 7.1 AI decision model evolution
- Move from current rule logic to **Utility AI** scoring with explainable factors.
- Later optionally layer GOAP for high-level objectives.

## 7.2 More need domains
- Energy/sleep
- Safety/fear
- Social
- Hygiene
- Curiosity/novelty

## 7.3 Social and group behavior
- Group-level memory sharing
- Following/helping/requesting items
- Emotion contagion and social comfort

## 7.4 Persistent world simulation
- Resource regrowth and scarcity
- Time schedules and role routines
- Ownership and territory systems

## 7.5 Content ecosystem expansion
- Additional interactables per need (chairs, heaters, beds, sinks, etc.)
- Multi-step interactions (prepare food, recharge battery, repair lights)
- Skill-gated interactions (certain NPCs can do certain tasks)

## 7.6 Narrative and explainability
- Structured thought events exportable to timeline view
- “Why did NPC do X?” debug panel in editor

## 7.7 Technical scaling
- Chunked perception updates
- Shared spatial query service
- ECS/DOTS exploration if population size becomes large

---

## 8) Suggested Near-Term Roadmap (30/60/90)

## 0–30 days
- Clean key observation/memory path
- Add invariant checks and debug assertions
- Begin extracting perception + memory services
- Improve README with setup + architecture diagrams

## 31–60 days
- Complete brain decomposition
- Introduce tuning profiles via ScriptableObjects
- Add utility-scored target selection (distance + need + confidence + accessibility)

## 61–90 days
- Add 1–2 new needs (e.g., energy + safety)
- Introduce multi-NPC conflict handling (reservations)
- Build runtime debug HUD for active need, state, target, and confidence

---

## 9) Risk Register

1. **Monolithic brain growth risk**
   - New features increasingly expensive and error-prone.

2. **Semantic model drift risk**
   - Need/resource/task concepts blending could reduce clarity.

3. **Debug complexity risk**
   - Without structured traces and reason codes, behavior regressions become hard to diagnose.

4. **Scene wiring fragility risk**
   - Missing colliders/layers/references can silently degrade behavior.

Mitigation: decomposition + validation checks + stronger docs + automated playmode tests.

---

## 10) Final Assessment

This is a promising, technically credible emergent AI prototype with multiple systems already interacting in meaningful ways. It is beyond a toy implementation and has a strong growth path.

The most valuable next step is not adding more features immediately, but **stabilizing and modularizing** what is already there so future expansion (new needs, social behavior, richer worlds) can happen faster and safer.

