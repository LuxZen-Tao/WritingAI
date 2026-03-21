using System.Collections.Generic;
using UnityEngine;

public class NpcMemoryService
{
    // Future memory categories should follow the same pattern used here:
    // 1. add a lightweight remembered data record,
    // 2. add focused write/cleanup/query helpers in this service,
    // 3. return small typed results so the brain stays the coordinator for narration and goals.
    public enum MemoryWriteResult
    {
        None,
        Updated,
        Added
    }

    public enum ComfortMemoryWriteResult
    {
        None,
        UpdatedNoLightingChange,
        UpdatedLightingChanged,
        Added
    }

    public enum LockedDoorMemoryWriteResult
    {
        None,
        Updated,
        Added
    }

    public enum ObservedInteractableKind
    {
        None,
        KeyItem,
        NeedSatisfier,
        Activity
    }

    public struct ObservedInteractableMemoryResult
    {
        public ObservedInteractableKind kind;
        public MemoryWriteResult writeResult;
        public NeedType needType;
        public bool isActivity;
        public ActivityType activityType;
    }

    public bool TryRememberObservedInteractable(
        List<RememberedInteractable> memory,
        Interactable interactable,
        GameObject actor,
        float now,
        out ObservedInteractableMemoryResult result)
    {
        result = default;

        if (memory == null || interactable == null)
            return false;

        if (interactable is IKeyItem && interactable is IPickupable pickupable && pickupable.CanPickUp(actor))
        {
            result.kind = ObservedInteractableKind.KeyItem;
            result.needType = NeedType.Key;
            result.writeResult = RememberInteractable(memory, interactable, NeedType.Key, now);
            return result.writeResult != MemoryWriteResult.None;
        }

        if (interactable is INeedSatisfier satisfier)
        {
            result.kind = ObservedInteractableKind.NeedSatisfier;
            result.needType = satisfier.GetNeedType();
            result.writeResult = RememberInteractable(memory, interactable, result.needType, now);
            return result.writeResult != MemoryWriteResult.None;
        }

        if (interactable is IActivityInteractable activity)
        {
            result.kind = ObservedInteractableKind.Activity;
            result.needType = NeedType.Key;
            result.isActivity = true;
            result.activityType = activity.GetActivityType();
            result.writeResult = RememberInteractable(memory, interactable, NeedType.Key, now, true, result.activityType);
            return result.writeResult != MemoryWriteResult.None;
        }

        return false;
    }

    public MemoryWriteResult RememberInteractable(
        List<RememberedInteractable> memory,
        Interactable interactable,
        NeedType needType,
        float now,
        bool isActivity = false,
        ActivityType activityType = ActivityType.Generic)
    {
        if (memory == null || interactable == null)
            return MemoryWriteResult.None;

        for (int i = 0; i < memory.Count; i++)
        {
            if (memory[i] == null || memory[i].interactable != interactable)
                continue;

            memory[i].lastKnownPosition = interactable.GetInteractionPoint();
            memory[i].needType = needType;
            memory[i].isActivity = isActivity;
            memory[i].activityType = activityType;
            memory[i].lastSeenTime = now;
            return MemoryWriteResult.Updated;
        }

        memory.Add(new RememberedInteractable(interactable, needType, interactable.GetInteractionPoint(), now, isActivity, activityType));
        return MemoryWriteResult.Added;
    }

    public void ForgetInvalidMemories(List<RememberedInteractable> memory)
    {
        if (memory == null)
            return;

        memory.RemoveAll(m => m == null || m.interactable == null);
    }

    public void CleanupInteractableMemory(List<RememberedInteractable> memory, float memoryDuration, float now)
    {
        if (memory == null)
            return;

        memory.RemoveAll(m => m == null || m.interactable == null || now - m.lastSeenTime > memoryDuration);
    }

    public bool ForgetRememberedInteractable(List<RememberedInteractable> memory, RememberedInteractable remembered)
    {
        if (memory == null || remembered == null)
            return false;

        return memory.Remove(remembered);
    }

    public ComfortMemoryWriteResult RememberComfortZone(List<RememberedComfortZone> rememberedComfortZones, RoomArea room, Vector3 position, bool wasLit, float now)
    {
        if (rememberedComfortZones == null || room == null)
            return ComfortMemoryWriteResult.None;

        for (int i = 0; i < rememberedComfortZones.Count; i++)
        {
            if (rememberedComfortZones[i] == null || rememberedComfortZones[i].room != room)
                continue;

            bool litChanged = rememberedComfortZones[i].wasLitWhenLastSeen != wasLit;
            rememberedComfortZones[i].lastKnownPosition = position;
            rememberedComfortZones[i].lastSeenTime = now;
            rememberedComfortZones[i].wasLitWhenLastSeen = wasLit;
            return litChanged ? ComfortMemoryWriteResult.UpdatedLightingChanged : ComfortMemoryWriteResult.UpdatedNoLightingChange;
        }

        rememberedComfortZones.Add(new RememberedComfortZone(room, position, now, wasLit));
        return ComfortMemoryWriteResult.Added;
    }

