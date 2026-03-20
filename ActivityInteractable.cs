using UnityEngine;

public class ActivityInteractable : Interactable, IActivityInteractable
{
    [Header("Activity")]
    [SerializeField] private ActivityType activityType = ActivityType.ArcadeMachine;
    [SerializeField] private float desirability = 1f;
    [SerializeField] private float minimumUseDuration = 2f;
    [SerializeField] private float maximumUseDuration = 6f;
    [SerializeField] private bool interruptible = true;
    [SerializeField] private Transform activityAnchor;

    private GameObject activeUser;

    public override bool CanInteract(GameObject interactor)
    {
        if (!base.CanInteract(interactor))
            return false;

        if (interactor == null)
            return false;

        return activeUser == null || activeUser == interactor;
    }

    public override void Interact(GameObject interactor)
    {
        BeginActivity(interactor);
    }

    public ActivityType GetActivityType() => activityType;
    public float GetDesirability() => Mathf.Max(0f, desirability);
    public float GetMinimumUseDuration() => Mathf.Max(0f, minimumUseDuration);
    public float GetMaximumUseDuration() => Mathf.Max(GetMinimumUseDuration(), maximumUseDuration);
    public bool IsInterruptible() => interruptible;
    public Vector3 GetActivityAnchorPoint()
    {
        if (activityAnchor != null)
            return activityAnchor.position;

        return GetInteractionPoint();
    }

    public bool BeginActivity(GameObject interactor)
    {
        if (!CanInteract(interactor))
            return false;

        activeUser = interactor;
        return true;
    }

    public void EndActivity(GameObject interactor)
    {
        if (interactor == null || activeUser != interactor)
            return;

        activeUser = null;
    }

    private void OnDisable()
    {
        activeUser = null;
    }
}
