using UnityEngine;

public class RestInteractable : Interactable, INeedSatisfier
{
    [Header("Rest Need")]
    [SerializeField] protected NeedType providedNeed = NeedType.Energy;

    [Header("Rest Session")]
    [SerializeField] protected float restRatePerSecond = 1.2f;
    [SerializeField] protected float maxRestPerSession = 4f;
    [SerializeField] protected float desirability = 1f;

    private float remainingSessionRecovery = 0f;
    private GameObject currentRestingActor;

    public float RestRatePerSecond => Mathf.Max(0f, restRatePerSecond);
    public float MaxRestPerSession => Mathf.Max(0f, maxRestPerSession);
    public float Desirability => Mathf.Max(0f, desirability);
    public float RemainingSessionRecovery => Mathf.Max(0f, remainingSessionRecovery);

    public override bool CanInteract(GameObject interactor)
    {
        if (!base.CanInteract(interactor))
            return false;

        if (interactor == null)
            return false;

        return currentRestingActor == null || currentRestingActor == interactor;
    }

    public override void Interact(GameObject interactor)
    {
        BeginRestSession(interactor);
    }

    public bool BeginRestSession(GameObject interactor)
    {
        if (!CanInteract(interactor))
            return false;

        currentRestingActor = interactor;
        remainingSessionRecovery = MaxRestPerSession;
        return true;
    }

    public float RecoverForSeconds(GameObject interactor, float deltaTime)
    {
        if (interactor == null || interactor != currentRestingActor)
            return 0f;

        if (deltaTime <= 0f || remainingSessionRecovery <= 0f || RestRatePerSecond <= 0f)
            return 0f;

        float recoveryAmount = Mathf.Min(RestRatePerSecond * deltaTime, remainingSessionRecovery);
        remainingSessionRecovery -= recoveryAmount;
        return recoveryAmount;
    }

    public bool IsSessionExhausted()
    {
        return remainingSessionRecovery <= 0.0001f;
    }

    public bool HasActiveSession(GameObject interactor)
    {
        return currentRestingActor == interactor && remainingSessionRecovery > 0.0001f;
    }

    public void EndRestSession(GameObject interactor)
    {
        if (interactor == null || currentRestingActor != interactor)
            return;

        currentRestingActor = null;
        remainingSessionRecovery = 0f;
    }

    public NeedType GetNeedType() => providedNeed;

    public float GetNeedValue() => MaxRestPerSession;
}
