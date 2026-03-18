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
		Interaction
	}

	[Header("Logging")]
	[SerializeField] private bool enableThinkingLogs = true;
	[SerializeField] private bool includeTimestamp = false;
	[SerializeField] private float repeatedThoughtCooldown = 2.5f;

	[Header("Category Filters")]
	[SerializeField] private bool logGeneral = true;
	[SerializeField] private bool logStateChange = true;
	[SerializeField] private bool logNeedShift = true;
	[SerializeField] private bool logMemory = true;
	[SerializeField] private bool logPerception = true;
	[SerializeField] private bool logInteraction = true;

	private readonly Dictionary<string, float> thoughtCooldowns = new Dictionary<string, float>();

	public void Think(
		string actorName,
		string stateName,
		string needName,
		float needValue,
		string urgencyBand,
		string message,
		string eventKey = null,
		ThoughtCategory category = ThoughtCategory.General)
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

		if (thoughtCooldowns.TryGetValue(cooldownKey, out float nextAllowedTime) && now < nextAllowedTime)
			return;

		thoughtCooldowns[cooldownKey] = now + Mathf.Max(0f, repeatedThoughtCooldown);

		string safeActor = string.IsNullOrWhiteSpace(actorName) ? "NPC" : actorName;
		string safeState = string.IsNullOrWhiteSpace(stateName) ? "UnknownState" : stateName;
		string safeNeed = string.IsNullOrWhiteSpace(needName) ? "UnknownNeed" : needName;
		string safeBand = string.IsNullOrWhiteSpace(urgencyBand) ? "UnknownBand" : urgencyBand;

		string header = "[" + safeActor +
			" | " + safeState +
			" | " + safeNeed + ": " + needValue.ToString("0.0") +
			" (" + safeBand + ")" +
			" | " + category + "]";

		if (includeTimestamp)
		{
			header = "[t=" + now.ToString("0.0") + "] " + header;
		}

		Debug.Log(header + " " + message, this);
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

		case ThoughtCategory.Interaction:
			return logInteraction;

		case ThoughtCategory.General:
		default:
			return logGeneral;
		}
	}
}