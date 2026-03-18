using UnityEngine;

public class LightSwitchInteractable : Interactable, INeedSatisfier
{
    [Header("Light Switch Settings")]
    public RoomArea targetRoom;

    private void Start()
    {
        if (targetRoom != null)
        {
            targetRoom.SetArtificialLights(targetRoom.AreArtificialLightsOn());
        }
    }

    public override void Interact(GameObject interactor)
    {
        if (!CanInteract(interactor))
            return;

        if (targetRoom == null)
        {
            Debug.LogWarning(interactableName + " has no targetRoom assigned.");
            return;
        }

        bool newArtificialState = !targetRoom.AreArtificialLightsOn();
        targetRoom.SetArtificialLights(newArtificialState);

        Debug.Log(interactableName + " used by " + interactor.name +
                  ". Artificial room light state is now " + newArtificialState);
    }

    public bool IsLightOn()
    {
        if (targetRoom == null)
            return false;

        return targetRoom.AreArtificialLightsOn();
    }

    public NeedType GetNeedType()
    {
        return NeedType.Comfort;
    }

    public float GetNeedValue()
    {
        // No direct comfort reward from touching the switch.
        // Comfort should come from the room being lit over time.
        return 0f;
    }
}