using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatSpace.Fog
{
    /// <summary>
    /// Static geometry of the fog grid plus the baked "emptiness" field.
    /// A cell index is y * Cols + x, with y increasing upward from Origin (matches
    /// Texture2D.SetPixels32 row order).
    /// </summary>
    public class FogGrid
    {
        public int Cols { get; }
        public int Rows { get; }
        public int CellCount { get; }
        public float CellSize { get; }
        public Vector2 Origin { get; }
        public Vector2 WorldSize { get; }
        public Vector2 Center => Origin + WorldSize * 0.5f;

        private readonly float[] _openness;

        public FogGrid(Bounds rawWorldBounds, float cellSize, float margin)
        {
            CellSize = Mathf.Max(1f, cellSize);
            var min = (Vector2)rawWorldBounds.min - new Vector2(margin, margin);
            var max = (Vector2)rawWorldBounds.max + new Vector2(margin, margin);
            Cols = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / CellSize));
            Rows = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / CellSize));
            CellCount = Cols * Rows;
            Origin = min;
            WorldSize = new Vector2(Cols * CellSize, Rows * CellSize);
            _openness = new float[CellCount];
        }

        public int WorldToCellIndex(Vector2 world)
        {
            var local = (world - Origin) / CellSize;
            var x = Mathf.Clamp(Mathf.FloorToInt(local.x), 0, Cols - 1);
            var y = Mathf.Clamp(Mathf.FloorToInt(local.y), 0, Rows - 1);
            return y * Cols + x;
        }

        public Vector2 CellCenter(int index)
        {
            var x = index % Cols;
            var y = index / Cols;
            return Origin + new Vector2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);
        }

        public float SampleBilinear(float[] field, Vector2 world)
        {
            // Continuous cell coordinates, cell centers at integer+0.5.
            var local = (world - Origin) / CellSize - new Vector2(0.5f, 0.5f);
            var x0 = Mathf.Clamp(Mathf.FloorToInt(local.x), 0, Cols - 1);
            var y0 = Mathf.Clamp(Mathf.FloorToInt(local.y), 0, Rows - 1);
            var x1 = Mathf.Min(x0 + 1, Cols - 1);
            var y1 = Mathf.Min(y0 + 1, Rows - 1);
            var tx = Mathf.Clamp01(local.x - x0);
            var ty = Mathf.Clamp01(local.y - y0);
            var a = Mathf.Lerp(field[y0 * Cols + x0], field[y0 * Cols + x1], tx);
            var b = Mathf.Lerp(field[y1 * Cols + x0], field[y1 * Cols + x1], tx);
            return Mathf.Lerp(a, b, ty);
        }

        public float Openness(int index) => _openness[index];

        public void BakeEmptiness(
            IReadOnlyList<Vector2> planetPositions,
            IReadOnlyList<(Vector2 a, Vector2 b)> segments,
            float threshold, float falloff)
        {
            for (var i = 0; i < CellCount; i++)
            {
                var c = CellCenter(i);
                var d = float.MaxValue;
                for (var p = 0; p < planetPositions.Count; p++)
                    d = Mathf.Min(d, Vector2.Distance(c, planetPositions[p]));
                for (var s = 0; s < segments.Count; s++)
                    d = Mathf.Min(d, DistancePointToSegment(c, segments[s].a, segments[s].b));
                _openness[i] = Smoothstep(threshold, threshold + Mathf.Max(0.001f, falloff), d);
            }
        }

        private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var lenSq = ab.sqrMagnitude;
            if (lenSq < 1e-6f) return Vector2.Distance(p, a);
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return Vector2.Distance(p, a + t * ab);
        }

        private static float Smoothstep(float edge0, float edge1, float x)
        {
            var t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
