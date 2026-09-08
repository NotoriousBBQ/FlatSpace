using UnityEngine;

namespace FlatSpace.Fog
{
    [CreateAssetMenu(fileName = "FogOfWarSettings", menuName = "Scriptable Objects/FogOfWarSettings")]
    public class FogOfWarSettings : ScriptableObject
    {
        [Header("Grid")]
        [Tooltip("Visibility grid cell size in world units. Smaller = smoother edges, more cost.")]
        public float cellSize = 25f;
        [Tooltip("Extra world padding added around the planet bounding box.")]
        public float boundsMargin = 250f;

        [Header("Vision radii (world units)")]
        public float planetVisionRadius = 220f;
        public float shipVisionRadius = 160f;

        [Header("Open space")]
        [Tooltip("Vision radius is multiplied by this where space is fully 'open' (deep void).")]
        public float openSpaceRadiusMultiplier = 2.5f;
        [Tooltip("Distance from the nearest planet or connection at which space starts counting as open.")]
        public float openSpaceThreshold = 200f;
        [Tooltip("Width of the smooth ramp above the threshold.")]
        public float openSpaceFalloff = 300f;

        [Header("Falloff / thresholds")]
        [Tooltip("Soft-rim width of a vision circle, world units.")]
        public float edgeSoftness = 40f;
        [Tooltip("Cell strength above which a cell becomes permanently 'explored'.")]
        public float visibleCutoff = 0.5f;
        [Tooltip("Sampled strength above which an object renders as fully Visible.")]
        public float visibleThreshold = 0.35f;

        [Header("Overlay colors")]
        public Color unseenColor = new Color(0.01f, 0.012f, 0.03f, 0.97f);
        public Color exploredColor = new Color(0.02f, 0.025f, 0.06f, 0.55f);
        [Tooltip("0..1 color multiplier for explored (not currently visible) planets and lines.")]
        [Range(0f, 1f)] public float exploredObjectDim = 0.45f;
    }
}
