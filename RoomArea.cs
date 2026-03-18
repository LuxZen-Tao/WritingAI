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

    public bool IsLit()
    {
        // Daylight can light the room without any switch use
        if (receivesDaylight && isDaytime)
        {
            return true;
        }

        // Otherwise check artificial lights
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
}