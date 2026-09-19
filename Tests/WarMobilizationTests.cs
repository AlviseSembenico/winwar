using System;
using System.Linq;
using AgesOfConflict;

internal static class WarMobilizationTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    public static void Main()
    {
        var route = new WarMobilization();
        Check(route.Plan(10, 1, new[] { 0, 6 }, _ => true, cell => cell == 9), "Reachable front");
        Check(route.path.SequenceEqual(new[] { 6, 7, 8, 9 }), "Choose nearest city, not first city");
        Check(!route.HasArrived && route.distance == 0f, "Declaration must not deploy instantly");
        route.Advance(0.1f, 0.4f);
        Check(Math.Abs(route.distance - 0.5f) < 0.0001f, "Twice expansion speed with fractional movement");
        route.Advance(0f, 0.4f);
        Check(route.distance == 0.5f, "Pause does not move troops");
        route.Advance(0.1f, 0.2f);
        Check(route.distance == 1.5f, "Nation expansion boost also boosts mobilization");
        Check(!route.HasArrived, "Combat remains blocked before arrival");
        route.Advance(10f, 0.4f);
        Check(route.HasArrived && route.distance == 3f, "Arrival clamps at front");

        // A wall blocks the direct route. Movement must go around it, not through water/enemies.
        Check(route.Plan(5, 3, new[] { 0 }, cell => cell != 1 && cell != 6, cell => cell == 4), "Detour exists");
        Check(Math.Abs(route.TotalDistance - (4f + 2f * Math.Sqrt(2))) < 0.0001f
            && !route.path.Contains(1) && !route.path.Contains(6), "Shortest safe diagonal detour");
        for (int i = 1; i < route.path.Count; i++)
        {
            int a = route.path[i - 1],
                b = route.path[i];
            Check(Math.Max(Math.Abs(a % 5 - b % 5), Math.Abs(a / 5 - b / 5)) == 1, "Adjacent cells without row wrapping");
        }

        Check(route.Plan(5, 5, new[] { 0 }, _ => true, cell => cell == 24), "Diagonal front reachable");
        Check(route.path.SequenceEqual(new[] { 0, 6, 12, 18, 24 }), "Straight diagonal instead of right-angle detour");
        Check(Math.Abs(route.TotalDistance - 4f * Math.Sqrt(2)) < 0.0001f, "Diagonal travel uses real distance");
        route.Advance(0.2f, 0.4f);
        Check(route.CurrentPathIndex == 0 && !route.HasArrived, "Diagonal step takes longer than one cardinal step");
        Check(Math.Abs(route.GetSegmentProgress(0) - 1f / Math.Sqrt(2)) < 0.0001f, "Animation follows actual distance");
        Check(!route.CanFollowRemainingPath(cell => cell != 1), "Lost corner cell invalidates diagonal route");
        Check(route.CanFollowRemainingPath(_ => true), "Friendly diagonals remain traversable");
        route.Advance(10f, 0.4f);
        Check(route.HasArrived && route.CurrentPathIndex == 4, "Diagonal arrival waits for the full length");
        Check(route.Plan(5, 5, new[] { 12 }, _ => true, cell => cell == 10 || cell == 24), "Multiple front points");
        Check(route.path.Last() == 10 && route.TotalDistance == 2f, "Target nearest point, not front center or far end");
        Check(!route.Plan(2, 2, new[] { 0 }, cell => cell == 0 || cell == 3, cell => cell == 3),
            "Cannot cut diagonally between blocked corners");
        Check(!route.Plan(3, 2, new[] { 2 }, cell => cell == 2 || cell == 3, cell => cell == 3),
            "Cannot wrap between map rows");

        Check(!route.Plan(3, 1, new[] { 0 }, cell => cell != 1, cell => cell == 2), "Disconnected front waits");
        Check(route.path.Count == 0 && !route.HasArrived && route.distance == 0f, "Failed replan clears old route");
        Check(!route.Plan(3, 1, Array.Empty<int>(), _ => true, _ => true), "No city cannot deploy");
        Check(!route.Plan(3, 1, new[] { 0, -1, 3 }, cell => cell != 0, _ => true), "Invalid/lost cities ignored");
        Check(!route.Plan(3, 1, new[] { 0 }, _ => true, _ => false), "No shared front waits");
        Check(
            route.Plan(3, 1, new[] { 0, 0 }, _ => true, cell => cell == 0) && route.HasArrived,
            "City already on front has zero travel distance"
        );
        route.Clear();
        Check(!route.HasArrived && route.path.Count == 0, "Cancellation clears route");
        Console.WriteLine("All mobilization routing and timing tests passed.");
    }
}
