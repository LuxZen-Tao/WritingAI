using System;
using System.Collections.Generic;
using UnityEngine;

public class NeedsManager : MonoBehaviour
{
    [Serializable]
    public class NeedState
    {
        public NeedType needType = NeedType.Comfort;
        public float currentValue = 10f;
        public float maxValue = 10f;
        public float urgentThreshold = 5f;
        public float decayPerSecond = 1f;
        public float recoveryPerSecond = 0f;
    }

    [Header("Needs")]
    public List<NeedState> needs = new List<NeedState>
    {
        new NeedState
        {
            needType = NeedType.Comfort,
            currentValue = 10f,
            maxValue = 10f,
            urgentThreshold = 5f,
            decayPerSecond = 2f,
            recoveryPerSecond = 1f
        },
        new NeedState
        {
            needType = NeedType.Hunger,
            currentValue = 10f,
            maxValue = 10f,
            urgentThreshold = 4f,
            decayPerSecond = 0.35f,
            recoveryPerSecond = 0f
        }
    };

    private void Awake()
    {
        EnsureNeed(NeedType.Comfort);
        EnsureNeed(NeedType.Hunger);
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
            if (state != null && state.currentValue < state.urgentThreshold)
                return true;
        }

        return false;
    }

    public NeedType GetMostUrgentNeed()
    {
        NeedType bestNeed = NeedType.Comfort;
        float bestUrgency = float.MinValue;
        bool foundUrgent = false;

        for (int i = 0; i < needs.Count; i++)
        {
            NeedState state = needs[i];
            if (state == null)
                continue;

            float urgency = GetUrgencyScore(state);
            if (urgency <= 0f)
                continue;

            foundUrgent = true;

            if (urgency > bestUrgency)
            {
                bestUrgency = urgency;
                bestNeed = state.needType;
            }
        }

        return foundUrgent ? bestNeed : NeedType.Comfort;
    }

    public bool IsNeedUrgent(NeedType needType)
    {
        NeedState state = GetState(needType);
        if (state == null)
            return false;

        return state.currentValue < state.urgentThreshold;
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

    private float GetUrgencyScore(NeedState state)
    {
        if (state.urgentThreshold <= 0f)
            return 0f;

        float missingFromThreshold = state.urgentThreshold - state.currentValue;
        if (missingFromThreshold <= 0f)
            return 0f;

        return missingFromThreshold / state.urgentThreshold;
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
            urgentThreshold = 5f,
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
        state.urgentThreshold = Mathf.Clamp(state.urgentThreshold, 0f, state.maxValue);
        state.currentValue = Mathf.Clamp(state.currentValue, 0f, state.maxValue);
        state.decayPerSecond = Mathf.Max(0f, state.decayPerSecond);
        state.recoveryPerSecond = Mathf.Max(0f, state.recoveryPerSecond);
    }
}
