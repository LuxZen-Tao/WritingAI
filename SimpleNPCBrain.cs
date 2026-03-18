using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class SimpleNPCBrain : MonoBehaviour
{
    public enum AIState
    {
        IdleWander,
        Explore,
        MoveToTarget,
        MoveToRememberedTarget,
        InteractWithTarget
    }

    [Header("State")]
    public AIState currentState = AIState.IdleWander;

    [Header("Movement Speeds")]
    public float idleMoveSpeed = 1f;
    public float needMoveSpeed = 2.5f;

    [Header("Needs")]
    public NeedsManager needsManager;

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

    [Header("Explore")]
    public float exploreRadius = 5f;
    public float exploreDuration = 3f;
    public float exploreStopDistance = 0.5f;

    [Header("Explore Recheck")]
    public float exploreRecheckInterval = 0.75f;
    private float exploreRecheckTimer = 0f;

    [Header("Idle Wander")]
    public float idleWanderRadius = 3f;
    public float idleWanderStopDistance = 0.5f;
    public float idlePauseDuration = 2f;
    public float minimumIdleMoveDistance = 0.5f;
    public float roomPointInset = 0.2f;

    [Header("Memory - Interactables")]
    public List<RememberedInteractable> memory = new List<RememberedInteractable>();
    public float rememberedTargetStopDistance = 1f;
    public float interactableMemoryDuration = 60f;

    [Header("Memory - Comfort Zones")]
    public List<RememberedComfortZone> rememberedComfortZones = new List<RememberedComfortZone>();
    public float comfortZoneMemoryDuration = 60f;
    public float rememberedComfortZoneStopDistance = 1f;

    [Header("Memory - Locations")]
    public List<RememberedLocation> rememberedLocations = new List<RememberedLocation>();
    public float locationMemoryDuration = 10f;
    public float locationAvoidanceRadius = 2f;

    [Header("Current Target")]
    public Interactable currentTarget;

    private readonly List<RoomArea> overlappingRooms = new List<RoomArea>();
    private RoomArea[] knownRooms;

    private NavMeshAgent agent;

    private float exploreTimer = 0f;
    private float idlePauseTimer = 0f;

    private Vector3 currentExplorePoint;
    private bool hasExplorePoint = false;

    private Vector3 currentIdlePoint;
    private bool hasIdlePoint = false;

    private RememberedInteractable currentMemoryTarget;
    private RememberedComfortZone currentComfortZoneTarget;
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

        if (needsManager == null)
        {
            needsManager = GetComponent<NeedsManager>();
        }

        if (needsManager == null)
        {
            Debug.LogError("No NeedsManager found on " + gameObject.name);
            enabled = false;
            return;
        }

        knownRooms = FindObjectsByType<RoomArea>(FindObjectsSortMode.None);

        currentState = AIState.IdleWander;
        hasIdlePoint = false;
        idlePauseTimer = idlePauseDuration;
    }

    private void Update()
    {
        needsManager.TickNeeds(IsCurrentAreaLit(), Time.deltaTime);
        CleanupLocationMemory();
        CleanupInteractableMemory();
        CleanupComfortZoneMemory();

        PassiveObserveVisibleInteractables();
        PassiveObserveVisibleComfortZones();

        if (HasUrgentNeed())
        {
            NeedType mostUrgentNeed = needsManager.GetMostUrgentNeed();
            bool needChanged = mostUrgentNeed != currentNeedType;
            currentNeedType = mostUrgentNeed;

            if (needChanged && IsNeedDrivenState(currentState))
            {
                RestartNeedSearch();
                return;
            }

            if (IsNeedCurrentlySatisfied(currentNeedType))
            {
                if (IsNeedDrivenState(currentState))
                {
                    AbortCurrentNeedAction();
                    return;
                }
            }
            else if (currentState == AIState.IdleWander)
            {
                ChangeState(AIState.Explore);
            }
        }
        else if (IsNeedDrivenState(currentState))
        {
            AbortCurrentNeedAction();
            return;
        }

        switch (currentState)
        {
            case AIState.IdleWander:
                HandleIdleWander();
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
            return currentRoom.IsLit();

        if (worldArea != null)
            return worldArea.IsDaytime();

        return false;
    }

    private bool IsNeedCurrentlySatisfied(NeedType needType)
    {
        if (needType == NeedType.Comfort)
            return IsCurrentAreaLit();

        return !needsManager.IsNeedUrgent(needType);
    }

    private bool HasUrgentNeed()
    {
        return needsManager != null && needsManager.HasUrgentNeed();
    }

    private bool IsNeedDrivenState(AIState state)
    {
        return state == AIState.Explore ||
               state == AIState.MoveToTarget ||
               state == AIState.MoveToRememberedTarget ||
               state == AIState.InteractWithTarget;
    }

    private void HandleIdleWander()
    {
        agent.speed = idleMoveSpeed;

        if (idlePauseTimer > 0f)
        {
            idlePauseTimer -= Time.deltaTime;

            if (agent.hasPath)
                agent.ResetPath();

            return;
        }

        if (!hasIdlePoint)
        {
            if (!TryPickIdlePoint())
            {
                idlePauseTimer = idlePauseDuration;

                if (agent.hasPath)
                    agent.ResetPath();

                return;
            }
        }

        agent.SetDestination(currentIdlePoint);

        if (!agent.pathPending && agent.remainingDistance <= idleWanderStopDistance)
        {
            hasIdlePoint = false;
            idlePauseTimer = idlePauseDuration;

            if (agent.hasPath)
                agent.ResetPath();
        }
    }

    private void ExploreForTarget()
    {
        if (IsNeedCurrentlySatisfied(currentNeedType))
        {
            AbortCurrentNeedAction();
            return;
        }

        exploreRecheckTimer -= Time.deltaTime;
        bool shouldRecheckKnownTargets = !hasExplorePoint || exploreRecheckTimer <= 0f;

        if (shouldRecheckKnownTargets)
        {
            exploreRecheckTimer = exploreRecheckInterval;

            // 1. Visible switch
            if (TryAcquireVisibleTarget(currentNeedType))
                return;

            // 2. Remembered switch
            if (TryUseRememberedTarget(currentNeedType))
                return;

            // 3. Visible lit room
            if (currentNeedType == NeedType.Comfort && TryMoveToVisibleComfortZone())
                return;

            // 4. Remembered lit room
            if (currentNeedType == NeedType.Comfort && TryMoveToRememberedComfortZone())
                return;

            // 5. Remembered room with comfort potential
            if (currentNeedType == NeedType.Comfort && TryMoveToRememberedPotentialComfortZone())
                return;
        }

        // 6. Broad explore
        agent.speed = needMoveSpeed;
        exploreTimer += Time.deltaTime;

        if (!hasExplorePoint)
        {
            if (!TryPickExplorePoint())
            {
                exploreTimer = 0f;
                agent.ResetPath();
                return;
            }
        }

        agent.SetDestination(currentExplorePoint);

        if (!agent.pathPending && agent.remainingDistance <= exploreStopDistance)
        {
            RememberLocation(currentExplorePoint);
            hasExplorePoint = false;
            exploreTimer = 0f;
            agent.ResetPath();
            return;
        }

        if (exploreTimer >= exploreDuration)
        {
            RememberLocation(currentExplorePoint);
            hasExplorePoint = false;
            exploreTimer = 0f;
            agent.ResetPath();
        }
    }

    private bool TryPickExplorePoint()
    {
        for (int i = 0; i < 16; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * exploreRadius;
            randomOffset.y = 0f;

            Vector3 candidate = transform.position + randomOffset;

            if (IsRecentlyVisited(candidate))
                continue;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                continue;

            if (IsRecentlyVisited(navHit.position))
                continue;

            if (!IsPathReachable(navHit.position))
                continue;

            currentExplorePoint = navHit.position;
            hasExplorePoint = true;
            exploreTimer = 0f;
            return true;
        }

        return false;
    }

    private bool TryPickIdlePoint()
    {
        List<Vector3> candidatePoints = new List<Vector3>();

        bool currentRoomLit = currentRoom != null && currentRoom.IsLit();
        bool outsideLit = IsOutsideAreaLit();

        if (currentRoomLit)
        {
            TryAddRoomIdleCandidates(currentRoom, candidatePoints, 6);
        }

        List<RoomArea> visibleComfortRooms = GetVisibleComfortRooms();

        for (int i = 0; i < visibleComfortRooms.Count; i++)
        {
            RoomArea room = visibleComfortRooms[i];

            if (room == null || room == currentRoom)
                continue;

            TryAddRoomIdleCandidates(room, candidatePoints, 4);
        }

        if (outsideLit)
        {
            TryAddOutsideIdleCandidates(candidatePoints, 4);
        }

        if (candidatePoints.Count > 0)
        {
            Vector3 chosen = candidatePoints[Random.Range(0, candidatePoints.Count)];
            currentIdlePoint = chosen;
            hasIdlePoint = true;
            return true;
        }

        return TryPickLocalIdlePoint();
    }

    private void TryAddRoomIdleCandidates(RoomArea room, List<Vector3> candidates, int attempts)
    {
        if (room == null)
            return;

        for (int i = 0; i < attempts; i++)
        {
            if (!room.TryGetRandomPointInBounds(roomPointInset, out Vector3 candidate))
                continue;

            float distance = Vector3.Distance(transform.position, candidate);
            if (distance < minimumIdleMoveDistance)
                continue;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                continue;

            if (!room.ContainsPoint(navHit.position))
                continue;

            if (!IsPathReachable(navHit.position))
                continue;

            candidates.Add(navHit.position);
        }
    }

    private void TryAddOutsideIdleCandidates(List<Vector3> candidates, int attempts)
    {
        for (int i = 0; i < attempts; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * idleWanderRadius;
            randomOffset.y = 0f;
            Vector3 candidate = transform.position + randomOffset;

            float distance = Vector3.Distance(transform.position, candidate);
            if (distance < minimumIdleMoveDistance)
                continue;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                continue;

            if (currentRoom != null && currentRoom.ContainsPoint(navHit.position))
                continue;

            if (!IsPathReachable(navHit.position))
                continue;

            candidates.Add(navHit.position);
        }
    }

    private bool TryPickLocalIdlePoint()
    {
        for (int i = 0; i < 10; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * idleWanderRadius;
            randomOffset.y = 0f;
            Vector3 candidate = transform.position + randomOffset;

            float distance = Vector3.Distance(transform.position, candidate);
            if (distance < minimumIdleMoveDistance)
                continue;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                continue;

            if (!IsPathReachable(navHit.position))
                continue;

            currentIdlePoint = navHit.position;
            hasIdlePoint = true;
            return true;
        }

        return false;
    }

    private List<RoomArea> GetVisibleComfortRooms()
    {
        List<RoomArea> rooms = new List<RoomArea>();

        if (knownRooms == null || knownRooms.Length == 0)
        {
            knownRooms = FindObjectsByType<RoomArea>(FindObjectsSortMode.None);
        }

        for (int i = 0; i < knownRooms.Length; i++)
        {
            RoomArea room = knownRooms[i];

            if (room == null)
                continue;

            if (room == currentRoom)
                continue;

            if (!room.IsLit())
                continue;

            Vector3 roomPoint = room.GetRoomCenterPoint();

            if (!CanSeePoint(roomPoint))
                continue;

            if (!IsPathReachable(roomPoint))
                continue;

            rooms.Add(room);
        }

        return rooms;
    }

    private void PassiveObserveVisibleComfortZones()
    {
        if (knownRooms == null || knownRooms.Length == 0)
        {
            knownRooms = FindObjectsByType<RoomArea>(FindObjectsSortMode.None);
        }

        for (int i = 0; i < knownRooms.Length; i++)
        {
            RoomArea room = knownRooms[i];

            if (room == null)
                continue;

            Vector3 roomPoint = room.GetRoomCenterPoint();

            if (!CanSeePoint(roomPoint))
                continue;

            RememberComfortZone(room, roomPoint, room.IsLit());
        }
    }

    private void RememberComfortZone(RoomArea room, Vector3 position, bool wasLit)
    {
        if (room == null)
            return;

        for (int i = 0; i < rememberedComfortZones.Count; i++)
        {
            if (rememberedComfortZones[i] != null && rememberedComfortZones[i].room == room)
            {
                rememberedComfortZones[i].lastKnownPosition = position;
                rememberedComfortZones[i].lastSeenTime = Time.time;
                rememberedComfortZones[i].wasLitWhenLastSeen = wasLit;
                return;
            }
        }

        rememberedComfortZones.Add(new RememberedComfortZone(room, position, Time.time, wasLit));
    }

    private void CleanupComfortZoneMemory()
    {
        rememberedComfortZones.RemoveAll(z =>
            z == null ||
            z.room == null ||
            Time.time - z.lastSeenTime > comfortZoneMemoryDuration
        );
    }

    private bool TryMoveToVisibleComfortZone()
    {
        List<RoomArea> visibleRooms = GetVisibleComfortRooms();

        RoomArea bestRoom = null;
        float bestDistance = Mathf.Infinity;

        for (int i = 0; i < visibleRooms.Count; i++)
        {
            RoomArea room = visibleRooms[i];
            if (room == null)
                continue;

            Vector3 point = room.GetRoomCenterPoint();
            float distance = Vector3.Distance(transform.position, point);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestRoom = room;
            }
        }

        if (bestRoom == null)
            return false;

        Vector3 bestPoint = bestRoom.GetRoomCenterPoint();
        bool wasLit = bestRoom.IsLit();

        RememberComfortZone(bestRoom, bestPoint, wasLit);
        currentComfortZoneTarget = new RememberedComfortZone(bestRoom, bestPoint, Time.time, wasLit);

        currentMemoryTarget = null;
        currentTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();
        ChangeState(AIState.MoveToRememberedTarget);
        return true;
    }

    private bool TryMoveToRememberedComfortZone()
    {
        RememberedComfortZone bestZone = null;
        float bestDistance = Mathf.Infinity;

        for (int i = 0; i < rememberedComfortZones.Count; i++)
        {
            RememberedComfortZone zone = rememberedComfortZones[i];

            if (zone == null || zone.room == null)
                continue;

            bool currentlyLit = zone.room.IsLit();
            bool knownLit = currentlyLit || zone.wasLitWhenLastSeen;

            if (!knownLit)
                continue;

            if (!IsPathReachable(zone.lastKnownPosition))
                continue;

            float distance = Vector3.Distance(transform.position, zone.lastKnownPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestZone = zone;
            }
        }

        if (bestZone == null)
            return false;

        currentComfortZoneTarget = bestZone;
        currentMemoryTarget = null;
        currentTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();
        ChangeState(AIState.MoveToRememberedTarget);
        return true;
    }

    private bool TryMoveToRememberedPotentialComfortZone()
    {
        RememberedComfortZone bestZone = null;
        float bestDistance = Mathf.Infinity;

        for (int i = 0; i < rememberedComfortZones.Count; i++)
        {
            RememberedComfortZone zone = rememberedComfortZones[i];

            if (zone == null || zone.room == null)
                continue;

            if (!IsPathReachable(zone.lastKnownPosition))
                continue;

            float distance = Vector3.Distance(transform.position, zone.lastKnownPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestZone = zone;
            }
        }

        if (bestZone == null)
            return false;

        currentComfortZoneTarget = bestZone;
        currentMemoryTarget = null;
        currentTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();
        ChangeState(AIState.MoveToRememberedTarget);
        return true;
    }

    private bool IsOutsideAreaLit()
    {
        if (currentRoom != null && currentRoom.worldArea != null)
        {
            return currentRoom.worldArea.IsDaytime();
        }

        return worldArea != null && worldArea.IsDaytime();
    }

    private bool IsPathReachable(Vector3 destination)
    {
        if (!agent.isOnNavMesh)
            return false;

        NavMeshPath path = new NavMeshPath();

        if (!agent.CalculatePath(destination, path))
            return false;

        return path.status == NavMeshPathStatus.PathComplete;
    }

    private bool CanSeePoint(Vector3 targetPoint)
    {
        Vector3 origin = eyePoint != null ? eyePoint.position : transform.position + Vector3.up * 1.5f;
        Vector3 directionToTarget = targetPoint - origin;
        float distance = directionToTarget.magnitude;

        if (distance > visionRange)
            return false;

        float angle = Vector3.Angle(transform.forward, directionToTarget);
        if (angle > visionAngle * 0.5f)
            return false;

        if (Physics.Raycast(origin, directionToTarget.normalized, out RaycastHit hit, distance, obstacleLayer))
        {
            return false;
        }

        return true;
    }

    private void PassiveObserveVisibleInteractables()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, visionRange, interactableLayer);

        foreach (Collider hit in hits)
        {
            Interactable interactable = hit.GetComponentInParent<Interactable>();

            if (interactable == null || !interactable.isEnabled)
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

            if (interactable == null || !interactable.isEnabled)
                continue;

            if (!CanSeeInteractable(interactable))
                continue;

            INeedSatisfier satisfier = interactable as INeedSatisfier;
            if (satisfier == null || satisfier.GetNeedType() != needType)
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

        if (bestTarget == null)
            return false;

        currentTarget = bestTarget;
        currentMemoryTarget = null;
        currentComfortZoneTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();

        ChangeState(AIState.MoveToTarget);
        return true;
    }

    private bool TryUseRememberedTarget(NeedType needType)
    {
        ForgetInvalidMemories();

        RememberedInteractable bestMemory = null;
        float bestDistance = Mathf.Infinity;

        foreach (RememberedInteractable remembered in memory)
        {
            if (remembered == null || remembered.interactable == null)
                continue;

            if (remembered.needType != needType)
                continue;

            if (!remembered.interactable.isEnabled || !remembered.interactable.CanInteract(gameObject))
                continue;

            float distance = Vector3.Distance(transform.position, remembered.lastKnownPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestMemory = remembered;
            }
        }

        if (bestMemory == null)
            return false;

        currentMemoryTarget = bestMemory;
        currentTarget = bestMemory.interactable;
        currentComfortZoneTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();

        ChangeState(AIState.MoveToRememberedTarget);
        return true;
    }

    private void MoveToCurrentTarget()
    {
        if (!HasUrgentNeed())
        {
            AbortCurrentNeedAction();
            return;
        }

        if (IsNeedCurrentlySatisfied(currentNeedType))
        {
            AbortCurrentNeedAction();
            return;
        }

        if (currentTarget == null)
        {
            ChangeState(AIState.Explore);
            return;
        }

        agent.speed = needMoveSpeed;
        Vector3 targetPosition = currentTarget.GetInteractionPoint();
        agent.SetDestination(targetPosition);

        if (!agent.pathPending && agent.remainingDistance <= currentTarget.interactionRange)
        {
            ChangeState(AIState.InteractWithTarget);
        }
    }

    private void MoveToRememberedTarget()
    {
        if (!HasUrgentNeed())
        {
            AbortCurrentNeedAction();
            return;
        }

        if (IsNeedCurrentlySatisfied(currentNeedType))
        {
            AbortCurrentNeedAction();
            return;
        }

        // Remembered comfort room / potential comfort room
        if (currentComfortZoneTarget != null)
        {
            if (currentComfortZoneTarget.room == null)
            {
                currentComfortZoneTarget = null;
                ChangeState(AIState.Explore);
                return;
            }

            agent.speed = needMoveSpeed;
            agent.SetDestination(currentComfortZoneTarget.lastKnownPosition);

            if (!agent.pathPending && agent.remainingDistance <= rememberedComfortZoneStopDistance)
            {
                if (IsNeedCurrentlySatisfied(NeedType.Comfort))
                {
                    AbortCurrentNeedAction();
                }
                else
                {
                    currentComfortZoneTarget = null;
                    ChangeState(AIState.Explore);
                }
            }

            return;
        }

        // Remembered interactable
        if (currentMemoryTarget == null || currentMemoryTarget.interactable == null)
        {
            currentMemoryTarget = null;
            currentTarget = null;
            ChangeState(AIState.Explore);
            return;
        }

        if (TryAcquireVisibleTarget(currentNeedType))
            return;

        agent.speed = needMoveSpeed;
        agent.SetDestination(currentMemoryTarget.lastKnownPosition);

        if (!agent.pathPending && agent.remainingDistance <= rememberedTargetStopDistance)
        {
            if (currentTarget != null && currentTarget.CanInteract(gameObject) && CanSeeInteractable(currentTarget))
            {
                ChangeState(AIState.MoveToTarget);
                return;
            }

            memory.Remove(currentMemoryTarget);
            currentMemoryTarget = null;
            currentTarget = null;
            ChangeState(AIState.Explore);
        }
    }

    private void InteractWithCurrentTarget()
    {
        if (!HasUrgentNeed())
        {
            AbortCurrentNeedAction();
            return;
        }

        if (currentTarget == null)
        {
            ChangeState(AIState.Explore);
            return;
        }

        agent.ResetPath();

        if (currentTarget.CanInteract(gameObject))
        {
            currentTarget.Interact(gameObject);
        }

        currentTarget = null;
        currentMemoryTarget = null;
        currentComfortZoneTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;

        if (IsNeedCurrentlySatisfied(currentNeedType))
        {
            ChangeState(AIState.IdleWander);
        }
        else if (HasUrgentNeed())
        {
            ChangeState(AIState.Explore);
        }
        else
        {
            ChangeState(AIState.IdleWander);
        }
    }

    private void AbortCurrentNeedAction()
    {
        currentTarget = null;
        currentMemoryTarget = null;
        currentComfortZoneTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();
        ChangeState(AIState.IdleWander);
    }

    private void RestartNeedSearch()
    {
        currentTarget = null;
        currentMemoryTarget = null;
        currentComfortZoneTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();
        ChangeState(AIState.Explore);
    }

    private void ChangeState(AIState newState)
    {
        if (currentState == newState)
            return;

        currentState = newState;
        Debug.Log("State changed to: " + currentState);

        switch (currentState)
        {
            case AIState.IdleWander:
                hasIdlePoint = false;
                idlePauseTimer = idlePauseDuration;
                break;

            case AIState.Explore:
                hasExplorePoint = false;
                exploreTimer = 0f;
                exploreRecheckTimer = 0f;
                break;
        }
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

        if (room == null)
            return;

        if (!overlappingRooms.Contains(room))
        {
            overlappingRooms.Add(room);
        }

        ResolveCurrentRoom();
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

        if (currentComfortZoneTarget != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(currentComfortZoneTarget.lastKnownPosition, 0.25f);
            Gizmos.DrawLine(transform.position, currentComfortZoneTarget.lastKnownPosition);
        }
    }
}
