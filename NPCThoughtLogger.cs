using System.Collections.Generic;
using UnityEngine;

public class NPCThoughtLogger : MonoBehaviour
{
    public enum ThoughtCategory
    {
        General,
        StateChange,
        NeedShift,
        Memory,
        Perception,
        Navigation,
        Interaction,
        Inventory,
        Summary
    }

    [Header("Logging")]
    [SerializeField] private bool enableThinkingLogs = true;
    [SerializeField] private bool includeTimestamp = false;
    [SerializeField] private float repeatedThoughtCooldown = 1.5f;

    [Header("Category Filters")]
    [SerializeField] private bool logGeneral = true;
    [SerializeField] private bool logStateChange = true;
    [SerializeField] private bool logNeedShift = true;
    [SerializeField] private bool logMemory = true;
    [SerializeField] private bool logPerception = true;
    [SerializeField] private bool logNavigation = true;
    [SerializeField] private bool logInteraction = true;
    [SerializeField] private bool logInventory = true;
    [SerializeField] private bool logSummary = true;

    [Header("Runtime View")]
    [SerializeField, TextArea(2, 4)] private string lastThought;
    [SerializeField, TextArea(2, 4)] private string lastSummary;
    [SerializeField] private ThoughtCategory lastCategory = ThoughtCategory.General;
    [SerializeField] private float lastThoughtTime = -999f;

    private readonly Dictionary<string, float> thoughtCooldowns = new Dictionary<string, float>();

    public void Think(
        string actorName,
        string stateName,
        string needName,
        float needValue,
        string urgencyBand,
        string message,
        string eventKey = null,
        ThoughtCategory category = ThoughtCategory.General,
        float cooldownOverride = -1f,
        bool bypassCooldown = false)
    {
        if (!enableThinkingLogs)
            return;

        if (string.IsNullOrWhiteSpace(message))
            return;

        if (!IsCategoryEnabled(category))
            return;

        string cooldownKey = string.IsNullOrWhiteSpace(eventKey)
            ? message
            : eventKey;

        float now = Time.time;
        float cooldown = cooldownOverride >= 0f
            ? cooldownOverride
            : Mathf.Max(0f, repeatedThoughtCooldown);

        if (!bypassCooldown &&
            cooldown > 0f &&
            thoughtCooldowns.TryGetValue(cooldownKey, out float nextAllowedTime) &&
            now < nextAllowedTime)
        {
            return;
        }

        if (!bypassCooldown && cooldown > 0f)
            thoughtCooldowns[cooldownKey] = now + cooldown;

        string safeActor = string.IsNullOrWhiteSpace(actorName) ? "NPC" : actorName;
        string safeState = string.IsNullOrWhiteSpace(stateName) ? "UnknownState" : stateName;
        string safeNeed = string.IsNullOrWhiteSpace(needName) ? "UnknownNeed" : needName;
        string safeBand = string.IsNullOrWhiteSpace(urgencyBand) ? "UnknownBand" : urgencyBand;

        string header = $"[NPC: {safeActor}] [{category}] State={safeState} | Need={safeNeed} {needValue:0.0} ({safeBand})";
        if (includeTimestamp)
            header = $"[t={now:0.0}] {header}";

        lastCategory = category;
        lastThoughtTime = now;
        if (category == ThoughtCategory.Summary)
            lastSummary = message;
        else
            lastThought = message;

        Debug.Log($"{header} | {message}", this);
    }

    private bool IsCategoryEnabled(ThoughtCategory category)
    {
        switch (category)
        {
            case ThoughtCategory.StateChange:
                return logStateChange;

            case ThoughtCategory.NeedShift:
                return logNeedShift;

            case ThoughtCategory.Memory:
                return logMemory;

            case ThoughtCategory.Perception:
                return logPerception;

            case ThoughtCategory.Navigation:
                return logNavigation;

            case ThoughtCategory.Interaction:
                return logInteraction;

            case ThoughtCategory.Inventory:
                return logInventory;

            case ThoughtCategory.Summary:
                return logSummary;

            case ThoughtCategory.General:
            default:
                return logGeneral;
        }
    }
}
