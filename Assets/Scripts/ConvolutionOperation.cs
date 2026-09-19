using System;
using UnityEngine;

namespace AgesOfConflict
{
    /// <summary>Evaluates one scalar convolution kernel in a square window around its center cell.</summary>
    public class ConvolutionOperation
    {
        private readonly WorldGenerator worldGenerator;

        public ConvolutionOperation(WorldGenerator worldGenerator)
        {
            this.worldGenerator = worldGenerator;
        }

        public float Evaluate(
            Vector2Int center,
            int size,
            Func<Vector2Int, Vector2Int, float> kernel
        )
        {
            float result = 0f;
            int minimumOffset = -size / 2;
            int maximumOffsetExclusive = minimumOffset + size;
            for (int y = minimumOffset; y < maximumOffsetExclusive; y++)
            for (int x = minimumOffset; x < maximumOffsetExclusive; x++)
            {
                Vector2Int sample = new Vector2Int(center.x + x, center.y + y);
                if (
                    sample.x < 0
                    || sample.x >= worldGenerator.width
                    || sample.y < 0
                    || sample.y >= worldGenerator.height
                )
                    continue;
                result += kernel(center, sample);
            }
            return result;
        }
    }
}
