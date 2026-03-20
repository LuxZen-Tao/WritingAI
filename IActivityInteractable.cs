using UnityEngine;

public interface IActivityInteractable
{
    ActivityType GetActivityType();
    float GetDesirability();
    float GetMinimumUseDuration();
    float GetMaximumUseDuration();
    bool IsInterruptible();
    Vector3 GetActivityAnchorPoint();
    bool BeginActivity(GameObject interactor);
    void EndActivity(GameObject interactor);
}
