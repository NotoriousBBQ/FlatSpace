using UnityEngine;

namespace FlatSpace.Fog
{
    /// <summary>
    /// One player's visibility state over the fog grid: a continuous strength per
    /// cell (replaced each turn) and a sticky "explored" bitset (OR-accumulated).
    /// </summary>
    public class VisibilityGrid
    {
        public int CellCount { get; }
        public float[] VisibleStrength { get; }

        private readonly ulong[] _explored;

        public VisibilityGrid(int cellCount)
        {
            CellCount = Mathf.Max(0, cellCount);
            VisibleStrength = new float[CellCount];
            _explored = new ulong[(CellCount + 63) / 64];
        }

        public void ClearVisible()
        {
            System.Array.Clear(VisibleStrength, 0, VisibleStrength.Length);
        }

        public void Observe(int index, float strength, float exploredCutoff)
        {
            if (index < 0 || index >= CellCount) return;
            if (strength > VisibleStrength[index]) VisibleStrength[index] = strength;
            if (strength > exploredCutoff) _explored[index >> 6] |= 1UL << (index & 63);
        }

        public bool IsExplored(int index)
        {
            if (index < 0 || index >= CellCount) return false;
            return (_explored[index >> 6] & (1UL << (index & 63))) != 0UL;
        }

        public int ExploredCount()
        {
            var n = 0;
            for (var i = 0; i < CellCount; i++)
                if (IsExplored(i)) n++;
            return n;
        }

        public byte[] GetExploredPacked()
        {
            var bytes = new byte[(CellCount + 7) / 8];
            for (var i = 0; i < CellCount; i++)
                if (IsExplored(i)) bytes[i >> 3] |= (byte)(1 << (i & 7));
            return bytes;
        }

        public void SetExploredPacked(byte[] data)
        {
            if (data == null) return;
            if (data.Length != (CellCount + 7) / 8) return;
            System.Array.Clear(_explored, 0, _explored.Length);
            for (var i = 0; i < CellCount; i++)
            {
                var bit = (data[i >> 3] >> (i & 7)) & 1;
                if (bit != 0) _explored[i >> 6] |= 1UL << (i & 63);
            }
        }
    }
}
