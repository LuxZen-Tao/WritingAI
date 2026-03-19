using UnityEngine;

public class AppleInteractable : FoodInteractable
{
    private void Reset()
    {
        interactableName = "Apple";
        hungerRestoreAmount = 4f;
        itemValue = 1f;
    }
}
