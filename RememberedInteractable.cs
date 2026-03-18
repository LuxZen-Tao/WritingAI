using UnityEngine;

[System.Serializable]
public class RememberedInteractable
{
    public Interactable interactable;
    public NeedType needType;
    public Vector3 lastKnownPosition;
    public float lastSeenTime;

    public RememberedInteractable(Interactable interactable, NeedType needType, Vector3 lastKnownPosition, float lastSeenTime)
    {
        this.interactable = interactable;
        this.needType = needType;
        this.lastKnownPosition = lastKnownPosition;
        this.lastSeenTime = lastSeenTime;
    }
}