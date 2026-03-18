using UnityEngine;

[System.Serializable]
public class RememberedComfortZone
{
	public RoomArea room;
	public Vector3 lastKnownPosition;
	public float lastSeenTime;
	public bool wasLitWhenLastSeen;

	public RememberedComfortZone(RoomArea room, Vector3 lastKnownPosition, float lastSeenTime, bool wasLitWhenLastSeen)
	{
		this.room = room;
		this.lastKnownPosition = lastKnownPosition;
		this.lastSeenTime = lastSeenTime;
		this.wasLitWhenLastSeen = wasLitWhenLastSeen;
	}
}