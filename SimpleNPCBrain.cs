using UnityEngine;

using UnityEngine;

public class SimpleNPCBrain : MonoBehaviour
{
	public enum AIState
	{
		Idle,
		CheckNeed,
		MoveToTarget,
		InteractWithTarget,
		Done
	}

	public AIState currentState = AIState.Idle;

	[Header("Need Setup")]
	public LightSwitchInteractable roomLightSwitch;

	[Header("Current Target")]
	public Interactable currentTarget;

	[Header("Movement")]
	public float moveSpeed = 2f;

	private void Start()
	{
		ChangeState(AIState.CheckNeed);
	}

	private void Update()
	{
		switch (currentState)
		{
		case AIState.Idle:
			break;

		case AIState.CheckNeed:
			EvaluateNeeds();
			break;

		case AIState.MoveToTarget:
			MoveToCurrentTarget();
			break;

		case AIState.InteractWithTarget:
			InteractWithCurrentTarget();
			break;

		case AIState.Done:
			break;
		}
	}

	private void EvaluateNeeds()
	{
		if (roomLightSwitch == null)
		{
			Debug.LogWarning("No roomLightSwitch assigned on " + gameObject.name);
			ChangeState(AIState.Done);
			return;
		}

		if (!roomLightSwitch.IsLightOn())
		{
			Debug.Log("Need comfort. Room is dark. Targeting switch.");
			currentTarget = roomLightSwitch;
			ChangeState(AIState.MoveToTarget);
		}
		else
		{
			Debug.Log("Light already on. No action needed.");
			ChangeState(AIState.Done);
		}
	}

	private void MoveToCurrentTarget()
	{
		if (currentTarget == null)
		{
			Debug.LogWarning("No target to move to.");
			ChangeState(AIState.Done);
			return;
		}

		Vector3 targetPosition = currentTarget.GetInteractionPoint();
		targetPosition.y = transform.position.y;

		transform.position = Vector3.MoveTowards(
			transform.position,
			targetPosition,
			moveSpeed * Time.deltaTime
		);

		float distance = Vector3.Distance(transform.position, targetPosition);

		if (distance <= currentTarget.interactionRange)
		{
			ChangeState(AIState.InteractWithTarget);
		}
	}

	private void InteractWithCurrentTarget()
	{
		if (currentTarget == null)
		{
			Debug.LogWarning("No target to interact with.");
			ChangeState(AIState.Done);
			return;
		}

		if (currentTarget.CanInteract(gameObject))
		{
			currentTarget.Interact(gameObject);
		}

		ChangeState(AIState.Done);
	}

	private void ChangeState(AIState newState)
	{
		currentState = newState;
		Debug.Log("State changed to: " + currentState);
	}
}