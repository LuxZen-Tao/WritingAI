using UnityEngine;

[System.Serializable]
public class RememberedLocation
{
	public Vector3 position;
	public float timeStored;

	public RememberedLocation(Vector3 position, float timeStored)
	{
		this.position = position;
		this.timeStored = timeStored;
	}
}