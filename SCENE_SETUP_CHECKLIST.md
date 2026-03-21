# WritingAI Scene Setup Checklist (Reliability Pass)

Use this checklist when creating or debugging a scene so AI failures are easier to diagnose.

## NPC Required Setup
- `SimpleNPCBrain` on NPC root.
- `NavMeshAgent` on same NPC root object.
- `NeedsManager` on same NPC root object.
- `NPCInventory` on same NPC root object (strongly recommended for key/door loop).
- NPC collider configured as **trigger** (required for room overlap tracking via `OnTriggerEnter/Exit`).
- Optional but recommended: assign `eyePoint` transform on the NPC for stable vision checks.

## Layer Setup
- `interactableLayer` on `SimpleNPCBrain` must include all interactable objects that NPC should perceive.
- `doorLayer` should include door colliders for probing. If unset, door probing falls back to `interactableLayer`.
- Ensure interactable/key objects are actually on queried layers.

## Interactable / Pickup Expectations
- Pickupable interactables should have at least one collider (self or child).
- Key objects should use `KeyInteractable` with a non-empty `keyId`.
- Pickupables should remain enabled/active when intended to be visible to perception.

## Activity / Observation Zone Setup
- Downtime activities should use `ActivityInteractable` or `ObservationZoneInteractable`.
- `ObservationZoneInteractable` should have an entry point that is reachable on the NavMesh.
- If a zone uses `seatPoints` or `innerRoamPoints`, those anchor points should also sit on the NavMesh.
- Add at least one `innerLookPoint` if you want authored head-turn behaviour during the session.
- If a custom `zoneCollider` is assigned, it should match the authored observation area rather than a large parent bounds volume.

## Room / Comfort Setup
- `RoomArea` objects should exist for indoor comfort logic.
- Room trigger volumes and boundaries should support NPC overlap callbacks.
- Comfort/light scenes should ensure room centers are reachable by NavMesh where expected.

## Door Setup
- `DoorInteractable` should have a `DoorController` on self or parent chain.
- Locked doors should have `requiredKeyId` set when `startsLocked == true`.
- Door should block navigation when closed (`blockingCollider` and/or `NavMeshObstacle` configured).

## NavMesh Expectations
- NPC start position must be on (or very near) baked NavMesh.
- Key targets, door interaction points, and comfort targets should have complete NavMesh paths where expected.

## Locked Door + Key Loop Sanity
- Locked door has a valid `requiredKeyId`.
- At least one matching key exists with the same key ID (case-insensitive match).
- Key is pickupable and discoverable through `interactableLayer`.
- NPC has `NPCInventory` to carry/use keys.

## Quick Debug Workflow
1. Enter play mode and watch Console warnings from `SimpleNPCBrain`, `DoorInteractable`, `DoorController`, and `KeyInteractable`.
2. Resolve any setup warnings first (missing layers/colliders/components).
3. Re-test key/door flow:
   - NPC observes key
   - NPC remembers locked door
   - NPC acquires matching key (visible or remembered)
   - NPC unlocks/opens door with inventory key
