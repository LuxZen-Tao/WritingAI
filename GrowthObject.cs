using System.Collections;
using UnityEngine;

public class GrowthObject : MonoBehaviour
{
	private float minScale;
	private float maxScale;
	private int steps;
	private float stepTime;

	private bool isFullyGrown = false;
	public bool IsAvailable => isFullyGrown;

	[Header("Post-Growth Physics")]
	public float enablePhysicsDelay = 0.5f;

	private Collider col;
	private Rigidbody rb;

	public void Init(float min, float max, int growthSteps, float timePerStep)
	{
		minScale = min;
		maxScale = max;
		steps = growthSteps;
		stepTime = timePerStep;

		SetupInitialState();
		StartCoroutine(Grow());
	}

	private void SetupInitialState()
	{
		col = GetComponent<Collider>();
		rb = GetComponent<Rigidbody>();

		// Disable collider during growth
		if (col != null)
			col.enabled = false;

		// Disable physics during growth
		if (rb != null)
		{
			rb.isKinematic = true;
			rb.useGravity = false;
			rb.linearVelocity = Vector3.zero;
			rb.angularVelocity = Vector3.zero;
		}
	}

	private IEnumerator Grow()
	{
		for (int i = 1; i <= steps; i++)
		{
			float t = (float)i / steps;
			float scale = Mathf.Lerp(minScale, maxScale, t);

			transform.localScale = Vector3.one * scale;

			yield return new WaitForSeconds(stepTime);
		}

		yield return StartCoroutine(EnableAfterGrowth());
	}

	private IEnumerator EnableAfterGrowth()
	{
		isFullyGrown = true;

		Debug.Log($"[{name}] Fully grown and now becoming interactable.");

		// Small delay before enabling physics (feels more natural)
		yield return new WaitForSeconds(enablePhysicsDelay);

		// Enable collider
		if (col != null)
			col.enabled = true;

		// Enable physics
		if (rb != null)
		{
			rb.isKinematic = false;
			rb.useGravity = true;
		}
	}
}