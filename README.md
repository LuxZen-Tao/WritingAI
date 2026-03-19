# 🧠 WritingAI — Emergent NPC AI Sandbox (Unity)

WritingAI is a Unity prototype focused on **needs-driven, environment-aware NPC behavior**.

Instead of scripting exact actions, the NPC:
- tracks internal needs,
- observes the world through vision,
- remembers useful things,
- navigates with NavMesh,
- interacts with world objects,
- and adapts when the world changes.

---

## Project Goal

Build a scalable AI loop where adding new interactables and environments increases behavioral depth **without rewriting core logic**.

This repository currently prioritizes:
- reliability of the core loop,
- clarity of state/action flow,
- and guardrails that surface setup/wiring mistakes early.

---

## Core Runtime Architecture

### 1) `SimpleNPCBrain` (orchestrator)
`SimpleNPCBrain` is the central coordinator for:
- state transitions,
- movement/action sequencing,
- interaction execution,
- opportunistic actions,
- locked-door fallback and key acquisition triggers.

### 2) `NpcPerceptionService`
Encapsulates perception and probe helpers:
- point/interactable visibility checks,
- visible interactable scans,
- visible matching-key selection,
- door-blocking probes toward destinations.

### 3) `NpcMemoryService`
Encapsulates NPC memory operations:
- remember/update/cleanup for interactables,
- comfort-zone memory,
- location memory (recently visited positions),
- locked-door memory,
- remembered matching-key lookup.

### 4) Interactable + Inventory ecosystem
- `Interactable` base class for world actions.
- `INeedSatisfier` for need-relevant objects.
- `IPickupable` for carriable items.
- `IKeyItem` for key/lock matching.
- `NPCInventory` for carrying, swapping, and key lookup.

---

## Current Gameplay Loop (High Level)

1. NPC evaluates need urgency.
2. NPC passively observes visible interactables/comfort areas.
3. NPC chooses visible or remembered targets.
4. NPC moves, handles doors, and interacts.
5. NPC updates memory + returns to idle/explore when stabilized.

The loop supports:
- need-driven actions,
- opportunistic pickups,
- memory-guided retries,
- and door/key handling.

---

## Locked Door + Key Flow

When pathing is blocked by a locked door:
1. NPC remembers the locked door and required key id.
2. NPC tries to acquire a **visible** matching key first.
3. If none visible, NPC tries a **remembered** matching key.
4. NPC picks up key, returns to door flow, unlocks via inventory key usage.
5. If no key is available, fallback behavior continues without hard-locking.

---

## Reliability / Guardrail Features

This project now includes guardrails to reduce silent failures:

- Runtime invariant checks in `SimpleNPCBrain` (throttled warnings):
  - conflicting target modes,
  - invalid remembered-target state,
  - invalid pending door references,
  - invalid disabled active targets,
  - key-memory leakage into need-target resolution.

- Runtime setup validation:
  - missing eye point / layer masks / inventory / trigger collider,
  - missing door controller chain,
  - missing pickup colliders.

- Editor-time validation (`OnValidate`) in:
  - `DoorController`,
  - `DoorInteractable`,
  - `KeyInteractable`.

---

## Setup & Scene Wiring

For exact scene requirements and debugging workflow, use:

👉 **[`SCENE_SETUP_CHECKLIST.md`](SCENE_SETUP_CHECKLIST.md)**

It covers:
- required NPC components,
- layer setup,
- collider expectations,
- room trigger assumptions,
- door hierarchy expectations,
- navmesh assumptions,
- key/locked-door sanity checklist.

---

## Main Systems in This Repo

- Needs: `NeedsManager`, `NeedType`
- Brain: `SimpleNPCBrain`
- Perception: `NpcPerceptionService`
- Memory: `NpcMemoryService`
- Interaction: `Interactable` + concrete interactables (food, key, switch, door)
- Inventory: `NPCInventory`
- Environment context: `RoomArea`, `WorldArea`
- Door/lock model: `DoorController`, `DoorInteractable`, `IKeyItem`
- Thought logging: `NPCThoughtLogger`

---

## Development Notes

- This is an active prototype; expect iterative stabilization/refactor passes.
- Current focus is reliability and maintainability of the existing core loop.
- New behavior features should be added only after core loop validation remains stable.
