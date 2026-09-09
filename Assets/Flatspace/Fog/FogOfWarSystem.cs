using System.Collections.Generic;
using UnityEngine;
using FlatSpace.Game;
using FlatSpace.AI;

namespace FlatSpace.Fog
{
    public class FogOfWarSystem : MonoBehaviour
    {
        public bool Ready { get; private set; }
        public FogViewMode ViewMode { get; private set; } = FogViewMode.NoFog;
        public int ViewPlayer { get; private set; }
        public int GridCols => _grid?.Cols ?? 0;
        public int GridRows => _grid?.Rows ?? 0;
        public Bounds GridWorldBounds =>
            new Bounds(_grid.Center, new Vector3(_grid.WorldSize.x, _grid.WorldSize.y, 0.1f));

        private FogGrid _grid;
        private FogOfWarSettings _settings;
        private VisibilityGrid[] _players;
        private float[] _displayStrength;      // resolved for the current view
        private bool[] _displayExplored;
        private List<Vector2> _planetPositions; // parallel to _planetNames
        private List<string> _planetNames;

        private const int OverlaySortingOrder = 1000;
        private GameObject _overlayGo;
        private SpriteRenderer _overlayRenderer;
        private Texture2D _overlayTex;
        private Color32[] _overlayBuffer;

        // ---- Init -------------------------------------------------------------

        public void Init(IReadOnlyList<Planet> planets, FogOfWarSettings settings, int numPlayers)
        {
            var positions = new List<Vector2>(planets.Count);
            _planetNames = new List<string>(planets.Count);
            foreach (var p in planets)
            {
                positions.Add(p.Position);
                _planetNames.Add(p.PlanetName);
            }
            var segments = GatherSegments();
            InitCore(positions, segments, settings, numPlayers);
        }

        public void InitForTest(IReadOnlyList<Vector2> planetPositions,
            IReadOnlyList<(Vector2, Vector2)> segments, FogOfWarSettings settings, int numPlayers)
        {
            _planetNames = new List<string>();
            InitCore(new List<Vector2>(planetPositions), new List<(Vector2, Vector2)>(segments),
                settings, numPlayers);
        }

        private void InitCore(List<Vector2> positions, List<(Vector2, Vector2)> segments,
            FogOfWarSettings settings, int numPlayers)
        {
            _settings = settings;
            _planetPositions = positions;

            var raw = new Bounds();
            if (positions.Count > 0)
            {
                raw = new Bounds(positions[0], Vector3.zero);
                foreach (var p in positions) raw.Encapsulate(p);
            }
            _grid = new FogGrid(raw, settings.cellSize, settings.boundsMargin);
            _grid.BakeEmptiness(positions, segments,
                settings.openSpaceThreshold, settings.openSpaceFalloff);

            _players = new VisibilityGrid[Mathf.Max(1, numPlayers)];
            for (var i = 0; i < _players.Length; i++)
                _players[i] = new VisibilityGrid(_grid.CellCount);

            _displayStrength = new float[_grid.CellCount];
            _displayExplored = new bool[_grid.CellCount];

            // Reset the view before ResolveDisplay so re-initialising with fewer players
            // while a Player-n view is active can't index past the reallocated _players array.
            ViewMode = FogViewMode.NoFog;
            ViewPlayer = 0;

            Ready = true;
            ResolveDisplay();
            BuildOverlay();
        }

        private static List<(Vector2, Vector2)> GatherSegments()
        {
            var raw = new List<(Vector3, Vector3)>();
            FlatSpace.Pathing.PathingSystem.Instance.ConnectionVectors(raw);
            var result = new List<(Vector2, Vector2)>(raw.Count);
            foreach (var (a, b) in raw) result.Add(((Vector2)a, (Vector2)b));
            return result;
        }

        // ---- Recompute ------------------------------------------------------

