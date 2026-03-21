using System.Collections.Generic;
using UnityEngine;

public class NpcPerceptionService
{
    private float visionRange;
    private float visionAngle;
    private LayerMask interactableLayer;
    private LayerMask obstacleLayer;
    private LayerMask doorLayer;
    private Transform eyePoint;

    public void Configure(
        float visionRange,
        float visionAngle,
        LayerMask interactableLayer,
        LayerMask obstacleLayer,
        LayerMask doorLayer,
        Transform eyePoint)
    {
        this.visionRange = visionRange;
        this.visionAngle = visionAngle;
        this.interactableLayer = interactableLayer;
        this.obstacleLayer = obstacleLayer;
        this.doorLayer = doorLayer;
        this.eyePoint = eyePoint;
    }

    public bool CanSeePoint(Transform observer, Vector3 forward, Vector3 targetPoint)
    {
        Vector3 origin = eyePoint != null ? eyePoint.position : observer.position + Vector3.up * 1.5f;
        Vector3 directionToTarget = targetPoint - origin;
        float distance = directionToTarget.magnitude;

        if (distance > visionRange)
            return false;

        float angle = Vector3.Angle(forward, directionToTarget);
        if (angle > visionAngle * 0.5f)
            return false;

        if (Physics.Raycast(origin, directionToTarget.normalized, out RaycastHit _, distance, obstacleLayer))
            return false;

        return true;
    }

    public bool CanSeeInteractable(Transform observer, Vector3 forward, Interactable interactable)
    {
        if (interactable == null)
            return false;

        Vector3 origin = eyePoint != null ? eyePoint.position : observer.position + Vector3.up * 1.5f;
        Vector3 target = interactable.GetInteractionPoint();
        Vector3 directionToTarget = target - origin;
        float distanceToTarget = directionToTarget.magnitude;

        if (distanceToTarget > visionRange)
            return false;

        float angleToTarget = Vector3.Angle(forward, directionToTarget);
        if (angleToTarget > visionAngle * 0.5f)
            return false;

        if (Physics.Raycast(origin, directionToTarget.normalized, out RaycastHit _, distanceToTarget, obstacleLayer))
            return false;

        return true;
    }

    public List<Interactable> GetVisibleInteractables(Transform observer, Vector3 forward)
    {
        List<Interactable> visible = new List<Interactable>();
        HashSet<Interactable> seenInteractables = new HashSet<Interactable>();
        Collider[] hits = Physics.OverlapSphere(observer.position, visionRange, interactableLayer);

        for (int i = 0; i < hits.Length; i++)
        {
            Interactable interactable = hits[i].GetComponentInParent<Interactable>();
            if (interactable == null || !interactable.isEnabled)
                continue;

            if (!seenInteractables.Add(interactable))
                continue;

            if (!CanSeeInteractable(observer, forward, interactable))
                continue;

            visible.Add(interactable);
        }

        return visible;
    }

    public bool TryFindBestVisibleMatchingKey(
        Transform observer,
        Vector3 forward,
        GameObject picker,
        string requiredKeyId,
        out Interactable bestKey)
    {
        bestKey = null;
        float bestDistance = Mathf.Infinity;
        List<Interactable> visible = GetVisibleInteractables(observer, forward);

        for (int i = 0; i < visible.Count; i++)
        {
            Interactable interactable = visible[i];
            if (!(interactable is IKeyItem keyItem))
                continue;

            if (!DoorController.KeyIdsMatch(requiredKeyId, keyItem.GetKeyId()))
                continue;

            if (!(interactable is IPickupable pickupable) || !pickupable.CanPickUp(picker))
                continue;

            float distance = Vector3.Distance(observer.position, interactable.GetInteractionPoint());
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestKey = interactable;
        }

        return bestKey != null;
    }

    public bool TryGetBlockingDoorTowards(Transform observer, Vector3 destination, GameObject interactor, out DoorInteractable door, float doorCheckDistance)
    {
        door = null;

        Vector3 origin = eyePoint != null ? eyePoint.position : observer.position + Vector3.up * 1.2f;
        Vector3 toDestination = destination - origin;
        toDestination.y = 0f;
        float distanceToDestination = toDestination.magnitude;
        if (distanceToDestination <= 0.05f)
            return false;

        Vector3 probeDirection = toDestination / distanceToDestination;
        float probeDistance = Mathf.Min(doorCheckDistance, distanceToDestination);
        int probeMask = doorLayer.value == 0 ? interactableLayer : doorLayer;

        if (!Physics.SphereCast(origin, 0.2f, probeDirection, out RaycastHit hit, probeDistance, probeMask, QueryTriggerInteraction.Collide))
            return false;

        DoorInteractable hitDoor = hit.collider.GetComponentInParent<DoorInteractable>();
        if (hitDoor == null || !hitDoor.CanInteract(interactor))
            return false;

        door = hitDoor;
        return true;
    }
}
