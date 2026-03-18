using System.Collections.Generic;
using UnityEngine;

public class NPCThoughtLogger : MonoBehaviour
{
    public enum ThoughtCategory
    {
        General,
        State,
        Need,
        Perception,
        Memory,
        Recall,
        Explore,
        Idle,
        Target,
        Interaction,
        Failure
    }

    [Header("Thought Logging")]
    public bool enableThoughtLogs = true;
    public bool includeTimestamp = false;
    public bool includeCategory = false;
    public float repeatedThoughtCooldown = 2.5f;

    private readonly Dictionary<string, float> thoughtCooldowns = new Dictionary<string, float>();

    public void Think(
        string npcName,
        string stateName,
        string needName,
        float needValue,
        string urgencyBand,
        string message,
        string eventKey = null,
        ThoughtCategory category = ThoughtCategory.General)
    {
        if (!enableThoughtLogs || string.IsNullOrEmpty(message))
            return;

        string key = string.IsNullOrEmpty(eventKey)
            ? category + ":" + message
            : category + ":" + eventKey;

        float now = Time.time;
        if (thoughtCooldowns.TryGetValue(key, out float nextAllowedTime) && now < nextAllowedTime)
            return;

        thoughtCooldowns[key] = now + Mathf.Max(0f, repeatedThoughtCooldown);

        string safeName = string.IsNullOrWhiteSpace(npcName) ? "NPC" : npcName;
        string safeState = string.IsNullOrWhiteSpace(stateName) ? "UnknownState" : stateName;
        string safeNeed = string.IsNullOrWhiteSpace(needName) ? "Need" : needName;
        string safeUrgency = string.IsNullOrWhiteSpace(urgencyBand) ? "Stable" : urgencyBand;

        string header = "[" + safeName + " | " + safeState + " | " + safeNeed + ": " + needValue.ToString("0.0") + " (" + safeUrgency + ")]";

        if (includeTimestamp)
        {
            header = "[t=" + Time.time.ToString("0.0") + "] " + header;
        }

        if (includeCategory)
        {
            header += " [" + category + "]";
        }

        Debug.Log(header + " " + message);
    }
}
