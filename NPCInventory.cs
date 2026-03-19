using System.Collections.Generic;
using UnityEngine;

public class NPCInventory : MonoBehaviour
{
    [Header("Inventory Settings")]
    public int maxSlots = 3;

    [Header("Drop Offset")]
    public float dropForwardOffset = 0.6f;

    [Header("Contents")]
    public List<Interactable> items = new List<Interactable>();

    public bool IsFull => items.Count >= maxSlots;
    public int Count => items.Count;

    public bool TryAddItem(Interactable item)
    {
        if (item == null)
            return false;

        if (IsFull)
            return false;

        IPickupable pickupable = item as IPickupable;
        if (pickupable == null || !pickupable.CanPickUp(gameObject))
            return false;

        items.Add(item);
        pickupable.OnPickedUp(this);
        return true;
    }

    public void RemoveItem(Interactable item)
    {
        items.Remove(item);
    }

    public void DropItem(Interactable item)
    {
        if (item == null || !items.Contains(item))
            return;

        items.Remove(item);

        IPickupable pickupable = item as IPickupable;
        if (pickupable != null)
        {
            Vector3 dropPosition = transform.position + transform.forward * dropForwardOffset;
            pickupable.OnDropped(dropPosition);
        }
    }

    public void DropAll()
    {
        for (int i = items.Count - 1; i >= 0; i--)
            DropItem(items[i]);
    }

    public Interactable GetLeastValuableItem()
    {
        Interactable leastValuable = null;
        float lowestValue = float.MaxValue;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null)
                continue;

            IPickupable pickupable = items[i] as IPickupable;
            if (pickupable == null)
                continue;

            float value = pickupable.GetItemValue();
            if (value < lowestValue)
            {
                lowestValue = value;
                leastValuable = items[i];
            }
        }

        return leastValuable;
    }

    public bool HasItemForNeed(NeedType needType)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null)
                continue;

            INeedSatisfier satisfier = items[i] as INeedSatisfier;
            if (satisfier != null && satisfier.GetNeedType() == needType)
                return true;
        }

        return false;
    }

    public Interactable GetBestItemForNeed(NeedType needType)
    {
        Interactable best = null;
        float bestValue = -1f;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null)
                continue;

            INeedSatisfier satisfier = items[i] as INeedSatisfier;
            if (satisfier == null || satisfier.GetNeedType() != needType)
                continue;

            float value = satisfier.GetNeedValue();
            if (value > bestValue)
            {
                bestValue = value;
                best = items[i];
            }
        }

        return best;
    }

    public bool TryGetMatchingKey(string lockId, out IKeyItem matchingKey)
    {
        matchingKey = null;

        if (string.IsNullOrWhiteSpace(lockId))
            return false;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null)
                continue;

            IKeyItem keyItem = items[i] as IKeyItem;
            if (keyItem == null)
                continue;

            if (!DoorController.KeyIdsMatch(lockId, keyItem.GetKeyId()))
                continue;

            matchingKey = keyItem;
            return true;
        }

        return false;
    }
}
