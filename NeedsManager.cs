using System;
using System.Collections.Generic;
using UnityEngine;

public class NeedsManager : MonoBehaviour
{
    public enum NeedUrgencyBand
    {
        Critical,
        Urgent,
        Low,
        Stable,
        Abundant
    }

    [Serializable]
    public class NeedState
    {
        public NeedType needType = NeedType.Comfort;
        public float currentValue = 10f;
        public float maxValue = 10f;
        public NeedBandThresholds bandThresholds = new NeedBandThresholds();
        public float decayPerSecond = 1f;
        public float recoveryPerSecond = 0f;
    }

    [Serializable]
    public class NeedBandThresholds
    {
        public float criticalThreshold = 2f;
        public float urgentThreshold = 5f;
        public float lowThreshold = 7f;
        public float stableThreshold = 9f;
    }

    [Header("Needs")]
    public List<NeedState> needs = new List<NeedState>
    {
        new NeedState
        {
            needType = NeedType.Comfort,
            currentValue = 10f,
            maxValue = 10f,
            bandThresholds = new NeedBandThresholds
            {
                criticalThreshold = 2f,
                urgentThreshold = 5f,
                lowThreshold = 7f,
                stableThreshold = 9f
            },
            decayPerSecond = 2f,
            recoveryPerSecond = 1f
        },
        new NeedState
        {
            needType = NeedType.Hunger,
            currentValue = 10f,
            maxValue = 10f,
            bandThresholds = new NeedBandThresholds
            {
                criticalThreshold = 2f,
                urgentThreshold = 4f,
                lowThreshold = 7f,
                stableThreshold = 9f
            },
            decayPerSecond = 0.35f,
            recoveryPerSecond = 0f
        },
        new NeedState
        {
            needType = NeedType.Energy,
            currentValue = 10f,
            maxValue = 10f,
            bandThresholds = new NeedBandThresholds
            {
                criticalThreshold = 2f,
                urgentThreshold = 4f,
                lowThreshold = 7f,
                stableThreshold = 9f
            },
            decayPerSecond = 0.45f,
            recoveryPerSecond = 0f
        }
    };

    private void Awake()
    {
        EnsureNeed(NeedType.Comfort);
        EnsureNeed(NeedType.Hunger);
        EnsureNeed(NeedType.Energy);
        ClampAll();
    }

    public void TickNeeds(bool isInComfortingArea, float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        for (int i = 0; i < needs.Count; i++)
        {
            NeedState state = needs[i];
            if (state == null)
                continue;

            float delta = GetPassiveDelta(state, isInComfortingArea) * deltaTime;
            if (Mathf.Abs(delta) > 0.0001f)
            {
                ModifyNeed(state.needType, delta);
            }
            else
            {
                ClampNeed(state);
            }
        }
    }

    public bool HasUrgentNeed()
    {
        for (int i = 0; i < needs.Count; i++)
        {
            NeedState state = needs[i];
            if (state == null)
                continue;

            if (IsNeedUrgent(state))
                return true;
        }

        return false;
    }

    public bool HasUrgentNeed(out NeedType mostUrgentNeed)
    {
        mostUrgentNeed = GetMostUrgentNeed();
        return IsNeedUrgent(mostUrgentNeed);
    }

    public NeedType GetMostUrgentNeed()
    {
        NeedType bestNeed = NeedType.Comfort;
        float bestScore = float.MinValue;
        bool foundUrgent = false;

        for (int i = 0; i < needs.Count; i++)
        {
            NeedState state = needs[i];
            if (state == null)
                continue;

            if (!IsNeedUrgent(state))
                continue;

            float score = GetNeedPriorityScore(state);
            if (score > bestScore)
            {
                bestScore = score;
                bestNeed = state.needType;
                foundUrgent = true;
            }
        }

        return foundUrgent ? bestNeed : NeedType.Comfort;
    }

    public bool IsNeedUrgent(NeedType needType)
    {
        NeedState state = GetState(needType);
        return IsNeedUrgent(state);
    }

    public bool ShouldOpportunisticallySatisfy(NeedType needType)
    {
        NeedState state = GetState(needType);
        if (state == null)
            return false;

        NeedUrgencyBand band = GetNeedUrgencyBand(state);
        return band == NeedUrgencyBand.Low;
    }

    public NeedUrgencyBand GetNeedUrgencyBand(NeedType needType)
    {
        NeedState state = GetState(needType);
        return GetNeedUrgencyBand(state);
    }

    public float GetNeedPriorityScore(NeedType needType)
    {
        NeedState state = GetState(needType);
        return GetNeedPriorityScore(state);
    }

    public float GetNeedMoveSpeedMultiplier(NeedType needType)
    {
        NeedUrgencyBand band = GetNeedUrgencyBand(needType);

        switch (band)
        {
            case NeedUrgencyBand.Critical:
                return 1.35f;
            case NeedUrgencyBand.Urgent:
                return 1.15f;
            case NeedUrgencyBand.Low:
                return 1f;
            case NeedUrgencyBand.Stable:
                return 0.9f;
            case NeedUrgencyBand.Abundant:
                return 0.8f;
            default:
                return 1f;
        }
    }

