public class ChairInteractable : RestInteractable
{
    private void Reset()
    {
        interactableName = "Chair";
        providedNeed = NeedType.Energy;
        restRatePerSecond = 0.85f;
        maxRestPerSession = 3f;
        desirability = 0.9f;
    }
}
