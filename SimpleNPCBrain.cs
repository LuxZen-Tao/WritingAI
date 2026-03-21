using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class SimpleNPCBrain : MonoBehaviour
{
    private enum InventoryUseAttempt
    {
        None,
        WaitingForHandReady,
        Used
    }

    private enum DoorUnlockAttempt
    {
        Failed,
        WaitingForHandReady,
        Unlocked
    }

    private enum PendingDoorPhase
    {
        None,
        ApproachingAnchor,
        Repositioning
    }

    private struct PendingDoorRuntime
    {
        public PendingDoorPhase phase;
        public float startTime;
        public float bestDistance;
        public float lastProgressTime;
        public bool repositionAttempted;
        public bool hasRecoveryPoint;
        public Vector3 recoveryPoint;
    }

    private enum MovementFailureKind
    {
        None,
        BlockedByDoor,
        BlockedByObstacle,
        BadChosenPoint,
        NoProgressDeadlock
    }

    private struct MovementRouteStepResult
    {
        public bool arrived;
        public bool failed;
        public bool redirectedToDoor;
        public MovementFailureKind failureKind;
        public NpcRouteCoordinator.MovementRecoveryResult recoveryResult;
    }

    public enum AIState
    {
        IdleWander,
        Explore,
        MoveToTarget,
        MoveToRememberedTarget,
        InteractWithTarget,
        Resting,
        PerformingActivity
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
    public float movementProgressDistanceThreshold = 0.2f;
    public float movementProgressPathDistanceThreshold = 0.2f;
    public float movementNoProgressTimeout = 2.25f;
    public float movementGoalTimeout = 12f;
    public int maxMovementRecoveryAttempts = 4;
    public float movementRecoverySampleRadius = 1.5f;
    public float movementRecoveryUnstuckRadius = 1.0f;
    public float movementPointClearanceRadius = 0.35f;
    public int movementPathFailureLimit = 3;

    [Header("Movement Point Quality")]
    public float movementPointSampleDistance = 2f;
    public float minimumMovementPointWallDistance = 0.2f;
    public float minimumExploreMoveDistance = 1f;

    [Header("Door Probe")]
    public float doorCheckDistance = 1.75f;
    public LayerMask doorLayer;
    public float doorInteractCooldown = 0.5f;
    public float doorStopDistance = 1.15f;
    public int maxExploreDoorRouteAttempts = 2;

    [Header("Pending Door Stabilization")]
    public float pendingDoorProgressDistanceThreshold = 0.15f;
    public float pendingDoorNoProgressTimeout = 1.5f;
    public float pendingDoorMaxDuration = 6f;
    public float pendingDoorRecoverySampleRadius = 0.9f;
    public float pendingDoorRecoveryArrivalDistance = 0.35f;

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

    [Header("Downtime Activities")]
    public float downtimeActivitySearchRadius = 5f;
    public float downtimeActivityCheckInterval = 1f;
    private float downtimeActivityCheckTimer = 0f;

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

    [Header("Hand Item Presentation")]
    [SerializeField] private float minimumHandUseDelay = 1.0f;
    [SerializeField] private float handPostUseVisibleDelay = 0.4f;
    [SerializeField] private float idleHandPocketDelay = 1.5f;

    [Header("Thought Logging")]
    [SerializeField] private NPCThoughtLogger thoughtLogger;

    [Header("Debug")]
    [SerializeField] private bool debugOpportunisticFlow = true;
    [SerializeField] private bool debugMovementRecovery = false;
    [SerializeField] private bool enableInvariantChecks = true;
    [SerializeField] private float invariantCheckInterval = 1f;
    [SerializeField] private bool logSetupValidation = true;

    private readonly List<RoomArea> overlappingRooms = new List<RoomArea>();
    private RoomArea[] knownRooms;
    private readonly NpcPerceptionService perceptionService = new NpcPerceptionService();
    private readonly NpcMemoryService memoryService = new NpcMemoryService();
    private readonly NpcRouteCoordinator routeCoordinator = new NpcRouteCoordinator();

    private NavMeshAgent agent;
    
    private Vector3 currentRestAnchorPosition;
    private bool hasRestAnchor = false;
    private float currentRestHoldTimer = 0f;

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
    private PendingDoorRuntime pendingDoorRuntime;
    private int currentExploreDoorRouteAttempts = 0;
    private RestInteractable currentRestInteractable;
    private float currentRestSessionElapsed = 0f;
    private float currentRestMinimumDuration = 0f;
    private float currentRestMaximumDuration = 0f;
    private float currentRestHoldTime = 0f;
    private Transform currentRestAnchor;
    private float currentRestAnchorSnapDistance = 0.35f;
    private IActivityInteractable currentActivityInteractable;
    private float currentActivityElapsed = 0f;
    private float currentActivityDuration = 0f;
    private Vector3 currentActivityLookPoint;
    private bool hasCurrentActivityLookPoint = false;
    private float currentActivityLookTimer = 0f;

    private readonly Dictionary<NeedType, NeedsManager.NeedUrgencyBand> lastNeedBands = new Dictionary<NeedType, NeedsManager.NeedUrgencyBand>();
    private readonly Dictionary<NeedType, bool> lastNeedUrgentFlags = new Dictionary<NeedType, bool>();
    private float lastOpportunisticComfortLightTime = -999f;
    private float lastMatchingLockedDoorKeyNarrationTime = -999f;
    private float invariantCheckTimer = 0f;
    private float lastInvariantWarningTime = -999f;
    private float handItemReadyToUseTime = 0f;
    private Interactable activePreparedHandUseItem;
    private Interactable trackedHandItem;
    private float trackedHandItemSinceTime = 0f;
    private Interactable pendingPostUsePocketItem;
    private float pendingPostUsePocketTime = -1f;
    private const float InvariantWarningCooldown = 2f;

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
        perceptionService.Configure(visionRange, visionAngle, interactableLayer, obstacleLayer, doorLayer, eyePoint);
        routeCoordinator.Configure(name, debugMovementRecovery);
        EnsureThoughtLogger();
        CacheInitialNeedState();

        currentState = AIState.IdleWander;
        hasIdlePoint = false;
        idlePauseTimer = idlePauseDuration;
        opportunisticCheckTimer = Random.Range(0f, opportunisticCheckInterval);
        downtimeActivityCheckTimer = Random.Range(0f, Mathf.Max(0.2f, downtimeActivityCheckInterval));
        invariantCheckTimer = Random.Range(0f, Mathf.Max(0.2f, invariantCheckInterval));
        ValidateRuntimeSetup();
        
        Narrate("What a beautiful day! Let's go for a wander 😊.", "state-idle-start");
    }

private void Update()
{
    routeCoordinator.Configure(name, debugMovementRecovery);
    needsManager.TickNeeds(IsCurrentAreaLit(), Time.deltaTime);
    ObserveNeedShifts();

    CleanupLocationMemory();
    CleanupInteractableMemory();
    CleanupComfortZoneMemory();
    CleanupLockedDoorMemory();
    RunInvariantChecks();

    PassiveObserveVisibleInteractables();
    PassiveObserveVisibleComfortZones();

    if (currentState != AIState.Resting &&
        currentState != AIState.PerformingActivity &&
        TryResumeLockedDoorMission())
    {
        return;
    }

    if (needsManager.HasUrgentNeed(out NeedType mostUrgentNeed))
    {
        bool needChanged = mostUrgentNeed != currentNeedType;

        // Do not replace the active need while currently resting.
        if (!(currentState == AIState.Resting && currentRestInteractable != null))
        {
            currentNeedType = mostUrgentNeed;
        }

        currentNeedActionIsUrgentDriven = true;

        if (needChanged && IsGoalExecutionState(currentState) && currentState != AIState.Resting && CanInterruptCurrentActionForUrgentNeed())
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
            if (IsGoalExecutionState(currentState) && currentState != AIState.Resting && CanInterruptCurrentActionForUrgentNeed())
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
    else if (currentNeedActionIsUrgentDriven && IsGoalExecutionState(currentState) && currentState != AIState.Resting && routeCoordinator.SubgoalCount == 0)
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

    if (currentState != AIState.Resting &&
        currentState != AIState.PerformingActivity &&
        routeCoordinator.SubgoalCount > 0 &&
        TryProcessTopSubgoal())
    {
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

        case AIState.Resting:
            HandleRestingState();
            break;

        case AIState.PerformingActivity:
            HandlePerformingActivityState();
            break;
    }

    HandleHandItemPresentation();
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
               state == AIState.InteractWithTarget ||
               state == AIState.Resting ||
               state == AIState.PerformingActivity;
    }

    private bool CanInterruptCurrentActionForUrgentNeed()
    {
        if (currentState != AIState.PerformingActivity)
            return true;

        if (currentActivityInteractable == null)
            return true;

        return currentActivityInteractable.IsInterruptible();
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
        if (HandlePendingDoorTarget())
            return;

        agent.speed = idleMoveSpeed;

        if (TryStartDowntimeActivity())
            return;

        if (idlePauseTimer > 0f)
        {
            idlePauseTimer -= Time.deltaTime;

            if (agent.hasPath)
                agent.ResetPath();

            ResetStallTimer();
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

                ResetStallTimer();
                return;
            }

            Narrate(Pick(
                "Just wandering...",
                "Everything seems fine. I'll move around a bit.",
                "I'll stay nearby for now."
            ), "idle-picked-point");
        }

        MovementRouteStepResult routeStep = ExecuteMovementRouteStep(currentIdlePoint, idleWanderStopDistance, "IdleWander");
        if (routeStep.redirectedToDoor)
            return;

        if (routeStep.failed)
        {
            RememberLocation(currentIdlePoint);
            LogMovementFailure("IdleWander", currentIdlePoint, routeStep.failureKind, routeStep.recoveryResult);
            hasIdlePoint = false;
            idlePauseTimer = 0.35f;

            if (agent.hasPath)
                agent.ResetPath();

            ClearMovementGoal();
            ResetStallTimer();
            return;
        }

        if (routeStep.arrived)
        {
            if (agent.hasPath)
                agent.ResetPath();

            ClearMovementGoal();
            hasIdlePoint = false;

            if (TryHandleNearbyIdleDoor())
            {
                idlePauseTimer = 0.25f;
                ResetStallTimer();
                return;
            }

            idlePauseTimer = idlePauseDuration;
            ResetStallTimer();
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

        if (currentNeedType == NeedType.Hunger)
        {
            InventoryUseAttempt inventoryUse = TryUseInventoryItemForNeed(NeedType.Hunger);
            if (inventoryUse == InventoryUseAttempt.Used)
            {
                Narrate("I have something for this. I'll use it.", "inventory-use-hunger");
                AbortCurrentNeedAction();
                return;
            }

            if (inventoryUse == InventoryUseAttempt.WaitingForHandReady)
            {
                if (agent.hasPath)
                    agent.ResetPath();
                return;
            }
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
                ClearMovementGoal();
            }

            return;
        }

        currentExploreDoorRouteAttempts = 0;
        MovementRouteStepResult routeStep = ExecuteMovementRouteStep(currentExplorePoint, exploreStopDistance, "ExplorePoint");
        if (routeStep.redirectedToDoor)
            return;

        if (routeStep.failed)
        {
            RememberLocation(currentExplorePoint);
            LogMovementFailure("ExplorePoint", currentExplorePoint, routeStep.failureKind, routeStep.recoveryResult);
            Narrate("I can't move through here. I'll try another route.", "explore-stalled-fallback");
            hasExplorePoint = false;
            exploreTimer = 0f;
            agent.ResetPath();
            ClearMovementGoal();
            return;
        }

        if (routeStep.arrived)
        {
            RememberLocation(currentExplorePoint);
            Narrate("Nothing useful here... moving on.", "explore-point-finished");
            hasExplorePoint = false;
            exploreTimer = 0f;
            agent.ResetPath();
            ClearMovementGoal();
            return;
        }

        if (exploreTimer >= exploreDuration)
        {
            RememberLocation(currentExplorePoint);
            Narrate("I've searched here long enough. Moving on.", "explore-point-abandon");
            hasExplorePoint = false;
            exploreTimer = 0f;
            agent.ResetPath();
            ClearMovementGoal();
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

            if (!TryGetQualifiedMovementPoint(candidate, minimumExploreMoveDistance, true, out Vector3 qualifiedPoint))
                continue;

            if (IsPathReachable(qualifiedPoint))
            {
                currentExplorePoint = qualifiedPoint;
                hasExplorePoint = true;
                exploreTimer = 0f;
                currentExploreDoorRouteAttempts = 0;
                return true;
            }

            if (!allowDoorBlockedExplore)
                continue;

            if (!IsExplorePointBlockedByDoor(qualifiedPoint, out DoorInteractable blockingDoor))
                continue;

            currentExplorePoint = qualifiedPoint;
            hasExplorePoint = true;
            exploreTimer = 0f;
            currentExploreDoorRouteAttempts = 0;
            QueueDoorHandling(blockingDoor, qualifiedPoint, "explore-beyond-door");
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

            if (!TryGetQualifiedMovementPoint(candidate, minimumIdleMoveDistance, false, out Vector3 qualifiedPoint))
                continue;

            if (!room.ContainsPoint(qualifiedPoint))
                continue;

            if (!IsPathReachable(qualifiedPoint))
                continue;

            candidates.Add(qualifiedPoint);
        }
    }

    private void TryAddOutsideIdleCandidates(List<Vector3> candidates, int attempts)
    {
        for (int i = 0; i < attempts; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * idleWanderRadius;
            randomOffset.y = 0f;
            Vector3 candidate = transform.position + randomOffset;

            if (!TryGetQualifiedMovementPoint(candidate, minimumIdleMoveDistance, false, out Vector3 qualifiedPoint))
                continue;

            if (currentRoom != null && currentRoom.ContainsPoint(qualifiedPoint))
                continue;

            if (!IsPathReachable(qualifiedPoint))
                continue;

            candidates.Add(qualifiedPoint);
        }
    }

    private bool TryPickLocalIdlePoint()
    {
        for (int i = 0; i < 10; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * idleWanderRadius;
            randomOffset.y = 0f;
            Vector3 candidate = transform.position + randomOffset;

            if (!TryGetQualifiedMovementPoint(candidate, minimumIdleMoveDistance, false, out Vector3 qualifiedPoint))
                continue;

            if (!IsPathReachable(qualifiedPoint))
                continue;

            currentIdlePoint = qualifiedPoint;
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
        NpcMemoryService.ComfortMemoryWriteResult writeResult = memoryService.RememberComfortZone(rememberedComfortZones, room, position, wasLit, Time.time);
        if (writeResult == NpcMemoryService.ComfortMemoryWriteResult.UpdatedLightingChanged)
        {
            Narrate(wasLit
                ? "That room looks lit now."
                : "That room just got darker.",
                "memory-comfort-update-" + room.GetInstanceID());
        }
        else if (writeResult == NpcMemoryService.ComfortMemoryWriteResult.Added && wasLit)
        {
            Narrate("That room looks lit. I'll remember it.", "memory-comfort-new-" + room.GetInstanceID());
        }
    }

    private void CleanupComfortZoneMemory()
    {
        memoryService.CleanupComfortZoneMemory(rememberedComfortZones, comfortZoneMemoryDuration, Time.time);
    }

    private bool TryMoveToVisibleComfortZone(float maxDistance = Mathf.Infinity)
    {
        if (!TryFindBestVisibleComfortRoom(maxDistance, out RoomArea bestRoom, out _))
            return false;

        Vector3 bestPoint = bestRoom.GetRoomCenterPoint();
        bool wasLit = bestRoom.IsLit();

        if (!IsPathReachable(bestPoint) && !TryHandleDoorForDestination(bestPoint))
        {
            RememberLocation(bestPoint);
            return false;
        }

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
        if (!memoryService.TryFindBestRememberedComfortZone(rememberedComfortZones, transform.position, true, out RememberedComfortZone bestZone))
            return false;

        if (!IsPathReachable(bestZone.lastKnownPosition) && !TryHandleDoorForDestination(bestZone.lastKnownPosition))
        {
            RememberLocation(bestZone.lastKnownPosition);
            return false;
        }

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
        if (!memoryService.TryFindBestRememberedComfortZone(rememberedComfortZones, transform.position, false, out RememberedComfortZone bestZone))
            return false;

        if (!IsPathReachable(bestZone.lastKnownPosition) && !TryHandleDoorForDestination(bestZone.lastKnownPosition))
        {
            RememberLocation(bestZone.lastKnownPosition);
            return false;
        }

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

    private bool TryCalculatePathTo(Vector3 destination, out NavMeshPath path)
    {
        path = new NavMeshPath();

        if (!agent.isOnNavMesh)
            return false;

        if (!agent.CalculatePath(destination, path))
            return false;

        return true;
    }

    private NavMeshPathStatus GetPathStatus(Vector3 destination)
    {
        if (!TryCalculatePathTo(destination, out NavMeshPath path))
            return NavMeshPathStatus.PathInvalid;

        return path.status;
    }

    private bool IsPathReachable(Vector3 destination)
    {
        if (!TryCalculatePathTo(destination, out NavMeshPath path))
            return false;

        return path.status == NavMeshPathStatus.PathComplete;
    }

    private bool TryGetQualifiedMovementPoint(Vector3 candidate, float minimumTravelDistance, bool avoidRecentVisit, out Vector3 qualifiedPoint)
    {
        qualifiedPoint = candidate;

        if (Vector3.Distance(transform.position, candidate) < Mathf.Max(0f, minimumTravelDistance))
            return false;

        if (avoidRecentVisit && IsRecentlyVisited(candidate))
            return false;

        if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, Mathf.Max(0.2f, movementPointSampleDistance), NavMesh.AllAreas))
            return false;

        if (avoidRecentVisit && IsRecentlyVisited(navHit.position))
            return false;

        if (!IsMovementPointUsable(navHit.position))
            return false;

        qualifiedPoint = navHit.position;
        return true;
    }

    private bool IsMovementPointUsable(Vector3 point)
    {
        if (!HasPointClearance(point))
            return false;

        return !IsMovementPointTooNearWall(point);
    }

    private bool IsMovementPointTooNearWall(Vector3 point)
    {
        float minimumWallDistance = Mathf.Max(0f, minimumMovementPointWallDistance);
        if (minimumWallDistance <= 0.001f)
            return false;

        if (!NavMesh.FindClosestEdge(point, out NavMeshHit edgeHit, NavMesh.AllAreas))
            return false;

        return edgeHit.distance < minimumWallDistance;
    }

    private bool CanSeePoint(Vector3 targetPoint)
    {
        return perceptionService.CanSeePoint(transform, transform.forward, targetPoint);
    }

    private void PassiveObserveVisibleInteractables()
    {
        List<Interactable> visibleInteractables = perceptionService.GetVisibleInteractables(transform, transform.forward);

        for (int i = 0; i < visibleInteractables.Count; i++)
        {
            Interactable interactable = visibleInteractables[i];
            RememberObservedInteractable(interactable);
        }
    }

    private void RememberObservedInteractable(Interactable interactable)
    {
        if (interactable == null)
            return;

        if (!memoryService.TryRememberObservedInteractable(memory, interactable, gameObject, Time.time, out NpcMemoryService.ObservedInteractableMemoryResult result))
            return;

        NarrateInteractableMemoryWrite(interactable, result.writeResult);
    }

    private bool TryStartDowntimeActivity()
    {
        if (HasUrgentNeed())
            return false;

        if (currentNeedActionIsUrgentDriven)
            return false;

        downtimeActivityCheckTimer -= Time.deltaTime;
        if (downtimeActivityCheckTimer > 0f)
            return false;

        downtimeActivityCheckTimer = Mathf.Max(0.2f, downtimeActivityCheckInterval);

        if (TryAcquireVisibleActivityTarget(downtimeActivitySearchRadius))
            return true;

        return TryUseRememberedActivityTarget(downtimeActivitySearchRadius);
    }

    private bool TryAcquireVisibleActivityTarget(float maxDistance)
    {
        List<Interactable> visibleInteractables = perceptionService.GetVisibleInteractables(transform, transform.forward);
        Interactable bestTarget = null;
        float bestScore = float.MinValue;
        float bestDistance = Mathf.Infinity;

        for (int i = 0; i < visibleInteractables.Count; i++)
        {
            Interactable interactable = visibleInteractables[i];
            if (!(interactable is IActivityInteractable activity))
                continue;

            if (!interactable.isEnabled || !interactable.CanInteract(gameObject))
                continue;

            float distance = Vector3.Distance(transform.position, interactable.GetInteractionPoint());
            if (distance > maxDistance)
                continue;

            float score = activity.GetDesirability() / (1f + distance);
            if (score > bestScore || (Mathf.Approximately(score, bestScore) && distance < bestDistance))
            {
                bestTarget = interactable;
                bestScore = score;
                bestDistance = distance;
            }
        }

        if (bestTarget == null)
            return false;

        IActivityInteractable bestActivity = bestTarget as IActivityInteractable;
        RememberInteractable(bestTarget, NeedType.Key, true, bestActivity.GetActivityType());
        if (!BeginDowntimeTargetMove(bestTarget, null))
            return false;

        Narrate("That looks fun for a quick break.", "activity-visible-target");
        return true;
    }

    private bool TryUseRememberedActivityTarget(float maxDistance)
    {
        if (!memoryService.TryFindBestRememberedActivityTarget(memory, gameObject, transform.position, maxDistance, out RememberedInteractable bestMemory))
            return false;

        if (!BeginDowntimeTargetMove(bestMemory.interactable, bestMemory))
            return false;

        Narrate("I remember something fun nearby.", "activity-remembered-target");
        return true;
    }

    private bool BeginDowntimeTargetMove(Interactable interactable, RememberedInteractable remembered)
    {
        currentTarget = interactable;
        currentMemoryTarget = remembered;
        currentComfortZoneTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();

        Vector3 targetPosition = remembered != null ? remembered.lastKnownPosition : interactable.GetInteractionPoint();
        if (!IsPathReachable(targetPosition) && !TryHandleDoorForDestination(targetPosition))
        {
            RememberLocation(targetPosition);
            ClearActiveTargets();
            return false;
        }

        ChangeState(remembered != null ? AIState.MoveToRememberedTarget : AIState.MoveToTarget);
        return true;
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
        {
            RememberLocation(targetPosition);
            ClearActiveTargets();
            return false;
        }

        ChangeState(AIState.MoveToTarget);
        return true;
    }

    private bool TryUseRememberedTarget(NeedType needType, float maxDistance = Mathf.Infinity)
    {
        if (!memoryService.TryFindBestRememberedNeedTarget(memory, needType, gameObject, transform.position, maxDistance, out RememberedInteractable bestMemory))
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
        {
            RememberLocation(bestMemory.lastKnownPosition);
            ClearActiveTargets();
            return false;
        }

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
        MovementRouteStepResult routeStep = ExecuteMovementRouteStep(targetPosition, currentTarget.interactionRange, "CurrentTarget");
        if (routeStep.redirectedToDoor)
            return;

        if (routeStep.failed)
        {
            RememberLocation(targetPosition);
            LogMovementFailure("CurrentTarget", targetPosition, routeStep.failureKind, routeStep.recoveryResult);
            Narrate("Something is blocking this route. I'll search for another path.", "move-target-stalled-fallback");
            HandleNeedActionFailure();
            return;
        }

        if (routeStep.arrived)
        {
            ClearMovementGoal();
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
            MovementRouteStepResult comfortRouteStep = ExecuteMovementRouteStep(currentComfortZoneTarget.lastKnownPosition, rememberedComfortZoneStopDistance, "RememberedComfortZone");
            if (comfortRouteStep.redirectedToDoor)
                return;

            if (comfortRouteStep.failed)
            {
                RememberLocation(currentComfortZoneTarget.lastKnownPosition);
                LogMovementFailure("RememberedComfortZone", currentComfortZoneTarget.lastKnownPosition, comfortRouteStep.failureKind, comfortRouteStep.recoveryResult);
                Narrate("I can't reach that lit area from here. I'll find another option.", "move-remembered-comfort-stalled-fallback");
                currentComfortZoneTarget = null;
                HandleNeedActionFailure();
                return;
            }

            if (comfortRouteStep.arrived)
            {
                ClearMovementGoal();
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
        MovementRouteStepResult rememberedRouteStep = ExecuteMovementRouteStep(currentMemoryTarget.lastKnownPosition, rememberedTargetStopDistance, "RememberedTarget");
        if (rememberedRouteStep.redirectedToDoor)
            return;

        if (rememberedRouteStep.failed)
        {
            RememberLocation(currentMemoryTarget.lastKnownPosition);
            LogMovementFailure("RememberedTarget", currentMemoryTarget.lastKnownPosition, rememberedRouteStep.failureKind, rememberedRouteStep.recoveryResult);
            Narrate("This route is blocked. I'll try a different lead.", "move-remembered-target-stalled-fallback");
            currentMemoryTarget = null;
            currentTarget = null;
            HandleNeedActionFailure();
            return;
        }

        if (rememberedRouteStep.arrived)
        {
            ClearMovementGoal();
            if (currentTarget != null && currentTarget.CanInteract(gameObject) && CanSeeInteractable(currentTarget))
            {
                Narrate("There it is.", "move-remembered-found-target");
                ChangeState(AIState.MoveToTarget);
                return;
            }

            Narrate("Nothing here... I need to try something else.", "move-remembered-not-found");
            ForgetRememberedInteractable(currentMemoryTarget);
            currentMemoryTarget = null;
            currentTarget = null;
            HandleNeedActionFailure();
        }
    }

    private void InteractWithCurrentTarget()
    {
        ResetStallTimer();
        Interactable interactedTarget = currentTarget;

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
            if (currentTarget is IActivityInteractable activityTarget)
            {
                if (activityTarget.BeginActivity(gameObject))
                {
                    currentActivityInteractable = activityTarget;
                    currentActivityElapsed = 0f;
                    float minDuration = activityTarget.GetMinimumUseDuration();
                    float maxDuration = activityTarget.GetMaximumUseDuration();
                    currentActivityDuration = Random.Range(minDuration, Mathf.Max(minDuration, maxDuration));
                    hasCurrentActivityLookPoint = false;
                    currentActivityLookTimer = 0f;

                    Vector3 activityAnchor = activityTarget.GetActivityAnchorPoint();
                    float anchorDistance = Vector3.Distance(transform.position, activityAnchor);
                    if (anchorDistance > 0.2f)
                    {
                        agent.Warp(activityAnchor);
                    }

                    Narrate("I'll spend a bit of time here.", "activity-start");
                    ChangeState(AIState.PerformingActivity);
                    return;
                }
            }

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

                RestInteractable restTarget = currentTarget as RestInteractable;
                if (restTarget != null && currentNeedType == restTarget.GetNeedType())
                {
                    if (restTarget.HasActiveSession(gameObject))
                    {
                        currentRestInteractable = restTarget;
                        currentRestSessionElapsed = 0f;
                        currentRestMinimumDuration = restTarget.MinimumRestDuration;
                        currentRestMaximumDuration = restTarget.MaximumRestDuration;

                        currentRestAnchorPosition = restTarget.RestAnchorPosition;
                        hasRestAnchor = true;
                        currentRestHoldTimer = restTarget.MinimumHoldTime;

                        if (agent.hasPath)
                            agent.ResetPath();

                        float anchorDistance = Vector3.Distance(transform.position, currentRestAnchorPosition);
                        if (anchorDistance > restTarget.AnchorSnapDistance)
                        {
                            agent.Warp(currentRestAnchorPosition);
                        }

                        Narrate("I'll rest here for a bit.", "rest-session-begin");
                        ChangeState(AIState.Resting);
                        return;
                    }
                }

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

        if (IsCurrentTargetMatchingTopSubgoalKey(interactedTarget) && TryProcessTopSubgoal())
            return;

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

private void HandleRestingState()
{
    ResetStallTimer();
    agent.ResetPath();

    if (currentRestInteractable == null)
    {
        Narrate("I can't rest here anymore. I'll look elsewhere.", "rest-missing-interactable");
        HandleNeedActionFailure();
        return;
    }

    if (!currentRestInteractable.isEnabled || !currentRestInteractable.CanInteract(gameObject))
    {
        Narrate("This resting spot is no longer available.", "rest-interactable-unavailable");
        StopRestingSession();
        HandleNeedActionFailure();
        return;
    }

    NeedType restNeedType = currentRestInteractable.GetNeedType();

    // Keep the active need aligned with the thing we're resting for.
    currentNeedType = restNeedType;

    // Hold the NPC at the anchor while resting.
    if (hasRestAnchor)
    {
        float anchorDistance = Vector3.Distance(transform.position, currentRestAnchorPosition);

        if (anchorDistance > 0.15f)
        {
            agent.Warp(currentRestAnchorPosition);
        }
    }

    currentRestSessionElapsed += Time.deltaTime;
    currentRestHoldTimer -= Time.deltaTime;

    float recoveredThisTick = currentRestInteractable.RecoverForSeconds(gameObject, Time.deltaTime);
    if (recoveredThisTick > 0f)
    {
        needsManager.ModifyNeed(restNeedType, recoveredThisTick);
    }

    bool minimumDurationSatisfied = currentRestSessionElapsed >= currentRestMinimumDuration;
    bool holdSatisfied = currentRestHoldTimer <= 0f;
    bool maximumDurationReached = currentRestMaximumDuration > 0f && currentRestSessionElapsed >= currentRestMaximumDuration;

    bool needRecoveredEnough = currentNeedActionIsUrgentDriven
        ? !needsManager.IsNeedUrgent(restNeedType)
        : !needsManager.ShouldOpportunisticallySatisfy(restNeedType);

    bool sessionCapReached = currentRestInteractable.IsSessionExhausted();

    bool shouldExitRest = maximumDurationReached ||
                          (minimumDurationSatisfied && holdSatisfied && needRecoveredEnough) ||
                          (minimumDurationSatisfied && holdSatisfied && sessionCapReached);

    if (!shouldExitRest)
        return;

    if (maximumDurationReached)
        Narrate("That's enough rest for now. Time to move again.", "rest-complete-max-duration");
    else if (needRecoveredEnough)
        Narrate("That rest helped. I can keep going.", "rest-complete-satisfied");
    else
        Narrate("I've gotten all I can from this spot.", "rest-complete-cap-reached");

    StopRestingSession();

    currentTarget = null;
    currentMemoryTarget = null;
    currentComfortZoneTarget = null;
    hasExplorePoint = false;
    hasIdlePoint = false;

    if (currentNeedActionIsUrgentDriven && HasUrgentNeed())
        ChangeState(AIState.Explore);
    else
        ChangeState(AIState.IdleWander);
}

    private void HandlePerformingActivityState()
    {
        ResetStallTimer();
        agent.ResetPath();

        if (currentActivityInteractable == null)
        {
            FinishActivitySession(false);
            return;
        }

        Interactable activityInteractable = currentActivityInteractable as Interactable;
        if (activityInteractable == null || !activityInteractable.isEnabled || !activityInteractable.CanInteract(gameObject))
        {
            Narrate("This activity isn't available anymore.", "activity-unavailable");
            FinishActivitySession(false);
            return;
        }

        if (HasUrgentNeed() && currentActivityInteractable.IsInterruptible())
        {
            Narrate("I need to handle something urgent first.", "activity-interrupted-urgent");
            FinishActivitySession(true);
            RestartNeedSearch();
            return;
        }

        Vector3 anchorPosition = currentActivityInteractable.GetActivityAnchorPoint();
        if (Vector3.Distance(transform.position, anchorPosition) > 0.15f)
        {
            agent.Warp(anchorPosition);
        }

        UpdateObservationZoneLookBehavior();

        currentActivityElapsed += Time.deltaTime;
        if (currentActivityElapsed < currentActivityDuration)
            return;

        Narrate("That was enough downtime. Back to wandering.", "activity-complete");
        FinishActivitySession(true);
        ChangeState(AIState.IdleWander);
    }

    private void UpdateObservationZoneLookBehavior()
    {
        ObservationZoneInteractable observationZone = currentActivityInteractable as ObservationZoneInteractable;
        if (observationZone == null)
            return;

        if (!observationZone.IsInsideZone(transform.position))
        {
            Vector3 anchor = observationZone.GetActivityAnchorPoint();
            if (Vector3.Distance(transform.position, anchor) > 0.15f)
                agent.Warp(anchor);
        }

        currentActivityLookTimer -= Time.deltaTime;
        if (currentActivityLookTimer > 0f && hasCurrentActivityLookPoint)
        {
            FaceTowardsPoint(currentActivityLookPoint);
            return;
        }

        if (observationZone.TryGetNextLookPoint(out Vector3 nextLookPoint))
        {
            currentActivityLookPoint = nextLookPoint;
            hasCurrentActivityLookPoint = true;
            currentActivityLookTimer = observationZone.GetNextLookDwellDuration();
            FaceTowardsPoint(currentActivityLookPoint);
            return;
        }

        hasCurrentActivityLookPoint = false;
        currentActivityLookTimer = 0f;
    }

    private InventoryUseAttempt TryUseInventoryItemForNeed(NeedType needType)
    {
        if (npcInventory == null || !npcInventory.HasItemForNeed(needType))
            return InventoryUseAttempt.None;

        Interactable handItem = npcInventory.GetHandItem();
        INeedSatisfier handSatisfier = handItem as INeedSatisfier;

        // If correct item is already in hand, wait until ready.
        if (handSatisfier != null && handSatisfier.GetNeedType() == needType)
        {
            if (!IsPreparedHandItemReady(handItem))
                return InventoryUseAttempt.WaitingForHandReady;

            if (!handItem.CanInteract(gameObject))
                return InventoryUseAttempt.None;

            handItem.Interact(gameObject);
            SchedulePostUsePocketingIfNeeded(handItem);
            return InventoryUseAttempt.Used;
        }

        // Otherwise fetch best item from inventory.
        Interactable bestItem = npcInventory.GetBestItemForNeed(needType);
        if (bestItem == null)
            return InventoryUseAttempt.None;

        bool readyNow = TryPrepareItemInHand(bestItem);

        // Freshly moved into hand or still waiting.
        if (!readyNow)
        {
            if (npcInventory.GetHandItem() == bestItem)
                return InventoryUseAttempt.WaitingForHandReady;

            return InventoryUseAttempt.None;
        }

        // Safety: now it should be in hand and ready.
        Interactable preparedHandItem = npcInventory.GetHandItem();
        if (!IsPreparedHandItemReady(preparedHandItem) || !preparedHandItem.CanInteract(gameObject))
            return InventoryUseAttempt.None;

        preparedHandItem.Interact(gameObject);
        SchedulePostUsePocketingIfNeeded(preparedHandItem);
        return InventoryUseAttempt.Used;
    }
    private void ValidateRuntimeSetup()
    {
        if (!logSetupValidation)
            return;

        if (eyePoint == null)
            Debug.LogWarning($"{name}: eyePoint is not assigned. Vision will use fallback head position.");

        if (interactableLayer.value == 0)
            Debug.LogWarning($"{name}: interactableLayer is empty. Visible interactable/key detection will fail.");

        if (doorLayer.value == 0)
            Debug.LogWarning($"{name}: doorLayer is empty. Door probing will fall back to interactableLayer.");

        if (npcInventory == null)
            Debug.LogWarning($"{name}: NPCInventory missing. Key storage and inventory item usage paths are disabled.");

        Collider selfCollider = GetComponent<Collider>();
        if (selfCollider == null || !selfCollider.isTrigger)
            Debug.LogWarning($"{name}: NPC should have a trigger collider for room overlap tracking (OnTriggerEnter/Exit).");

        DoorInteractable[] doors = FindObjectsByType<DoorInteractable>(FindObjectsSortMode.None);
        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] != null && doors[i].GetDoorController() == null)
                Debug.LogWarning($"{name}: DoorInteractable '{doors[i].name}' has no DoorController in parent chain.");
        }

        Interactable[] interactables = FindObjectsByType<Interactable>(FindObjectsSortMode.None);
        for (int i = 0; i < interactables.Length; i++)
        {
            Interactable interactable = interactables[i];
            if (interactable == null)
                continue;

            if (interactable is IPickupable)
            {
                Collider pickupCollider = interactable.GetComponentInChildren<Collider>();
                if (pickupCollider == null)
                    Debug.LogWarning($"{name}: Pickupable '{interactable.name}' is missing a collider.");
            }
        }
    }

    private void RunInvariantChecks()
    {
        if (!enableInvariantChecks)
            return;

        invariantCheckTimer -= Time.deltaTime;
        if (invariantCheckTimer > 0f)
            return;

        invariantCheckTimer = Mathf.Max(0.2f, invariantCheckInterval);
        CheckPrimaryTargetInvariant();
        CheckRememberedStateInvariant();
        CheckPendingDoorInvariant();
        CheckCurrentTargetValidityInvariant();
    }

    private void CheckPrimaryTargetInvariant()
    {
        int activeModes = 0;
        if (currentTarget != null) activeModes++;
        if (currentMemoryTarget != null) activeModes++;
        if (currentComfortZoneTarget != null) activeModes++;

        bool bridgeRememberedInteractable = currentTarget != null &&
                                            currentMemoryTarget != null &&
                                            currentMemoryTarget.interactable == currentTarget &&
                                            currentComfortZoneTarget == null;

        if (activeModes > 1 && !bridgeRememberedInteractable)
            WarnInvariant("Invariant: conflicting target modes active (currentTarget/currentMemoryTarget/currentComfortZoneTarget).");
    }

    private void CheckRememberedStateInvariant()
    {
        if (currentState != AIState.MoveToRememberedTarget)
            return;

        if (currentMemoryTarget == null && currentComfortZoneTarget == null)
            WarnInvariant("Invariant: MoveToRememberedTarget has no remembered target context.");
    }

    private void CheckPendingDoorInvariant()
    {
        if (pendingDoorTarget == null)
            return;

        if (pendingDoorTarget.GetDoorController() == null)
        {
            WarnInvariant("Invariant: pendingDoorTarget has no DoorController. Clearing pending door state.");
            ResetPendingDoorState();
        }
    }

    private void CheckCurrentTargetValidityInvariant()
    {
        if (currentTarget == null)
            return;

        if (!currentTarget.isEnabled)
        {
            WarnInvariant("Invariant: currentTarget is disabled. Clearing current target context.");
            currentTarget = null;
            if (currentMemoryTarget != null && currentMemoryTarget.interactable == null)
                currentMemoryTarget = null;
        }
    }

    private void WarnInvariant(string message)
    {
        if (Time.time < lastInvariantWarningTime + InvariantWarningCooldown)
            return;

        lastInvariantWarningTime = Time.time;
        Debug.LogWarning($"{name}: {message}");
    }

    private void AbortCurrentNeedAction()
    {
        DebugAbort("AbortCurrentNeedAction");
        ClearAllSubgoals("aborted current need action");

        StopRestingSession();
        FinishActivitySession(true);
        ResetStallTimer();
        ClearMovementGoal();
        currentTarget = null;
        currentMemoryTarget = null;
        currentComfortZoneTarget = null;
        currentNeedActionIsUrgentDriven = false;
        ResetPendingDoorState();
        currentExploreDoorRouteAttempts = 0;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();
        ChangeState(AIState.IdleWander);
    }

    private void RestartNeedSearch()
    {
        ClearAllSubgoals("priority switch restart");
        StopRestingSession();
        FinishActivitySession(true);
        ResetStallTimer();
        ClearMovementGoal();
        currentTarget = null;
        currentMemoryTarget = null;
        currentComfortZoneTarget = null;
        currentNeedActionIsUrgentDriven = true;
        ResetPendingDoorState();
        currentExploreDoorRouteAttempts = 0;
        hasExplorePoint = false;
        hasIdlePoint = false;
        agent.ResetPath();
        ChangeState(AIState.Explore);
    }

    private void StopRestingSession()
    {
        if (currentRestInteractable != null)
        {
            currentRestInteractable.EndRestSession(gameObject);
        }

        currentRestInteractable = null;
        currentRestSessionElapsed = 0f;
        currentRestMinimumDuration = 0f;
        currentRestMaximumDuration = 0f;

        hasRestAnchor = false;
        currentRestAnchorPosition = Vector3.zero;
        currentRestHoldTimer = 0f;
    }

    private void FinishActivitySession(bool clearTarget)
    {
        if (currentActivityInteractable != null)
            currentActivityInteractable.EndActivity(gameObject);

        currentActivityInteractable = null;
        currentActivityElapsed = 0f;
        currentActivityDuration = 0f;
        hasCurrentActivityLookPoint = false;
        currentActivityLookTimer = 0f;

        if (!clearTarget)
            return;

        currentTarget = null;
        currentMemoryTarget = null;
        currentComfortZoneTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
    }

    private void CommitToRestAnchor(bool forceSnap)
    {
        if (currentRestInteractable == null)
            return;

        Transform anchor = currentRestAnchor != null ? currentRestAnchor : currentRestInteractable.RestAnchor;
        if (anchor == null)
            return;

        Vector3 anchorPosition = anchor.position;
        float distanceToAnchor = Vector3.Distance(transform.position, anchorPosition);
        float snapDistance = Mathf.Max(0.01f, currentRestAnchorSnapDistance);
        if (forceSnap || distanceToAnchor > snapDistance)
        {
            if (!agent.Warp(anchorPosition))
                transform.position = anchorPosition;
        }
        else
        {
            transform.position = anchorPosition;
        }

        if (agent.hasPath)
            agent.ResetPath();
    }

    private float GetNeedMoveSpeed()
    {
        if (needsManager == null)
            return needMoveSpeed;

        return needMoveSpeed * needsManager.GetNeedMoveSpeedMultiplier(currentNeedType);
    }

    private void HandleNeedActionFailure()
    {
        ClearMovementGoal();
        ResetPendingDoorState();
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
        ClearMovementGoal();

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

            case AIState.Resting:
            case AIState.PerformingActivity:
                if (agent.hasPath)
                    agent.ResetPath();
                break;
        }

        Narrate(GetStateTransitionThought(previousState, currentState), "state-change-" + previousState + "-" + currentState);
    }

    private void RememberInteractable(Interactable interactable, NeedType needType, bool isActivity = false, ActivityType activityType = ActivityType.Generic)
    {
        NpcMemoryService.MemoryWriteResult writeResult = memoryService.RememberInteractable(memory, interactable, needType, Time.time, isActivity, activityType);
        NarrateInteractableMemoryWrite(interactable, writeResult);
    }

    private void NarrateInteractableMemoryWrite(Interactable interactable, NpcMemoryService.MemoryWriteResult writeResult)
    {
        if (interactable == null)
            return;

        if (writeResult == NpcMemoryService.MemoryWriteResult.Updated)
            Narrate("I've seen this before.", "memory-update-interactable-" + interactable.GetInstanceID());
        else if (writeResult == NpcMemoryService.MemoryWriteResult.Added)
            Narrate("I'll remember this for later.", "memory-new-interactable-" + interactable.GetInstanceID());
    }

    private void CleanupInteractableMemory()
    {
        memoryService.CleanupInteractableMemory(memory, interactableMemoryDuration, Time.time);
    }

    private void RememberLocation(Vector3 position)
    {
        memoryService.RememberLocation(rememberedLocations, position, Time.time);
    }

    private void CleanupLocationMemory()
    {
        memoryService.CleanupLocationMemory(rememberedLocations, locationMemoryDuration, Time.time);
    }

    private void CleanupLockedDoorMemory()
    {
        memoryService.CleanupLockedDoorMemory(rememberedLockedDoors, lockedDoorMemoryDuration, Time.time);
    }

    private void RememberLockedDoor(DoorInteractable door)
    {
        NpcMemoryService.LockedDoorMemoryWriteResult writeResult = memoryService.RememberLockedDoor(rememberedLockedDoors, door, Time.time);
        if (writeResult == NpcMemoryService.LockedDoorMemoryWriteResult.Added)
            Narrate("I can't open that. I'll remember it.", "memory-locked-door-" + door.GetInstanceID());
    }

    private void ForgetRememberedInteractable(RememberedInteractable remembered)
    {
        memoryService.ForgetRememberedInteractable(memory, remembered);
    }

    private bool IsKeyUsefulForRememberedLockedDoor(IKeyItem keyItem)
    {
        return memoryService.IsKeyUsefulForRememberedLockedDoor(rememberedLockedDoors, keyItem);
    }

    private bool IsRecentlyVisited(Vector3 position)
    {
        return memoryService.IsRecentlyVisited(rememberedLocations, position, locationAvoidanceRadius);
    }

    private bool CanSeeInteractable(Interactable interactable)
    {
        return perceptionService.CanSeeInteractable(transform, transform.forward, interactable);
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

        DoorUnlockAttempt unlockAttempt = TryUseInventoryKeyOnDoor(door);
        if (unlockAttempt == DoorUnlockAttempt.Unlocked)
        {
            door.Interact(gameObject);
            return controller.IsOpen;
        }

        if (unlockAttempt == DoorUnlockAttempt.WaitingForHandReady)
            return false;

        if (controller.IsLocked)
            RememberLockedDoor(door);

        return false;
    }

    private DoorUnlockAttempt TryUseInventoryKeyOnDoor(DoorInteractable door)
    {
        if (door == null || npcInventory == null)
            return DoorUnlockAttempt.Failed;

        DoorController controller = door.GetDoorController();
        if (controller == null || !controller.IsLocked)
            return DoorUnlockAttempt.Failed;

        if (!npcInventory.TryGetMatchingKey(controller.RequiredKeyId, out IKeyItem key))
            return DoorUnlockAttempt.Failed;

        Interactable keyInteractable = key as Interactable;
        bool waitingForHandReady;
        if (!TryPrepareKeyForDoorUse(keyInteractable, out waitingForHandReady))
        {
            if (waitingForHandReady)
                return DoorUnlockAttempt.WaitingForHandReady;
            return DoorUnlockAttempt.Failed;
        }

        IKeyItem heldKey = npcInventory.GetHandItem() as IKeyItem;
        if (heldKey == null)
            return DoorUnlockAttempt.Failed;

        bool unlocked = controller.TryUnlock(heldKey.GetKeyId());
        if (unlocked)
        {
            SchedulePostUsePocketingIfNeeded(npcInventory.GetHandItem());
            Narrate("Good thing I kept this key. That lock is open now.", "door-unlock-success");
            return DoorUnlockAttempt.Unlocked;
        }

        return DoorUnlockAttempt.Failed;
    }

    private bool TryPrepareKeyForDoorUse(Interactable keyInteractable, out bool waitingForHandReady)
    {
        waitingForHandReady = false;

        if (keyInteractable == null || npcInventory == null)
            return false;

        if (npcInventory.GetHandItem() == keyInteractable)
        {
            if (!IsPreparedHandItemReady(keyInteractable))
            {
                if (activePreparedHandUseItem != keyInteractable)
                    MarkHandItemDrawnForUse(keyInteractable);

                waitingForHandReady = true;
                return false;
            }

            return true;
        }

        if (!TryPrepareItemInHand(keyInteractable))
        {
            if (npcInventory.GetHandItem() == keyInteractable && !IsPreparedHandItemReady(keyInteractable))
                waitingForHandReady = true;

            return false;
        }

        return true;
    }

    private bool TryPrepareItemInHand(Interactable desiredItem)
    {
        if (desiredItem == null || npcInventory == null)
            return false;

        // Already holding the correct item: only usable once delay has passed.
        if (npcInventory.GetHandItem() == desiredItem)
        {
            if (activePreparedHandUseItem != desiredItem)
                MarkHandItemDrawnForUse(desiredItem);

            return IsPreparedHandItemReady(desiredItem);
        }

        // Still in a blocked/transition window.
        if (!IsHandItemReadyToUse())
            return false;

        bool movedToHand = false;

        if (npcInventory.HasHandItem)
            movedToHand = npcInventory.TrySwapHandItemWithInventoryItem(desiredItem);
        else
            movedToHand = npcInventory.TryMoveInventoryItemToHand(desiredItem) || npcInventory.TrySetHandItem(desiredItem);

        if (!movedToHand)
            return false;

        // Important: after moving it into hand, do NOT allow instant use.
        MarkHandItemDrawnForUse(desiredItem);
        CancelPendingPostUsePocketing(desiredItem);
        return false;
    }
    private void HandleHandItemPresentation()
    {
        if (npcInventory == null)
            return;

        Interactable currentHandItem = npcInventory.GetHandItem();

        if (currentHandItem != trackedHandItem)
        {
            trackedHandItem = currentHandItem;
            trackedHandItemSinceTime = Time.time;
            if (currentHandItem != activePreparedHandUseItem)
                activePreparedHandUseItem = null;
        }

        ProcessPendingPostUsePocketing(currentHandItem);

        if (currentState != AIState.IdleWander)
            return;

        if (currentHandItem == null || currentHandItem != trackedHandItem)
            return;

        if (pendingPostUsePocketItem == currentHandItem)
            return;

        if (!IsHandItemReadyToUse())
            return;

        if (Time.time - trackedHandItemSinceTime < Mathf.Max(0f, idleHandPocketDelay))
            return;

        npcInventory.TryMoveHandItemToInventory();
    }

    private void SchedulePostUsePocketingIfNeeded(Interactable usedItem)
    {
        if (npcInventory == null || usedItem == null)
            return;

        if (npcInventory.GetHandItem() != usedItem)
            return;

        float holdDuration = Mathf.Max(0f, handPostUseVisibleDelay);
        pendingPostUsePocketItem = usedItem;
        pendingPostUsePocketTime = Time.time + holdDuration;
        handItemReadyToUseTime = Mathf.Max(handItemReadyToUseTime, pendingPostUsePocketTime);
        activePreparedHandUseItem = null;
    }

    private void ProcessPendingPostUsePocketing(Interactable currentHandItem)
    {
        if (pendingPostUsePocketItem == null || npcInventory == null)
            return;

        if (currentHandItem != pendingPostUsePocketItem)
        {
            pendingPostUsePocketItem = null;
            pendingPostUsePocketTime = -1f;
            return;
        }

        if (Time.time < pendingPostUsePocketTime || !IsHandItemReadyToUse())
            return;

        bool pocketed = npcInventory.TryMoveHandItemToInventory();
        if (pocketed || npcInventory.GetHandItem() != pendingPostUsePocketItem)
        {
            pendingPostUsePocketItem = null;
            pendingPostUsePocketTime = -1f;
            activePreparedHandUseItem = null;
        }
    }

    private void CancelPendingPostUsePocketing(Interactable item)
    {
        if (item == null)
            return;

        if (pendingPostUsePocketItem != item)
            return;

        pendingPostUsePocketItem = null;
        pendingPostUsePocketTime = -1f;
    }

    private void MarkHandItemDrawnForUse(Interactable item)
    {
        activePreparedHandUseItem = item;
        handItemReadyToUseTime = Time.time + Mathf.Max(1f, minimumHandUseDelay);
    }

    private bool IsPreparedHandItemReady(Interactable item)
    {
        if (item == null || npcInventory == null)
            return false;

        if (npcInventory.GetHandItem() != item)
            return false;

        if (activePreparedHandUseItem != item)
            return false;

        return IsHandItemReadyToUse();
    }

    private bool IsHandItemReadyToUse()
    {
        return Time.time >= handItemReadyToUseTime;
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

                case NeedType.Energy:
                    if (currentBand == NeedsManager.NeedUrgencyBand.Critical)
                        return "I'm exhausted. I need to rest now.";
                    return "I'm getting tired.";

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

            case AIState.Resting:
                return Pick(
                    "Time to recover for a moment.",
                    "I'll rest until I feel better."
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

            bool hasVisibleOpportunity = HasEasyVisibleOpportunity(needType);
            bool hasRememberedOpportunity = HasEasyRememberedOpportunity(needType, opportunisticTargetMaxDistance);

            if (!hasVisibleOpportunity && !hasRememberedOpportunity)
            {
                DebugFlow("Opportunistic check: rejected " + needType + " because no easy visible or remembered opportunity was found within range " + opportunisticTargetMaxDistance);
                continue;
            }

            float score = needsManager.GetNeedPriorityScore(needType);
            DebugFlow("Opportunistic check: " + needType + " has opportunistic opportunity with priority score " + score);

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

        if (TryUseRememberedTarget(bestNeed, opportunisticTargetMaxDistance))
        {
            DebugFlow("Opportunistic action: remembered target acquisition succeeded for need " + bestNeed);
            Narrate("I remember a nearby spot that can help.", "opportunity-remembered-interactable");
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
            RememberLocation(targetPosition);
            ClearActiveTargets();
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

    private bool HasEasyRememberedOpportunity(NeedType needType, float maxDistance)
    {
        return memoryService.HasRememberedOpportunity(memory, needType, gameObject, transform.position, maxDistance);
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
        {
            RememberLocation(targetPosition);
            ClearActiveTargets();
            return false;
        }

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

            RestInteractable restInteractable = interactable as RestInteractable;
            float candidateScore = restInteractable != null
                ? restInteractable.Desirability / (1f + distance)
                : -distance;
            float bestScore = bestTarget is RestInteractable bestRestInteractable
                ? bestRestInteractable.Desirability / (1f + bestDistance)
                : -bestDistance;

            bool isBetterCandidate = bestTarget == null ||
                                     candidateScore > bestScore ||
                                     (Mathf.Approximately(candidateScore, bestScore) && distance < bestDistance);

            if (!isBetterCandidate)
            {
                DebugFlow($"[FindTarget] Rejected {interactable.name}: lower desirability-distance score ({candidateScore} <= {bestScore})");
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

    private NpcRouteCoordinator.MovementRecoverySettings GetMovementRecoverySettings()
    {
        return new NpcRouteCoordinator.MovementRecoverySettings
        {
            stallVelocityThreshold = stallVelocityThreshold,
            stallTimeThreshold = stallTimeThreshold,
            movementProgressDistanceThreshold = movementProgressDistanceThreshold,
            movementProgressPathDistanceThreshold = movementProgressPathDistanceThreshold,
            movementNoProgressTimeout = movementNoProgressTimeout,
            movementGoalTimeout = movementGoalTimeout,
            maxMovementRecoveryAttempts = maxMovementRecoveryAttempts,
            movementRecoverySampleRadius = movementRecoverySampleRadius,
            movementRecoveryUnstuckRadius = movementRecoveryUnstuckRadius,
            movementPointClearanceRadius = movementPointClearanceRadius,
            movementPathFailureLimit = movementPathFailureLimit
        };
    }

    private void BeginMovementGoal(Vector3 target)
    {
        routeCoordinator.BeginMovementGoal(target, transform.position, Time.time);
    }

    private void MarkMovementProgress(string reason)
    {
        routeCoordinator.MarkMovementProgress(reason, Time.time);
    }

    private bool HasPointClearance(Vector3 point)
    {
        return routeCoordinator.HasPointClearance(point, obstacleLayer, movementPointClearanceRadius);
    }

    private void ResetStallTimer()
    {
        routeCoordinator.ResetStallTimer();
    }

    private void ClearMovementGoal()
    {
        routeCoordinator.ClearMovementGoal();
    }

    private void DebugMovement(string message)
    {
        routeCoordinator.DebugMovement(message);
    }

    private NpcRouteCoordinator.MovementRecoveryResult EvaluateMovementRecovery(Vector3 target, string contextTag)
    {
        return routeCoordinator.TryRecoverMovementGoal(
            agent,
            transform.position,
            target,
            Time.time,
            Time.deltaTime,
            GetMovementRecoverySettings(),
            obstacleLayer,
            IsPathReachable,
            IsMovementRecoveryPointUsable,
            contextTag);
    }

    private MovementRouteStepResult ExecuteMovementRouteStep(Vector3 target, float stopDistance, string contextTag)
    {
        MovementRouteStepResult result = default;

        BeginMovementGoal(target);
        agent.SetDestination(target);

        if (TryRedirectPartialPathToPendingDoor(target, contextTag))
        {
            result.redirectedToDoor = true;
            return result;
        }

        result.recoveryResult = EvaluateMovementRecovery(target, contextTag);
        if (result.recoveryResult.failed)
        {
            result.failed = true;
            result.failureKind = ClassifyMovementFailure(target, result.recoveryResult, UsesStrictPointQuality(contextTag));

            if (result.failureKind == MovementFailureKind.BlockedByDoor)
            {
                if (TryHandleDoorForDestination(target))
                    result.failed = false;
            }
            else if (result.failureKind == MovementFailureKind.BlockedByObstacle && TryAcquireKeyForBlockingDoorOnFailure(target))
            {
                result.failed = false;
            }

            return result;
        }

        result.arrived = !agent.pathPending && agent.remainingDistance <= stopDistance;
        return result;
    }

    private bool UsesStrictPointQuality(string contextTag)
    {
        return contextTag == "ExplorePoint" ||
               contextTag == "IdleWander" ||
               contextTag == "RememberedComfortZone";
    }

    private bool IsMovementRecoveryPointUsable(Vector3 point)
    {
        return IsMovementPointUsable(point);
    }

    private MovementFailureKind ClassifyMovementFailure(
        Vector3 target,
        NpcRouteCoordinator.MovementRecoveryResult recoveryResult,
        bool useStrictPointQuality)
    {
        if (TryGetBlockingDoorTowards(target, out _))
            return MovementFailureKind.BlockedByDoor;

        if (recoveryResult.trigger == NpcRouteCoordinator.MovementRecoveryTrigger.StallTimeout ||
            recoveryResult.trigger == NpcRouteCoordinator.MovementRecoveryTrigger.NoProgressTimeout ||
            recoveryResult.trigger == NpcRouteCoordinator.MovementRecoveryTrigger.GoalTimeout)
        {
            return MovementFailureKind.NoProgressDeadlock;
        }

        if (useStrictPointQuality && !IsMovementPointUsable(target))
            return MovementFailureKind.BadChosenPoint;

        if (recoveryResult.pathStatus == NavMeshPathStatus.PathInvalid)
            return useStrictPointQuality ? MovementFailureKind.BadChosenPoint : MovementFailureKind.BlockedByObstacle;

        if (recoveryResult.pathStatus == NavMeshPathStatus.PathPartial)
            return useStrictPointQuality ? MovementFailureKind.BadChosenPoint : MovementFailureKind.BlockedByObstacle;

        return MovementFailureKind.BlockedByObstacle;
    }

    private bool TryAcquireKeyForBlockingDoorOnFailure(Vector3 target)
    {
        if (!TryGetBlockingDoorTowards(target, out DoorInteractable stalledDoor))
            return false;

        DoorController stalledController = stalledDoor?.GetDoorController();
        if (stalledController == null || !stalledController.IsLocked || HasMatchingInventoryKey(stalledController))
            return false;

        return TryAcquireMatchingKeyForLockedDoor(stalledController.RequiredKeyId);
    }

    private void LogMovementFailure(
        string contextTag,
        Vector3 target,
        MovementFailureKind failureKind,
        NpcRouteCoordinator.MovementRecoveryResult recoveryResult)
    {
        DebugMovement(
            $"[{contextTag}] Route failure classified as {failureKind} @ {target} | trigger={recoveryResult.trigger} | action={recoveryResult.action} | path={recoveryResult.pathStatus}");
    }

    private bool TryGetBlockingDoorTowards(Vector3 destination, out DoorInteractable door)
    {
        return perceptionService.TryGetBlockingDoorTowards(transform, destination, gameObject, out door, doorCheckDistance);
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

        if (controller.IsOpen)
        {
            if (routeCoordinator.TryPeekTopSubgoal(out NpcRouteCoordinator.RouteSubgoal topSubgoal) && topSubgoal.blockedDoor == door)
            {
                routeCoordinator.PopTopSubgoal("blocked door already open");
                TryResumeParentGoalAfterSubgoal();
                return true;
            }

            ResetPendingDoorState();
            agent.ResetPath();
            BeginMovementGoal(destination);
            agent.SetDestination(destination);
            ResetStallTimer();
            return true;
        }

        QueueDoorHandling(door, destination, "blocked-route");
        return true;
    }

    // Stabilized pending-door flow:
    // detect blocking door -> approach door anchor -> unlock/interact -> repath original destination
    // if that route stops making progress, try one local reposition and then fail upward explicitly.
    private bool HandlePendingDoorTarget()
    {
        if (pendingDoorTarget == null)
            return false;

        DoorInteractable door = pendingDoorTarget;
        DoorController controller = door.GetDoorController();
        if (controller == null || !door.isEnabled)
        {
            FailPendingDoorHandling("door became invalid");
            return true;
        }

        if (controller.IsOpen)
        {
            CompletePendingDoorHandling("blocked door opened while pending");
            return true;
        }

        if (!door.CanInteract(gameObject))
        {
            FailPendingDoorHandling("door no longer interactable");
            return true;
        }

        Vector3 doorAnchor = GetPendingDoorAnchorPoint(door);
        UpdatePendingDoorProgress(doorAnchor);

        if (HasPendingDoorTimedOut(controller))
        {
            if (TryStartPendingDoorRecovery(door, doorAnchor, "timeout"))
                return true;

            FailPendingDoorHandling("pending door timed out");
            return true;
        }

        Vector3 approachPoint = GetPendingDoorApproachPoint(door);
        agent.speed = GetActionMoveSpeed();
        BeginMovementGoal(approachPoint);
        agent.SetDestination(approachPoint);

        if (pendingDoorRuntime.phase == PendingDoorPhase.Repositioning && IsAtPendingDoorApproachPoint(approachPoint))
        {
            pendingDoorRuntime.phase = PendingDoorPhase.ApproachingAnchor;
            pendingDoorRuntime.hasRecoveryPoint = false;
            pendingDoorRuntime.lastProgressTime = Time.time;
            MarkMovementProgress("pending-door-reposition-arrived");
            LogPendingDoorInfo($"Pending door recovered by reposition: door={door.name}");
        }

        if (!IsWithinPendingDoorInteractionRange(door, doorAnchor))
            return true;

        FaceTowardsPoint(doorAnchor);

        if (controller.IsLocked)
        {
            if (TryHandleLockedPendingDoor(door, controller))
                return true;
        }

        if (Time.time < lastDoorInteractTime + doorInteractCooldown)
            return true;

        pendingDoorRuntime.lastProgressTime = Time.time;
        pendingDoorTarget.Interact(gameObject);
        lastDoorInteractTime = Time.time;
        MarkMovementProgress("pending-door-interact");
        ResetStallTimer();

        if (controller.IsOpen)
            CompletePendingDoorHandling("pending door interacted open");

        return true;
    }

    private void QueueDoorHandling(DoorInteractable door, Vector3 destination)
    {
        QueueDoorHandling(door, destination, "route");
    }

    private void QueueDoorHandling(DoorInteractable door, Vector3 destination, string reason)
    {
        if (door == null)
            return;

        bool startedNewDoor = pendingDoorTarget != door || pendingDoorRuntime.phase == PendingDoorPhase.None;
        pendingDoorTarget = door;
        pendingDoorDestination = destination;
        hasPendingDoorDestination = true;

        if (startedNewDoor)
        {
            Vector3 doorAnchor = GetPendingDoorAnchorPoint(door);
            pendingDoorRuntime = new PendingDoorRuntime
            {
                phase = PendingDoorPhase.ApproachingAnchor,
                startTime = Time.time,
                bestDistance = Vector3.Distance(transform.position, doorAnchor),
                lastProgressTime = Time.time,
                repositionAttempted = false,
                hasRecoveryPoint = false,
                recoveryPoint = Vector3.zero
            };

            LogPendingDoorInfo($"Pending door started: door={door.name}, reason={reason}");
        }

        agent.ResetPath();
        Vector3 approachPoint = GetPendingDoorApproachPoint(door);
        BeginMovementGoal(approachPoint);
        agent.SetDestination(approachPoint);
        MarkMovementProgress("door-handling-queued");
        ResetStallTimer();
    }

    private void ResetPendingDoorState()
    {
        pendingDoorTarget = null;
        pendingDoorDestination = Vector3.zero;
        hasPendingDoorDestination = false;
        pendingDoorRuntime = default;
    }

    private Vector3 GetPendingDoorAnchorPoint(DoorInteractable door)
    {
        return door != null ? door.GetInteractionPoint() : transform.position;
    }

    private Vector3 GetPendingDoorApproachPoint(DoorInteractable door)
    {
        if (pendingDoorRuntime.hasRecoveryPoint)
            return pendingDoorRuntime.recoveryPoint;

        return GetPendingDoorAnchorPoint(door);
    }

    private void UpdatePendingDoorProgress(Vector3 doorAnchor)
    {
        float currentDistance = Vector3.Distance(transform.position, doorAnchor);
        if (currentDistance + pendingDoorProgressDistanceThreshold >= pendingDoorRuntime.bestDistance)
            return;

        pendingDoorRuntime.bestDistance = currentDistance;
        pendingDoorRuntime.lastProgressTime = Time.time;
        MarkMovementProgress("pending-door-progress");
    }

    private bool HasPendingDoorTimedOut(DoorController controller)
    {
        float elapsed = Time.time - pendingDoorRuntime.startTime;
        if (elapsed >= Mathf.Max(0.1f, pendingDoorMaxDuration))
        {
            LogPendingDoorWarning($"Pending door timed out: door={pendingDoorTarget?.name}, reason=max-duration, elapsed={elapsed:F2}s");
            return true;
        }

        if (ShouldSuspendPendingDoorNoProgressTimeout(controller))
            return false;

        float noProgressElapsed = Time.time - pendingDoorRuntime.lastProgressTime;
        if (noProgressElapsed < Mathf.Max(0.1f, pendingDoorNoProgressTimeout))
            return false;

        LogPendingDoorWarning($"Pending door timed out: door={pendingDoorTarget?.name}, reason=no-progress, elapsed={noProgressElapsed:F2}s");
        return true;
    }

    private bool ShouldSuspendPendingDoorNoProgressTimeout(DoorController controller)
    {
        if (Time.time < lastDoorInteractTime + doorInteractCooldown)
            return true;

        return controller != null && controller.IsLocked && IsPendingDoorWaitingForPreparedKey(controller);
    }

    private bool IsPendingDoorWaitingForPreparedKey(DoorController controller)
    {
        if (controller == null || npcInventory == null || string.IsNullOrWhiteSpace(controller.RequiredKeyId))
            return false;

        if (!npcInventory.TryGetMatchingKey(controller.RequiredKeyId, out IKeyItem matchingKey))
            return false;

        Interactable keyInteractable = matchingKey as Interactable;
        if (keyInteractable == null)
            return false;

        return npcInventory.GetHandItem() == keyInteractable && !IsPreparedHandItemReady(keyInteractable);
    }

    private bool IsAtPendingDoorApproachPoint(Vector3 approachPoint)
    {
        float arrivalDistance = Mathf.Max(0.1f, pendingDoorRecoveryArrivalDistance);
        if (Vector3.Distance(transform.position, approachPoint) <= arrivalDistance)
            return true;

        return !agent.pathPending && agent.hasPath && agent.remainingDistance <= arrivalDistance;
    }

    private bool IsWithinPendingDoorInteractionRange(DoorInteractable door, Vector3 doorAnchor)
    {
        float interactionDistance = Mathf.Max(doorStopDistance, door != null ? door.interactionRange : 0f);
        if (Vector3.Distance(transform.position, doorAnchor) <= interactionDistance)
            return true;

        return !agent.pathPending && agent.hasPath && agent.remainingDistance <= interactionDistance;
    }

    private bool TryFindPendingDoorRecoveryPoint(Vector3 doorAnchor, out Vector3 recoveryPoint)
    {
        float sampleRadius = Mathf.Max(0.2f, pendingDoorRecoverySampleRadius);

        for (int i = 0; i < 8; i++)
        {
            Vector2 offset = Random.insideUnitCircle * sampleRadius;
            Vector3 candidate = doorAnchor + new Vector3(offset.x, 0f, offset.y);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                continue;

            if (Vector3.Distance(hit.position, doorAnchor) <= 0.1f)
                continue;

            if (!IsMovementPointUsable(hit.position))
                continue;

            if (!IsPathReachable(hit.position))
                continue;

            recoveryPoint = hit.position;
            return true;
        }

        recoveryPoint = doorAnchor;
        return false;
    }

    private bool TryStartPendingDoorRecovery(DoorInteractable door, Vector3 doorAnchor, string reason)
    {
        if (door == null || pendingDoorRuntime.repositionAttempted)
            return false;

        if (!TryFindPendingDoorRecoveryPoint(doorAnchor, out Vector3 recoveryPoint))
            return false;

        pendingDoorRuntime.repositionAttempted = true;
        pendingDoorRuntime.phase = PendingDoorPhase.Repositioning;
        pendingDoorRuntime.hasRecoveryPoint = true;
        pendingDoorRuntime.recoveryPoint = recoveryPoint;
        pendingDoorRuntime.lastProgressTime = Time.time;

        agent.ResetPath();
        BeginMovementGoal(recoveryPoint);
        agent.SetDestination(recoveryPoint);
        MarkMovementProgress("pending-door-reposition");
        LogPendingDoorInfo($"Pending door reposition attempt: door={door.name}, reason={reason}");
        return true;
    }

    private bool TryHandleLockedPendingDoor(DoorInteractable door, DoorController controller)
    {
        if (door == null || controller == null)
        {
            FailPendingDoorHandling("locked door data missing");
            return true;
        }

        if (!HasMatchingInventoryKey(controller))
        {
            if (TryEscalatePendingDoorToSubgoal(door, controller, "pending-door-no-key"))
                return true;

            FailPendingDoorHandling("locked door has no reachable key path");
            return true;
        }

        DoorUnlockAttempt unlockAttempt = TryUseInventoryKeyOnDoor(door);
        if (unlockAttempt == DoorUnlockAttempt.WaitingForHandReady)
        {
            pendingDoorRuntime.lastProgressTime = Time.time;
            MarkMovementProgress("pending-door-key-prep");
            return true;
        }

        if (unlockAttempt == DoorUnlockAttempt.Unlocked)
        {
            pendingDoorRuntime.lastProgressTime = Time.time;
            MarkMovementProgress("pending-door-unlocked");
            ResetStallTimer();
            return false;
        }

        if (TryStartPendingDoorRecovery(door, GetPendingDoorAnchorPoint(door), "unlock-failed"))
            return true;

        FailPendingDoorHandling("locked door remained unresolved after key retry");
        return true;
    }

    private bool TryEscalatePendingDoorToSubgoal(DoorInteractable door, DoorController controller, string reason)
    {
        if (door == null || controller == null || string.IsNullOrWhiteSpace(controller.RequiredKeyId))
            return false;

        Vector3 blockedDestination = hasPendingDoorDestination ? pendingDoorDestination : GetPendingDoorAnchorPoint(door);
        bool pushed = routeCoordinator.TryPushLockedDoorSubgoal(
            door,
            controller.RequiredKeyId,
            blockedDestination,
            blockedDestination,
            currentNeedType,
            currentNeedActionIsUrgentDriven,
            reason);

        RememberLockedDoor(door);
        if (pushed)
            LogPendingDoorInfo($"Pending door escalated to subgoal: door={door.name}, key={controller.RequiredKeyId}, reason={reason}");

        ResetPendingDoorState();
        agent.ResetPath();
        ClearMovementGoal();
        ResetStallTimer();
        return TryProcessTopSubgoal();
    }

    private void CompletePendingDoorHandling(string reason)
    {
        DoorInteractable resolvedDoor = pendingDoorTarget;
        bool hadPendingDestination = hasPendingDoorDestination;
        Vector3 repathDestination = pendingDoorDestination;
        bool wasTopSubgoalDoor = routeCoordinator.TryPeekTopSubgoal(out NpcRouteCoordinator.RouteSubgoal topSubgoal) && topSubgoal.blockedDoor == resolvedDoor;

        ResetPendingDoorState();
        agent.ResetPath();
        ClearMovementGoal();
        ResetStallTimer();

        if (wasTopSubgoalDoor)
        {
            routeCoordinator.PopTopSubgoal(reason);
            TryResumeParentGoalAfterSubgoal();
            return;
        }

        if (hadPendingDestination)
        {
            BeginMovementGoal(repathDestination);
            agent.SetDestination(repathDestination);
            return;
        }

        RepathCurrentMovementDestination();
    }

    private void FailPendingDoorHandling(string reason)
    {
        DoorInteractable failedDoor = pendingDoorTarget;
        bool wasTopSubgoalDoor = routeCoordinator.TryPeekTopSubgoal(out NpcRouteCoordinator.RouteSubgoal topSubgoal) && topSubgoal.blockedDoor == failedDoor;

        LogPendingDoorWarning($"Pending door failed completely: door={failedDoor?.name}, reason={reason}, state={currentState}");

        ResetPendingDoorState();
        agent.ResetPath();
        ClearMovementGoal();
        ResetStallTimer();

        if (wasTopSubgoalDoor)
        {
            routeCoordinator.PopTopSubgoal("pending door failure");
            if (TryResumeParentGoalAfterSubgoal())
                return;
        }

        switch (currentState)
        {
            case AIState.Explore:
                if (hasExplorePoint)
                    RememberLocation(currentExplorePoint);
                hasExplorePoint = false;
                exploreTimer = 0f;
                break;

            case AIState.MoveToTarget:
            case AIState.MoveToRememberedTarget:
                HandleNeedActionFailure();
                return;

            default:
                if (HasUrgentNeed())
                    ChangeState(AIState.Explore);
                else
                    ChangeState(AIState.IdleWander);
                break;
        }
    }

    private bool TryRedirectPartialPathToPendingDoor(Vector3 destination, string contextTag)
    {
        if (pendingDoorTarget != null || agent == null || !agent.isOnNavMesh || agent.pathPending || !agent.hasPath)
            return false;

        if (agent.pathStatus == NavMeshPathStatus.PathComplete)
            return false;

        if (!TryGetBlockingDoorTowards(destination, out DoorInteractable blockingDoor))
            return false;

        DebugMovement($"[{contextTag}] Redirecting partial path through door {blockingDoor.name}");
        QueueDoorHandling(blockingDoor, destination, "partial-path");
        return true;
    }

    private void LogPendingDoorInfo(string message)
    {
        Debug.Log($"{name}: {message}");
    }

    private void LogPendingDoorWarning(string message)
    {
        Debug.LogWarning($"{name}: {message}");
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

    private void ClearAllSubgoals(string reason)
    {
        routeCoordinator.ClearAllSubgoals(reason);
    }

    private bool IsCurrentTargetMatchingTopSubgoalKey(Interactable target)
    {
        if (target == null || !routeCoordinator.TryPeekTopSubgoal(out NpcRouteCoordinator.RouteSubgoal subgoal))
            return false;

        if (!target.isEnabled || !target.CanInteract(gameObject))
            return false;

        IKeyItem keyItem = target as IKeyItem;
        return keyItem != null && DoorController.KeyIdsMatch(subgoal.requiredKeyId, keyItem.GetKeyId());
    }

    private bool IsInventoryReadyForTopSubgoal(NpcRouteCoordinator.RouteSubgoal subgoal, out DoorController controller)
    {
        controller = subgoal.blockedDoor != null ? subgoal.blockedDoor.GetDoorController() : null;
        if (controller == null)
            return false;

        if (!controller.IsLocked)
            return true;

        return HasMatchingInventoryKey(controller);
    }

    private bool TryResumeLockedDoorMission()
    {
        if (!routeCoordinator.TryPeekTopSubgoal(out _))
            return false;

        routeCoordinator.PopResolvedSubgoals(out bool poppedResolvedSubgoal);

        if (poppedResolvedSubgoal)
            return TryResumeParentGoalAfterSubgoal();

        return TryProcessTopSubgoal();
    }

    private bool TryProcessTopSubgoal()
    {
        if (!routeCoordinator.TryPeekTopSubgoal(out NpcRouteCoordinator.RouteSubgoal subgoal))
            return false;

        currentNeedType = subgoal.originatingNeedType;
        currentNeedActionIsUrgentDriven = subgoal.originatedFromUrgentNeed;

        if (subgoal.blockedDoor == null || string.IsNullOrWhiteSpace(subgoal.requiredKeyId))
        {
            routeCoordinator.PopTopSubgoal("invalid top subgoal data");
            return TryResumeParentGoalAfterSubgoal();
        }

        if (!subgoal.blockedDoor.isEnabled || !subgoal.blockedDoor.CanInteract(gameObject))
        {
            routeCoordinator.PopTopSubgoal("door no longer interactable");
            return TryResumeParentGoalAfterSubgoal();
        }

        DoorController controller = subgoal.blockedDoor.GetDoorController();
        if (routeCoordinator.IsSubgoalResolved(subgoal, controller))
        {
            routeCoordinator.PopTopSubgoal("top subgoal resolved");
            return TryResumeParentGoalAfterSubgoal();
        }

        if (!IsInventoryReadyForTopSubgoal(subgoal, out controller))
        {
            if (IsCurrentTargetMatchingTopSubgoalKey(currentTarget))
                return true;

            bool foundKey = TryAcquireMatchingKeyForLockedDoor(subgoal.requiredKeyId);
            if (foundKey)
            {
                DebugMovement($"Subgoal key targeted: door={subgoal.blockedDoor.name}, key={subgoal.requiredKeyId}");
                return true;
            }

            if (currentTarget == null)
            {
                routeCoordinator.PopTopSubgoal("no reachable matching key found");
                return TryResumeParentGoalAfterSubgoal();
            }

            return true;
        }

        hasExplorePoint = false;
        hasIdlePoint = false;
        if (currentState == AIState.IdleWander)
            ChangeState(AIState.Explore);

        QueueDoorHandling(subgoal.blockedDoor, subgoal.blockedDestination, "resume-subgoal");
        DebugMovement($"Resuming subgoal for door={subgoal.blockedDoor.name}, depth={routeCoordinator.SubgoalCount}");
        return true;
    }

    private bool TryResumeParentGoalAfterSubgoal()
    {
        if (routeCoordinator.TryPeekTopSubgoal(out NpcRouteCoordinator.RouteSubgoal parentSubgoal))
        {
            DebugMovement($"Returning to parent subgoal for door={parentSubgoal.blockedDoor?.name}");
            return TryProcessTopSubgoal();
        }

        if (TryGetCurrentRouteObjective(out Vector3 destination))
        {
            if (IsPathReachable(destination) || TryHandleDoorForDestination(destination))
            {
                BeginMovementGoal(destination);
                agent.SetDestination(destination);
                DebugMovement($"Subgoal stack cleared. Resuming base route destination {destination}");
                return true;
            }
        }

        if (HasUrgentNeed())
            ChangeState(AIState.Explore);
        else
            ChangeState(AIState.IdleWander);

        return false;
    }

    private bool TryGetCurrentRouteObjective(out Vector3 destination)
    {
        if (currentState == AIState.MoveToTarget && currentTarget != null)
        {
            destination = currentTarget.GetInteractionPoint();
            return true;
        }

        if (currentState == AIState.MoveToRememberedTarget)
        {
            if (currentComfortZoneTarget != null)
            {
                destination = currentComfortZoneTarget.lastKnownPosition;
                return true;
            }

            if (currentMemoryTarget != null)
            {
                destination = currentMemoryTarget.lastKnownPosition;
                return true;
            }
        }

        if (currentState == AIState.Explore && hasExplorePoint)
        {
            destination = currentExplorePoint;
            return true;
        }

        if (hasPendingDoorDestination)
        {
            destination = pendingDoorDestination;
            return true;
        }

        destination = transform.position;
        return false;
    }

    private bool TryAcquireMatchingKeyForLockedDoor(string requiredKeyId)
    {
        if (string.IsNullOrWhiteSpace(requiredKeyId))
            return false;

        if (TryFindBestVisibleMatchingKey(requiredKeyId, out Interactable visibleKey))
        {
            RememberObservedInteractable(visibleKey);
            return CommitMatchingKeyTarget(visibleKey, null, true);
        }

        if (TryFindBestRememberedMatchingKey(requiredKeyId, out RememberedInteractable rememberedKey))
        {
            return CommitMatchingKeyTarget(rememberedKey.interactable, rememberedKey, false);
        }

        return false;
    }

    private bool TryFindBestVisibleMatchingKey(string requiredKeyId, out Interactable bestKey)
    {
        return perceptionService.TryFindBestVisibleMatchingKey(transform, transform.forward, gameObject, requiredKeyId, out bestKey);
    }

    private bool TryFindBestRememberedMatchingKey(string requiredKeyId, out RememberedInteractable bestMemory)
    {
        return memoryService.TryFindBestRememberedMatchingKey(memory, requiredKeyId, gameObject, transform.position, out bestMemory);
    }

    private bool CommitMatchingKeyTarget(Interactable keyTarget, RememberedInteractable rememberedKeyMemory, bool isVisibleKey)
    {
        ResetPendingDoorState();
        currentExploreDoorRouteAttempts = 0;

        currentTarget = keyTarget;
        currentMemoryTarget = rememberedKeyMemory;
        currentComfortZoneTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        if (routeCoordinator.TryPeekTopSubgoal(out NpcRouteCoordinator.RouteSubgoal topSubgoal))
        {
            currentNeedType = topSubgoal.originatingNeedType;
            currentNeedActionIsUrgentDriven = topSubgoal.originatedFromUrgentNeed;
        }
        else
        {
            currentNeedActionIsUrgentDriven = false;
        }

        agent.ResetPath();

        Vector3 targetPosition = rememberedKeyMemory != null
            ? rememberedKeyMemory.lastKnownPosition
            : currentTarget.GetInteractionPoint();

        if (IsPathReachable(targetPosition) || TryHandleDoorForDestination(targetPosition))
        {
            if (isVisibleKey)
            {
                Narrate("I can see the key I need. Going to get it.", "key-retrieval-visible");
                if (routeCoordinator.SubgoalCount > 0)
                    DebugMovement("Subgoal key targeted from visible key.");
                ChangeState(AIState.MoveToTarget);
            }
            else
            {
                Narrate("I remember seeing a key that could open that door.", "key-retrieval-remembered");
                if (routeCoordinator.SubgoalCount > 0)
                    DebugMovement("Subgoal key targeted from memory.");
                ChangeState(AIState.MoveToRememberedTarget);
            }

            return true;
        }

        currentTarget = null;
        currentMemoryTarget = null;
        return false;
    }

    private void RepathCurrentMovementDestination()
    {
        switch (currentState)
        {
            case AIState.Explore:
                if (hasExplorePoint)
                {
                    BeginMovementGoal(currentExplorePoint);
                    agent.SetDestination(currentExplorePoint);
                }
                break;

            case AIState.MoveToTarget:
                if (currentTarget != null)
                {
                    BeginMovementGoal(currentTarget.GetInteractionPoint());
                    agent.SetDestination(currentTarget.GetInteractionPoint());
                }
                break;

            case AIState.MoveToRememberedTarget:
                if (currentComfortZoneTarget != null)
                {
                    BeginMovementGoal(currentComfortZoneTarget.lastKnownPosition);
                    agent.SetDestination(currentComfortZoneTarget.lastKnownPosition);
                }
                else if (currentMemoryTarget != null)
                {
                    BeginMovementGoal(currentMemoryTarget.lastKnownPosition);
                    agent.SetDestination(currentMemoryTarget.lastKnownPosition);
                }
                break;
        }
    }

    private void FaceTowardsPoint(Vector3 point)
    {
        Vector3 flatDirection = point - transform.position;
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(flatDirection.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 6f);
    }

    private void ClearActiveTargets()
    {
        if (currentActivityInteractable != null)
            currentActivityInteractable.EndActivity(gameObject);

        ClearMovementGoal();
        currentActivityInteractable = null;
        currentActivityElapsed = 0f;
        currentActivityDuration = 0f;
        hasCurrentActivityLookPoint = false;
        currentActivityLookTimer = 0f;
        currentTarget = null;
        currentMemoryTarget = null;
        currentComfortZoneTarget = null;
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

    private void OnValidate()
    {
        if (invariantCheckInterval < 0.2f)
            invariantCheckInterval = 0.2f;

        if (doorCheckDistance < 0.25f)
            doorCheckDistance = 0.25f;

        perceptionService.Configure(visionRange, visionAngle, interactableLayer, obstacleLayer, doorLayer, eyePoint);
        routeCoordinator.Configure(name, debugMovementRecovery);
    }
}
