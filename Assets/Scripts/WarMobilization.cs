using System;
using System.Collections.Generic;

namespace AgesOfConflict
{
    /// <summary>A shortest friendly-land route, measured in map cells.</summary>
    public sealed class WarMobilization
    {
        public readonly List<int> path = new List<int>();
        private readonly List<float> cumulativeDistances = new List<float>();
        private int mapWidth;
        public float distance { get; private set; }
        public float TotalDistance =>
            cumulativeDistances.Count == 0 ? 0f : cumulativeDistances[cumulativeDistances.Count - 1];
        public int CurrentPathIndex { get; private set; }
        public bool HasArrived => path.Count > 0 && distance >= TotalDistance;

        public void Clear()
        {
            path.Clear();
            cumulativeDistances.Clear();
            distance = 0f;
            CurrentPathIndex = 0;
        }

        // Multi-source Dijkstra chooses the closest reachable point on the entire front.
        // Diagonals cost their real length, so fewer steps never beat a shorter route.
        public bool Plan(
            int width,
            int height,
            IEnumerable<int> origins,
            Func<int, bool> canTraverse,
            Func<int, bool> isFront
        )
        {
            Clear();
            mapWidth = width;
            var parents = new Dictionary<int, int>();
            var costs = new Dictionary<int, float>();
            var queue = new SortedSet<(float cost, int cell)>();
            foreach (int origin in origins)
            {
                if (origin < 0 || origin >= width * height || parents.ContainsKey(origin) || !canTraverse(origin))
                    continue;
                parents.Add(origin, -1);
                costs.Add(origin, 0f);
                queue.Add((0f, origin));
            }

            while (queue.Count > 0)
            {
                var closest = queue.Min;
                queue.Remove(closest);
                int current = closest.cell;
                if (isFront(current))
                {
                    for (int cell = current; cell >= 0; cell = parents[cell])
                        path.Add(cell);
                    path.Reverse();
                    foreach (int cell in path)
                        cumulativeDistances.Add(costs[cell]);
                    return true;
                }

                int x = current % width;
                int y = current / width;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx,
                        ny = y + dy;
                    if ((dx == 0 && dy == 0) || nx < 0 || nx >= width || ny < 0 || ny >= height)
                        continue;
                    bool diagonal = dx != 0 && dy != 0;
                    // Never cut a corner through water or another nation's territory.
                    if (diagonal && (!canTraverse(y * width + nx) || !canTraverse(ny * width + x)))
                        continue;
                    Visit(ny * width + nx, current, diagonal ? (float)Math.Sqrt(2) : 1f);
                }
            }
            return false;

            void Visit(int next, int parent, float length)
            {
                if (!canTraverse(next))
                    return;
                float cost = costs[parent] + length;
                if (costs.TryGetValue(next, out float oldCost))
                {
                    if (cost >= oldCost)
                        return;
                    queue.Remove((oldCost, next));
                }
                parents[next] = parent;
                costs[next] = cost;
                queue.Add((cost, next));
            }
        }

        public float GetSegmentProgress(int startIndex)
        {
            float start = cumulativeDistances[startIndex];
            float length = cumulativeDistances[startIndex + 1] - start;
            return Math.Max(0f, Math.Min(1f, (distance - start) / length));
        }

        public void Complete()
        {
            distance = TotalDistance;
            CurrentPathIndex = Math.Max(0, path.Count - 1);
        }

        public bool CanFollowRemainingPath(Func<int, bool> canTraverse)
        {
            for (int i = CurrentPathIndex; i < path.Count; i++)
            {
                if (!canTraverse(path[i]))
                    return false;
                if (i + 1 >= path.Count)
                    continue;
                int x = path[i] % mapWidth,
                    y = path[i] / mapWidth;
                int nx = path[i + 1] % mapWidth,
                    ny = path[i + 1] / mapWidth;
                if (x != nx && y != ny && (!canTraverse(y * mapWidth + nx) || !canTraverse(ny * mapWidth + x)))
                    return false;
            }
            return path.Count > 0;
        }

        public void Advance(float deltaTime, float expansionIntervalSeconds)
        {
            if (path.Count == 0 || deltaTime <= 0f)
                return;
            // Expansion grows one cell deep per pass; mobilization travels two cells per pass.
            distance = Math.Min(
                TotalDistance,
                distance + 2f * deltaTime / Math.Max(0.0001f, expansionIntervalSeconds)
            );
            while (CurrentPathIndex + 1 < path.Count && distance >= cumulativeDistances[CurrentPathIndex + 1])
                CurrentPathIndex++;
        }
    }
}
