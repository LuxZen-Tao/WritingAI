# WritingAI

WritingAI is a Unity NPC sandbox focused on emergent behaviour from needs, perception, memory, navigation, and environment state instead of fixed scripted sequences.

The current runtime supports:
- urgent and low-priority needs for `Comfort`, `Hunger`, and `Energy`
- passive observation of interactables, rooms, light state, and locked doors
- remembered targets, remembered comfort zones, and recently visited dead ends
- inventory pickup/use flow for food and keys
- locked-door recovery via matching-key retrieval and subgoal resumption
- downtime activities, including authored observation zones, when no urgent need is active

## What Is Working

The strongest working loops in the project today are:
- need-driven target selection with interruption when a more urgent need appears
- inventory-first hunger resolution and opportunistic item pickup
- comfort recovery driven by actual room lighting instead of fake direct rewards
- blocked-route recovery through remembered locked doors and matching keys
- downtime activity sessions with authored anchor points and look targets

### Activity observation zones

`ObservationZoneInteractable` is working as a leisure activity target:
- it is perceived through the same interactable vision pass as other activities
- it is remembered as an activity target for later reuse
- once started, the NPC snaps to an authored seat, roam point, or entry point
- while active, the NPC can rotate through authored look points for a dwell duration
- the session is interruptible if an urgent need appears

This pass also tightened the observation-zone loop:
- remembered activity targets now stay focused on the remembered activity instead of being hijacked by unrelated visible need targets
- multi-collider interactables are de-duplicated in perception scans
- observation zones avoid immediate look-point repeats and use collider-aware inside-zone checks

## Runtime Architecture

### `SimpleNPCBrain`
Main orchestrator for:
- state transitions and action ownership
- need solving and opportunistic behaviour
- movement, interaction, rest, and activity session flow
- door/key recovery and subgoal resumption
- runtime validation, narration, and debug logging hooks

### `NeedsManager`
Owns:
- need values and max ranges
- urgency bands and thresholds
- passive decay/recovery rules
- priority scoring and move-speed multipliers

### `NpcPerceptionService`
Owns:
- visible interactable scans
- line-of-sight checks for points and interactables
- visible matching-key lookup
- door probes toward a destination

### `NpcMemoryService`
Owns:
- interactable memory writes and refreshes
- comfort-zone memory
- recent-location avoidance memory
- locked-door memory
- remembered-target queries for needs, activities, and keys

### `NpcRouteCoordinator`
Owns:
- movement progress tracking
- soft repath / alternate approach / local unstuck recovery
- route subgoal stack for locked-door key resolution

### `NPCInventory`
Owns:
- carried item slots and hand slot
- hand-draw timing
- item lookup for needs and keys
- pickup value comparisons and swap decisions

## Main World Contracts

### Core interfaces and bases
- `Interactable`
- `INeedSatisfier`
- `IPickupable`
- `IKeyItem`
- `IActivityInteractable`

### Need and utility interactables
- `FoodInteractable`
- `AppleInteractable`
- `RestInteractable`
- `ChairInteractable`
- `BedInteractable`
- `LightSwitchInteractable`

### Activity interactables
- `ActivityInteractable`
- `ObservationZoneInteractable`

### Door/key loop
- `DoorController`
- `DoorInteractable`
- `KeyInteractable`

### Environment
- `RoomArea`
- `WorldArea`
- `GrowthSpawner`
- `GrowthObject`

`GrowthSpawner` and `GrowthObject` are currently isolated world-content helpers. They are not integrated into the NPC decision model yet, but they are part of the repo and should be treated as optional scene content.

## Runtime Flow

1. `NeedsManager` updates needs each frame.
2. `SimpleNPCBrain` passively observes visible interactables and cleans memory.
3. If an urgent need exists, the NPC searches visible targets, remembered targets, comfort rooms, or explore points.
4. If a route is blocked by a locked door, the brain records the door and pushes a key-retrieval subgoal.
5. `NpcRouteCoordinator` monitors path progress and tries recovery when a route degrades.
6. On arrival, the NPC interacts, rests, performs a downtime activity, picks up an item, or unlocks a door.
7. When the action completes, the NPC either restarts urgent search or returns to idle wandering.

## Memory Model

The runtime stores:
- `RememberedInteractable`
- `RememberedComfortZone`
- `RememberedLocation`
- `RememberedLockedDoor`

This gives the NPC enough continuity to:
- retry known helpful targets after losing sight of them
- revisit lit rooms for comfort
- avoid recently failed explore points
- return to blocked goals after finding a key

## Known Gaps And Next Improvements

The project is in a good prototype state, but these areas still need work:
- `SimpleNPCBrain` is still very large and should be decomposed further
- activities and key subgoals still share `NeedType.Key` as a storage label in interactable memory, which works but is semantically rough
- there are no automated play mode tests around doors, memory decay, or downtime activities yet
- multi-NPC reservation/claim handling does not exist yet
- tuning values are mostly inspector fields rather than data assets or profiles

## Docs Map

- Project overview: `README.md`
- Scene wiring checklist: [`SCENE_SETUP_CHECKLIST.md`](SCENE_SETUP_CHECKLIST.md)
- Audit notes: [`PROJECT_AUDIT_REPORT.md`](PROJECT_AUDIT_REPORT.md)
- New developer onboarding: [`ReadMe/NEW_DEVELOPER_GUIDE.md`](ReadMe/NEW_DEVELOPER_GUIDE.md)

## Current Status

This is an active prototype with a credible single-NPC loop. The current codebase is strong enough for adding more authored content and for refactoring toward cleaner services, but it still needs tighter semantics, tests, and tooling before it will scale comfortably for a larger team.
