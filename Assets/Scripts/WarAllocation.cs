using System;
using System.Collections.Generic;

namespace AgesOfConflict
{
    /// <summary>Shares an army between fronts, redistributing percentage points equally.</summary>
    public static class WarAllocation
    {
        public static void SetPercentage(IList<float> percentages, int index, float percentage)
        {
            percentages[index] = Math.Max(0f, Math.Min(100f, percentage));
            if (percentages.Count == 1)
                return;

            float remaining = 100f - percentages[index];
            var adjustable = new List<int>();
            for (int i = 0; i < percentages.Count; i++)
                if (i != index)
                    adjustable.Add(i);

            // Pin exhausted fronts at zero, then share the remaining change equally.
            while (adjustable.Count > 0)
            {
                float total = 0f;
                foreach (int i in adjustable)
                    total += percentages[i];
                float change = (remaining - total) / adjustable.Count;
                bool clamped = false;
                for (int j = adjustable.Count - 1; j >= 0; j--)
                {
                    int i = adjustable[j];
                    if (percentages[i] + change >= 0f)
                        continue;
                    percentages[i] = 0f;
                    adjustable.RemoveAt(j);
                    clamped = true;
                }
                if (clamped)
                    continue;
                foreach (int i in adjustable)
                    percentages[i] = Math.Max(0f, percentages[i] + change);
                break;
            }
        }
    }
}
