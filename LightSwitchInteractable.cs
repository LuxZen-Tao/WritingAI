using UnityEngine;

public class LightSwitchInteractable : Interactable, INeedSatisfier
{
    [Header("Light Switch Settings")]
    public RoomArea targetRoom;

    [Header("Anti-Flicker")]
    public float interactCooldown = 1f;

    private float lastInteractTime = -999f;

    private void Start()
    {
        if (targetRoom != null)
        {
            targetRoom.SetArtificialLights(targetRoom.AreArtificialLightsOn());
        }
    }

    public override bool CanInteract(GameObject interactor)
    {
        if (!base.CanInteract(interactor))
            return false;

        if (targetRoom == null)
            return false;

        if (Time.time < lastInteractTime + interactCooldown)
            return false;

        // Only allow this switch to satisfy comfort if it would improve comfort.
        // If the room's artificial lights are already on, don't let the NPC toggle it off.
        if (targetRoom.AreArtificialLightsOn())
            return false;

        return true;
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

        bool newArtificialState = true;
        targetRoom.SetArtificialLights(newArtificialState);
        lastInteractTime = Time.time;

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
        return 0f;
    }
}