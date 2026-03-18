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

    private struct VisibleSatisfierInfo
    {
        public Interactable interactable;
        public INeedSatisfier satisfier;
        public float distance;

        public VisibleSatisfierInfo(Interactable interactable, INeedSatisfier satisfier, float distance)
        {
            this.interactable = interactable;
            this.satisfier = satisfier;
            this.distance = distance;
        }
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
    public float comfortUrgencyHysteresis = 0.75f;
    public float comfortDecayRate = 2f;
    public float comfortRecoveryRate = 1f;

    [Header("Room Context")]
    public RoomArea currentRoom;
    public RoomArea defaultWorldArea;

    [Header("Vision")]
    public float visionRange = 8f;
    [Range(0f, 360f)]
    public float visionAngle = 90f;
    public LayerMask interactableLayer;
    public LayerMask obstacleLayer;
    public Transform eyePoint;
    public float perceptionInterval = 0.15f;

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

    [Header("Navigation")]
    public float destinationRepathThreshold = 0.1f;
    public float randomPointMinDistance = 0.5f;

    [Header("Memory - Interactables")]
    public List<RememberedInteractable> memory = new List<RememberedInteractable>();
    public float rememberedTargetStopDistance = 1f;
    public float interactableMemoryDuration = 120f;
    public int maxRememberedInteractables = 64;

    [Header("Memory - Locations")]
    public List<RememberedLocation> rememberedLocations = new List<RememberedLocation>();
    public float locationMemoryDuration = 10f;
    public float locationAvoidanceRadius = 2f;
    public int maxRememberedLocations = 96;

    [Header("Current Target")]
    public Interactable currentTarget;

    private readonly Collider[] perceptionHits = new Collider[64];
    private readonly List<VisibleSatisfierInfo> visibleSatisfiers = new List<VisibleSatisfierInfo>();
    private readonly List<RoomArea> overlappingRooms = new List<RoomArea>();

    private NavMeshAgent agent;
    private float searchTimer;
    private float exploreTimer;
    private float idleTimer;
    private float perceptionTimer;

    private Vector3 currentExplorePoint;
    private bool hasExplorePoint;

    private Vector3 currentIdlePoint;
    private bool hasIdlePoint;

    private RememberedInteractable currentMemoryTarget;
    private NeedType currentNeedType = NeedType.Comfort;
    private bool comfortNeedActive;

    private Vector3 lastDestination = Vector3.positiveInfinity;
    private bool hasLastDestination;
    private bool hasLoggedLayerWarning;

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        if (agent == null)
        {
            Debug.LogError("No NavMeshAgent found on " + gameObject.name);
            enabled = false;
            return;
        }

        ValidateConfiguration();
        RefreshComfortUrgency();
        RefreshPerception();
        ChangeState(AIState.CheckNeed);
    }

    private void Update()
    {
        UpdateComfort();
        RefreshComfortUrgency();

        CleanupLocationMemory();
        ForgetInvalidMemories();

        perceptionTimer += Time.deltaTime;
        if (perceptionTimer >= perceptionInterval)
        {
            perceptionTimer = 0f;
            RefreshPerception();
        }

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

    private void ValidateConfiguration()
    {
        if (interactableLayer.value == 0 || obstacleLayer.value == 0)
        {
            hasLoggedLayerWarning = true;
            Debug.LogWarning(gameObject.name + " has empty interactable/obstacle layer mask configuration.");
        }
    }

    private RoomArea GetActiveArea()
    {
        if (currentRoom != null)
            return currentRoom;

        return defaultWorldArea;
    }

    private void UpdateComfort()
    {
        RoomArea activeArea = GetActiveArea();

        if (activeArea == null)
            return;

        if (!activeArea.IsLit())
        {
            comfort -= comfortDecayRate * Time.deltaTime;
        }
        else
        {
            comfort += comfortRecoveryRate * Time.deltaTime;
        }

        comfort = Mathf.Clamp(comfort, 0f, maxComfort);
    }

    private void RefreshComfortUrgency()
    {
        float resolveThreshold = comfortThreshold + comfortUrgencyHysteresis;

        if (comfortNeedActive)
        {
            if (comfort >= resolveThreshold)
            {
                comfortNeedActive = false;
            }

            return;
        }

        comfortNeedActive = comfort < comfortThreshold;
    }

    private void EvaluateNeeds()
    {
        if (IsNeedUrgent(NeedType.Comfort))
        {
            currentNeedType = NeedType.Comfort;
            ChangeState(AIState.FindTarget);
            return;
        }

        ChangeState(AIState.Idle);
    }

    private void HandleIdleState()
    {
        if (IsNeedUrgent(NeedType.Comfort))
        {
            currentNeedType = NeedType.Comfort;
            ChangeState(AIState.FindTarget);
            return;
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
        if (IsNeedUrgent(NeedType.Comfort))
        {
            currentNeedType = NeedType.Comfort;
            ClearCurrentIntent(true);
            ChangeState(AIState.FindTarget);
            return;
        }

        agent.speed = idleMoveSpeed;

        if (!hasIdlePoint)
        {
            bool foundPoint = TryPickIdlePoint();

            if (!foundPoint)
            {
                ChangeState(AIState.Idle);
                return;
            }
        }

        SetDestinationIfNeeded(currentIdlePoint);

        if (!agent.pathPending && agent.remainingDistance <= idleWanderStopDistance)
        {
            RememberLocation(currentIdlePoint);
            hasIdlePoint = false;
            ChangeState(AIState.Idle);
            return;
        }

        if (HasInvalidCurrentPath())
        {
            hasIdlePoint = false;
            ChangeState(AIState.Idle);
            return;
        }

        idleTimer += Time.deltaTime;

        if (idleTimer >= idleWanderDuration)
        {
            RememberLocation(currentIdlePoint);
            hasIdlePoint = false;
            idleTimer = 0f;
            ChangeState(AIState.Idle);
        }
    }

    private void FindBestTargetForNeed(NeedType needType)
    {
        if (!IsNeedUrgent(needType))
        {
            AbortCurrentNeedAction();
            return;
        }

        if (needType == NeedType.Comfort)
        {
            RoomArea activeArea = GetActiveArea();
            if (activeArea != null && activeArea.IsLit())
            {
                ChangeState(AIState.Idle);
                return;
            }
        }

        if (TryAcquireVisibleTarget(needType))
            return;

        if (TryUseRememberedTarget(needType))
            return;

        searchTimer = 0f;
        ChangeState(AIState.Search);
    }

    private void RefreshPerception()
    {
        if (!hasLoggedLayerWarning && (interactableLayer.value == 0 || obstacleLayer.value == 0))
        {
            hasLoggedLayerWarning = true;
            Debug.LogWarning(gameObject.name + " has empty interactable/obstacle layer mask configuration.");
        }

        visibleSatisfiers.Clear();

        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, visionRange, perceptionHits, interactableLayer);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = perceptionHits[i];

            if (hit == null)
                continue;

            Interactable interactable = hit.GetComponentInParent<Interactable>();

            if (interactable == null || !interactable.isEnabled)
                continue;

            if (!CanSeeInteractable(interactable))
                continue;

            INeedSatisfier satisfier = interactable as INeedSatisfier;
            if (satisfier == null)
                continue;

            float distance = Vector3.Distance(transform.position, interactable.GetInteractionPoint());
            visibleSatisfiers.Add(new VisibleSatisfierInfo(interactable, satisfier, distance));
            RememberInteractable(interactable, satisfier.GetNeedType());
        }
    }

    private bool TryAcquireVisibleTarget(NeedType needType)
    {
        Interactable bestTarget = null;
        float bestDistance = Mathf.Infinity;

        for (int i = 0; i < visibleSatisfiers.Count; i++)
        {
            VisibleSatisfierInfo entry = visibleSatisfiers[i];

            if (entry.satisfier.GetNeedType() != needType)
                continue;

            if (!entry.interactable.CanInteract(gameObject))
                continue;

            if (entry.distance < bestDistance)
            {
                bestDistance = entry.distance;
                bestTarget = entry.interactable;
            }
        }

        if (bestTarget == null)
            return false;

        currentTarget = bestTarget;
        currentMemoryTarget = null;
        hasExplorePoint = false;
        hasIdlePoint = false;
        ResetNavigationTracking();

        ChangeState(AIState.MoveToTarget);
        return true;
    }

    private bool TryUseRememberedTarget(NeedType needType)
    {
        RememberedInteractable bestMemory = null;
        float bestScore = Mathf.Infinity;

        for (int i = 0; i < memory.Count; i++)
        {
            RememberedInteractable remembered = memory[i];
            if (remembered == null || remembered.interactable == null)
                continue;

            if (remembered.needType != needType)
                continue;

            if (!remembered.interactable.isEnabled || !remembered.interactable.CanInteract(gameObject))
                continue;

            float distance = Vector3.Distance(transform.position, remembered.lastKnownPosition);
            float recencyPenalty = (Time.time - remembered.lastSeenTime) * 0.1f;
            float score = distance + recencyPenalty;

            if (score < bestScore)
            {
                bestScore = score;
                bestMemory = remembered;
            }
        }

        if (bestMemory == null)
            return false;

        currentMemoryTarget = bestMemory;
        currentTarget = bestMemory.interactable;
        hasExplorePoint = false;
        hasIdlePoint = false;
        ResetNavigationTracking();

        ChangeState(AIState.MoveToRememberedTarget);
        return true;
    }

    private void RememberInteractable(Interactable interactable, NeedType needType)
    {
        if (interactable == null)
            return;

        for (int i = 0; i < memory.Count; i++)
        {
            RememberedInteractable remembered = memory[i];
            if (remembered != null && remembered.interactable == interactable)
            {
                remembered.lastKnownPosition = interactable.GetInteractionPoint();
                remembered.needType = needType;
                remembered.lastSeenTime = Time.time;
                return;
            }
        }

        if (memory.Count >= maxRememberedInteractables)
        {
            RemoveOldestInteractableMemory();
        }

        memory.Add(new RememberedInteractable(interactable, needType, interactable.GetInteractionPoint(), Time.time));
    }

    private void RemoveOldestInteractableMemory()
    {
        int oldestIndex = -1;
        float oldestTime = Mathf.Infinity;

        for (int i = 0; i < memory.Count; i++)
        {
            RememberedInteractable remembered = memory[i];
            if (remembered == null)
            {
                oldestIndex = i;
                break;
            }

            if (remembered.lastSeenTime < oldestTime)
            {
                oldestTime = remembered.lastSeenTime;
                oldestIndex = i;
            }
        }

        if (oldestIndex >= 0)
        {
            memory.RemoveAt(oldestIndex);
        }
    }

    private void ForgetInvalidMemories()
    {
        memory.RemoveAll(m =>
            m == null ||
            m.interactable == null ||
            Time.time - m.lastSeenTime > interactableMemoryDuration);
    }

    private void RememberLocation(Vector3 position)
    {
        for (int i = 0; i < rememberedLocations.Count; i++)
        {
            if (Vector3.Distance(rememberedLocations[i].position, position) <= locationAvoidanceRadius * 0.5f)
            {
                rememberedLocations[i].position = position;
                rememberedLocations[i].timeStored = Time.time;
                return;
            }
        }

        if (rememberedLocations.Count >= maxRememberedLocations)
        {
            rememberedLocations.RemoveAt(0);
        }

        rememberedLocations.Add(new RememberedLocation(position, Time.time));
    }

    private void CleanupLocationMemory()
    {
        rememberedLocations.RemoveAll(loc => Time.time - loc.timeStored > locationMemoryDuration);
    }

    private bool IsRecentlyVisited(Vector3 position)
    {
        for (int i = 0; i < rememberedLocations.Count; i++)
        {
            if (Vector3.Distance(position, rememberedLocations[i].position) <= locationAvoidanceRadius)
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
            return false;

        return true;
    }

    private void SearchForTarget()
    {
        if (!IsNeedUrgent(currentNeedType))
        {
            AbortCurrentNeedAction();
            return;
        }

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
        if (!IsNeedUrgent(currentNeedType))
        {
            AbortCurrentNeedAction();
            return;
        }

        if (TryAcquireVisibleTarget(currentNeedType))
            return;

        agent.speed = needMoveSpeed;
        exploreTimer += Time.deltaTime;

        if (!hasExplorePoint)
        {
            bool foundPoint = TryPickExplorePoint();

            if (!foundPoint)
            {
                exploreTimer = 0f;
                ChangeState(AIState.Search);
                return;
            }
        }

        SetDestinationIfNeeded(currentExplorePoint);

        if (!agent.pathPending && agent.remainingDistance <= exploreStopDistance)
        {
            RememberLocation(currentExplorePoint);
            exploreTimer = 0f;
            hasExplorePoint = false;
            ResetNavigationTracking();

            if (TryAcquireVisibleTarget(currentNeedType))
                return;

            if (TryUseRememberedTarget(currentNeedType))
                return;

            ChangeState(AIState.FindTarget);
            return;
        }

        if (HasInvalidCurrentPath())
        {
            hasExplorePoint = false;
            ChangeState(AIState.Search);
            return;
        }

        if (exploreTimer >= exploreDuration)
        {
            RememberLocation(currentExplorePoint);
            exploreTimer = 0f;
            hasExplorePoint = false;
            ResetNavigationTracking();

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

            if (randomOffset.sqrMagnitude < randomPointMinDistance * randomPointMinDistance)
                continue;

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
                return true;
            }
        }

        return false;
    }

    private bool TryPickIdlePoint()
    {
        for (int i = 0; i < 10; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * idleWanderRadius;
            randomOffset.y = 0f;

            if (randomOffset.sqrMagnitude < randomPointMinDistance * randomPointMinDistance)
                continue;

            Vector3 candidatePoint = transform.position + randomOffset;

            if (IsRecentlyVisited(candidatePoint))
                continue;

            if (NavMesh.SamplePosition(candidatePoint, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
            {
                if (IsRecentlyVisited(navHit.position))
                    continue;

                currentIdlePoint = navHit.position;
                hasIdlePoint = true;
                idleTimer = 0f;
                return true;
            }
        }

        return false;
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
            ChangeState(AIState.FindTarget);
            return;
        }

        if (!currentTarget.CanInteract(gameObject) || !currentTarget.isEnabled)
        {
            ChangeState(AIState.FindTarget);
            return;
        }

        agent.speed = needMoveSpeed;
        SetDestinationIfNeeded(currentTarget.GetInteractionPoint());

        if (!agent.pathPending && agent.remainingDistance <= currentTarget.interactionRange)
        {
            if (!IsNeedUrgent(currentNeedType))
            {
                AbortCurrentNeedAction();
                return;
            }

            ChangeState(AIState.InteractWithTarget);
            return;
        }

        if (HasInvalidCurrentPath())
        {
            currentTarget = null;
            ChangeState(AIState.FindTarget);
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
            currentMemoryTarget = null;
            currentTarget = null;
            ChangeState(AIState.FindTarget);
            return;
        }

        if (TryAcquireVisibleTarget(currentNeedType))
            return;

        agent.speed = needMoveSpeed;
        SetDestinationIfNeeded(currentMemoryTarget.lastKnownPosition);

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
            return;
        }

        if (HasInvalidCurrentPath())
        {
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
            ChangeState(AIState.FindTarget);
            return;
        }

        agent.ResetPath();
        ResetNavigationTracking();

        if (currentTarget.CanInteract(gameObject))
        {
            currentTarget.Interact(gameObject);
        }

        ClearCurrentIntent(false);
        ChangeState(AIState.Idle);
    }

    private void SetDestinationIfNeeded(Vector3 destination)
    {
        if (!hasLastDestination)
        {
            agent.SetDestination(destination);
            lastDestination = destination;
            hasLastDestination = true;
            return;
        }

        if (Vector3.Distance(lastDestination, destination) <= destinationRepathThreshold)
            return;

        agent.SetDestination(destination);
        lastDestination = destination;
    }

    private bool HasInvalidCurrentPath()
    {
        if (agent.pathPending)
            return false;

        if (!agent.hasPath)
            return true;

        return agent.pathStatus == NavMeshPathStatus.PathInvalid || agent.pathStatus == NavMeshPathStatus.PathPartial;
    }

    private void ResetNavigationTracking()
    {
        hasLastDestination = false;
        lastDestination = Vector3.positiveInfinity;
        agent.ResetPath();
    }

    private void ChangeState(AIState newState)
    {
        if (currentState == newState)
            return;

        currentState = newState;

        switch (currentState)
        {
            case AIState.Search:
                searchTimer = 0f;
                ResetNavigationTracking();
                break;

            case AIState.Explore:
                exploreTimer = 0f;
                hasExplorePoint = false;
                break;

            case AIState.Idle:
                idleTimer = 0f;
                hasIdlePoint = false;
                ResetNavigationTracking();
                break;

            case AIState.IdleWander:
                idleTimer = 0f;
                hasIdlePoint = false;
                break;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        RoomArea room = other.GetComponent<RoomArea>();

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
        RoomArea room = other.GetComponent<RoomArea>();

        if (room == null)
            return;

        overlappingRooms.Remove(room);
        ResolveCurrentRoom();
    }

    private void ResolveCurrentRoom()
    {
        RoomArea bestRoom = null;
        float bestDistanceSqr = Mathf.Infinity;

        for (int i = overlappingRooms.Count - 1; i >= 0; i--)
        {
            RoomArea room = overlappingRooms[i];
            if (room == null)
            {
                overlappingRooms.RemoveAt(i);
                continue;
            }

            float distanceSqr = (room.transform.position - transform.position).sqrMagnitude;
            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                bestRoom = room;
            }
        }

        currentRoom = bestRoom;
    }

    private bool IsNeedUrgent(NeedType needType)
    {
        switch (needType)
        {
            case NeedType.Comfort:
                return comfortNeedActive;
        }

        return false;
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
