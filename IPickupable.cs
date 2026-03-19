using UnityEngine;

public interface IPickupable
{
    float GetItemValue();
    bool CanPickUp(GameObject picker);
    void OnPickedUp(NPCInventory inventory);
    void OnDropped(Vector3 position);
}
