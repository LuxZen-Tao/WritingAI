using UnityEngine;

public class DoorInteractable : Interactable
{
    [Header("Door Link")]
    [SerializeField] private DoorController doorController;

    private void Awake()
    {
        if (doorController == null)
        {
            doorController = GetComponentInParent<DoorController>();
        }
    }

    private void OnValidate()
    {
        if (doorController == null)
            doorController = GetComponentInParent<DoorController>();

        if (doorController == null)
            Debug.LogWarning($"{name}: DoorInteractable expects a DoorController on this object or a parent.");
    }

    public override bool CanInteract(GameObject interactor)
    {
        if (!base.CanInteract(interactor))
            return false;

        return GetDoorController() != null;
    }

    public override void Interact(GameObject interactor)
    {
        if (!CanInteract(interactor))
            return;

        GetDoorController().Interact();
    }

    public DoorController GetDoorController()
    {
        if (doorController == null)
        {
            doorController = GetComponentInParent<DoorController>();
        }

        return doorController;
    }
}
