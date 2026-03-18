using UnityEngine;

public abstract class Interactable : MonoBehaviour
{
    [Header("Interactable Settings")]
    public string interactableName = "Interactable";
    public float interactionRange = 1.5f;
    public bool isEnabled = true;
    public Transform interactionPoint;

    public virtual bool CanInteract(GameObject interactor)
    {
        return isEnabled;
    }

    public abstract void Interact(GameObject interactor);

    public virtual Vector3 GetInteractionPoint()
    {
        if (interactionPoint != null)
        {
            return interactionPoint.position;
        }

        return transform.position;
    }
}