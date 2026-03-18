using UnityEngine;

[System.Serializable]
public class RememberedComfortZone
{
	public RoomArea room;
	public Vector3 lastKnownPosition;
	public float lastSeenTime;

	public RememberedComfortZone(RoomArea room, Vector3 lastKnownPosition, float lastSeenTime)
	{
		this.room = room;
		this.lastKnownPosition = lastKnownPosition;
		this.lastSeenTime = lastSeenTime;
	}
}