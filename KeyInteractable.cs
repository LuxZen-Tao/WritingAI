using UnityEngine;

public class KeyInteractable : Interactable, IPickupable, IKeyItem
{
    [Header("Key Settings")]
    [SerializeField] private string keyId = "default_key";

    [Header("Inventory")]
    public bool canBePickedUp = true;
    public float itemValue = 0.75f;

    private NPCInventory carriedByInventory;

    public string GetKeyId()
    {
        return keyId;
    }

    public override void Interact(GameObject interactor)
    {
        // Keys are used by door handling from inventory.
    }

    public float GetItemValue()
    {
        return itemValue;
    }

    public bool CanPickUp(GameObject picker)
    {
        return canBePickedUp && isEnabled && carriedByInventory == null;
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
