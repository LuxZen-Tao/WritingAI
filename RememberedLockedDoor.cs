using UnityEngine;

[System.Serializable]
public class RememberedLockedDoor
{
    public DoorInteractable door;
    public Vector3 lastKnownPosition;
    public string requiredKeyId;
    public float lastSeenTime;

    public RememberedLockedDoor(DoorInteractable door, Vector3 lastKnownPosition, string requiredKeyId, float lastSeenTime)
    {
        this.door = door;
        this.lastKnownPosition = lastKnownPosition;
        this.requiredKeyId = requiredKeyId;
        this.lastSeenTime = lastSeenTime;
    }
}
