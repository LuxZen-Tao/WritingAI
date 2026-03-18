using UnityEngine;

public class WorldArea : MonoBehaviour
{
    [Header("World Settings")]
    public string worldName = "World";

    [Tooltip("Directional light / sun / global daylight source.")]
    public Light worldLight;

    public bool IsDaytime()
    {
        if (worldLight != null)
        {
            return worldLight.enabled;
        }

        return false;
    }
}