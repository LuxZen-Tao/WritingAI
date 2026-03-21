# WritingAI New Developer Guide

This guide is for developers joining the project and needing a practical map of how the current Unity runtime is wired, what each system owns, and where to extend it safely.

## 1. What This Project Is

WritingAI is a single-NPC emergent behaviour prototype.

The NPC is not following scripted story beats. Instead, behaviour comes from:
- changing need values
- vision and room awareness
- remembered world state
- NavMesh movement and recovery
- interactable contracts
- locked-door/key fallback logic
- downtime activities when no urgent need is active

The main implementation lives in `Assets/Scripts/WritingAI`.

## 2. Core Runtime Ownership

### `SimpleNPCBrain`
This is still the top-level coordinator. It owns:
- AI state transitions
- target selection and action flow
- idle wandering and exploration
- need interruption logic
- movement execution
- rest and downtime activity sessions
- narration/debug emission
- setup guardrails

### `NeedsManager`
Owns the numeric side of needs:
- value ranges
- decay/recovery
- urgency bands
- priority scoring
- move-speed multipliers

Current built-in needs:
- `Comfort`
- `Hunger`
- `Energy`

`NeedType.Key` exists as a utility label for key/activity memory paths, not as a real body need.

### `NpcPerceptionService`
Owns world queries:
- visible interactables
- line of sight checks
- visible matching key lookup
- door probes along a route

### `NpcMemoryService`
Owns remembered data:
- interactables
- comfort zones
- recent locations
- locked doors

It also provides selectors for remembered need targets, remembered activity targets, and remembered keys.

### `NpcRouteCoordinator`
Owns movement recovery and subgoals:
- stall detection
- soft repath
- alternate approach
- local unstuck move
- locked-door key subgoal stack

### `NPCInventory`
Owns carried items:
- pocket slots
- hand slot
- draw/use timing
- inventory lookups for food and keys
- swap/drop decisions

## 3. Scene Wiring

### NPC root
The NPC GameObject should have:
- `SimpleNPCBrain`
- `NavMeshAgent`
- `NeedsManager`
- `NPCInventory` recommended
- a trigger collider for room overlap tracking

Recommended:
- assign `eyePoint` on the brain for stable sight lines
- add `NPCThoughtLogger` for readable runtime logs

### Layers
`SimpleNPCBrain` relies on layer masks.

You need:
- `interactableLayer` including all interactables the NPC should perceive
- `doorLayer` including door colliders used for blocking-door probes
- `obstacleLayer` for vision blocking and movement clearance checks

If layers are wrong, perception and door handling degrade fast.

### NavMesh
Authoring assumptions:
- NPC starts on the NavMesh
- interaction points are reachable
- door interaction points are reachable
- observation-zone anchors are reachable
- room comfort destinations are reachable

If movement looks inconsistent, validate NavMesh before changing AI code.

## 4. How To Author World Objects

### Need satisfiers
Objects that solve needs usually inherit `Interactable` and implement `INeedSatisfier`.

Examples:
- `FoodInteractable` for hunger
- `RestInteractable` / `ChairInteractable` / `BedInteractable` for energy
- `LightSwitchInteractable` for comfort via room lighting

Important rule:
- comfort should come from room state changing, not from directly adding comfort in the interactable

### Pickupables
Objects that can be carried implement `IPickupable`.

Current examples:
- `FoodInteractable`
- `KeyInteractable`

Expectations:
- at least one collider
- object placed on the interactable layer
- item remains logically enabled when intended to be discoverable

### Doors and keys
For locked-door behaviour you need:
- `DoorController` on the moving/blocking door object
- `DoorInteractable` on the interaction point or on a child with controller in parent chain
- `requiredKeyId` filled in when the door starts locked
- matching `KeyInteractable` using the same key id

