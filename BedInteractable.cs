public class BedInteractable : RestInteractable
{
    private void Reset()
    {
        interactableName = "Bed";
        providedNeed = NeedType.Energy;
        restRatePerSecond = 1.8f;
        maxRestPerSession = 7f;
        minimumRestDuration = 4.5f;
        maximumRestDuration = 12f;
        desirability = 1.5f;
    }
}
