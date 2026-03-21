# WritingAI

WritingAI is a Unity NPC sandbox built around needs, perception, memory, navigation, and world interaction rather than fixed scripted sequences.

The current repository is centered on a single NPC runtime that can:
- track comfort, hunger, and energy
- observe interactables and room lighting
- remember useful objects and places
- carry and use inventory items
- resolve locked-door routes by finding matching keys
- perform downtime activities when no urgent need is active

## Runtime Architecture

### `SimpleNPCBrain`
Primary orchestrator for:
- state changes
- need-driven target acquisition
- movement and interaction flow
- opportunistic pickups and comfort actions
- rest/activity sessions
- locked-door recovery and subgoal resumption
- runtime guardrails and narration hooks

### `NeedsManager`
Owns need values, urgency bands, passive decay/recovery, and need priority scoring.

Current built-in needs:
- `Comfort`
- `Hunger`
- `Energy`

### `NpcPerceptionService`
Encapsulates world queries for:
- visible interactables
- point visibility checks
- visible matching-key lookup
- blocking-door probes toward a destination

### `NpcMemoryService`
Owns memory writes and cleanup for:
- remembered interactables
- remembered comfort zones
- recently visited locations
- remembered locked doors
- remembered target lookups for need solving and activities

### `NpcRouteCoordinator`
Handles movement recovery and subgoal state for blocked routes:
- stall detection
- soft repath / offset approach / unstuck attempts
- locked-door key-retrieval subgoals
- parent-goal resumption after subgoal completion

### `NPCInventory`
Provides:
- pocket inventory slots
- a visible hand slot
- hand draw/use delays
- inventory lookup for need items and keys
- pickup value comparisons for swap decisions

## Main World Contracts

### Base types
- `Interactable`: common interaction base
- `INeedSatisfier`: objects that satisfy a need
- `IPickupable`: objects that can be carried
- `IKeyItem`: key identity contract for locks
- `IActivityInteractable`: downtime activity contract

### Need / recovery interactables
- `FoodInteractable`
- `AppleInteractable`
- `RestInteractable`
- `ChairInteractable`
- `BedInteractable`
- `LightSwitchInteractable`

### Door and key loop
- `DoorController`: open / close / lock state, motion, nav blocking
- `DoorInteractable`: world-facing door interaction point
- `KeyInteractable`: pickupable key with case-insensitive key matching via `DoorController.KeyIdsMatch`

### Downtime activities
- `ActivityInteractable`
- `ObservationZoneInteractable`

### Environment
- `RoomArea`: room bounds, daylight participation, artificial lighting
- `WorldArea`: coarse daytime state

## Runtime Flow

1. `NeedsManager` updates needs each frame from environment state.
2. `SimpleNPCBrain` cleans memory and passively observes visible objects and comfort zones.
3. If a route is blocked by a locked door, the brain records the door and pushes a key-retrieval subgoal.
4. The NPC chooses a visible target, a remembered target, a remembered lit room, or an explore point.
5. `NpcRouteCoordinator` monitors movement progress and attempts recovery if the route stalls.
6. On arrival, the NPC interacts, rests, performs an activity, picks up an item, or unlocks a door.
7. After the action resolves, the NPC either continues solving urgent needs or returns to idle wandering.

## Memory Model

The NPC stores four distinct memory types:
- `RememberedInteractable`
- `RememberedComfortZone`
- `RememberedLocation`
- `RememberedLockedDoor`

This allows the NPC to:
- retry unseen targets from memory
- remember lit rooms
- avoid recently visited dead ends
- resume a blocked mission after finding a key

## Reliability and Wiring Guardrails

The repo now includes runtime validation and invariants for common Unity setup mistakes:
- missing `NavMeshAgent`, `NeedsManager`, `NPCInventory`, eye point, or trigger collider
- empty interactable / door layer masks
- pickupables without colliders
- `DoorInteractable` without a parent `DoorController`
- conflicting active target modes in the brain
- invalid remembered-target and pending-door state

Editor-time validation is also present in:
- `DoorController`
- `DoorInteractable`
- `KeyInteractable`

## Script Layout

```text
WritingAI/
|- SimpleNPCBrain.cs
|- NeedsManager.cs
|- NpcPerceptionService.cs
|- NpcMemoryService.cs
|- NpcRouteCoordinator.cs
|- NPCInventory.cs
|- NPCThoughtLogger.cs
|- Interactable.cs
|- INeedSatisfier.cs
|- IPickupable.cs
|- IKeyItem.cs
|- IActivityInteractable.cs
|- DoorController.cs
|- DoorInteractable.cs
|- KeyInteractable.cs
|- LightSwitchInteractable.cs
|- FoodInteractable.cs
|- AppleInteractable.cs
|- RestInteractable.cs
|- ChairInteractable.cs
|- BedInteractable.cs
|- ActivityInteractable.cs
|- ObservationZoneInteractable.cs
|- RoomArea.cs
|- WorldArea.cs
|- RememberedInteractable.cs
|- RememberedComfortZone.cs
|- RememberedLocation.cs
|- RememberedLockedDoor.cs
|- NeedType.cs
|- ActivityType.cs
|- SCENE_SETUP_CHECKLIST.md
|- PROJECT_AUDIT_REPORT.md
```

## Scene Setup

Use [`SCENE_SETUP_CHECKLIST.md`](SCENE_SETUP_CHECKLIST.md) for the authoritative scene wiring checklist.

Key requirements:
- NPC root must have `SimpleNPCBrain`, `NavMeshAgent`, and `NeedsManager`
- `NPCInventory` is strongly recommended for food storage and key usage
- the NPC collider should be a trigger for room overlap tracking
- interactables and doors must be placed on the queried layers
- locked doors need a valid `requiredKeyId`
- matching keys must share the same key id

## Current Status

This is still a prototype, but the core runtime is now split into clearer services and includes stronger route recovery, memory handling, scene validation, and door/key behavior than the earlier version of the project.