The runtime flow is:
1. route hits a locked door
2. door is remembered
3. matching visible or remembered key is selected
4. key is picked up and stored in inventory
5. subgoal returns to the blocked door
6. door unlocks and original goal resumes

### Activities
Downtime activities implement `IActivityInteractable`.

Use:
- `ActivityInteractable` for simple static activities
- `ObservationZoneInteractable` for authored “watch/look around here” areas

Observation zone authoring:
- `entryPoint`: where the NPC approaches
- `seatPoints`: best anchors for stationary usage
- `innerRoamPoints`: optional alternate anchors if wandering inside the zone is allowed
- `innerLookPoints`: optional look-at targets used during the activity
- `zoneCollider`: bounds used to validate that the NPC stays inside the area

Current behaviour:
- visible zones can be chosen directly
- remembered zones can be revisited later
- the NPC will rotate through look points during the session
- urgent needs can interrupt the session if the activity is marked interruptible

## 5. Room And Comfort Wiring

### `WorldArea`
This is the simple daytime source.
- assign `worldLight`
- if the light is enabled, `IsDaytime()` returns true

### `RoomArea`
This defines local comfort context.

Key fields:
- `receivesDaylight`
- `worldArea`
- `controlledLights`
- `fallbackLitState`

Comfort logic:
- daylight counts if the room receives daylight and the world is daytime
- artificial lights count if controlled lights are on
- otherwise fallback lit state is used

The NPC remembers lit comfort zones and can return to them later.

## 6. Debugging Workflow

Use `NPCThoughtLogger` first. It is the fastest way to understand decisions.

Useful categories:
- `StateChange`
- `NeedShift`
- `Memory`
- `Perception`
- `Navigation`
- `Interaction`
- `Inventory`

When something breaks:
1. check Unity Console warnings first
2. confirm layer masks on `SimpleNPCBrain`
3. confirm interaction points and anchors are on the NavMesh
4. confirm colliders exist and are active
5. confirm room trigger volumes are set up for overlap tracking
6. confirm doors have controllers and valid key ids
7. only then start changing behaviour code

Also use `SCENE_SETUP_CHECKLIST.md` as the baseline wiring checklist.

## 7. Current Codebase Reality

The project is in a solid prototype phase, but there are still constraints:
- `SimpleNPCBrain` is large and mixes policy, action flow, and runtime guardrails
- activities and keys are still stored in interactable memory using `NeedType.Key`
- there are no automated tests covering core loops yet
- most tuning lives directly in inspector fields

This means:
- prefer small, local changes
- avoid moving semantics around casually without tracing door, memory, and downtime flows
- document any new inspector field you add

## 8. Safe Extension Patterns

If you add a new need:
1. add it to `NeedType`
2. add default state in `NeedsManager`
3. add satisfiers implementing `INeedSatisfier`
4. update selection logic only where necessary
5. update README docs and scene checklist

If you add a new interactable:
1. inherit `Interactable`
2. implement only the contracts you actually need
3. expose a stable interaction point
4. make sure it is visible on the correct layer
5. verify memory and `CanInteract` rules

If you add a new downtime activity:
1. implement `IActivityInteractable`
2. define desirability and min/max duration
3. author a reachable activity anchor
4. decide whether urgent needs can interrupt it
5. test both visible and remembered acquisition paths

## 9. Recommended Next Refactors

If you want to improve the project without changing design direction, the highest-value follow-ups are:
- split more policy out of `SimpleNPCBrain`
- separate “resource/subgoal type” from `NeedType`
- add play mode coverage for doors, memory decay, and downtime activity selection
- move tuning data into ScriptableObject profiles

## 10. Start Here

For a new developer, the fastest useful read order is:
1. `README.md`
2. `SCENE_SETUP_CHECKLIST.md`
3. `SimpleNPCBrain.cs`
4. `NpcMemoryService.cs`
5. `NpcPerceptionService.cs`
6. `NpcRouteCoordinator.cs`
7. the interactables you plan to extend
