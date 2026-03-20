using System.Collections.Generic;
using UnityEngine;

public class ObservationZoneInteractable : Interactable, IActivityInteractable
{
    [Header("Observation Activity")]
    [SerializeField] private ActivityType activityType = ActivityType.ObservationZone;
    [SerializeField] private float desirability = 1.2f;
    [SerializeField] private float minimumUseDuration = 4f;
    [SerializeField] private float maximumUseDuration = 10f;
    [SerializeField] private bool interruptible = true;

    [Header("Zone Authoring")]
    [SerializeField] private Collider zoneCollider;
    [SerializeField] private Transform entryPoint;
    [SerializeField] private bool allowSmallWanderWithinZone = false;
    [SerializeField] private List<Transform> innerRoamPoints = new List<Transform>();
    [SerializeField] private List<Transform> innerLookPoints = new List<Transform>();
    [SerializeField] private List<Transform> seatPoints = new List<Transform>();

    [Header("Look Behavior")]
    [SerializeField] private float minimumLookDwellTime = 1.25f;
    [SerializeField] private float maximumLookDwellTime = 3f;
    [SerializeField] private int maxLookChanges = 0;

    private GameObject activeUser;
    private Transform activeAnchor;
    private int lookChangesThisSession = 0;

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

    public bool BeginActivity(GameObject interactor)
    {
        if (!CanInteract(interactor))
            return false;

        activeUser = interactor;
        lookChangesThisSession = 0;
        activeAnchor = ChooseAnchorPoint();
        return true;
    }

    public void EndActivity(GameObject interactor)
    {
        if (interactor == null || activeUser != interactor)
            return;

        activeUser = null;
        activeAnchor = null;
        lookChangesThisSession = 0;
    }

    public Vector3 GetActivityAnchorPoint()
    {
        if (activeAnchor != null)
            return activeAnchor.position;

        Transform fallback = ChooseAnchorPoint();
        if (fallback != null)
            return fallback.position;

        return GetInteractionPoint();
    }

    public override Vector3 GetInteractionPoint()
    {
        if (entryPoint != null)
            return entryPoint.position;

        return base.GetInteractionPoint();
    }

    public bool TryGetNextLookPoint(out Vector3 lookPoint)
    {
        lookPoint = Vector3.zero;

        if (innerLookPoints == null || innerLookPoints.Count == 0)
            return false;

        if (maxLookChanges > 0 && lookChangesThisSession >= maxLookChanges)
            return false;

        Transform chosen = innerLookPoints[Random.Range(0, innerLookPoints.Count)];
        if (chosen == null)
            return false;

        lookChangesThisSession++;
        lookPoint = chosen.position;
        return true;
    }

    public float GetNextLookDwellDuration()
    {
        float min = Mathf.Max(0.1f, minimumLookDwellTime);
        float max = Mathf.Max(min, maximumLookDwellTime);
        return Random.Range(min, max);
    }

    public bool IsInsideZone(Vector3 worldPosition)
    {
        if (zoneCollider == null)
            return true;

        return zoneCollider.bounds.Contains(worldPosition);
    }

    private Transform ChooseAnchorPoint()
    {
        Transform seat = PickRandomPoint(seatPoints);
        if (seat != null)
            return seat;

        if (allowSmallWanderWithinZone)
        {
            Transform roam = PickRandomPoint(innerRoamPoints);
            if (roam != null)
                return roam;
        }

        if (entryPoint != null)
            return entryPoint;

        return transform;
    }

    private Transform PickRandomPoint(List<Transform> points)
    {
        if (points == null || points.Count == 0)
            return null;

        List<Transform> validPoints = new List<Transform>();
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] != null)
                validPoints.Add(points[i]);
        }

        if (validPoints.Count == 0)
            return null;

        return validPoints[Random.Range(0, validPoints.Count)];
    }

    private void OnValidate()
    {
        if (zoneCollider == null)
            zoneCollider = GetComponent<Collider>();
    }

    private void OnDisable()
    {
        activeUser = null;
        activeAnchor = null;
        lookChangesThisSession = 0;
    }
}
