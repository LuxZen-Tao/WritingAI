using UnityEngine;

public class AppleInteractable : Interactable, INeedSatisfier
{
    [Header("Apple Settings")]
    public float hungerRestoreAmount = 4f;
    public bool consumeOnUse = true;
    public bool disableObjectOnUse = true;

    private bool hasBeenConsumed = false;

    public override bool CanInteract(GameObject interactor)
    {
        if (!base.CanInteract(interactor))
            return false;

        if (consumeOnUse && hasBeenConsumed)
            return false;

        return true;
    }

    public override void Interact(GameObject interactor)
    {
        if (!CanInteract(interactor))
            return;

        NeedsManager needsManager = interactor.GetComponent<NeedsManager>();
        if (needsManager != null)
        {
            needsManager.ModifyNeed(NeedType.Hunger, hungerRestoreAmount);
        }

        if (!consumeOnUse)
            return;

        hasBeenConsumed = true;
        isEnabled = false;

        if (disableObjectOnUse)
        {
            gameObject.SetActive(false);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public NeedType GetNeedType()
    {
        return NeedType.Hunger;
    }

    public float GetNeedValue()
    {
        return hungerRestoreAmount;
    }
}
