using UnityEngine;

public class KeyInteractable : Interactable, IPickupable, IKeyItem
{
    [Header("Key Settings")]
    [SerializeField] private string keyId = "default_key";

    [Header("Inventory")]
    public bool canBePickedUp = true;
    public float itemValue = 0.75f;

    private NPCInventory carriedByInventory;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(keyId))
            Debug.LogWarning($"{name}: KeyInteractable keyId is empty.");

        Collider itemCollider = GetComponentInChildren<Collider>();
        if (itemCollider == null)
            Debug.LogWarning($"{name}: KeyInteractable should have a collider for perception/pickup.");
    }

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
