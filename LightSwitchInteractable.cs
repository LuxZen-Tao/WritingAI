using UnityEngine;

public class LightSwitchInteractable : Interactable
{
	public Light targetLight;
	public bool lightIsOn = false;

	private void Start()
	{
		if (targetLight != null)
		{
			targetLight.enabled = lightIsOn;
		}
	}

	public override void Interact(GameObject interactor)
	{
		if (!CanInteract(interactor)) return;

		lightIsOn = true;

		if (targetLight != null)
		{
			targetLight.enabled = true;
		}

		Debug.Log(interactableName + " was used by " + interactor.name);
	}

	public bool IsLightOn()
	{
		return lightIsOn;
	}
}