    public void CleanupComfortZoneMemory(List<RememberedComfortZone> rememberedComfortZones, float memoryDuration, float now)
    {
        if (rememberedComfortZones == null)
            return;

        rememberedComfortZones.RemoveAll(z => z == null || z.room == null || now - z.lastSeenTime > memoryDuration);
    }

    public void RememberLocation(List<RememberedLocation> rememberedLocations, Vector3 position, float now)
    {
        if (rememberedLocations == null)
            return;

        rememberedLocations.Add(new RememberedLocation(position, now));
    }

    public void CleanupLocationMemory(List<RememberedLocation> rememberedLocations, float memoryDuration, float now)
    {
        if (rememberedLocations == null)
            return;

        rememberedLocations.RemoveAll(loc => now - loc.timeStored > memoryDuration);
    }

    public bool IsRecentlyVisited(List<RememberedLocation> rememberedLocations, Vector3 position, float locationAvoidanceRadius)
    {
        if (rememberedLocations == null)
            return false;

        for (int i = 0; i < rememberedLocations.Count; i++)
        {
            RememberedLocation loc = rememberedLocations[i];
            if (loc == null)
                continue;

            if (Vector3.Distance(position, loc.position) <= locationAvoidanceRadius)
                return true;
        }

        return false;
    }

    public void CleanupLockedDoorMemory(List<RememberedLockedDoor> rememberedLockedDoors, float memoryDuration, float now)
    {
        if (rememberedLockedDoors == null)
            return;

        rememberedLockedDoors.RemoveAll(lockedDoorMemory =>
        {
            if (lockedDoorMemory == null || lockedDoorMemory.door == null)
                return true;

            if (now - lockedDoorMemory.lastSeenTime > memoryDuration)
                return true;

            DoorController controller = lockedDoorMemory.door.GetDoorController();
            if (controller == null || !controller.IsLocked)
                return true;

            if (string.IsNullOrWhiteSpace(lockedDoorMemory.requiredKeyId))
                return true;

            return false;
        });
    }

    public LockedDoorMemoryWriteResult RememberLockedDoor(List<RememberedLockedDoor> rememberedLockedDoors, DoorInteractable door, float now)
    {
        if (rememberedLockedDoors == null || door == null)
            return LockedDoorMemoryWriteResult.None;

        DoorController controller = door.GetDoorController();
        if (controller == null || !controller.IsLocked)
            return LockedDoorMemoryWriteResult.None;

        string requiredKeyId = controller.RequiredKeyId;
        if (string.IsNullOrWhiteSpace(requiredKeyId))
            return LockedDoorMemoryWriteResult.None;

        for (int i = 0; i < rememberedLockedDoors.Count; i++)
        {
            RememberedLockedDoor remembered = rememberedLockedDoors[i];
            if (remembered == null || remembered.door != door)
                continue;

            remembered.lastKnownPosition = door.GetInteractionPoint();
            remembered.requiredKeyId = requiredKeyId;
            remembered.lastSeenTime = now;
            return LockedDoorMemoryWriteResult.Updated;
        }

        rememberedLockedDoors.Add(new RememberedLockedDoor(door, door.GetInteractionPoint(), requiredKeyId, now));
        return LockedDoorMemoryWriteResult.Added;
    }

    public bool IsKeyUsefulForRememberedLockedDoor(List<RememberedLockedDoor> rememberedLockedDoors, IKeyItem keyItem)
    {
        if (rememberedLockedDoors == null || keyItem == null)
            return false;

        string keyId = keyItem.GetKeyId();
        if (string.IsNullOrWhiteSpace(keyId))
            return false;

        for (int i = 0; i < rememberedLockedDoors.Count; i++)
        {
            RememberedLockedDoor lockedDoorMemory = rememberedLockedDoors[i];
            if (lockedDoorMemory == null || string.IsNullOrWhiteSpace(lockedDoorMemory.requiredKeyId))
                continue;

            DoorController controller = lockedDoorMemory.door != null ? lockedDoorMemory.door.GetDoorController() : null;
            if (controller == null || !controller.IsLocked)
                continue;

            if (DoorController.KeyIdsMatch(lockedDoorMemory.requiredKeyId, keyId))
                return true;
        }

        return false;
    }

    public bool TryFindBestRememberedMatchingKey(
        List<RememberedInteractable> memory,
        string requiredKeyId,
        GameObject picker,
        Vector3 originPosition,
        out RememberedInteractable bestMemory)
    {
        bestMemory = null;
        if (memory == null)
            return false;

        ForgetInvalidMemories(memory);
        float bestDistance = Mathf.Infinity;

        for (int i = 0; i < memory.Count; i++)
        {
            RememberedInteractable remembered = memory[i];
            if (remembered == null || remembered.interactable == null)
                continue;

            Interactable interactable = remembered.interactable;
            if (!interactable.isEnabled)
                continue;

            if (!(interactable is IKeyItem keyItem))
                continue;

            if (!DoorController.KeyIdsMatch(requiredKeyId, keyItem.GetKeyId()))
                continue;

            if (!(interactable is IPickupable pickupable) || !pickupable.CanPickUp(picker))
                continue;

            float distance = Vector3.Distance(originPosition, remembered.lastKnownPosition);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestMemory = remembered;
        }

        return bestMemory != null;
    }

