private void InteractWithCurrentTarget()
{
    if (currentTarget == null)
    {
        Debug.LogWarning("No target to interact with.");
        ChangeState(AIState.Idle);
        return;
    }

    agent.ResetPath();

    if (currentTarget.CanInteract(gameObject))
    {
        currentTarget.Interact(gameObject);
    }

    currentTarget = null;
    currentMemoryTarget = null;
    hasExplorePoint = false;
    hasIdlePoint = false;
    ChangeState(AIState.Idle);
}