        public void Recompute()
        {
            if (!Ready) return;
            var gameAI = Gameboard.Instance != null ? Gameboard.Instance.GameAI : null;
            if (gameAI == null) return;

            var sources = new List<(int player, Vector2 pos, float radius)>();
            var planetList = gameAI.GameAIMap.PlanetList;

            for (var pl = 0; pl < _players.Length; pl++)
            {
                foreach (var planet in planetList)
                {
                    if (planet.GetPopulationFraction(pl) > 0f)
                        sources.Add((pl, planet.Position, _settings.planetVisionRadius));
                    foreach (var ship in planet.DockedShips)
                        if (ship.Owner == pl)
                        {
                            sources.Add((pl, planet.Position, _settings.shipVisionRadius));
                            break;
                        }
                }
            }

            var corridorPts = new List<Vector2>();
            var corridorSamples = new List<Vector2>();
            foreach (var order in gameAI.CurrentAIOrders)
            {
                if (order.Type != GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport &&
                    order.Type != GameAI.GameAIOrder.OrderType.OrderTypeShipTransport)
                    continue;
                if (order.PlayerId < 0 || order.PlayerId >= _players.Length) continue;
                if (string.IsNullOrEmpty(order.Origin) || string.IsNullOrEmpty(order.Target)) continue;

                var path = gameAI.GameAIMap.GetPath(order.Origin, order.Target);
                if (path == null || path.PathNodes.Count == 0) continue;

                corridorPts.Clear();
                foreach (var n in path.PathNodes) corridorPts.Add(n.Position);

                var totalLen = 0f;
                for (var i = 1; i < corridorPts.Count; i++)
                    totalLen += Vector2.Distance(corridorPts[i - 1], corridorPts[i]);
                var progress = order.TotalDelay > 0
                    ? Mathf.Clamp01((float)(order.TotalDelay - order.TimingDelay) / order.TotalDelay)
                    : 1f;

                corridorSamples.Clear();
                CollectCorridorSamples(corridorPts, totalLen * progress,
                    _settings.shipVisionRadius * 0.75f, corridorSamples);
                foreach (var s in corridorSamples)
                    sources.Add((order.PlayerId, s, _settings.shipVisionRadius));
            }

            RecomputeFromSources(sources);
        }

        /// <summary>
        /// Sample points along `pathPts` from distance 0 up to `revealedDist`, spaced `step`
        /// apart, always including the start and the exact point at `revealedDist`. Used to
        /// reveal the whole corridor an in-flight ship has traversed so no planet between two
        /// turn positions is skipped.
        /// </summary>
        public static void CollectCorridorSamples(
            System.Collections.Generic.IReadOnlyList<Vector2> pathPts,
            float revealedDist, float step, System.Collections.Generic.List<Vector2> outSamples)
        {
            if (pathPts == null || pathPts.Count == 0) return;
            if (pathPts.Count == 1) { outSamples.Add(pathPts[0]); return; }

            step = Mathf.Max(1f, step);
            revealedDist = Mathf.Max(0f, revealedDist);

            outSamples.Add(PointAlongPath(pathPts, 0f));
            for (var d = step; d < revealedDist; d += step)
                outSamples.Add(PointAlongPath(pathPts, d));
            outSamples.Add(PointAlongPath(pathPts, revealedDist));
        }

        private static Vector2 PointAlongPath(
            System.Collections.Generic.IReadOnlyList<Vector2> pts, float dist)
        {
            if (pts.Count == 0) return default;
            if (pts.Count == 1 || dist <= 0f) return pts[0];
            var travelled = 0f;
            for (var i = 1; i < pts.Count; i++)
            {
                var seg = Vector2.Distance(pts[i - 1], pts[i]);
                if (travelled + seg >= dist || i == pts.Count - 1)
                {
                    var t = seg > 0f ? Mathf.Clamp01((dist - travelled) / seg) : 0f;
                    return Vector2.Lerp(pts[i - 1], pts[i], t);
                }
                travelled += seg;
            }
            return pts[pts.Count - 1];
        }

        /// <summary>Rebuild every player's visibility from an explicit source list. Test seam.</summary>
        public void RecomputeFromSources(IEnumerable<(int player, Vector2 pos, float radius)> sources)
        {
            if (!Ready) return;
            foreach (var vg in _players) vg.ClearVisible();

            var byPlayer = new List<(Vector2 pos, float radius)>[_players.Length];
            for (var i = 0; i < byPlayer.Length; i++) byPlayer[i] = new List<(Vector2, float)>();
            foreach (var (player, pos, radius) in sources)
                if (player >= 0 && player < _players.Length)
                    byPlayer[player].Add((pos, radius));

            for (var pl = 0; pl < _players.Length; pl++)
            {
                var srcs = byPlayer[pl];
                if (srcs.Count == 0) continue;
                var vg = _players[pl];
                for (var i = 0; i < _grid.CellCount; i++)
                {
                    var c = _grid.CellCenter(i);
                    var openness = _grid.Openness(i);
                    var best = 0f;
                    for (var s = 0; s < srcs.Count; s++)
                    {
                        var eff = Mathf.Lerp(srcs[s].radius,
                            srcs[s].radius * _settings.openSpaceRadiusMultiplier, openness);
                        var strength = Mathf.Clamp01(
                            (eff - Vector2.Distance(c, srcs[s].pos)) / Mathf.Max(0.001f, _settings.edgeSoftness));
                        if (strength > best) best = strength;
                        if (best >= 1f) break;
                    }
                    if (best > 0f) vg.Observe(i, best, _settings.visibleCutoff);
                }
            }

            ResolveDisplay();
        }

        // ---- View / sampling ----------------------------------------------

        public void SetViewMode(FogViewMode mode, int playerIndex)
        {
            ViewMode = mode;
            ViewPlayer = Mathf.Clamp(playerIndex, 0, Mathf.Max(0, _players.Length - 1));
            ResolveDisplay();
        }