    public bool TryFindBestRememberedNeedTarget(
        List<RememberedInteractable> memory,
        NeedType needType,
        GameObject actor,
        Vector3 originPosition,
        float maxDistance,
        out RememberedInteractable bestMemory)
    {
        bestMemory = null;
        if (memory == null)
            return false;

        ForgetInvalidMemories(memory);

        float bestDistance = Mathf.Infinity;
        float bestScore = float.MinValue;

        for (int i = 0; i < memory.Count; i++)
        {
            RememberedInteractable remembered = memory[i];
            if (!IsRememberedNeedTargetCandidate(remembered, needType, actor))
                continue;

            float distance = Vector3.Distance(originPosition, remembered.lastKnownPosition);
            if (distance > maxDistance)
                continue;

            RestInteractable restInteractable = remembered.interactable as RestInteractable;
            float score = restInteractable != null
                ? restInteractable.Desirability / (1f + distance)
                : -distance;

            if (score > bestScore || (Mathf.Approximately(score, bestScore) && distance < bestDistance))
            {
                bestMemory = remembered;
                bestDistance = distance;
                bestScore = score;
            }
        }

        return bestMemory != null;
    }

    public bool TryFindBestRememberedActivityTarget(
        List<RememberedInteractable> memory,
        GameObject actor,
        Vector3 originPosition,
        float maxDistance,
        out RememberedInteractable bestMemory)
    {
        bestMemory = null;
        if (memory == null)
            return false;

        ForgetInvalidMemories(memory);

        float bestDistance = Mathf.Infinity;
        float bestScore = float.MinValue;

        for (int i = 0; i < memory.Count; i++)
        {
            RememberedInteractable remembered = memory[i];
            if (!IsRememberedActivityCandidate(remembered, actor))
                continue;

            float distance = Vector3.Distance(originPosition, remembered.lastKnownPosition);
            if (distance > maxDistance)
                continue;

            IActivityInteractable activity = remembered.interactable as IActivityInteractable;
            float score = activity.GetDesirability() / (1f + distance);
            if (score > bestScore || (Mathf.Approximately(score, bestScore) && distance < bestDistance))
            {
                bestMemory = remembered;
                bestDistance = distance;
                bestScore = score;
            }
        }

        return bestMemory != null;
    }

    public bool TryFindBestRememberedComfortZone(
        List<RememberedComfortZone> rememberedComfortZones,
        Vector3 originPosition,
        bool requireKnownLit,
        out RememberedComfortZone bestZone)
    {
        bestZone = null;
        if (rememberedComfortZones == null)
            return false;

        float bestDistance = Mathf.Infinity;

        for (int i = 0; i < rememberedComfortZones.Count; i++)
        {
            RememberedComfortZone zone = rememberedComfortZones[i];
            if (zone == null || zone.room == null)
                continue;

            if (requireKnownLit)
            {
                bool currentlyLit = zone.room.IsLit();
                bool knownLit = currentlyLit || zone.wasLitWhenLastSeen;
                if (!knownLit)
                    continue;
            }

            float distance = Vector3.Distance(originPosition, zone.lastKnownPosition);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestZone = zone;
        }

        return bestZone != null;
    }

    public bool HasRememberedOpportunity(
        List<RememberedInteractable> memory,
        NeedType needType,
        GameObject actor,
        Vector3 originPosition,
        float maxDistance)
    {
        if (memory == null)
            return false;

        ForgetInvalidMemories(memory);

        for (int i = 0; i < memory.Count; i++)
        {
            RememberedInteractable remembered = memory[i];
            if (remembered == null || remembered.interactable == null)
                continue;

            if (remembered.needType != needType)
                continue;

            if (!remembered.interactable.isEnabled || !remembered.interactable.CanInteract(actor))
                continue;

            if (Vector3.Distance(originPosition, remembered.lastKnownPosition) <= maxDistance)
                return true;
        }

        return false;
    }

    private bool IsRememberedNeedTargetCandidate(RememberedInteractable remembered, NeedType needType, GameObject actor)
    {
        if (remembered == null || remembered.interactable == null)
            return false;

        if (remembered.needType != needType)
            return false;

        if (remembered.isActivity)
            return false;

        if (remembered.interactable is IKeyItem)
            return false;

        if (!(remembered.interactable is INeedSatisfier))
            return false;

        return remembered.interactable.isEnabled && remembered.interactable.CanInteract(actor);
    }

    private bool IsRememberedActivityCandidate(RememberedInteractable remembered, GameObject actor)
    {
        if (remembered == null || remembered.interactable == null)
            return false;

        if (!remembered.isActivity)
            return false;

        if (!(remembered.interactable is IActivityInteractable))
            return false;

        return remembered.interactable.isEnabled && remembered.interactable.CanInteract(actor);
    }
}
