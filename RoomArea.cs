using UnityEngine;

public class RoomArea : MonoBehaviour
{
    [Header("Room Settings")]
    public string roomName = "Room";

    [Header("Natural Light")]
    public bool receivesDaylight = false;
    public bool isDaytime = true;

    [Header("Artificial Lights Controlled By Switches")]
    public Light[] controlledLights;

    [Header("Fallback")]
    public bool fallbackLitState = false;
    private Collider cachedRoomCollider;

    public bool IsLit()
    {
        if (receivesDaylight && isDaytime)
        {
            return true;
        }

        if (HasControlledLights())
            return AnyControlledLightOn();

        return fallbackLitState;
    }

    public void SetArtificialLights(bool litState)
    {
        fallbackLitState = litState;

        if (HasControlledLights())
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
        if (HasControlledLights())
            return AnyControlledLightOn();

        return fallbackLitState;
    }

    private bool HasControlledLights()
    {
        return controlledLights != null && controlledLights.Length > 0;
    }

    private bool AnyControlledLightOn()
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

    public bool ContainsPoint(Vector3 worldPoint)
    {
        if (!TryGetRoomBounds(out Bounds bounds))
            return false;

        return bounds.Contains(worldPoint);
    }

    public bool TryGetRandomPointInBounds(float inset, out Vector3 worldPoint)
    {
        worldPoint = transform.position;

        if (!TryGetRoomBounds(out Bounds bounds))
            return false;

        float clampedInsetX = Mathf.Clamp(inset, 0f, bounds.extents.x * 0.95f);
        float clampedInsetZ = Mathf.Clamp(inset, 0f, bounds.extents.z * 0.95f);

        float minX = bounds.min.x + clampedInsetX;
        float maxX = bounds.max.x - clampedInsetX;
        float minZ = bounds.min.z + clampedInsetZ;
        float maxZ = bounds.max.z - clampedInsetZ;

        if (minX > maxX || minZ > maxZ)
            return false;

        worldPoint = new Vector3(
            Random.Range(minX, maxX),
            transform.position.y,
            Random.Range(minZ, maxZ));

        return true;
    }

    private bool TryGetRoomBounds(out Bounds bounds)
    {
        if (cachedRoomCollider == null)
        {
            cachedRoomCollider = GetComponent<Collider>();

            if (cachedRoomCollider == null)
            {
                cachedRoomCollider = GetComponentInChildren<Collider>();
            }
        }

        if (cachedRoomCollider == null)
        {
            bounds = default;
            return false;
        }

        bounds = cachedRoomCollider.bounds;
        return true;
    }
}
