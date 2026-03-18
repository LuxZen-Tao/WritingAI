using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class SimpleNPCBrain : MonoBehaviour
{
    public enum AIState
    {
        Idle,
        IdleWander,
        CheckNeed,
        FindTarget,
        Search,
        Explore,
        MoveToTarget,
        MoveToRememberedTarget,
        InteractWithTarget
    }

    [Header("State")]
    public AIState currentState = AIState.Idle;

    [Header("Movement Speeds")]
    public float idleMoveSpeed = 1f;
    public float needMoveSpeed = 2.5f;

    [Header("Comfort")]
    public float comfort = 10f;
    public float maxComfort = 10f;
    public float comfortThreshold = 5f;
    public float comfortDecayRate = 2f;
    public float comfortRecoveryRate = 1f;

    [Header("Area Context")]
    public RoomArea currentRoom;
    public WorldArea worldArea;

    [Header("Vision")]
    public float visionRange = 8f;
    [Range(0f, 360f)]
    public float visionAngle = 90f;
    public LayerMask interactableLayer;
    public LayerMask obstacleLayer;
    public Transform eyePoint;

    [Header("Search")]
    public float searchTurnSpeed = 60f;
    public float searchDuration = 2f;

    [Header("Explore")]
    public float exploreRadius = 4f;
    public float exploreDuration = 3f;
    public float exploreStopDistance = 0.5f;

    [Header("Idle Wander")]
    public float idleWanderRadius = 3f;
    public float idleWanderDuration = 4f;
    public float idleWanderStopDistance = 0.5f;
    public float idlePauseDuration = 2f;
    public float minimumIdleMoveDistance = 1.0f;

    [Header("Memory - Interactables")]
    public List<RememberedInteractable> memory = new List<RememberedInteractable>();
    public float rememberedTargetStopDistance = 1f;
    public float interactableMemoryDuration = 60f;

    [Header("Memory - Locations")]
    public List<RememberedLocation> rememberedLocations = new List<RememberedLocation>();
    public float locationMemoryDuration = 10f;
    public float locationAvoidanceRadius = 2f;

    [Header("Current Target")]
    public Interactable currentTarget;

    private NavMeshAgent agent;
    private float searchTimer = 0f;
    private float exploreTimer = 0f;
    private float idleTimer = 0f;

    private Vector3 currentExplorePoint;
    private bool hasExplorePoint = false;

    private Vector3 currentIdlePoint;
    private bool hasIdlePoint = false;

    private RememberedInteractable currentMemoryTarget;
    private NeedType currentNeedType = NeedType.Comfort;

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        if (agent == null)
        {
            Debug.LogError("No NavMeshAgent found on " + gameObject.name);
            enabled = false;
            return;
        }

        ChangeState(AIState.CheckNeed);
    }

    private void Update()
    {
        UpdateComfort();
        CleanupLocationMemory();
        CleanupInteractableMemory();
        PassiveObserveVisibleInteractables();

        switch (currentState)
        {
            case AIState.Idle:
                HandleIdleState();
                break;

            case AIState.IdleWander:
                HandleIdleWander();
                break;

            case AIState.CheckNeed:
                EvaluateNeeds();
                break;

            case AIState.FindTarget:
                FindBestTargetForNeed(currentNeedType);
                break;

            case AIState.Search:
                SearchForTarget();
                break;

            case AIState.Explore:
                ExploreForTarget();
                break;

            case AIState.MoveToTarget:
                MoveToCurrentTarget();
                break;

            case AIState.MoveToRememberedTarget:
                MoveToRememberedTarget();
                break;

            case AIState.InteractWithTarget:
                InteractWithCurrentTarget();
                break;
        }
    }

    private bool IsCurrentAreaLit()
    {
        if (currentRoom != null)
        {
            return currentRoom.IsLit();
        }

        if (worldArea != null)
        {
            return worldArea.IsDaytime();
        }

        return false;
    }

    private string GetCurrentAreaName()
    {
        if (currentRoom != null)
        {
            return currentRoom.roomName;
        }

        if (worldArea != null)
        {
            return worldArea.worldName;
        }

        return "Unknown Area";
    }

    private void UpdateComfort()
    {
        bool areaIsLit = IsCurrentAreaLit();

        if (!areaIsLit)
        {
            comfort -= comfortDecayRate * Time.deltaTime;
        }
        else
        {
            comfort += comfortRecoveryRate * Time.deltaTime;
        }

        comfort = Mathf.Clamp(comfort, 0f, maxComfort);
    }

    private bool HasUrgentNeed()
    {
        return comfort < comfortThreshold;
    }

    private void EvaluateNeeds()
    {
        if (HasUrgentNeed())
        {
            currentNeedType = NeedType.Comfort;

            if (currentRoom != null && currentRoom.IsLit())
            {
                ChangeState(AIState.Idle);
                return;
            }

            Debug.Log("Comfort is low in " + GetCurrentAreaName() + ". Looking for something to improve comfort.");
            ChangeState(AIState.FindTarget);
        }
        else
        {
            ChangeState(AIState.Idle);
        }
    }

    private void HandleIdleState()
    {
        if (HasUrgentNeed())
        {
            currentNeedType = NeedType.Comfort;

            if (currentRoom != null && currentRoom.IsLit())
            {
                // Need is recovering in a valid comfort zone, but can still wander locally
            }
            else
            {
                ChangeState(AIState.FindTarget);
                return;
            }
        }

        idleTimer += Time.deltaTime;

        if (idleTimer >= idlePauseDuration)
        {
            idleTimer = 0f;
            ChangeState(AIState.IdleWander);
        }
    }

    private void HandleIdleWander()
    {
        agent.speed = idleMoveSpeed;

        if (HasUrgentNeed())
        {
            currentNeedType = NeedType.Comfort;

            if (!(currentRoom != null && currentRoom.IsLit()))
            {
                hasIdlePoint = false;
                agent.ResetPath();
                ChangeState(AIState.FindTarget);
                return;
            }
        }

        if (!hasIdlePoint)
        {
            bool foundPoint = TryPickIdlePoint();

            if (!foundPoint)
            {
                agent.ResetPath();
                ChangeState(AIState.Idle);
                return;
            }
        }

        agent.SetDestination(currentIdlePoint);

        if (!agent.pathPending && agent.remainingDistance <= idleWanderStopDistance)
        {
            // No recent-location memory for idle wandering
            hasIdlePoint = false;
            agent.ResetPath();
            ChangeState(AIState.Idle);
            return;
        }

        idleTimer += Time.deltaTime;

        if (idleTimer >= idleWanderDuration)
        {
            // No recent-location memory for idle wandering
            hasIdlePoint = false;
            idleTimer = 0f;
            agent.ResetPath();
            ChangeState(AIState.Idle);
        }
    }

    private void FindBestTargetForNeed(NeedType needType)
    {
        if (TryAcquireVisibleTarget(needType))
            return;

        if (TryUseRememberedTarget(needType))
            return;

        Debug.Log("No visible or remembered target found for " + needType + ". Searching...");
        searchTimer = 0f;
        ChangeState(AIState.Search);
    }

    private void PassiveObserveVisibleInteractables()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, visionRange, interactableLayer);

        foreach (Collider hit in hits)
        {
            Interactable interactable = hit.GetComponentInParent<Interactable>();

            if (interactable == null)
                continue;

            if (!interactable.isEnabled)
                continue;

            if (!CanSeeInteractable(interactable))
                continue;

            INeedSatisfier satisfier = interactable as INeedSatisfier;

            if (satisfier == null)
                continue;

            RememberInteractable(interactable, satisfier.GetNeedType());
        }
    }

    private bool TryAcquireVisibleTarget(NeedType needType)
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, visionRange, interactableLayer);

        Interactable bestTarget = null;
        float bestDistance = Mathf.Infinity;

        foreach (Collider hit in hits)
        {
            Interactable interactable = hit.GetComponentInParent<Interactable>();

            if (interactable == null)
                continue;

            if (!interactable.isEnabled)
                continue;

            if (!CanSeeInteractable(interactable))
                continue;

            INeedSatisfier satisfier = interactable as INeedSatisfier;

            if (satisfier == null)
                continue;

            if (satisfier.GetNeedType() != needType)
                continue;

            if (!interactable.CanInteract(gameObject))
                continue;

            RememberInteractable(interactable, needType);

            float distance = Vector3.Distance(transform.position, interactable.GetInteractionPoint());

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestTarget = interactable;
            }
        }

        if (bestTarget != null)
        {
            currentTarget = bestTarget;
            currentMemoryTarget = null;
            hasExplorePoint = false;
            hasIdlePoint = false;
            agent.ResetPath();

            Debug.Log("Locked visible target: " + bestTarget.interactableName);
            ChangeState(AIState.MoveToTarget);
            return true;
        }

        return false;
    }

    private bool TryUseRememberedTarget(NeedType needType)
    {
        ForgetInvalidMemories();

        RememberedInteractable bestMemory = null;
        float bestDistance = Mathf.Infinity;

        foreach (RememberedInteractable remembered in memory)
        {
            if (remembered == null)
                continue;

            if (remembered.interactable == null)
                continue;

            if (remembered.needType != needType)
                continue;

            if (!remembered.interactable.isEnabled)
                continue;

            if (!remembered.interactable.CanInteract(gameObject))
                continue;

            float distance = Vector3.Distance(transform.position, remembered.lastKnownPosition);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestMemory = remembered;
            }
        }

        if (bestMemory != null)
        {
            currentMemoryTarget = bestMemory;
            currentTarget = bestMemory.interactable;
            hasExplorePoint = false;
            hasIdlePoint = false;
            agent.ResetPath();

            Debug.Log("Using remembered target: " + bestMemory.interactable.interactableName);
            ChangeState(AIState.MoveToRememberedTarget);
            return true;
        }

        return false;
    }

    private void RememberInteractable(Interactable interactable, NeedType needType)
    {
        if (interactable == null)
            return;

        for (int i = 0; i < memory.Count; i++)
        {
            if (memory[i] != null && memory[i].interactable == interactable)
            {
                memory[i].lastKnownPosition = interactable.GetInteractionPoint();
                memory[i].needType = needType;
                memory[i].lastSeenTime = Time.time;
                return;
            }
        }

        memory.Add(new RememberedInteractable(
            interactable,
            needType,
            interactable.GetInteractionPoint(),
            Time.time
        ));
    }

    private void ForgetInvalidMemories()
    {
        memory.RemoveAll(m => m == null || m.interactable == null);
    }

    private void CleanupInteractableMemory()
    {
        memory.RemoveAll(m =>
            m == null ||
            m.interactable == null ||
            Time.time - m.lastSeenTime > interactableMemoryDuration
        );
    }

    private void RememberLocation(Vector3 position)
    {
        rememberedLocations.Add(new RememberedLocation(position, Time.time));
    }

    private void CleanupLocationMemory()
    {
        rememberedLocations.RemoveAll(loc => Time.time - loc.timeStored > locationMemoryDuration);
    }

    private bool IsRecentlyVisited(Vector3 position)
    {
        foreach (RememberedLocation loc in rememberedLocations)
        {
            if (Vector3.Distance(position, loc.position) <= locationAvoidanceRadius)
            {
                return true;
            }
        }

        return false;
    }

    private bool CanSeeInteractable(Interactable interactable)
    {
        if (interactable == null)
            return false;

        Vector3 origin = eyePoint != null ? eyePoint.position : transform.position + Vector3.up * 1.5f;
        Vector3 target = interactable.GetInteractionPoint();

        Vector3 directionToTarget = target - origin;
        float distanceToTarget = directionToTarget.magnitude;

        if (distanceToTarget > visionRange)
            return false;

        float angleToTarget = Vector3.Angle(transform.forward, directionToTarget);

        if (angleToTarget > visionAngle * 0.5f)
            return false;

        if (Physics.Raycast(origin, directionToTarget.normalized, out RaycastHit hit, distanceToTarget, obstacleLayer))
        {
            return false;
        }

        return true;
    }

    private void SearchForTarget()
    {
        if (TryAcquireVisibleTarget(currentNeedType))
            return;

        searchTimer += Time.deltaTime;
        transform.Rotate(0f, searchTurnSpeed * Time.deltaTime, 0f);

        if (searchTimer >= searchDuration)
        {
            searchTimer = 0f;
            ChangeState(AIState.Explore);
        }
    }

    private void ExploreForTarget()
    {
        if (TryAcquireVisibleTarget(currentNeedType))
            return;

        agent.speed = needMoveSpeed;
        exploreTimer += Time.deltaTime;

        if (!hasExplorePoint)
        {
            bool foundPoint = TryPickExplorePoint();

            if (!foundPoint)
            {
                Debug.Log("Could not find explore point. Returning to search.");
                exploreTimer = 0f;
                ChangeState(AIState.Search);
                return;
            }
        }

        agent.SetDestination(currentExplorePoint);

        if (!agent.pathPending && agent.remainingDistance <= exploreStopDistance)
        {
            RememberLocation(currentExplorePoint);
            exploreTimer = 0f;
            hasExplorePoint = false;
            agent.ResetPath();

            if (TryAcquireVisibleTarget(currentNeedType))
                return;

            if (TryUseRememberedTarget(currentNeedType))
                return;

            ChangeState(AIState.FindTarget);
            return;
        }

        if (exploreTimer >= exploreDuration)
        {
            RememberLocation(currentExplorePoint);
            exploreTimer = 0f;
            hasExplorePoint = false;
            agent.ResetPath();

            if (TryAcquireVisibleTarget(currentNeedType))
                return;

            if (TryUseRememberedTarget(currentNeedType))
                return;

            ChangeState(AIState.FindTarget);
        }
    }

    private bool TryPickExplorePoint()
    {
        for (int i = 0; i < 12; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * exploreRadius;
            randomOffset.y = 0f;

            Vector3 candidatePoint = transform.position + randomOffset;

            if (IsRecentlyVisited(candidatePoint))
                continue;

            if (NavMesh.SamplePosition(candidatePoint, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
            {
                if (IsRecentlyVisited(navHit.position))
                    continue;

                currentExplorePoint = navHit.position;
                hasExplorePoint = true;
                exploreTimer = 0f;

                Debug.Log("New explore point chosen: " + currentExplorePoint);
                return true;
            }
        }

        return false;
    }

    private bool TryPickIdlePoint()
    {
        if (ShouldConstrainIdleToComfortRoom())
        {
            return TryPickIdlePointInsideCurrentRoom();
        }

        for (int i = 0; i < 10; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * idleWanderRadius;
            randomOffset.y = 0f;

            Vector3 candidatePoint = transform.position + randomOffset;

            float distanceFromCurrent = Vector3.Distance(transform.position, candidatePoint);
            if (distanceFromCurrent < minimumIdleMoveDistance)
                continue;

            if (NavMesh.SamplePosition(candidatePoint, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
            {
                currentIdlePoint = navHit.position;
                hasIdlePoint = true;
                idleTimer = 0f;
                return true;
            }
        }

        return false;
    }

    private bool TryPickIdlePointInsideCurrentRoom()
    {
        if (currentRoom == null)
            return false;

        for (int i = 0; i < 16; i++)
        {
            if (!currentRoom.TryGetRandomPointInBounds(randomPointMinDistance, out Vector3 candidatePoint))
                continue;

            if (NavMesh.SamplePosition(candidatePoint, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
            {
                if (!currentRoom.ContainsPoint(navHit.position))
                    continue;

                currentIdlePoint = navHit.position;
                hasIdlePoint = true;
                idleTimer = 0f;
                return true;
            }
        }

        return false;
    }

    private void AbortCurrentNeedAction()
    {
        currentTarget = null;
        currentMemoryTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();

        Debug.Log("Need no longer urgent. Aborting current target/action.");
        ChangeState(AIState.Idle);
    }

    private void MoveToCurrentTarget()
    {
        if (!IsNeedUrgent(currentNeedType))
        {
            AbortCurrentNeedAction();
            return;
        }

        if (currentTarget == null)
        {
            Debug.LogWarning("No target to move to.");
            ChangeState(AIState.Idle);
            return;
        }

        agent.speed = needMoveSpeed;

        Vector3 targetPosition = currentTarget.GetInteractionPoint();
        agent.SetDestination(targetPosition);

        if (!agent.pathPending && agent.remainingDistance <= currentTarget.interactionRange)
        {
            if (!IsNeedUrgent(currentNeedType))
            {
                AbortCurrentNeedAction();
                return;
            }

            ChangeState(AIState.InteractWithTarget);
        }
    }

    private void MoveToRememberedTarget()
    {
        if (!IsNeedUrgent(currentNeedType))
        {
            AbortCurrentNeedAction();
            return;
        }

        if (currentMemoryTarget == null || currentMemoryTarget.interactable == null)
        {
            Debug.Log("Remembered target is no longer valid.");
            currentMemoryTarget = null;
            currentTarget = null;
            ChangeState(AIState.FindTarget);
            return;
        }

        if (TryAcquireVisibleTarget(currentNeedType))
            return;

        agent.speed = needMoveSpeed;
        agent.SetDestination(currentMemoryTarget.lastKnownPosition);

        if (!agent.pathPending && agent.remainingDistance <= rememberedTargetStopDistance)
        {
            if (!IsNeedUrgent(currentNeedType))
            {
                AbortCurrentNeedAction();
                return;
            }

            if (currentTarget != null && currentTarget.CanInteract(gameObject) && CanSeeInteractable(currentTarget))
            {
                ChangeState(AIState.MoveToTarget);
                return;
            }

            memory.Remove(currentMemoryTarget);
            currentMemoryTarget = null;
            currentTarget = null;
            ChangeState(AIState.FindTarget);
        }
    }

    private void InteractWithCurrentTarget()
    {
        if (currentTarget == null)
        {
            Debug.LogWarning("No target to interact with.");
            ChangeState(AIState.Idle);
            return;
        }

        agent.ResetPath();

        if (currentTarget.CanInteract(gameObject))
        {
            currentTarget.Interact(gameObject);
        }

        currentTarget = null;
        currentMemoryTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        ChangeState(AIState.Idle);
    }

    private void ChangeState(AIState newState)
    {
        if (currentState == newState)
            return;

        currentState = newState;
        Debug.Log("State changed to: " + currentState);

        switch (currentState)
        {
            case AIState.Search:
                searchTimer = 0f;
                agent.ResetPath();
                break;

            case AIState.Explore:
                exploreTimer = 0f;
                hasExplorePoint = false;
                break;

            case AIState.Idle:
                idleTimer = 0f;
                hasIdlePoint = false;
                agent.ResetPath();
                break;

            case AIState.IdleWander:
                idleTimer = 0f;
                hasIdlePoint = false;
                break;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        RoomArea room = other.GetComponentInParent<RoomArea>();

        if (room == null)
            return;

        if (!overlappingRooms.Contains(room))
        {
            overlappingRooms.Add(room);
        }

        ResolveCurrentRoom();
    }

    private void OnTriggerStay(Collider other)
    {
        RoomArea room = other.GetComponentInParent<RoomArea>();

        if (room != null)
        {
            currentRoom = room;
            Debug.Log(gameObject.name + " entered room: " + room.roomName);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        RoomArea room = other.GetComponentInParent<RoomArea>();

        if (room == null)
            return;

        overlappingRooms.Remove(room);
        ResolveCurrentRoom();
    }

    private void ResolveCurrentRoom()
    {
        RoomArea containingRoom = null;
        float containingDistanceSqr = Mathf.Infinity;
        RoomArea fallbackRoom = null;
        float fallbackDistanceSqr = Mathf.Infinity;

        for (int i = overlappingRooms.Count - 1; i >= 0; i--)
        {
            RoomArea room = overlappingRooms[i];
            if (room == null)
            {
                overlappingRooms.RemoveAt(i);
                continue;
            }

            float distanceSqr = (room.transform.position - transform.position).sqrMagnitude;

            if (room.ContainsPoint(transform.position))
            {
                if (distanceSqr < containingDistanceSqr)
                {
                    containingDistanceSqr = distanceSqr;
                    containingRoom = room;
                }
                continue;
            }

            if (distanceSqr < fallbackDistanceSqr)
            {
                fallbackDistanceSqr = distanceSqr;
                fallbackRoom = room;
            }
        }

        currentRoom = containingRoom != null ? containingRoom : fallbackRoom;
    }

    private bool IsNeedUrgent(NeedType needType)
    {
        switch (needType)
        {
            case NeedType.Comfort:
                return comfortNeedActive && !IsComfortSatisfiedByEnvironment();
        }

        return false;
    }

    private bool IsComfortSatisfiedByEnvironment()
    {
        RoomArea activeArea = GetActiveArea();
        return activeArea != null && activeArea.IsLit();
    }

    private bool ShouldConstrainIdleToComfortRoom()
    {
        return currentRoom != null && currentRoom.IsLit();
    }

    private void ClearCurrentIntent(bool clearNavigation)
    {
        currentTarget = null;
        currentMemoryTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;

        if (clearNavigation)
        {
            ResetNavigationTracking();
        }
    }

    private void AbortCurrentNeedAction()
    {
        ClearCurrentIntent(true);
        ChangeState(AIState.Idle);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = eyePoint != null ? eyePoint.position : transform.position + Vector3.up * 1.5f;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, visionRange);

        Vector3 leftBoundary = Quaternion.Euler(0f, -visionAngle * 0.5f, 0f) * transform.forward;
        Vector3 rightBoundary = Quaternion.Euler(0f, visionAngle * 0.5f, 0f) * transform.forward;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + leftBoundary * visionRange);
        Gizmos.DrawLine(origin, origin + rightBoundary * visionRange);

        if (hasExplorePoint)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(currentExplorePoint, 0.2f);
            Gizmos.DrawLine(transform.position, currentExplorePoint);
        }

        if (hasIdlePoint)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawSphere(currentIdlePoint, 0.2f);
            Gizmos.DrawLine(transform.position, currentIdlePoint);
        }

        if (currentMemoryTarget != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(currentMemoryTarget.lastKnownPosition, 0.25f);
            Gizmos.DrawLine(transform.position, currentMemoryTarget.lastKnownPosition);
        }
    }
}