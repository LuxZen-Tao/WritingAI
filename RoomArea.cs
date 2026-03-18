using UnityEngine;
using UnityEngine.AI;

public class RoomArea : MonoBehaviour
{
    [Header("Room Settings")]
    public string roomName = "Room";

    [Header("World Link")]
    public WorldArea worldArea;

    [Header("Natural Light")]
    public bool receivesDaylight = false;

    [Header("Artificial Lights Controlled By Switches")]
    public Light[] controlledLights;

    [Header("Fallback")]
    public bool fallbackLitState = false;

    private Collider roomCollider;

    private void Awake()
    {
        roomCollider = GetComponent<Collider>();

        if (roomCollider == null)
        {
            Debug.LogError(roomName + " needs a Collider to define room bounds.");
        }
    }

    public bool IsLit()
    {
        if (HasDaylight())
        {
            return true;
        }

        if (AreArtificialLightsOn())
        {
            return true;
        }

        return fallbackLitState;
    }

    public bool HasDaylight()
    {
        if (!receivesDaylight)
            return false;

        if (worldArea == null)
            return false;

        return worldArea.IsDaytime();
    }

    public void SetArtificialLights(bool litState)
    {
        fallbackLitState = litState;

        if (controlledLights != null && controlledLights.Length > 0)
        {
            for (int i = 0; i < controlledLights.Length; i++)
            {
                if (controlledLights[i] != null)
                {
                    controlledLights[i].enabled = litState;
                }
            }
        }

        Debug.Log(roomName + " artificial light state changed to: " + litState);
    }

    public bool AreArtificialLightsOn()
    {
        if (controlledLights != null && controlledLights.Length > 0)
        {
            for (int i = 0; i < controlledLights.Length; i++)
            {
                if (controlledLights[i] != null && controlledLights[i].enabled)
                {
                    return true;
                }
            }

            return false;
        }

        return fallbackLitState;
    }

    public bool ContainsPoint(Vector3 point)
    {
        if (roomCollider == null)
            return false;

        Vector3 closest = roomCollider.ClosestPoint(point);

        return Vector3.Distance(closest, point) < 0.05f;
    }

    public bool TryGetRandomNavigablePointInsideRoom(out Vector3 point)
    {
        point = Vector3.zero;

        if (roomCollider == null)
            return false;

        Bounds bounds = roomCollider.bounds;

        for (int i = 0; i < 20; i++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                bounds.center.y,
                Random.Range(bounds.min.z, bounds.max.z)
            );

            if (!ContainsPoint(candidate))
                continue;

            if (NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 1.5f, NavMesh.AllAreas))
            {
                if (!ContainsPoint(navHit.position))
                    continue;

                point = navHit.position;
                return true;
            }
        }

        return false;
    }
}