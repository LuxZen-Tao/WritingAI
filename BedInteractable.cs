public class BedInteractable : RestInteractable
{
    private void Reset()
    {
        interactableName = "Bed";
        providedNeed = NeedType.Energy;
        restRatePerSecond = 1.8f;
        maxRestPerSession = 7f;
        desirability = 1.5f;
    }
}
