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

    [Header("Movement Stall Detection")]
    public float stallVelocityThreshold = 0.1f;
    public float stallTimeThreshold = 0.75f;
    private float stallTimer = 0f;

    [Header("Door Probe")]
    public float doorCheckDistance = 1.75f;
    public LayerMask doorLayer;
    public float doorInteractCooldown = 0.5f;
    public float doorStopDistance = 1.15f;
    public int maxExploreDoorRouteAttempts = 2;

    [Header("Opportunistic Needs")]
    public float opportunisticTargetMaxDistance = 2.5f;
    public float opportunisticComfortLightCooldown = 6f;
    [Header("Opportunistic Check")]
    public float opportunisticCheckInterval = 0.5f;
    private float opportunisticCheckTimer = 0f;
    private const float KeyPickupPriorityMultiplier = 3f;
    private const float MatchingLockedDoorKeyPriorityMultiplier = 3f;
    private const float MatchingLockedDoorNarrationCooldown = 4f;

    [Header("Idle Wander")]
    public float idleWanderRadius = 3f;
    public float idleWanderStopDistance = 0.5f;
    public float idlePauseDuration = 2f;
    public float minimumIdleMoveDistance = 0.5f;
    public float roomPointInset = 0.2f;

    [Header("Idle Door Checking")]
    public float idleDoorSearchRadius = 2f;
    public float idleDoorSearchCooldown = 1f;
    private float lastIdleDoorSearchTime = -999f;

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

    [Header("Memory - Locked Doors")]
    public List<RememberedLockedDoor> rememberedLockedDoors = new List<RememberedLockedDoor>();
    public float lockedDoorMemoryDuration = 90f;

    [Header("Current Target")]
    public Interactable currentTarget;

    [Header("Inventory")]
    public NPCInventory npcInventory;

    [Header("Thought Logging")]
    [SerializeField] private NPCThoughtLogger thoughtLogger;

    [Header("Debug")]
    [SerializeField] private bool debugOpportunisticFlow = true;

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
    private bool currentNeedActionIsUrgentDriven = false;
    private float lastDoorInteractTime = -999f;
    private DoorInteractable pendingDoorTarget;
    private Vector3 pendingDoorDestination;
    private bool hasPendingDoorDestination = false;
    private int currentExploreDoorRouteAttempts = 0;

    private readonly Dictionary<NeedType, NeedsManager.NeedUrgencyBand> lastNeedBands = new Dictionary<NeedType, NeedsManager.NeedUrgencyBand>();
    private readonly Dictionary<NeedType, bool> lastNeedUrgentFlags = new Dictionary<NeedType, bool>();
    private float lastOpportunisticComfortLightTime = -999f;
    private float lastMatchingLockedDoorKeyNarrationTime = -999f;

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

        if (npcInventory == null)
        {
            npcInventory = GetComponent<NPCInventory>();
        }

        knownRooms = FindObjectsByType<RoomArea>(FindObjectsSortMode.None);
        EnsureThoughtLogger();
        CacheInitialNeedState();

        currentState = AIState.IdleWander;
        hasIdlePoint = false;
        idlePauseTimer = idlePauseDuration;
        opportunisticCheckTimer = Random.Range(0f, opportunisticCheckInterval);
        
        Narrate("What a beautiful day! Let's go for a wander 😊.", "state-idle-start");
    }

    private void Update()
    {
        needsManager.TickNeeds(IsCurrentAreaLit(), Time.deltaTime);
        ObserveNeedShifts();

        CleanupLocationMemory();
        CleanupInteractableMemory();
        CleanupComfortZoneMemory();
        CleanupLockedDoorMemory();

        PassiveObserveVisibleInteractables();
        PassiveObserveVisibleComfortZones();

        if (needsManager.HasUrgentNeed(out NeedType mostUrgentNeed))
        {
            bool needChanged = mostUrgentNeed != currentNeedType;
            currentNeedType = mostUrgentNeed;
            currentNeedActionIsUrgentDriven = true;

            if (needChanged && IsGoalExecutionState(currentState))
            {
                Narrate(Pick(
                    "Something else is more urgent now. I need to switch priorities.",
                    "Hold on... another need just got worse. Changing course."
                ), "need-switch-priority");
                RestartNeedSearch();
                return;
            }

            if (currentNeedActionIsUrgentDriven && IsNeedCurrentlySatisfied(currentNeedType))
            {
                if (IsGoalExecutionState(currentState))
                {
                    Narrate(Pick(
                        "Okay, that's better.",
                        "Much better. I can ease off now."
                    ), "need-satisfied-in-action");
                    AbortCurrentNeedAction();
                    return;
                }
            }
            else if (currentState == AIState.IdleWander)
            {
                ChangeState(AIState.Explore);
            }
        }
        else if (currentNeedActionIsUrgentDriven && IsGoalExecutionState(currentState))
        {
            Narrate("I feel okay again. Back to wandering.", "return-to-idle-no-urgent");
            AbortCurrentNeedAction();
            return;
        }
        else if (currentState == AIState.IdleWander)
        {
            opportunisticCheckTimer -= Time.deltaTime;

            if (opportunisticCheckTimer <= 0f)
            {
                opportunisticCheckTimer = opportunisticCheckInterval;

                if (TryHandleOpportunisticNeed())
                    return;
            }
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

    private bool IsGoalExecutionState(AIState state)
    {
        return state == AIState.Explore ||
               state == AIState.MoveToTarget ||
               state == AIState.MoveToRememberedTarget ||
               state == AIState.InteractWithTarget;
    }

    private bool IsNeedDrivenState(AIState state)
    {
        return IsGoalExecutionState(state);
    }

    private float GetActionMoveSpeed()
    {
        return currentNeedActionIsUrgentDriven ? GetNeedMoveSpeed() : idleMoveSpeed;
    }

    private void HandleIdleWander()
    {
        ResetStallTimer();
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
                Narrate(Pick(
                    "I'll stay put for a moment.",
                    "Nothing better nearby. I'll wait here."
                ), "idle-no-point");

                idlePauseTimer = idlePauseDuration;

                if (agent.hasPath)
                    agent.ResetPath();

                return;
            }

            Narrate(Pick(
                "Just wandering...",
                "Everything seems fine. I'll move around a bit.",
                "I'll stay nearby for now."
            ), "idle-picked-point");
        }

        agent.SetDestination(currentIdlePoint);

        if (!agent.pathPending && agent.remainingDistance <= idleWanderStopDistance)
        {
            if (agent.hasPath)
                agent.ResetPath();

            hasIdlePoint = false;

            if (TryHandleNearbyIdleDoor())
            {
                idlePauseTimer = 0.25f;
                return;
            }

            idlePauseTimer = idlePauseDuration;
        }
    }

    private void ExploreForTarget()
    {
        if (HandlePendingDoorTarget())
            return;

        if (currentNeedActionIsUrgentDriven && IsNeedCurrentlySatisfied(currentNeedType))
        {
            Narrate("Okay, that's better.", "explore-need-satisfied");
            AbortCurrentNeedAction();
            return;
        }

        if (currentNeedType == NeedType.Hunger && TryUseInventoryItemForNeed(NeedType.Hunger))
        {
            Narrate("I have something for this. I'll use it.", "inventory-use-hunger");
            AbortCurrentNeedAction();
            return;
        }

        exploreRecheckTimer -= Time.deltaTime;
        bool shouldRecheckKnownTargets = !hasExplorePoint || exploreRecheckTimer <= 0f;

        if (shouldRecheckKnownTargets)
        {
            exploreRecheckTimer = exploreRecheckInterval;

            if (TryAcquireVisibleTarget(currentNeedType))
                return;

            if (TryUseRememberedTarget(currentNeedType))
                return;

            if (currentNeedType == NeedType.Comfort && TryMoveToVisibleComfortZone())
                return;

            if (currentNeedType == NeedType.Comfort && TryMoveToRememberedComfortZone())
                return;

            if (currentNeedType == NeedType.Comfort && TryMoveToRememberedPotentialComfortZone())
                return;
        }

        agent.speed = GetActionMoveSpeed();
        exploreTimer += Time.deltaTime;

        if (!hasExplorePoint)
        {
            Narrate(Pick(
                "I need to find something...",
                "I'll look around.",
                "Nothing obvious yet. I'll keep searching."
            ), "explore-start-sweep");

            if (!TryPickExplorePoint())
            {
                Narrate(Pick(
                    "Nothing useful here...",
                    "I can't find a good route."
                ), "explore-failed-point");

                exploreTimer = 0f;
                agent.ResetPath();
                return;
            }

            Narrate("I'll check over there.", "explore-new-point");
        }

        if (!IsPathReachable(currentExplorePoint))
        {
            if (TryHandleDoorForDestination(currentExplorePoint))
            {
                currentExploreDoorRouteAttempts++;
                return;
            }

            currentExploreDoorRouteAttempts++;
            if (currentExploreDoorRouteAttempts >= Mathf.Max(1, maxExploreDoorRouteAttempts))
            {
                RememberLocation(currentExplorePoint);
                Narrate("I can't find a usable door for that route. I'll search elsewhere.", "explore-door-route-failed");
                hasExplorePoint = false;
                exploreTimer = 0f;
                currentExploreDoorRouteAttempts = 0;
                agent.ResetPath();
            }

            return;
        }

        agent.SetDestination(currentExplorePoint);
        currentExploreDoorRouteAttempts = 0;

        if (IsStalled())
        {
            RememberLocation(currentExplorePoint);
            Narrate("I can't move through here. I'll try another route.", "explore-stalled-fallback");
            hasExplorePoint = false;
            exploreTimer = 0f;
            agent.ResetPath();
            return;
        }

        if (!agent.pathPending && agent.remainingDistance <= exploreStopDistance)
        {
            RememberLocation(currentExplorePoint);
            Narrate("Nothing useful here... moving on.", "explore-point-finished");
            hasExplorePoint = false;
            exploreTimer = 0f;
            agent.ResetPath();
            return;
        }

        if (exploreTimer >= exploreDuration)
        {
            RememberLocation(currentExplorePoint);
            Narrate("I've searched here long enough. Moving on.", "explore-point-abandon");
            hasExplorePoint = false;
            exploreTimer = 0f;
            agent.ResetPath();
        }
    }

    private bool TryPickExplorePoint()
    {
        bool allowDoorBlockedExplore = currentNeedActionIsUrgentDriven && HasUrgentNeed();

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

            if (IsPathReachable(navHit.position))
            {
                currentExplorePoint = navHit.position;
                hasExplorePoint = true;
                exploreTimer = 0f;
                currentExploreDoorRouteAttempts = 0;
                return true;
            }

            if (!allowDoorBlockedExplore)
                continue;

            if (!IsExplorePointBlockedByDoor(navHit.position, out DoorInteractable blockingDoor))
                continue;

            currentExplorePoint = navHit.position;
            hasExplorePoint = true;
            exploreTimer = 0f;
            currentExploreDoorRouteAttempts = 0;
            QueueDoorHandling(blockingDoor, navHit.position);
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
                bool litChanged = rememberedComfortZones[i].wasLitWhenLastSeen != wasLit;
                rememberedComfortZones[i].lastKnownPosition = position;
                rememberedComfortZones[i].lastSeenTime = Time.time;
                rememberedComfortZones[i].wasLitWhenLastSeen = wasLit;

                if (litChanged)
                {
                    Narrate(wasLit
                        ? "That room looks lit now."
                        : "That room just got darker.",
                        "memory-comfort-update-" + room.GetInstanceID());
                }

                return;
            }
        }

        rememberedComfortZones.Add(new RememberedComfortZone(room, position, Time.time, wasLit));

        if (wasLit)
        {
            Narrate("That room looks lit. I'll remember it.", "memory-comfort-new-" + room.GetInstanceID());
        }
    }

    private void CleanupComfortZoneMemory()
    {
        rememberedComfortZones.RemoveAll(z =>
            z == null ||
            z.room == null ||
            Time.time - z.lastSeenTime > comfortZoneMemoryDuration
        );
    }

    private bool TryMoveToVisibleComfortZone(float maxDistance = Mathf.Infinity)
    {
        if (!TryFindBestVisibleComfortRoom(maxDistance, out RoomArea bestRoom, out _))
            return false;

        Vector3 bestPoint = bestRoom.GetRoomCenterPoint();
        bool wasLit = bestRoom.IsLit();

        if (!IsPathReachable(bestPoint) && !TryHandleDoorForDestination(bestPoint))
            return false;

        Narrate("There's a lit area over there. That should help.", "perception-visible-comfort-target");

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

            float distance = Vector3.Distance(transform.position, zone.lastKnownPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestZone = zone;
            }
        }

        if (bestZone == null)
            return false;

        if (!IsPathReachable(bestZone.lastKnownPosition) && !TryHandleDoorForDestination(bestZone.lastKnownPosition))
            return false;

        Narrate("I remember a lit place. I'll head there.", "recall-comfort-known-lit");

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

            float distance = Vector3.Distance(transform.position, zone.lastKnownPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestZone = zone;
            }
        }

        if (bestZone == null)
            return false;

        if (!IsPathReachable(bestZone.lastKnownPosition) && !TryHandleDoorForDestination(bestZone.lastKnownPosition))
            return false;

        Narrate("I've been near a place that might help. I'll try it again.", "recall-comfort-potential");

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

    private bool TryAcquireVisibleTarget(NeedType needType, float maxDistance = Mathf.Infinity)
    {
        if (!TryFindBestVisibleTarget(needType, maxDistance, out Interactable bestTarget, out _))
            return false;

        Narrate("I see something that could help.", "perception-visible-interactable");
        RememberInteractable(bestTarget, needType);

        Narrate(Pick(
            "That's exactly what I need.",
            "Perfect. I'll go there.",
            "That should help."
        ), "target-acquired-visible");

        currentTarget = bestTarget;
        currentMemoryTarget = null;
        currentComfortZoneTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();

        Vector3 targetPosition = currentTarget.GetInteractionPoint();
        if (!IsPathReachable(targetPosition) && !TryHandleDoorForDestination(targetPosition))
            return false;

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

        Narrate(Pick(
            "I remember something that could help.",
            "I've seen this before. Let me try that.",
            "Let me try that place again."
        ), "recall-interactable-target");

        currentMemoryTarget = bestMemory;
        currentTarget = bestMemory.interactable;
        currentComfortZoneTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();

        if (!IsPathReachable(bestMemory.lastKnownPosition) && !TryHandleDoorForDestination(bestMemory.lastKnownPosition))
            return false;

        ChangeState(AIState.MoveToRememberedTarget);
        return true;
    }

    private void MoveToCurrentTarget()
    {
        if (HandlePendingDoorTarget())
            return;

        if (currentNeedActionIsUrgentDriven && !HasUrgentNeed())
        {
            Narrate("I don't need this anymore.", "move-target-no-urgent");
            AbortCurrentNeedAction();
            return;
        }

        if (currentNeedActionIsUrgentDriven && IsNeedCurrentlySatisfied(currentNeedType))
        {
            Narrate("Already feeling better. I'll stop here.", "move-target-need-satisfied");
            AbortCurrentNeedAction();
            return;
        }

        if (currentTarget == null)
        {
            Narrate("I lost the target. I'll search again.", "move-target-lost");
            HandleNeedActionFailure();
            return;
        }

        agent.speed = GetActionMoveSpeed();
        Vector3 targetPosition = currentTarget.GetInteractionPoint();
        agent.SetDestination(targetPosition);

        if (IsStalled())
        {
            if (TryHandleDoorForDestination(targetPosition))
                return;

            Narrate("Something is blocking this route. I'll search for another path.", "move-target-stalled-fallback");
            HandleNeedActionFailure();
            return;
        }

        if (!agent.pathPending && agent.remainingDistance <= currentTarget.interactionRange)
        {
            ChangeState(AIState.InteractWithTarget);
        }
    }

    private void MoveToRememberedTarget()
    {
        if (HandlePendingDoorTarget())
            return;

        if (currentNeedActionIsUrgentDriven && !HasUrgentNeed())
        {
            Narrate("I'm okay now. No need to keep chasing this.", "move-remembered-no-urgent");
            AbortCurrentNeedAction();
            return;
        }

        if (currentNeedActionIsUrgentDriven && IsNeedCurrentlySatisfied(currentNeedType))
        {
            Narrate("That did the trick. I'll calm down.", "move-remembered-need-satisfied");
            AbortCurrentNeedAction();
            return;
        }

        if (currentComfortZoneTarget != null)
        {
            if (currentComfortZoneTarget.room == null)
            {
                Narrate("That place is gone... I'll keep searching.", "move-remembered-comfort-missing-room");
                currentComfortZoneTarget = null;
                HandleNeedActionFailure();
                return;
            }

            agent.speed = GetActionMoveSpeed();
            agent.SetDestination(currentComfortZoneTarget.lastKnownPosition);

            if (IsStalled())
            {
                if (TryHandleDoorForDestination(currentComfortZoneTarget.lastKnownPosition))
                    return;

                Narrate("I can't reach that lit area from here. I'll find another option.", "move-remembered-comfort-stalled-fallback");
                currentComfortZoneTarget = null;
                HandleNeedActionFailure();
                return;
            }

            if (!agent.pathPending && agent.remainingDistance <= rememberedComfortZoneStopDistance)
            {
                if (IsNeedCurrentlySatisfied(NeedType.Comfort))
                {
                    Narrate("Much better.", "move-remembered-comfort-solved");
                    AbortCurrentNeedAction();
                }
                else
                {
                    Narrate("Still not enough comfort here. I need another option.", "move-remembered-comfort-fallback");
                    currentComfortZoneTarget = null;
                    HandleNeedActionFailure();
                }
            }

            return;
        }

        if (currentMemoryTarget == null || currentMemoryTarget.interactable == null)
        {
            Narrate("That lead went cold. I'll look for something else.", "move-remembered-invalid");
            currentMemoryTarget = null;
            currentTarget = null;
            HandleNeedActionFailure();
            return;
        }

        if (TryAcquireVisibleTarget(currentNeedType))
            return;

        agent.speed = GetActionMoveSpeed();
        agent.SetDestination(currentMemoryTarget.lastKnownPosition);

        if (IsStalled())
        {
            if (TryHandleDoorForDestination(currentMemoryTarget.lastKnownPosition))
                return;

            Narrate("This route is blocked. I'll try a different lead.", "move-remembered-target-stalled-fallback");
            currentMemoryTarget = null;
            currentTarget = null;
            HandleNeedActionFailure();
            return;
        }

        if (!agent.pathPending && agent.remainingDistance <= rememberedTargetStopDistance)
        {
            if (currentTarget != null && currentTarget.CanInteract(gameObject) && CanSeeInteractable(currentTarget))
            {
                Narrate("There it is.", "move-remembered-found-target");
                ChangeState(AIState.MoveToTarget);
                return;
            }

            Narrate("Nothing here... I need to try something else.", "move-remembered-not-found");
            memory.Remove(currentMemoryTarget);
            currentMemoryTarget = null;
            currentTarget = null;
            HandleNeedActionFailure();
        }
    }

    private void InteractWithCurrentTarget()
    {
        ResetStallTimer();

        if (currentNeedActionIsUrgentDriven && !HasUrgentNeed())
        {
            Narrate("Never mind, I don't need this right now.", "interact-no-urgent");
            AbortCurrentNeedAction();
            return;
        }

        if (currentTarget == null)
        {
            Narrate("No target to use... I'll search again.", "interact-missing-target");
            HandleNeedActionFailure();
            return;
        }

        agent.ResetPath();

        if (currentTarget.CanInteract(gameObject))
        {
            bool handledAsPickup = false;

            IPickupable pickupable = currentTarget as IPickupable;
            bool isUrgentlyHungry = currentNeedActionIsUrgentDriven && currentNeedType == NeedType.Hunger;

            if (pickupable != null && npcInventory != null && !isUrgentlyHungry)
            {
                handledAsPickup = TryPickUpCurrentTarget(pickupable);
            }

            if (!handledAsPickup)
            {
                Narrate(Pick(
                    "This should fix things.",
                    "Let's see...",
                    "Alright, this might help."
                ), "interact-attempt");

                currentTarget.Interact(gameObject);
                Narrate("That worked.", "interact-complete");
            }
        }
        else
        {
            Narrate("That didn't work...", "interact-failed");
        }

        currentTarget = null;
        currentMemoryTarget = null;
        currentComfortZoneTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;

        if (currentNeedActionIsUrgentDriven && IsNeedCurrentlySatisfied(currentNeedType))
        {
            Narrate(Pick(
                "Much better.",
                "That solved it.",
                "I feel okay again."
            ), "need-satisfied-after-interact");
            ChangeState(AIState.IdleWander);
        }
        else if (HasUrgentNeed())
        {
            Narrate("Still need more. Keep looking.", "interact-still-urgent");
            ChangeState(AIState.Explore);
        }
        else
        {
            ChangeState(AIState.IdleWander);
        }
    }

    private bool TryPickUpCurrentTarget(IPickupable pickupable)
    {
        if (!pickupable.CanPickUp(gameObject))
        {
            DebugPickup("Pickup rejected by CanPickUp().");
            return false;
        }

        Interactable item = pickupable as Interactable;
        if (item == null)
        {
            DebugPickup("Pickup target is not an Interactable.");
            return false;
        }

        if (!npcInventory.IsFull)
        {
            if (npcInventory.TryAddItem(item))
            {
                Narrate("I'll take that for later.", "pickup-store");
                DebugPickup("Pickup succeeded and stored for later: " + item.name);
                return true;
            }

            DebugPickup("Inventory had space but TryAddItem() failed for: " + item.name);
            return false;
        }

        Interactable leastValuable = npcInventory.GetLeastValuableItem();
        if (leastValuable == null)
        {
            DebugPickup("Inventory reported full but no least valuable item was found.");
            return false;
        }

        IPickupable leastPickupable = leastValuable as IPickupable;
        if (leastPickupable == null)
        {
            DebugPickup("Least valuable carried item is not IPickupable.");
            return false;
        }

        if (pickupable.GetItemValue() > leastPickupable.GetItemValue())
        {
            Narrate("This is better than what I'm carrying. I'll make room.", "pickup-swap");
            npcInventory.DropItem(leastValuable);

            if (!npcInventory.TryAddItem(item))
            {
                Debug.LogWarning(gameObject.name + ": dropped inventory item but failed to pick up replacement.");
                DebugPickup("Swap pickup failed after dropping an item.");
                return false;
            }

            DebugPickup("Swapped inventory item and picked up: " + item.name);
            return true;
        }

        DebugPickup("Pickup declined because carried items were equal or better value.");
        return false;
    }

    private bool TryUseInventoryItemForNeed(NeedType needType)
    {
        if (npcInventory == null || !npcInventory.HasItemForNeed(needType))
            return false;

        Interactable item = npcInventory.GetBestItemForNeed(needType);
        if (item == null)
            return false;

        if (!item.CanInteract(gameObject))
            return false;

        item.Interact(gameObject);
        return true;
    }

    private void AbortCurrentNeedAction()
    {
        DebugAbort("AbortCurrentNeedAction");

        ResetStallTimer();
        currentTarget = null;
        currentMemoryTarget = null;
        currentComfortZoneTarget = null;
        currentNeedActionIsUrgentDriven = false;
        pendingDoorTarget = null;
        hasPendingDoorDestination = false;
        currentExploreDoorRouteAttempts = 0;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();
        ChangeState(AIState.IdleWander);
    }

    private void RestartNeedSearch()
    {
        ResetStallTimer();
        currentTarget = null;
        currentMemoryTarget = null;
        currentComfortZoneTarget = null;
        currentNeedActionIsUrgentDriven = true;
        pendingDoorTarget = null;
        hasPendingDoorDestination = false;
        currentExploreDoorRouteAttempts = 0;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();
        ChangeState(AIState.Explore);
    }

    private float GetNeedMoveSpeed()
    {
        if (needsManager == null)
            return needMoveSpeed;

        return needMoveSpeed * needsManager.GetNeedMoveSpeedMultiplier(currentNeedType);
    }

    private void HandleNeedActionFailure()
    {
        pendingDoorTarget = null;
        hasPendingDoorDestination = false;
        currentExploreDoorRouteAttempts = 0;

        if (currentNeedActionIsUrgentDriven || HasUrgentNeed())
        {
            ChangeState(AIState.Explore);
            return;
        }

        AbortCurrentNeedAction();
    }

    private void ChangeState(AIState newState)
    {
        if (currentState == newState)
            return;

        AIState previousState = currentState;
        currentState = newState;
        ResetStallTimer();

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

        Narrate(GetStateTransitionThought(previousState, currentState), "state-change-" + previousState + "-" + currentState);
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
                Narrate("I've seen this before.", "memory-update-interactable-" + interactable.GetInstanceID());
                return;
            }
        }

        memory.Add(new RememberedInteractable(
            interactable,
            needType,
            interactable.GetInteractionPoint(),
            Time.time
        ));

        Narrate("I'll remember this for later.", "memory-new-interactable-" + interactable.GetInstanceID());
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

    private void CleanupLockedDoorMemory()
    {
        rememberedLockedDoors.RemoveAll(lockedDoorMemory =>
        {
            if (lockedDoorMemory == null || lockedDoorMemory.door == null)
                return true;

            if (Time.time - lockedDoorMemory.lastSeenTime > lockedDoorMemoryDuration)
                return true;

            DoorController controller = lockedDoorMemory.door.GetDoorController();
            if (controller == null || !controller.IsLocked)
                return true;

            if (string.IsNullOrWhiteSpace(lockedDoorMemory.requiredKeyId))
                return true;

            return false;
        });
    }

    private void RememberLockedDoor(DoorInteractable door)
    {
        if (door == null)
            return;

        DoorController controller = door.GetDoorController();
        if (controller == null || !controller.IsLocked)
            return;

        string requiredKeyId = controller.RequiredKeyId;
        if (string.IsNullOrWhiteSpace(requiredKeyId))
            return;

        for (int i = 0; i < rememberedLockedDoors.Count; i++)
        {
            RememberedLockedDoor remembered = rememberedLockedDoors[i];
            if (remembered == null || remembered.door != door)
                continue;

            remembered.lastKnownPosition = door.GetInteractionPoint();
            remembered.requiredKeyId = requiredKeyId;
            remembered.lastSeenTime = Time.time;
            return;
        }

        rememberedLockedDoors.Add(new RememberedLockedDoor(
            door,
            door.GetInteractionPoint(),
            requiredKeyId,
            Time.time
        ));

        Narrate("I can't open that. I'll remember it.", "memory-locked-door-" + door.GetInstanceID());
    }

    private bool IsKeyUsefulForRememberedLockedDoor(IKeyItem keyItem)
    {
        if (keyItem == null)
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

    private bool TryHandleNearbyIdleDoor()
    {
        if (Time.time < lastIdleDoorSearchTime + idleDoorSearchCooldown)
            return false;

        lastIdleDoorSearchTime = Time.time;

        int mask = doorLayer.value == 0 ? interactableLayer : doorLayer;
        Collider[] hits = Physics.OverlapSphere(transform.position, idleDoorSearchRadius, mask, QueryTriggerInteraction.Collide);

        DoorInteractable bestDoor = null;
        float bestDistance = Mathf.Infinity;

        for (int i = 0; i < hits.Length; i++)
        {
            DoorInteractable door = hits[i].GetComponentInParent<DoorInteractable>();
            if (door == null)
                continue;

            if (!door.isEnabled)
                continue;

            if (!door.CanInteract(gameObject))
                continue;

            float distance = Vector3.Distance(transform.position, door.GetInteractionPoint());
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestDoor = door;
        }

        if (bestDoor == null)
            return false;

        DoorController controller = bestDoor.GetDoorController();
        if (controller == null)
            return false;

        if (controller.IsOpen)
            return false;

        if (TryUnlockAndOpenDoorFromInventory(bestDoor))
        {
            Narrate("I can open this door. That gives me more room to wander.", "idle-door-opened");
            return true;
        }

        return false;
    }

    private bool TryUnlockAndOpenDoorFromInventory(DoorInteractable door)
    {
        if (door == null)
            return false;

        DoorController controller = door.GetDoorController();
        if (controller == null)
            return false;

        if (controller.IsOpen)
            return false;

        door.Interact(gameObject);

        if (controller.IsOpen)
            return true;

        if (npcInventory == null)
        {
            if (controller.IsLocked)
                RememberLockedDoor(door);
            return false;
        }

        if (TryUseInventoryKeyOnDoor(door))
        {
            door.Interact(gameObject);
            return controller.IsOpen;
        }

        if (controller.IsLocked)
            RememberLockedDoor(door);

        return false;
    }

    private bool TryUseInventoryKeyOnDoor(DoorInteractable door)
    {
        if (door == null || npcInventory == null)
            return false;

        DoorController controller = door.GetDoorController();
        if (controller == null || !controller.IsLocked)
            return false;

        if (!npcInventory.TryGetMatchingKey(controller.RequiredKeyId, out IKeyItem key))
            return false;

        bool unlocked = controller.TryUnlock(key.GetKeyId());
        if (unlocked)
        {
            Narrate("Good thing I kept this key. That lock is open now.", "door-unlock-success");
        }

        return unlocked;
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

        Gizmos.color = new Color(1f, 0.78f, 0.94f);
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
            Gizmos.color = new Color(0f, 1f, 0.99f);
            Gizmos.DrawSphere(currentComfortZoneTarget.lastKnownPosition, 0.25f);
            Gizmos.DrawLine(transform.position, currentComfortZoneTarget.lastKnownPosition);
        }

        Gizmos.color = new Color(0.02f, 0.22f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, idleDoorSearchRadius);
        
        Gizmos.color = new Color(1f, 0.78f, 0f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, opportunisticTargetMaxDistance);
    }

    private void EnsureThoughtLogger()
    {
        if (thoughtLogger != null)
            return;

        thoughtLogger = GetComponent<NPCThoughtLogger>();

        if (thoughtLogger == null)
        {
            thoughtLogger = gameObject.AddComponent<NPCThoughtLogger>();
        }
    }

    private void CacheInitialNeedState()
    {
        if (needsManager == null || needsManager.needs == null)
            return;

        for (int i = 0; i < needsManager.needs.Count; i++)
        {
            NeedsManager.NeedState need = needsManager.needs[i];
            if (need == null)
                continue;

            lastNeedBands[need.needType] = needsManager.GetNeedUrgencyBand(need.needType);
            lastNeedUrgentFlags[need.needType] = needsManager.IsNeedUrgent(need.needType);
        }
    }

    private void ObserveNeedShifts()
    {
        if (needsManager == null || needsManager.needs == null)
            return;

        for (int i = 0; i < needsManager.needs.Count; i++)
        {
            NeedsManager.NeedState need = needsManager.needs[i];
            if (need == null)
                continue;

            NeedType needType = need.needType;
            NeedsManager.NeedUrgencyBand currentBand = needsManager.GetNeedUrgencyBand(needType);
            bool isUrgent = needsManager.IsNeedUrgent(needType);

            if (!lastNeedBands.TryGetValue(needType, out NeedsManager.NeedUrgencyBand previousBand))
            {
                lastNeedBands[needType] = currentBand;
                lastNeedUrgentFlags[needType] = isUrgent;
                continue;
            }

            bool hadUrgentFlag = lastNeedUrgentFlags.TryGetValue(needType, out bool wasUrgent) && wasUrgent;

            if (currentBand != previousBand || isUrgent != hadUrgentFlag)
            {
                Narrate(GetNeedShiftThought(needType, previousBand, currentBand, isUrgent), "need-band-" + needType + "-" + previousBand + "-" + currentBand);
            }

            lastNeedBands[needType] = currentBand;
            lastNeedUrgentFlags[needType] = isUrgent;
        }
    }

    private string GetNeedShiftThought(NeedType needType, NeedsManager.NeedUrgencyBand previousBand, NeedsManager.NeedUrgencyBand currentBand, bool isUrgent)
    {
        if (isUrgent)
        {
            switch (needType)
            {
                case NeedType.Comfort:
                    if (currentBand == NeedsManager.NeedUrgencyBand.Critical)
                        return "This is bad. I need light now.";
                    return "It's getting dark... I don't like this.";

                case NeedType.Hunger:
                    if (currentBand == NeedsManager.NeedUrgencyBand.Critical)
                        return "This is bad. I need food now.";
                    return "I'm starting to get hungry.";

                default:
                    return "I need to deal with this soon.";
            }
        }

        if (previousBand == NeedsManager.NeedUrgencyBand.Critical || previousBand == NeedsManager.NeedUrgencyBand.Urgent)
            return "Okay, that's better.";

        return "Still doing fine.";
    }

    private string GetStateTransitionThought(AIState previousState, AIState newState)
    {
        switch (newState)
        {
            case AIState.IdleWander:
                return Pick(
                    "I'm feeling fine. I'll just wander for now.",
                    "Everything seems steady. Back to wandering."
                );

            case AIState.Explore:
                return Pick(
                    "I need something... time to search.",
                    "I'll look around for what I need."
                );

            case AIState.MoveToTarget:
                return Pick(
                    "I've got a lead. Moving toward something useful.",
                    "I see what I need. Heading there now."
                );

            case AIState.MoveToRememberedTarget:
                return Pick(
                    "I think I remember something that might help.",
                    "Memory might save me here. Let's try that."
                );

            case AIState.InteractWithTarget:
                return Pick(
                    "Let's try this.",
                    "This should help."
                );

            default:
                return "I'll keep going.";
        }
    }

    private bool TryHandleOpportunisticNeed()
    {
        if (needsManager == null || needsManager.needs == null)
        {
            DebugFlow("Opportunistic check aborted: needsManager or needs list is null.");
            return false;
        }

        if (needsManager.HasUrgentNeed())
        {
            DebugFlow("Opportunistic check aborted: urgent need exists.");
            return false;
        }

        DebugFlow("Opportunistic check started.");

        NeedType bestNeed = NeedType.Comfort;
        float bestScore = float.MinValue;
        bool foundOpportunity = false;

        for (int i = 0; i < needsManager.needs.Count; i++)
        {
            NeedsManager.NeedState need = needsManager.needs[i];
            if (need == null)
            {
                DebugFlow("Opportunistic check: skipped null need entry.");
                continue;
            }

            NeedType needType = need.needType;
            DebugFlow("Opportunistic check: evaluating need " + needType);

            if (!needsManager.ShouldOpportunisticallySatisfy(needType))
            {
                DebugFlow("Opportunistic check: rejected " + needType + " because ShouldOpportunisticallySatisfy returned false.");
                continue;
            }

            if (!HasEasyVisibleOpportunity(needType))
            {
                DebugFlow("Opportunistic check: rejected " + needType + " because no easy visible opportunity was found within range " + opportunisticTargetMaxDistance);
                continue;
            }

            float score = needsManager.GetNeedPriorityScore(needType);
            DebugFlow("Opportunistic check: " + needType + " has visible opportunity with priority score " + score);

            if (score <= bestScore)
            {
                DebugFlow("Opportunistic check: " + needType + " lost to current best score " + bestScore);
                continue;
            }

            bestNeed = needType;
            bestScore = score;
            foundOpportunity = true;

            DebugFlow("Opportunistic check: " + needType + " is the new best opportunistic need.");
        }

        if (!foundOpportunity)
        {
            if (TryAcquireOpportunisticPickupTarget(opportunisticTargetMaxDistance))
            {
                Narrate("That might be useful. I'll grab it.", "opportunity-item-pickup");
                return true;
            }

            DebugFlow("Opportunistic check finished: no valid opportunity selected.");
            return false;
        }

        currentNeedType = bestNeed;
        currentNeedActionIsUrgentDriven = false;

        DebugFlow("Opportunistic target selected for need: " + bestNeed + " with score " + bestScore);

        if (bestNeed == NeedType.Comfort)
        {
            if (TryHandleOpportunisticComfortLight())
            {
                DebugFlow("Opportunistic action: comfort light switch interaction target acquired.");
                Narrate("This room is dim. I'll flip that switch quickly.", "opportunity-comfort-light-switch");
                return true;
            }

            DebugFlow("Opportunistic action: trying visible comfort zone within range " + opportunisticTargetMaxDistance);

            if (TryMoveToVisibleComfortZone(opportunisticTargetMaxDistance))
            {
                DebugFlow("Opportunistic action: comfort zone move succeeded.");
                Narrate("That nearby lit area could help a little.", "opportunity-comfort-room");
                return true;
            }

            DebugFlow("Opportunistic action: comfort zone move failed.");
        }

        DebugFlow("Opportunistic action: trying visible target for need " + bestNeed + " within range " + opportunisticTargetMaxDistance);

        if (TryAcquireVisibleTarget(bestNeed, opportunisticTargetMaxDistance))
        {
            DebugFlow("Opportunistic action: visible target acquisition succeeded for need " + bestNeed);
            Narrate("Easy opportunity. I'll handle this quickly.", "opportunity-interactable");
            return true;
        }

        if (TryAcquireOpportunisticPickupTarget(opportunisticTargetMaxDistance))
        {
            Narrate("That might be useful. I'll grab it.", "opportunity-item-pickup");
            return true;
        }

        DebugFlow("Opportunistic action: visible target acquisition failed for need " + bestNeed);
        return false;
    }

    private bool TryAcquireOpportunisticPickupTarget(float maxDistance)
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, visionRange, interactableLayer);
        DebugFlow($"[PickupTarget] Start opportunistic pickup scan | hits={hits.Length} | maxDistance={maxDistance}");

        Interactable bestItem = null;
        float bestScore = float.MinValue;
        float bestDistance = Mathf.Infinity;

        for (int i = 0; i < hits.Length; i++)
        {
            Interactable interactable = hits[i].GetComponentInParent<Interactable>();
            if (interactable == null)
            {
                DebugFlow("[PickupTarget] Rejected collider: no Interactable in parent.");
                continue;
            }

            if (!interactable.isEnabled)
            {
                DebugFlow($"[PickupTarget] Rejected {interactable.name}: isEnabled = false");
                continue;
            }

            if (!CanSeeInteractable(interactable))
            {
                DebugFlow($"[PickupTarget] Rejected {interactable.name}: cannot see interactable");
                continue;
            }

            IPickupable pickupable = interactable as IPickupable;
            if (pickupable == null)
            {
                DebugFlow($"[PickupTarget] Rejected {interactable.name}: not IPickupable");
                continue;
            }

            if (!pickupable.CanPickUp(gameObject))
            {
                DebugFlow($"[PickupTarget] Rejected {interactable.name}: CanPickUp returned false");
                continue;
            }

            float distance = Vector3.Distance(transform.position, interactable.GetInteractionPoint());
            if (distance > maxDistance)
            {
                DebugFlow($"[PickupTarget] Rejected {interactable.name}: too far ({distance} > {maxDistance})");
                continue;
            }

            float score = pickupable.GetItemValue();
            IKeyItem keyItem = interactable as IKeyItem;
            bool keyMatchesRememberedLockedDoor = false;
            if (keyItem != null)
            {
                score *= KeyPickupPriorityMultiplier;
                keyMatchesRememberedLockedDoor = IsKeyUsefulForRememberedLockedDoor(keyItem);
                if (keyMatchesRememberedLockedDoor)
                    score *= MatchingLockedDoorKeyPriorityMultiplier;
            }

            if (score < bestScore)
                continue;

            if (Mathf.Approximately(score, bestScore) && distance >= bestDistance)
                continue;

            DebugFlow($"[PickupTarget] Candidate accepted: {interactable.name} | score={score} | distance={distance}");
            bestItem = interactable;
            bestScore = score;
            bestDistance = distance;
        }

        if (bestItem == null)
        {
            DebugFlow("[PickupTarget] FAILED: no eligible opportunistic pickup target found.");
            return false;
        }

        currentTarget = bestItem;
        currentMemoryTarget = null;
        currentComfortZoneTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        currentNeedActionIsUrgentDriven = false;
        agent.ResetPath();

        Vector3 targetPosition = currentTarget.GetInteractionPoint();
        if (!IsPathReachable(targetPosition) && !TryHandleDoorForDestination(targetPosition))
        {
            DebugFlow($"[PickupTarget] Selected {currentTarget.name} but destination is unreachable.");
            return false;
        }

        DebugFlow($"[PickupTarget] SUCCESS: selected {currentTarget.name} | score={bestScore} | distance={bestDistance}");

        if (bestItem is IKeyItem bestKeyItem &&
            IsKeyUsefulForRememberedLockedDoor(bestKeyItem) &&
            Time.time >= lastMatchingLockedDoorKeyNarrationTime + MatchingLockedDoorNarrationCooldown)
        {
            lastMatchingLockedDoorKeyNarrationTime = Time.time;
            Narrate("That key could help with that locked door.", "opportunity-key-matches-locked-door");
        }

        ChangeState(AIState.MoveToTarget);
        return true;
    }

    private bool HasEasyVisibleOpportunity(NeedType needType)
    {
        if (needType == NeedType.Comfort)
        {
            if (HasEasyVisibleComfortLightOpportunity())
                return true;

            if (TryFindBestVisibleComfortRoom(opportunisticTargetMaxDistance, out _, out _))
                return true;
        }

        return TryFindBestVisibleTarget(needType, opportunisticTargetMaxDistance, out _, out _);
    }

    private bool HasEasyVisibleComfortLightOpportunity()
    {
        if (Time.time < lastOpportunisticComfortLightTime + opportunisticComfortLightCooldown)
            return false;

        return TryFindBestVisibleComfortLightSwitch(opportunisticTargetMaxDistance, out _, out _);
    }

    private bool TryHandleOpportunisticComfortLight()
    {
        if (!HasEasyVisibleComfortLightOpportunity())
            return false;

        if (!TryFindBestVisibleComfortLightSwitch(opportunisticTargetMaxDistance, out LightSwitchInteractable bestSwitch, out _))
            return false;

        RememberInteractable(bestSwitch, NeedType.Comfort);
        currentNeedType = NeedType.Comfort;
        currentTarget = bestSwitch;
        currentMemoryTarget = null;
        currentComfortZoneTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();

        Vector3 targetPosition = currentTarget.GetInteractionPoint();
        if (!IsPathReachable(targetPosition) && !TryHandleDoorForDestination(targetPosition))
            return false;

        lastOpportunisticComfortLightTime = Time.time;
        ChangeState(AIState.MoveToTarget);
        return true;
    }

    private bool TryFindBestVisibleComfortLightSwitch(float maxDistance, out LightSwitchInteractable bestSwitch, out float bestDistance)
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, visionRange, interactableLayer);

        bestSwitch = null;
        bestDistance = Mathf.Infinity;
        bool currentAreaLit = IsCurrentAreaLit();

        for (int i = 0; i < hits.Length; i++)
        {
            Interactable interactable = hits[i].GetComponentInParent<Interactable>();
            LightSwitchInteractable lightSwitch = interactable as LightSwitchInteractable;

            if (lightSwitch == null || !lightSwitch.isEnabled)
                continue;

            if (!CanSeeInteractable(lightSwitch))
                continue;

            if (!lightSwitch.CanInteract(gameObject))
                continue;

            RoomArea targetRoom = lightSwitch.targetRoom;
            if (targetRoom == null || targetRoom.IsLit())
                continue;

            bool isCurrentRoomSwitch = currentRoom != null && targetRoom == currentRoom;
            float roomDistance = Vector3.Distance(transform.position, targetRoom.GetRoomCenterPoint());
            bool isNearbyRoom = roomDistance <= maxDistance * 1.5f;

            if (!isCurrentRoomSwitch && !isNearbyRoom)
                continue;

            if (currentAreaLit && !isCurrentRoomSwitch)
                continue;

            float distance = Vector3.Distance(transform.position, lightSwitch.GetInteractionPoint());
            if (distance > maxDistance || distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestSwitch = lightSwitch;
        }

        return bestSwitch != null;
    }

    private bool TryFindBestVisibleTarget(NeedType needType, float maxDistance, out Interactable bestTarget, out float bestDistance)
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, visionRange, interactableLayer);

        DebugFlow($"[FindTarget] Start search for {needType} | Hits found: {hits.Length} | visionRange: {visionRange} | maxDistance: {maxDistance}");

        bestTarget = null;
        bestDistance = Mathf.Infinity;

        foreach (Collider hit in hits)
        {
            Interactable interactable = hit.GetComponentInParent<Interactable>();

            if (interactable == null)
            {
                DebugFlow("[FindTarget] Skipped: no Interactable on collider " + hit.name);
                continue;
            }

            DebugFlow($"[FindTarget] Checking: {interactable.name}");

            if (!interactable.isEnabled)
            {
                DebugFlow($"[FindTarget] Rejected {interactable.name}: isEnabled = false");
                continue;
            }

            if (!CanSeeInteractable(interactable))
            {
                DebugFlow($"[FindTarget] Rejected {interactable.name}: cannot see (vision/angle/obstruction)");
                continue;
            }

            INeedSatisfier satisfier = interactable as INeedSatisfier;
            if (satisfier == null)
            {
                DebugFlow($"[FindTarget] Rejected {interactable.name}: not INeedSatisfier");
                continue;
            }

            if (satisfier.GetNeedType() != needType)
            {
                DebugFlow($"[FindTarget] Rejected {interactable.name}: wrong need type ({satisfier.GetNeedType()} != {needType})");
                continue;
            }

            if (!interactable.CanInteract(gameObject))
            {
                DebugFlow($"[FindTarget] Rejected {interactable.name}: CanInteract returned false");
                continue;
            }

            float distance = Vector3.Distance(transform.position, interactable.GetInteractionPoint());

            if (distance > maxDistance)
            {
                DebugFlow($"[FindTarget] Rejected {interactable.name}: too far ({distance} > {maxDistance})");
                continue;
            }

            if (distance >= bestDistance)
            {
                DebugFlow($"[FindTarget] Rejected {interactable.name}: not closer than current best ({distance} >= {bestDistance})");
                continue;
            }

            DebugFlow($"[FindTarget] Candidate accepted: {interactable.name} at distance {distance}");

            bestDistance = distance;
            bestTarget = interactable;
        }

        if (bestTarget != null)
        {
            DebugFlow($"[FindTarget] SUCCESS: Selected {bestTarget.name} at distance {bestDistance}");
            return true;
        }

        DebugFlow($"[FindTarget] FAILED: No valid target found for {needType}");
        return false;
    }

    private bool TryFindBestVisibleComfortRoom(float maxDistance, out RoomArea bestRoom, out float bestDistance)
    {
        List<RoomArea> visibleRooms = GetVisibleComfortRooms();
        bestRoom = null;
        bestDistance = Mathf.Infinity;

        for (int i = 0; i < visibleRooms.Count; i++)
        {
            RoomArea room = visibleRooms[i];
            if (room == null)
                continue;

            Vector3 point = room.GetRoomCenterPoint();
            float distance = Vector3.Distance(transform.position, point);

            if (distance > maxDistance || distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestRoom = room;
        }

        return bestRoom != null;
    }

    private bool IsStalled()
    {
        if (agent == null || !agent.isOnNavMesh || agent.pathPending || !agent.hasPath)
        {
            stallTimer = 0f;
            return false;
        }

        if (agent.remainingDistance <= agent.stoppingDistance + 0.05f)
        {
            stallTimer = 0f;
            return false;
        }

        if (agent.velocity.sqrMagnitude < stallVelocityThreshold * stallVelocityThreshold)
        {
            stallTimer += Time.deltaTime;
            return stallTimer >= stallTimeThreshold;
        }

        stallTimer = 0f;
        return false;
    }

    private void ResetStallTimer()
    {
        stallTimer = 0f;
    }

    private bool TryGetBlockingDoorTowards(Vector3 destination, out DoorInteractable door)
    {
        door = null;

        Vector3 origin = eyePoint != null ? eyePoint.position : transform.position + Vector3.up * 1.2f;
        Vector3 toDestination = destination - origin;
        toDestination.y = 0f;
        float distanceToDestination = toDestination.magnitude;
        if (distanceToDestination <= 0.05f)
            return false;

        Vector3 probeDirection = toDestination / distanceToDestination;
        float probeDistance = Mathf.Min(doorCheckDistance, distanceToDestination);

        int probeMask = doorLayer.value == 0 ? interactableLayer : doorLayer;

        if (Physics.SphereCast(origin, 0.2f, probeDirection, out RaycastHit hit, probeDistance, probeMask, QueryTriggerInteraction.Collide))
        {
            DoorInteractable hitDoor = hit.collider.GetComponentInParent<DoorInteractable>();
            if (hitDoor != null && hitDoor.CanInteract(gameObject))
            {
                door = hitDoor;
                return true;
            }
        }

        return false;
    }

    private bool IsExplorePointBlockedByDoor(Vector3 destination, out DoorInteractable door)
    {
        door = null;

        if (IsPathReachable(destination))
            return false;

        return TryGetBlockingDoorTowards(destination, out door);
    }

    private bool TryHandleDoorForDestination(Vector3 destination)
    {
        if (!TryGetBlockingDoorTowards(destination, out DoorInteractable door))
            return false;

        DoorController controller = door.GetDoorController();
        if (controller == null)
            return false;

        if (controller.IsLocked && !HasMatchingInventoryKey(controller))
        {
            RememberLockedDoor(door);
            return false;
        }

        if (controller.IsOpen)
        {
            pendingDoorTarget = null;
            hasPendingDoorDestination = false;
            agent.ResetPath();
            RepathCurrentMovementDestination();
            ResetStallTimer();
            return true;
        }

        pendingDoorTarget = door;
        pendingDoorDestination = destination;
        hasPendingDoorDestination = true;
        agent.ResetPath();
        ResetStallTimer();
        return true;
    }

    private bool HandlePendingDoorTarget()
    {
        if (pendingDoorTarget == null)
            return false;

        DoorController controller = pendingDoorTarget.GetDoorController();
        if (controller == null)
        {
            pendingDoorTarget = null;
            hasPendingDoorDestination = false;
            return false;
        }

        if (controller.IsOpen)
        {
            pendingDoorTarget = null;
            Vector3 repathDestination = hasPendingDoorDestination ? pendingDoorDestination : transform.position;
            hasPendingDoorDestination = false;
            agent.ResetPath();
            agent.SetDestination(repathDestination);
            ResetStallTimer();
            return true;
        }

        Vector3 doorPoint = pendingDoorTarget.GetInteractionPoint();
        agent.speed = GetActionMoveSpeed();
        agent.SetDestination(doorPoint);

        if (!agent.pathPending && agent.remainingDistance <= Mathf.Max(doorStopDistance, pendingDoorTarget.interactionRange))
        {
            if (Time.time < lastDoorInteractTime + doorInteractCooldown)
                return true;

            if (!pendingDoorTarget.CanInteract(gameObject))
            {
                pendingDoorTarget = null;
                hasPendingDoorDestination = false;
                return false;
            }

            pendingDoorTarget.Interact(gameObject);
            lastDoorInteractTime = Time.time;

            if (!controller.IsOpen && controller.IsLocked)
            {
                if (TryUseInventoryKeyOnDoor(pendingDoorTarget))
                {
                    pendingDoorTarget.Interact(gameObject);
                }
                else
                {
                    RememberLockedDoor(pendingDoorTarget);
                    pendingDoorTarget = null;
                    hasPendingDoorDestination = false;
                    return false;
                }
            }

            ResetStallTimer();
        }

        return true;
    }

    private void QueueDoorHandling(DoorInteractable door, Vector3 destination)
    {
        pendingDoorTarget = door;
        pendingDoorDestination = destination;
        hasPendingDoorDestination = true;
        agent.ResetPath();
        ResetStallTimer();
    }

    private bool HasMatchingInventoryKey(DoorController controller)
    {
        if (controller == null)
            return false;

        if (!controller.IsLocked)
            return true;

        if (npcInventory == null)
            return false;

        return npcInventory.TryGetMatchingKey(controller.RequiredKeyId, out _);
    }

    private void RepathCurrentMovementDestination()
    {
        switch (currentState)
        {
            case AIState.Explore:
                if (hasExplorePoint)
                    agent.SetDestination(currentExplorePoint);
                break;

            case AIState.MoveToTarget:
                if (currentTarget != null)
                    agent.SetDestination(currentTarget.GetInteractionPoint());
                break;

            case AIState.MoveToRememberedTarget:
                if (currentComfortZoneTarget != null)
                {
                    agent.SetDestination(currentComfortZoneTarget.lastKnownPosition);
                }
                else if (currentMemoryTarget != null)
                {
                    agent.SetDestination(currentMemoryTarget.lastKnownPosition);
                }
                break;
        }
    }

    private string Pick(params string[] options)
    {
        if (options == null || options.Length == 0)
            return string.Empty;

        if (options.Length == 1)
            return options[0];

        return options[Random.Range(0, options.Length)];
    }

    private void Narrate(
        string message,
        string eventKey = null,
        NPCThoughtLogger.ThoughtCategory category = NPCThoughtLogger.ThoughtCategory.General)
    {
        if (thoughtLogger == null || string.IsNullOrEmpty(message))
            return;

        NeedsManager.NeedUrgencyBand band = needsManager != null
            ? needsManager.GetNeedUrgencyBand(currentNeedType)
            : NeedsManager.NeedUrgencyBand.Stable;

        float value = needsManager != null
            ? needsManager.GetNeedValue(currentNeedType)
            : 0f;

        thoughtLogger.Think(
            gameObject.name,
            currentState.ToString(),
            currentNeedType.ToString(),
            value,
            band.ToString(),
            message,
            eventKey,
            category
        );
    }

    private void DebugAbort(string reason)
    {
        if (!debugOpportunisticFlow)
            return;

        Debug.Log($"{name} | {reason} | urgentDriven={currentNeedActionIsUrgentDriven} | state={currentState} | need={currentNeedType}");
    }

    private void DebugPickup(string reason)
    {
        if (!debugOpportunisticFlow)
            return;

        string targetName = currentTarget != null ? currentTarget.name : "null";
        Debug.Log($"{name} | Pickup | {reason} | target={targetName} | state={currentState} | need={currentNeedType} | urgentDriven={currentNeedActionIsUrgentDriven}");
    }

    private void DebugFlow(string reason)
    {
        if (!debugOpportunisticFlow)
            return;

        Debug.Log($"{name} | Flow | {reason} | state={currentState} | need={currentNeedType} | urgentDriven={currentNeedActionIsUrgentDriven}");
    }
}
