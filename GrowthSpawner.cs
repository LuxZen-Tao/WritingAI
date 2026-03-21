using System.Collections;
using UnityEngine;

public class GrowthSpawner : MonoBehaviour
{
	[Header("Spawn Settings")]
	public GameObject prefab;
	public float spawnInterval = 10f; // time between spawns

	[Header("Growth Settings")]
	public float growthStepTime = 2f; // time between growth stages
	public int growthSteps = 5;       // number of steps (0.2 → 1.0)
	public float minScale = 0.2f;
	public float maxScale = 1f;

	[Header("Spawn Area")]
	public Vector3 spawnOffset; // optional offset

	private void Start()
	{
		StartCoroutine(SpawnLoop());
	}

	private IEnumerator SpawnLoop()
	{
		while (true)
		{
			Spawn();
			yield return new WaitForSeconds(spawnInterval);
		}
	}

	private void Spawn()
	{
		if (prefab == null)
		{
			Debug.LogWarning("GrowthSpawner: No prefab assigned.");
			return;
		}

		GameObject obj = Instantiate(prefab, transform.position + spawnOffset, Quaternion.identity);

		// Start at min scale
		obj.transform.localScale = Vector3.one * minScale;

		// Add / get growth component
		GrowthObject growth = obj.GetComponent<GrowthObject>();
		if (growth == null)
		{
			growth = obj.AddComponent<GrowthObject>();
		}

		growth.Init(minScale, maxScale, growthSteps, growthStepTime);
	}
}