    public void ModifyNeed(NeedType needType, float amount)
    {
        NeedState state = GetState(needType);
        if (state == null)
            return;

        state.currentValue += amount;
        ClampNeed(state);
    }

    public float GetNeedValue(NeedType needType)
    {
        NeedState state = GetState(needType);
        return state != null ? state.currentValue : 0f;
    }

    private float GetPassiveDelta(NeedState state, bool isInComfortingArea)
    {
        if (state.needType == NeedType.Comfort)
        {
            return isInComfortingArea ? state.recoveryPerSecond : -state.decayPerSecond;
        }

        return -state.decayPerSecond;
    }

    private bool IsNeedUrgent(NeedState state)
    {
        if (state == null)
            return false;

        return GetNeedUrgencyBand(state) <= NeedUrgencyBand.Urgent;
    }

    private NeedUrgencyBand GetNeedUrgencyBand(NeedState state)
    {
        if (state == null)
            return NeedUrgencyBand.Stable;

        float value = Mathf.Clamp(state.currentValue, 0f, state.maxValue);
        NeedBandThresholds thresholds = GetValidatedThresholds(state);

        if (value <= thresholds.criticalThreshold)
            return NeedUrgencyBand.Critical;

        if (value <= thresholds.urgentThreshold)
            return NeedUrgencyBand.Urgent;

        if (value <= thresholds.lowThreshold)
            return NeedUrgencyBand.Low;

        if (value <= thresholds.stableThreshold)
            return NeedUrgencyBand.Stable;

        return NeedUrgencyBand.Abundant;
    }

    private float GetNeedPriorityScore(NeedState state)
    {
        if (state == null)
            return float.MinValue;

        NeedUrgencyBand band = GetNeedUrgencyBand(state);
        float bandWeight = GetBandWeight(band);

        if (bandWeight <= 0f)
            return bandWeight;

        float normalizedDeficit = state.maxValue > 0f
            ? 1f - Mathf.Clamp01(state.currentValue / state.maxValue)
            : 1f;

        return bandWeight + normalizedDeficit;
    }

    private float GetBandWeight(NeedUrgencyBand band)
    {
        switch (band)
        {
            case NeedUrgencyBand.Critical:
                return 3f;
            case NeedUrgencyBand.Urgent:
                return 2f;
            case NeedUrgencyBand.Low:
                return 1f;
            case NeedUrgencyBand.Stable:
                return 0f;
            case NeedUrgencyBand.Abundant:
                return -1f;
            default:
                return 0f;
        }
    }

    private NeedState GetState(NeedType needType)
    {
        for (int i = 0; i < needs.Count; i++)
        {
            NeedState state = needs[i];
            if (state != null && state.needType == needType)
            {
                return state;
            }
        }

        return null;
    }

    private void EnsureNeed(NeedType needType)
    {
        if (GetState(needType) != null)
            return;

        needs.Add(new NeedState
        {
            needType = needType,
            currentValue = 10f,
            maxValue = 10f,
            bandThresholds = new NeedBandThresholds
            {
                criticalThreshold = 2f,
                urgentThreshold = 5f,
                lowThreshold = 7f,
                stableThreshold = 9f
            },
            decayPerSecond = 1f,
            recoveryPerSecond = 0f
        });
    }

    private void ClampAll()
    {
        for (int i = 0; i < needs.Count; i++)
        {
            ClampNeed(needs[i]);
        }
    }

    private void ClampNeed(NeedState state)
    {
        if (state == null)
            return;

        state.maxValue = Mathf.Max(0f, state.maxValue);
        state.currentValue = Mathf.Clamp(state.currentValue, 0f, state.maxValue);
        state.decayPerSecond = Mathf.Max(0f, state.decayPerSecond);
        state.recoveryPerSecond = Mathf.Max(0f, state.recoveryPerSecond);
        ClampThresholds(state);
    }

    private void ClampThresholds(NeedState state)
    {
        if (state.bandThresholds == null)
            state.bandThresholds = new NeedBandThresholds();

        NeedBandThresholds thresholds = state.bandThresholds;

        thresholds.criticalThreshold = Mathf.Clamp(thresholds.criticalThreshold, 0f, state.maxValue);
        thresholds.urgentThreshold = Mathf.Clamp(thresholds.urgentThreshold, thresholds.criticalThreshold, state.maxValue);
        thresholds.lowThreshold = Mathf.Clamp(thresholds.lowThreshold, thresholds.urgentThreshold, state.maxValue);
        thresholds.stableThreshold = Mathf.Clamp(thresholds.stableThreshold, thresholds.lowThreshold, state.maxValue);
    }

    private NeedBandThresholds GetValidatedThresholds(NeedState state)
    {
        if (state.bandThresholds == null)
        {
            state.bandThresholds = new NeedBandThresholds();
        }

        ClampThresholds(state);
        return state.bandThresholds;
    }
}
