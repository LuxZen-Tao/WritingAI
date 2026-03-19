using System.Collections.Generic;
using UnityEngine;

public class NpcMemoryService
{
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

    public MemoryWriteResult RememberInteractable(List<RememberedInteractable> memory, Interactable interactable, NeedType needType, float now)
    {
        if (memory == null || interactable == null)
            return MemoryWriteResult.None;

        for (int i = 0; i < memory.Count; i++)
        {
            if (memory[i] == null || memory[i].interactable != interactable)
                continue;

            memory[i].lastKnownPosition = interactable.GetInteractionPoint();
            memory[i].needType = needType;
            memory[i].lastSeenTime = now;
            return MemoryWriteResult.Updated;
        }

        memory.Add(new RememberedInteractable(interactable, needType, interactable.GetInteractionPoint(), now));
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

    public bool RememberLockedDoor(List<RememberedLockedDoor> rememberedLockedDoors, DoorInteractable door, float now)
    {
        if (rememberedLockedDoors == null || door == null)
            return false;

        DoorController controller = door.GetDoorController();
        if (controller == null || !controller.IsLocked)
            return false;

        string requiredKeyId = controller.RequiredKeyId;
        if (string.IsNullOrWhiteSpace(requiredKeyId))
            return false;

        for (int i = 0; i < rememberedLockedDoors.Count; i++)
        {
            RememberedLockedDoor remembered = rememberedLockedDoors[i];
            if (remembered == null || remembered.door != door)
                continue;

            remembered.lastKnownPosition = door.GetInteractionPoint();
            remembered.requiredKeyId = requiredKeyId;
            remembered.lastSeenTime = now;
            return false;
        }

        rememberedLockedDoors.Add(new RememberedLockedDoor(door, door.GetInteractionPoint(), requiredKeyId, now));
        return true;
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
}
