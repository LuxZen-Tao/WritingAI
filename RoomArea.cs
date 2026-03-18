using UnityEngine;
using UnityEngine.AI;

public class RoomArea : MonoBehaviour
{
	[Header("Room Settings")]
	public string roomName = "Room";

	[Header("World Link")]
	public WorldArea worldArea;

	[Header("Natural Light")]
	public bool receivesDaylight = false;

	[Header("Artificial Lights Controlled By Switches")]
	public Light[] controlledLights;

	[Header("Fallback")]
	public bool fallbackLitState = false;

	private Collider cachedRoomCollider;
	private Collider roomCollider;

	private void Awake()
	{
		roomCollider = GetComponent<Collider>();

		if (roomCollider == null)
		{
			Debug.LogError(roomName + " needs a Collider to define room bounds.");
		}
	}

	public bool IsLit()
	{
		if (HasDaylight())
			return true;

		if (AreArtificialLightsOn())
			return true;

		return fallbackLitState;
	}

	public bool HasDaylight()
	{
		if (!receivesDaylight)
			return false;

		if (worldArea == null)
			return false;

		return worldArea.IsDaytime();
	}

	public void SetArtificialLights(bool litState)
	{
		fallbackLitState = litState;

		if (controlledLights != null && controlledLights.Length > 0)
		{
			for (int i = 0; i < controlledLights.Length; i++)
			{
				if (controlledLights[i] != null)
				{
					controlledLights[i].enabled = litState;
				}
			}
		}

		Debug.Log(roomName + " artificial light state changed to: " + litState);
	}

	public bool AreArtificialLightsOn()
	{
		if (controlledLights != null && controlledLights.Length > 0)
		{
			for (int i = 0; i < controlledLights.Length; i++)
			{
				if (controlledLights[i] != null && controlledLights[i].enabled)
				{
					return true;
				}
			}

			return false;
		}

		return fallbackLitState;
	}

	public bool ContainsPoint(Vector3 point)
	{
		if (roomCollider == null)
			return false;

		Vector3 closest = roomCollider.ClosestPoint(point);
		return Vector3.Distance(closest, point) < 0.1f;
	}

	public Vector3 GetRoomCenterPoint()
	{
		if (roomCollider != null)
		{
			return roomCollider.bounds.center;
		}

		return transform.position;
	}

	public bool TryGetRandomNavigablePointInsideRoom(out Vector3 point)
	{
		point = Vector3.zero;

		if (roomCollider == null)
			return false;

		Bounds bounds = roomCollider.bounds;

		for (int i = 0; i < 20; i++)
		{
			Vector3 candidate = new Vector3(
				Random.Range(bounds.min.x, bounds.max.x),
				bounds.center.y,
				Random.Range(bounds.min.z, bounds.max.z)
			);

			if (!ContainsPoint(candidate))
				continue;

			if (NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 1.5f, NavMesh.AllAreas))
			{
				if (!ContainsPoint(navHit.position))
					continue;

				point = navHit.position;
				return true;
			}
		}

		return false;
	}

	public bool TryGetRandomPointInBounds(float inset, out Vector3 worldPoint)
	{
		worldPoint = transform.position;

		if (!TryGetRoomBounds(out Bounds bounds))
			return false;

		float clampedInsetX = Mathf.Clamp(inset, 0f, bounds.extents.x * 0.95f);
		float clampedInsetZ = Mathf.Clamp(inset, 0f, bounds.extents.z * 0.95f);

		float minX = bounds.min.x + clampedInsetX;
		float maxX = bounds.max.x - clampedInsetX;
		float minZ = bounds.min.z + clampedInsetZ;
		float maxZ = bounds.max.z - clampedInsetZ;

		if (minX > maxX || minZ > maxZ)
			return false;

		for (int i = 0; i < 8; i++)
		{
			Vector3 candidate = new Vector3(
				Random.Range(minX, maxX),
				transform.position.y,
				Random.Range(minZ, maxZ));

			if (!ContainsPoint(candidate))
				continue;

			worldPoint = candidate;
			return true;
		}

		return false;
	}

	private bool TryGetRoomBounds(out Bounds bounds)
	{
		if (!TryGetRoomCollider(out Collider foundRoomCollider))
		{
			bounds = default;
			return false;
		}

		bounds = foundRoomCollider.bounds;
		return true;
	}

	private bool TryGetRoomCollider(out Collider foundRoomCollider)
	{
		if (cachedRoomCollider == null)
		{
			cachedRoomCollider = GetComponent<Collider>();

			if (cachedRoomCollider == null)
			{
				cachedRoomCollider = GetComponentInChildren<Collider>();
			}
		}

		foundRoomCollider = cachedRoomCollider;
		return foundRoomCollider != null;
	}
}