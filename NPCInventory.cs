using System.Collections.Generic;
using UnityEngine;

public class NPCInventory : MonoBehaviour
{
    [Header("Inventory Settings")]
    public int maxSlots = 3;

    [Header("Hand Slot")]
    [SerializeField] private Transform handAnchor;
    [SerializeField] private Vector3 heldItemLocalPosition = Vector3.zero;
    [SerializeField] private Vector3 heldItemLocalEulerAngles = Vector3.zero;
    private Vector3 heldItemOriginalLocalScale = Vector3.one;

    [Header("Drop Offset")]
    public float dropForwardOffset = 0.6f;

    [Header("Contents")]
    public List<Interactable> items = new List<Interactable>();

    [SerializeField] private Interactable handItem;

    private Rigidbody heldItemRigidbody;
    private bool heldItemHadRigidbody;
    private bool heldItemRigidbodyWasKinematic;
    private bool heldItemRigidbodyUsedGravity;

    private Collider[] heldItemColliders;
    private bool[] heldItemColliderStates;

    public bool IsFull => items.Count >= maxSlots;
    public int Count => items.Count;
    public bool HasHandItem => handItem != null;

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
        if (item != null && handItem == item)
        {
            ClearHandItem();
            return;
        }

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
        ClearHandItem();

        for (int i = items.Count - 1; i >= 0; i--)
            DropItem(items[i]);
    }

    public Interactable GetHandItem()
    {
        return handItem;
    }

    public bool TrySetHandItem(Interactable item)
    {
        if (item == null || handItem != null)
            return false;

        IPickupable pickupable = item as IPickupable;
        if (pickupable == null)
            return false;

        bool isInPocketInventory = items.Contains(item);
        if (!isInPocketInventory)
        {
            if (!pickupable.CanPickUp(gameObject))
                return false;

            pickupable.OnPickedUp(this);
        }
        else
        {
            items.Remove(item);
        }

        handItem = item;
        AttachItemToHand(handItem);
        return true;
    }

    public bool TryMoveHandItemToInventory()
    {
        if (handItem == null || IsFull)
            return false;

        Interactable previousHandItem = handItem;
        handItem = null;

        DetachItemFromHand(previousHandItem);
        previousHandItem.gameObject.SetActive(false);
        items.Add(previousHandItem);
        return true;
    }

    public bool TryMoveInventoryItemToHand(Interactable item)
    {
        if (item == null || handItem != null || !items.Contains(item))
            return false;

        items.Remove(item);
        handItem = item;
        AttachItemToHand(handItem);
        return true;
    }

    public bool TrySwapHandItemWithInventoryItem(Interactable pocketItem)
    {
        if (pocketItem == null || handItem == null || !items.Contains(pocketItem))
            return false;

        Interactable previousHandItem = handItem;
        items.Remove(pocketItem);

        handItem = null;
        DetachItemFromHand(previousHandItem);
        previousHandItem.gameObject.SetActive(false);
        items.Add(previousHandItem);

        handItem = pocketItem;
        AttachItemToHand(handItem);
        return true;
    }

    public void ClearHandItem()
    {
        if (handItem == null)
            return;

        Interactable itemToClear = handItem;
        handItem = null;

        DetachItemFromHand(itemToClear);

        IPickupable pickupable = itemToClear as IPickupable;
        if (pickupable != null)
        {
            Vector3 dropPosition = transform.position + transform.forward * dropForwardOffset;
            pickupable.OnDropped(dropPosition);
        }
    }

    private void AttachItemToHand(Interactable item)
    {
        if (item == null)
            return;

        item.gameObject.SetActive(true);

        Transform targetAnchor = handAnchor != null ? handAnchor : transform;

        // Preserve world scale before reparenting.
        Vector3 originalWorldScale = item.transform.lossyScale;

        item.transform.SetParent(targetAnchor, true);

        // Place/orient in hand.
        item.transform.localPosition = heldItemLocalPosition;
        item.transform.localRotation = Quaternion.Euler(heldItemLocalEulerAngles);

        // Rebuild local scale so world scale stays visually the same.
        Vector3 parentScale = targetAnchor.lossyScale;
        item.transform.localScale = new Vector3(
            parentScale.x != 0f ? originalWorldScale.x / parentScale.x : originalWorldScale.x,
            parentScale.y != 0f ? originalWorldScale.y / parentScale.y : originalWorldScale.y,
            parentScale.z != 0f ? originalWorldScale.z / parentScale.z : originalWorldScale.z
        );

        heldItemRigidbody = item.GetComponent<Rigidbody>();
        heldItemHadRigidbody = heldItemRigidbody != null;
        if (heldItemHadRigidbody)
        {
            heldItemRigidbodyWasKinematic = heldItemRigidbody.isKinematic;
            heldItemRigidbodyUsedGravity = heldItemRigidbody.useGravity;
            heldItemRigidbody.isKinematic = true;
            heldItemRigidbody.useGravity = false;
            heldItemRigidbody.linearVelocity = Vector3.zero;
            heldItemRigidbody.angularVelocity = Vector3.zero;
        }

        heldItemColliders = item.GetComponentsInChildren<Collider>(true);
        heldItemColliderStates = new bool[heldItemColliders.Length];
        for (int i = 0; i < heldItemColliders.Length; i++)
        {
            if (heldItemColliders[i] == null)
                continue;

            heldItemColliderStates[i] = heldItemColliders[i].enabled;
            heldItemColliders[i].enabled = false;
        }
    }
    private void DetachItemFromHand(Interactable item)
    {
        if (item == null)
            return;

        item.transform.SetParent(null, true);

        if (heldItemHadRigidbody && heldItemRigidbody != null)
        {
            heldItemRigidbody.isKinematic = heldItemRigidbodyWasKinematic;
            heldItemRigidbody.useGravity = heldItemRigidbodyUsedGravity;
        }

        if (heldItemColliders != null && heldItemColliderStates != null)
        {
            int count = Mathf.Min(heldItemColliders.Length, heldItemColliderStates.Length);
            for (int i = 0; i < count; i++)
            {
                if (heldItemColliders[i] == null)
                    continue;

                heldItemColliders[i].enabled = heldItemColliderStates[i];
            }
        }

        heldItemRigidbody = null;
        heldItemHadRigidbody = false;
        heldItemColliders = null;
        heldItemColliderStates = null;
    }

    public Interactable GetLeastValuableItem()
    {
        Interactable leastValuable = null;
        float lowestValue = float.MaxValue;

        if (handItem != null && TryGetPickupableValue(handItem, out float handValue))
        {
            leastValuable = handItem;
            lowestValue = handValue;
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (!TryGetPickupableValue(items[i], out float value))
                continue;

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
        INeedSatisfier handSatisfier = handItem as INeedSatisfier;
        if (handSatisfier != null && handSatisfier.GetNeedType() == needType)
            return true;

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

        INeedSatisfier handSatisfier = handItem as INeedSatisfier;
        if (handSatisfier != null && handSatisfier.GetNeedType() == needType)
        {
            best = handItem;
            bestValue = handSatisfier.GetNeedValue();
        }

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

        IKeyItem handKeyItem = handItem as IKeyItem;
        if (handKeyItem != null && DoorController.KeyIdsMatch(lockId, handKeyItem.GetKeyId()))
        {
            matchingKey = handKeyItem;
            return true;
        }

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

    private bool TryGetPickupableValue(Interactable item, out float value)
    {
        value = 0f;

        if (item == null)
            return false;

        IPickupable pickupable = item as IPickupable;
        if (pickupable == null)
            return false;

        value = pickupable.GetItemValue();
        return true;
    }
}
