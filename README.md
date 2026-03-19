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
- Configurable `maxSlots` (default 3)
- Items are hidden from the world while held
- When urgently hungry, the NPC checks its inventory first before searching the world
- When picking up food opportunistically (low hunger band), the NPC stores it for later
- If inventory is full but a more valuable item is available, the NPC drops its least-precious item and picks up the better one

### 🔹 Room & Environment System

#### 🏠 RoomArea

- Defines environmental context for a space
- Tracks:
  - Artificial lights (switch-controlled)
  - Natural light (daylight)
- Determines if a space is lit or dark

#### ☀️ Lighting Model

- Natural light (e.g., sun through windows) is independent of switches
- Artificial light is controlled by `LightSwitchInteractable`
- NPC **comfort depends on actual room brightness**, not interaction rewards

### 🔹 Light Switch Behaviour

- Toggles artificial lights only
- Does **NOT** directly modify NPC comfort
- Changes the environment, which then affects the NPC over time

## 🔁 Behaviour Loop

```
Need decreases (e.g., dark room)
  → NPC decides it needs comfort
  → Searches or recalls a solution
  → Moves to target
  → Interacts (e.g., turns on light)
  → Environment changes (room becomes lit)
  → Comfort recovers over time
  → NPC returns to idle wandering
```

## 🧠 Design Philosophy

- **Systems over scripts** — behaviour emerges from interacting rules, not handwritten sequences
- **Environment-driven outcomes** — the world state determines NPC wellbeing
- **Interruptible goals** — the NPC abandons actions when conditions change
- **Memory-informed decisions** — past observations guide future behaviour
- **Small rules → emergent behaviour** — complexity arises naturally as systems layer together

## 🧪 Current Features

- ✅ Needs-driven AI (Comfort + Hunger)
- ✅ Modular `NeedsManager` foundation for additional needs
- ✅ `FoodInteractable` base class — shared food logic (hunger restore, pickup, inventory)
- ✅ `AppleInteractable` — extends `FoodInteractable` with apple-specific defaults
- ✅ NPC inventory (`NPCInventory`) — carry food for later use
- ✅ Inventory-first hunger resolution — NPC uses held food before searching the world
- ✅ Opportunistic food pickup — NPC stores food when hunger is low rather than eating immediately
- ✅ Inventory swap — NPC drops least-precious item to collect a more valuable world item
- ✅ `IPickupable` interface — any interactable can become a carriable item
- ✅ Vision-based perception (FOV + raycasting)
- ✅ Passive world observation during all states
- ✅ Interactable memory (object + need + last known position)
- ✅ Location memory with time-based decay
- ✅ Idle wandering behaviour
- ✅ NavMesh navigation with per-context speeds
- ✅ Room-based lighting system
- ✅ Separation of daylight vs artificial lighting
- ✅ Dynamic goal interruption (need urgency checks in all active states)

## 🔮 Future Improvements

- Multiple needs (hunger, energy, social, etc.)
- Advanced memory (room familiarity, priority weighting)
- Time-of-day system (global day/night cycle)
- Multiple interactables per need (e.g., lamps, heaters, chairs)
- Behaviour trees or utility AI layer
- Social behaviours (multiple NPCs interacting)
- Emotional state modelling
- World simulation (economy, routines, jobs)

## 🎯 Goal

To build a scalable AI system where adding new **needs**, **objects**, and **environments** naturally increases behavioural complexity without rewriting core logic.

## 🛠 Tech Stack

- **Unity** (C#)
- **NavMesh** Navigation
- **Physics** (raycasting for vision)
- Custom AI state system

## ✅ Scene Reliability Checklist

For required component/layer/collider/door/navmesh setup, use:

- [`SCENE_SETUP_CHECKLIST.md`](SCENE_SETUP_CHECKLIST.md)

## 📌 Status

Active development — foundational systems in place, expanding toward richer emergent behaviour.

## 👤 Author

Luke — building systems, games, and ideas that evolve over time.
