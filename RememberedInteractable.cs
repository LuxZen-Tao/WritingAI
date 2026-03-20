using UnityEngine;

[System.Serializable]
public class RememberedInteractable
{
    public Interactable interactable;
    public NeedType needType;
    public bool isActivity;
    public ActivityType activityType;
    public Vector3 lastKnownPosition;
    public float lastSeenTime;

    public RememberedInteractable(Interactable interactable, NeedType needType, Vector3 lastKnownPosition, float lastSeenTime)
    {
        this.interactable = interactable;
        this.needType = needType;
        this.isActivity = false;
        this.activityType = ActivityType.Generic;
        this.lastKnownPosition = lastKnownPosition;
        this.lastSeenTime = lastSeenTime;
    }

    public RememberedInteractable(Interactable interactable, NeedType needType, Vector3 lastKnownPosition, float lastSeenTime, bool isActivity, ActivityType activityType)
    {
        this.interactable = interactable;
        this.needType = needType;
        this.isActivity = isActivity;
        this.activityType = activityType;
        this.lastKnownPosition = lastKnownPosition;
        this.lastSeenTime = lastSeenTime;
    }
}
