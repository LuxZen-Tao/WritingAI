using UnityEngine;

public class FoodInteractable : Interactable, INeedSatisfier, IPickupable
{
    [Header("Food Settings")]
    public float hungerRestoreAmount = 3f;
    public bool consumeOnUse = true;
    public bool disableObjectOnUse = true;

    [Header("Inventory")]
    public bool canBePickedUp = true;
    public float itemValue = 1f;

    private bool hasBeenConsumed = false;
    private NPCInventory carriedByInventory = null;

    public bool IsInInventory => carriedByInventory != null;

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
            needsManager.ModifyNeed(NeedType.Hunger, hungerRestoreAmount);

        if (!consumeOnUse)
            return;

        Consume();
    }

    protected void Consume()
    {
        hasBeenConsumed = true;
        isEnabled = false;

        if (carriedByInventory != null)
        {
            carriedByInventory.RemoveItem(this);
            carriedByInventory = null;
        }

        if (disableObjectOnUse)
            gameObject.SetActive(false);
        else
            Destroy(gameObject);
    }

    // INeedSatisfier
    public NeedType GetNeedType() => NeedType.Hunger;
    public float GetNeedValue() => hungerRestoreAmount;

    // IPickupable
    public float GetItemValue() => itemValue;

    public bool CanPickUp(GameObject picker)
    {
        return canBePickedUp && isEnabled && !hasBeenConsumed && carriedByInventory == null;
    }

    public void OnPickedUp(NPCInventory inventory)
    {
        carriedByInventory = inventory;
        gameObject.SetActive(false);
    }

    public void OnDropped(Vector3 position)
    {
        carriedByInventory = null;
        transform.position = position;
        gameObject.SetActive(true);
    }
}
