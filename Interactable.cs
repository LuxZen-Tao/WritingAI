using UnityEngine;

public abstract class Interactable : MonoBehaviour
{
	public string interactableName = "Interactable";
	public float interactionRange = 1.5f;
	public bool isEnabled = true;

	public virtual bool CanInteract(GameObject interactor)
	{
		return isEnabled;
	}

	public abstract void Interact(GameObject interactor);

	public virtual Vector3 GetInteractionPoint()
	{
		return transform.position;
	}
}