        private void ResolveDisplay()
        {
            if (!Ready) return;
            if (ViewMode != FogViewMode.NoFog)
            {
                for (var i = 0; i < _grid.CellCount; i++)
                {
                    if (ViewMode == FogViewMode.AllPlayers)
                    {
                        var s = 0f; var e = false;
                        foreach (var vg in _players)
                        {
                            if (vg.VisibleStrength[i] > s) s = vg.VisibleStrength[i];
                            e |= vg.IsExplored(i);
                        }
                        _displayStrength[i] = s;
                        _displayExplored[i] = e;
                    }
                    else
                    {
                        var vg = _players[ViewPlayer];
                        _displayStrength[i] = vg.VisibleStrength[i];
                        _displayExplored[i] = vg.IsExplored(i);
                    }
                }
            }

            RefreshOverlay();
        }

        public FogSample Sample(Vector2 worldPos)
        {
            if (!Ready || ViewMode == FogViewMode.NoFog)
                return new FogSample { Strength = 1f, Explored = true };
            var idx = _grid.WorldToCellIndex(worldPos);
            return new FogSample
            {
                Strength = _grid.SampleBilinear(_displayStrength, worldPos),
                Explored = _displayExplored[idx],
            };
        }

        public FogVisibility Classify(Vector2 worldPos)
        {
            if (!Ready || ViewMode == FogViewMode.NoFog) return FogVisibility.Visible;
            var s = Sample(worldPos);
            if (s.Strength > _settings.visibleThreshold) return FogVisibility.Visible;
            return s.Explored ? FogVisibility.Explored : FogVisibility.Hidden;
        }

        // ---- Save hooks ---------------------------------------------------

        public byte[] GetExploredPacked(int player)
        {
            if (!Ready || player < 0 || player >= _players.Length) return System.Array.Empty<byte>();
            return _players[player].GetExploredPacked();
        }

        public void SetExploredPacked(int player, byte[] data)
        {
            if (!Ready || player < 0 || player >= _players.Length) return;
            _players[player].SetExploredPacked(data);
            ResolveDisplay();
        }

        // ---- Overlay color fill (used by Task 6) -------------------------

        public void FillOverlayColors(Color32[] buffer)
        {
            if (!Ready || buffer.Length != _grid.CellCount) return;
            for (var i = 0; i < _grid.CellCount; i++)
            {
                Color c;
                if (ViewMode == FogViewMode.NoFog)
                {
                    c = new Color(0, 0, 0, 0);
                }
                else
                {
                    var explored = _displayExplored[i];
                    c = explored ? _settings.exploredColor : _settings.unseenColor;
                    c.a = Mathf.Lerp(c.a, 0f, _displayStrength[i]);
                }
                buffer[i] = c;
            }
        }

        // ---- Overlay ----------------------------------------------------------

        private void BuildOverlay()
        {
            DestroyOverlay();
            _overlayTex = new Texture2D(_grid.Cols, _grid.Rows, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _overlayBuffer = new Color32[_grid.CellCount];

            var sprite = Sprite.Create(_overlayTex,
                new Rect(0, 0, _grid.Cols, _grid.Rows),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 1f / _grid.CellSize);

            _overlayGo = new GameObject("FogOverlay");
            _overlayGo.transform.SetParent(transform, false);
            _overlayGo.transform.localPosition = new Vector3(_grid.Center.x, _grid.Center.y, 0f);
            _overlayRenderer = _overlayGo.AddComponent<SpriteRenderer>();
            _overlayRenderer.sprite = sprite;
            _overlayRenderer.sortingOrder = OverlaySortingOrder;
            RefreshOverlay();
        }

        public void RefreshOverlay()
        {
            if (_overlayRenderer == null) return;
            var noFog = ViewMode == FogViewMode.NoFog;
            _overlayRenderer.enabled = !noFog;
            if (noFog) return;
            FillOverlayColors(_overlayBuffer);
            _overlayTex.SetPixels32(_overlayBuffer);
            _overlayTex.Apply(false);
        }

        public void DestroyOverlay()
        {
            if (_overlayGo != null) DestroyImmediate(_overlayGo);
            if (_overlayTex != null) DestroyImmediate(_overlayTex);
            _overlayGo = null; _overlayRenderer = null; _overlayTex = null; _overlayBuffer = null;
        }

        private void OnDestroy() => DestroyOverlay();

        // ---- Gizmo ----

        private void OnDrawGizmosSelected()
        {
            if (!Ready) return;
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.4f);
            Gizmos.DrawWireCube(GridWorldBounds.center, GridWorldBounds.size);
            if (_settings == null || _planetPositions == null) return;
            Gizmos.color = new Color(1f, 1f, 0.4f, 0.5f);
            foreach (var p in _planetPositions)
                Gizmos.DrawWireSphere(new Vector3(p.x, p.y, 0f), _settings.planetVisionRadius);
        }
    }
}
