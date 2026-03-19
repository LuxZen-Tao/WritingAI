public class ChairInteractable : RestInteractable
{
    private void Reset()
    {
        interactableName = "Chair";
        providedNeed = NeedType.Energy;
        restRatePerSecond = 0.85f;
        maxRestPerSession = 3f;
        minimumRestDuration = 2.5f;
        maximumRestDuration = 6f;
        desirability = 0.9f;
    }
}
