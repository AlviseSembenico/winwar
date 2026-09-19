using System;
using System.Linq;
using AgesOfConflict;

internal static class WarAllocationTests
{
    private static void Check(float[] actual, params float[] expected)
    {
        if (
            actual.Length != expected.Length
            || actual.Where((value, i) => Math.Abs(value - expected[i]) > 0.001f).Any()
        )
            throw new Exception($"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }

    public static void Main()
    {
        var pair = new[] { 50f, 50f };
        WarAllocation.SetPercentage(pair, 0, 70f);
        Check(pair, 70f, 30f);
        WarAllocation.SetPercentage(pair, 1, 80f);
        Check(pair, 20f, 80f);

        var fronts = new[] { 40f, 35f, 25f };
        WarAllocation.SetPercentage(fronts, 0, 60f);
        Check(fronts, 60f, 25f, 15f);
        WarAllocation.SetPercentage(fronts, 0, 20f);
        Check(fronts, 20f, 45f, 35f);
        WarAllocation.SetPercentage(fronts, 0, 95f);
        Check(fronts, 95f, 5f, 0f);
        WarAllocation.SetPercentage(fronts, 0, 100f);
        Check(fronts, 100f, 0f, 0f);
        WarAllocation.SetPercentage(fronts, 0, 0f);
        Check(fronts, 0f, 50f, 50f);

        var single = new[] { 10f };
        WarAllocation.SetPercentage(single, 0, 65f);
        Check(single, 65f);
        WarAllocation.SetPercentage(single, 0, -10f);
        Check(single, 0f);
        WarAllocation.SetPercentage(single, 0, 110f);
        Check(single, 100f);

        // New declarations keep their requested share and rebalance existing fronts.
        var newFront = new[] { 80f, 20f, 0f };
        WarAllocation.SetPercentage(newFront, 2, 30f);
        Check(newFront, 65f, 5f, 30f);
        // Cancellation sets the removed share to zero before removing the front.
        WarAllocation.SetPercentage(newFront, 2, 0f);
        Check(newFront, 80f, 20f, 0f);

        var random = new Random(42);
        for (int count = 2; count <= 20; count++)
        {
            var shares = Enumerable.Repeat(100f / count, count).ToArray();
            for (int step = 0; step < 1000; step++)
            {
                int index = random.Next(count);
                float target = (float)random.NextDouble() * 100f;
                WarAllocation.SetPercentage(shares, index, target);
                if (
                    Math.Abs(shares.Sum() - 100f) > 0.001f
                    || Math.Abs(shares[index] - target) > 0.001f
                    || shares.Any(value => value < 0f || value > 100f)
                )
                    throw new Exception("Allocations must stay bounded, sum to 100%, and honor the edited slider.");
            }
        }
        Console.WriteLine("All war allocation tests passed (including 19,000 randomized slider changes).");
    }
}
