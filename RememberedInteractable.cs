using UnityEngine;

[System.Serializable]
public class RememberedInteractable
{
	public Interactable interactable;
	public NeedType needType;
	public Vector3 lastKnownPosition;

	public RememberedInteractable(Interactable interactable, NeedType needType, Vector3 lastKnownPosition)
	{
		this.interactable = interactable;
		this.needType = needType;
		this.lastKnownPosition = lastKnownPosition;
	}
}