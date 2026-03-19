# 🧠 Emergent NPC AI System (Unity)

A foundational NPC AI framework built in Unity, designed around needs-driven behaviour, environmental interaction, and emergent systems rather than hardcoded scripts.

## 🚀 Overview

This project explores how simple rules can combine to produce believable, dynamic NPC behaviour.

Instead of scripting exact actions, NPCs:

- Develop needs (e.g., comfort)
- Perceive their environment (vision system)
- Remember useful objects and explored locations
- Decide what to do based on current state
- Act on the world (interact with objects)
- Are affected by environmental changes over time

The goal is to create a system where complexity emerges naturally as more systems are layered in.

## 🧩 Core Systems

### 🔹 Needs System

- NPCs have internal states (currently: **comfort** and **hunger**)
- Needs increase or decrease over time depending on environment
- Behaviour is driven by need thresholds

### 🔹 Perception (Vision System)

- Field of view (angle + range)
- Line-of-sight checks (raycasting)
- Detects interactable objects dynamically
- Runs passively (even during idle)

### 🔹 Memory System

#### 🧠 Interactable Memory

NPC remembers useful objects it has seen. Stores:
- Object reference
- Need type it satisfies
- Last known position

#### 📍 Location Memory

- NPC remembers recently visited positions
- Avoids revisiting the same area repeatedly
- Locations expire after a set time (dynamic world assumption)

### 🔹 State Machine Behaviour

NPC operates using a state-driven loop:

| State | Description |
|---|---|
| `Idle` | Pauses briefly before wandering |
| `IdleWander` | Moves to a random nearby point |
| `CheckNeed` | Evaluates current need levels |
| `FindTarget` | Searches visible objects and memory for a solution |
| `Search` | Rotates in place to scan the environment |
| `Explore` | Moves to new, unvisited areas |
| `MoveToTarget` | Navigates to a visible target |
| `MoveToRememberedTarget` | Navigates to a recalled object position |
| `InteractWithTarget` | Performs the interaction on arrival |

Transitions are dynamic and interruptible based on world changes.

### 🔹 Navigation

- Uses Unity **NavMeshAgent**
- Separate movement speeds:
  - Idle wandering (slow)
  - Need-driven movement (faster)
- Avoids recently explored areas

### 🔹 Interactable System

All world objects inherit from a base `Interactable` class. Examples:
- **Light switches**
- **Food items** (apple, etc.)
- **Doors**

Each interactable:
- Defines if it can be used (`CanInteract`)
- Provides an interaction point
- Can optionally satisfy a need (via `INeedSatisfier`)
- Can optionally be picked up and stored (via `IPickupable`)

### 🔹 Food System

Food items share a common `FoodInteractable` base class that handles hunger restoration, consumption, and inventory pickup. Specific food types (e.g., `AppleInteractable`) extend this base with their own default values.

- `FoodInteractable` — base class for all food; implements `INeedSatisfier` and `IPickupable`
- `itemValue` — controls how precious this food is when deciding inventory swaps

### 🔹 NPC Inventory

NPCs can carry a limited number of items using the `NPCInventory` component.

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
