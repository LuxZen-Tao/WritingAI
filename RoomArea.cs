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
}
