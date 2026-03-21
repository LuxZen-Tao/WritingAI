using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class NpcRouteCoordinator
{
    public enum RouteSubgoalType
    {
        LockedDoorKeyResolution
    }

    public struct RouteSubgoal
    {
        public RouteSubgoalType type;
        public DoorInteractable blockedDoor;
        public string requiredKeyId;
        public Vector3 blockedDestination;
        public Vector3 originalDestination;
        public NeedType originatingNeedType;
        public bool originatedFromUrgentNeed;
        public string debugLabel;
    }

    public struct MovementRecoverySettings
    {
        public float stallVelocityThreshold;
        public float stallTimeThreshold;
        public float movementProgressDistanceThreshold;
        public float movementProgressPathDistanceThreshold;
        public float movementNoProgressTimeout;
        public float movementGoalTimeout;
        public int maxMovementRecoveryAttempts;
        public float movementRecoverySampleRadius;
        public float movementRecoveryUnstuckRadius;
        public float movementPointClearanceRadius;
        public int movementPathFailureLimit;
    }

    private readonly List<RouteSubgoal> routeSubgoalStack = new List<RouteSubgoal>();

    private string ownerName = "NPC";
    private bool debugLogging = false;

    private Vector3 activeMovementGoalPosition;
    private bool hasActiveMovementGoal = false;
    private float movementGoalStartTime = 0f;
    private float movementGoalBestDistance = Mathf.Infinity;
    private float movementLastProgressTime = 0f;
    private int movementGoalRecoveryAttempts = 0;
    private int movementPathFailureCount = 0;
    private float stallTimer = 0f;

    public int SubgoalCount => routeSubgoalStack.Count;

    public void Configure(string owner, bool enableDebugLogging)
    {
        ownerName = string.IsNullOrWhiteSpace(owner) ? "NPC" : owner;
        debugLogging = enableDebugLogging;
    }

    public void ResetStallTimer()
    {
        stallTimer = 0f;
    }

    public void ClearMovementGoal()
    {
        hasActiveMovementGoal = false;
        movementGoalRecoveryAttempts = 0;
        movementPathFailureCount = 0;
        stallTimer = 0f;
    }

    public void BeginMovementGoal(Vector3 target, Vector3 currentPosition, float now)
    {
        if (!hasActiveMovementGoal || Vector3.Distance(activeMovementGoalPosition, target) > 0.3f)
        {
            activeMovementGoalPosition = target;
            hasActiveMovementGoal = true;
            movementGoalStartTime = now;
            movementGoalBestDistance = Vector3.Distance(currentPosition, target);
            movementLastProgressTime = now;
            movementGoalRecoveryAttempts = 0;
            movementPathFailureCount = 0;
            stallTimer = 0f;
            DebugMovement($"Begin goal @ {target}");
        }
    }

    public void MarkMovementProgress(string reason, float now)
    {
        movementLastProgressTime = now;
        movementPathFailureCount = 0;
        stallTimer = 0f;
        DebugMovement($"Progress: {reason}");
    }

    public bool HasPointClearance(Vector3 point, LayerMask obstacleLayer, float clearanceRadius)
    {
        if (clearanceRadius <= 0.01f)
            return true;

        Collider[] hits = Physics.OverlapSphere(point + Vector3.up * 0.2f, clearanceRadius, obstacleLayer, QueryTriggerInteraction.Ignore);
        return hits == null || hits.Length == 0;
    }

    public bool TryRecoverMovementGoal(
        NavMeshAgent agent,
        Vector3 currentPosition,
        Vector3 target,
        float now,
        float deltaTime,
        MovementRecoverySettings settings,
        LayerMask obstacleLayer,
        Func<Vector3, bool> isPathReachable,
        string contextTag)
    {
        UpdateMovementProgress(agent, currentPosition, target, now, deltaTime, settings);

        bool timedOut = HasMovementGoalTimedOut(now, settings);
        bool pathFailed = movementPathFailureCount >= Mathf.Max(1, settings.movementPathFailureLimit);
        bool attemptsExceeded = movementGoalRecoveryAttempts >= Mathf.Max(1, settings.maxMovementRecoveryAttempts);
        if (!timedOut && !pathFailed && !attemptsExceeded)
            return false;

        if (attemptsExceeded)
        {
            DebugMovement($"[{contextTag}] Abandoning goal after too many recoveries.");
            ClearMovementGoal();
            return true;
        }

        movementGoalRecoveryAttempts++;
        stallTimer = 0f;
        DebugMovement($"[{contextTag}] Recovery attempt {movementGoalRecoveryAttempts}");

        if (movementGoalRecoveryAttempts == 1)
        {
            agent.ResetPath();
            agent.SetDestination(target);
            MarkMovementProgress("soft-repath", now);
            return false;
        }

        if (movementGoalRecoveryAttempts == 2 &&
            TryFindNearbyRecoveryPoint(target, settings.movementRecoverySampleRadius, obstacleLayer, isPathReachable, settings.movementPointClearanceRadius, out Vector3 angledPoint))
        {
            agent.ResetPath();
            agent.SetDestination(angledPoint);
            MarkMovementProgress("offset-approach", now);
            return false;
        }

        if (movementGoalRecoveryAttempts == 3 &&
            TryFindNearbyRecoveryPoint(currentPosition, settings.movementRecoveryUnstuckRadius, obstacleLayer, isPathReachable, settings.movementPointClearanceRadius, out Vector3 unstuckPoint))
        {
            agent.ResetPath();
            agent.SetDestination(unstuckPoint);
            MarkMovementProgress("local-unstuck", now);
            return false;
        }

        DebugMovement($"[{contextTag}] Recovery exhausted, abandoning local route point.");
        ClearMovementGoal();
        return true;
    }

    public bool TryPushLockedDoorSubgoal(
        DoorInteractable door,
        string requiredKeyId,
        Vector3 blockedDestination,
        Vector3 originalDestination,
        NeedType originatingNeedType,
        bool originatedFromUrgentNeed,
        string debugLabel = null)
    {
        if (door == null || string.IsNullOrWhiteSpace(requiredKeyId))
            return false;

        if (TryPeekTopSubgoal(out RouteSubgoal topSubgoal) &&
            topSubgoal.blockedDoor == door &&
            topSubgoal.requiredKeyId == requiredKeyId)
        {
            return false;
        }

        if (HasSubgoalForDoor(door))
            return false;

        RouteSubgoal subgoal = new RouteSubgoal
        {
            type = RouteSubgoalType.LockedDoorKeyResolution,
            blockedDoor = door,
            requiredKeyId = requiredKeyId,
            blockedDestination = blockedDestination,
            originalDestination = originalDestination,
            originatingNeedType = originatingNeedType,
            originatedFromUrgentNeed = originatedFromUrgentNeed,
            debugLabel = string.IsNullOrWhiteSpace(debugLabel) ? door.name : debugLabel
        };

        routeSubgoalStack.Add(subgoal);
        DebugMovement($"Subgoal pushed: door={door.name}, key={requiredKeyId}, depth={routeSubgoalStack.Count}, label={subgoal.debugLabel}");
        return true;
    }

    public bool TryPeekTopSubgoal(out RouteSubgoal subgoal)
    {
        if (routeSubgoalStack.Count == 0)
        {
            subgoal = default;
            return false;
        }

        subgoal = routeSubgoalStack[routeSubgoalStack.Count - 1];
        return true;
    }

    public bool PopTopSubgoal(string reason)
    {
        if (routeSubgoalStack.Count == 0)
            return false;

        RouteSubgoal popped = routeSubgoalStack[routeSubgoalStack.Count - 1];
        routeSubgoalStack.RemoveAt(routeSubgoalStack.Count - 1);
        DebugMovement($"Subgoal popped: door={popped.blockedDoor?.name}, reason={reason}, remainingDepth={routeSubgoalStack.Count}");
        return true;
    }

    public void ClearAllSubgoals(string reason)
    {
        if (routeSubgoalStack.Count == 0)
            return;

        DebugMovement($"Cleared all subgoals ({routeSubgoalStack.Count}): {reason}");
        routeSubgoalStack.Clear();
    }

    public bool HasSubgoalForDoor(DoorInteractable door)
    {
        if (door == null)
            return false;

        for (int i = 0; i < routeSubgoalStack.Count; i++)
        {
            if (routeSubgoalStack[i].blockedDoor == door)
                return true;
        }

        return false;
    }

    public bool IsSubgoalResolved(RouteSubgoal subgoal, DoorController controller)
    {
        return subgoal.blockedDoor == null || controller == null || controller.IsOpen || !controller.IsLocked;
    }

    public bool PopResolvedSubgoals(out bool poppedAny)
    {
        poppedAny = false;

        while (TryPeekTopSubgoal(out RouteSubgoal topSubgoal))
        {
            DoorController controller = topSubgoal.blockedDoor != null ? topSubgoal.blockedDoor.GetDoorController() : null;
            if (!IsSubgoalResolved(topSubgoal, controller))
                break;

            PopTopSubgoal("resume-mission-resolved");
            poppedAny = true;
        }

        return TryPeekTopSubgoal(out _);
    }

    public void DebugMovement(string message)
    {
        if (!debugLogging)
            return;

        Debug.Log($"[NpcRouteCoordinator:{ownerName}] {message}");
    }

    private void UpdateMovementProgress(
        NavMeshAgent agent,
        Vector3 currentPosition,
        Vector3 target,
        float now,
        float deltaTime,
        MovementRecoverySettings settings)
    {
        if (!hasActiveMovementGoal)
            BeginMovementGoal(target, currentPosition, now);

        float currentDistance = Vector3.Distance(currentPosition, target);
        if (currentDistance + settings.movementProgressDistanceThreshold < movementGoalBestDistance)
        {
            movementGoalBestDistance = currentDistance;
            MarkMovementProgress("distance", now);
        }

        if (!agent.pathPending && agent.hasPath)
        {
            if (agent.remainingDistance + settings.movementProgressPathDistanceThreshold < movementGoalBestDistance)
            {
                movementGoalBestDistance = agent.remainingDistance;
                MarkMovementProgress("path-distance", now);
            }

            if (agent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                movementPathFailureCount++;
                DebugMovement($"Path degraded ({agent.pathStatus}) failure count {movementPathFailureCount}");
            }
        }

        if (!agent.isOnNavMesh || agent.pathPending || !agent.hasPath)
        {
            stallTimer = 0f;
            return;
        }

        if (agent.remainingDistance <= agent.stoppingDistance + 0.05f)
        {
            stallTimer = 0f;
            return;
        }

        if (agent.velocity.sqrMagnitude < settings.stallVelocityThreshold * settings.stallVelocityThreshold)
            stallTimer += deltaTime;
        else
            stallTimer = 0f;
    }

    private bool HasMovementGoalTimedOut(float now, MovementRecoverySettings settings)
    {
        if (!hasActiveMovementGoal)
            return false;

        if (stallTimer >= settings.stallTimeThreshold)
        {
            DebugMovement($"Stall timeout ({stallTimer:F2}s >= {settings.stallTimeThreshold:F2}s)");
            return true;
        }

        if (now - movementLastProgressTime >= settings.movementNoProgressTimeout)
        {
            DebugMovement($"No-progress timeout ({now - movementLastProgressTime:F2}s >= {settings.movementNoProgressTimeout:F2}s)");
            return true;
        }

        if (now - movementGoalStartTime >= settings.movementGoalTimeout)
        {
            DebugMovement($"Goal timeout ({now - movementGoalStartTime:F2}s >= {settings.movementGoalTimeout:F2}s)");
            return true;
        }

        return false;
    }

    private bool TryFindNearbyRecoveryPoint(
        Vector3 center,
        float radius,
        LayerMask obstacleLayer,
        Func<Vector3, bool> isPathReachable,
        float clearanceRadius,
        out Vector3 point)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector2 circle = UnityEngine.Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
                continue;

            if (!HasPointClearance(hit.position, obstacleLayer, clearanceRadius))
                continue;

            if (!isPathReachable(hit.position))
                continue;

            point = hit.position;
            return true;
        }

        point = center;
        return false;
    }
}
