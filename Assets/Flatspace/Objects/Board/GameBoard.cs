using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatSpace.Pathing;
using Unity.VisualScripting;
using UnityEditor;
//using UnityEditor.SceneManagement;
//using UnityEditorInternal;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;
using FlatSpace.AI;
using FlatSpace.Fog;
using Game.UI.MainGameScreenUI;

namespace FlatSpace
{
    namespace Game
    {
        public class Gameboard : MonoBehaviour
        {
            private static Gameboard _instance;
            public static Gameboard Instance => _instance;
            [SerializeField] public BoardConfiguration IntialBoardState;
            public string _boardDesignPath;

            [SerializeField] private float _minCameraOrtho = 1;
            [SerializeField] private float _maxCameraOrtho = 15;
            [SerializeField] private float _cameraOrthoStep = 0.1f;
            [SerializeField] private float _cameraInitialOrtho = 5f;
            public GameAI GameAI {get; private set;}
            [SerializeField] private GameAIConstants gameAIConstants;
            [SerializeField] private LineDrawObject _lineDrawObjectPrefab;
            [SerializeField] private LineDrawObject _orderLineDrawObjectPrefab;
            // Order graphics: a line per order in flight, plus disabled lines waiting for reuse
            // (see DisplayOrderGraphics).
            private readonly Dictionary<GameAI.GameAIOrder, LineDrawObject> _orderLines = new();
            private readonly Stack<LineDrawObject> _orderLinePool = new();
            [SerializeField] private ScrollRect _scrollRect;

            [SerializeField] private FlatSpace.Fog.FogOfWarSettings _fogOfWarSettings;
            [SerializeField] private bool _logAIEvents = false;
            // Testing toggle, same precedent as _logAIEvents: when set, pressing Start Run on the
            // Flatspace scene's own IntialBoardState runs the full 400-turn match 3 times in a row,
            // resetting to the initial board state between runs so each gets its own tuning log.
            [SerializeField] private bool _repeatRunThreeTimes = false;
            private FlatSpace.Fog.FogOfWarSystem _fogOfWarSystem;
            public FlatSpace.Fog.FogOfWarSystem FogOfWar => _fogOfWarSystem;

            public PlanetUIObject _PlanetUIPrefab;
            public PlanetUIObject[] _PlanetUIPrefabs;

            private MapInputActions _mapInputActions;
            private Camera _camera;
            public float CameraOrtho =>  _camera.orthographicSize;
            public Vector2 ScrollRectPosition => _scrollRect.normalizedPosition;

            public int NumPlayers = 2;
            public List<Player> players = new List<Player>();
            public int owningPlayerId = 0;

            private readonly List<PlanetUIObject> _planetUIObjects = new List<PlanetUIObject>();
            private readonly List<PlayerNotification> _playerNotifications = new List<PlayerNotification>();
            
            private MainScreenUIController _mainScreenUIController;
            private PlanetDetailUIController _planetDetailUIController;
            private FleetUIController _fleetUIController;
            public int TurnNumber { get; private set; }= 1;
            // The match reached MatchSettings.TurnLimit (0 = Unlimited never does); no further turns run.
            public bool TurnLimitReached { get; private set; }

            // Human player mode: which events stop the timer, the event list builder, and the last turn's events (for the message list).
            public FlatSpace.Human.HumanPauseSettings HumanPause { get; private set; } = FlatSpace.Human.HumanPauseSettings.Defaults();
            private FlatSpace.Human.HumanTurnEvents _humanEvents = new FlatSpace.Human.HumanTurnEvents();
            public List<FlatSpace.Human.HumanEvent> LastHumanEvents { get; private set; } = new List<FlatSpace.Human.HumanEvent>();
            // The event that stopped the timer on the last turn, null when nothing did.
            public FlatSpace.Human.HumanEvent? PausedByEvent { get; private set; }
            private bool _defeatReported;
            private bool _turnLimitReported;

            /// <summary>The human's notification list (see HumanNotificationList); reset with every init.</summary>
            public FlatSpace.Human.HumanNotificationList HumanNotifications { get; private set; } = new FlatSpace.Human.HumanNotificationList();
            private float orthoChange;
            // Frame on which a UI panel handled a click (e.g. the Planet Detail fleet icon opening
            // the Fleet UI). Update() polls the raw mouse independently of the EventSystem, so
            // without this the same click would immediately be treated as an "outside" click and
            // close the panel that just opened.
            private int _panelClickFrame = -1;
            void Start()
            {

            }

            // UpdatePlanet is called once per frame
            void Update()
            {
                UpdateLineHover();
                // P opens the Planets screen; while its search box has the keyboard the key is a letter, so only the button or Esc closes it.
                if (HumanPlayerId >= 0 && Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame
                    && !PlanetsScreen.IsOpen && !PlanetsScreen.AnyTextFieldFocused())
                    TogglePlanets();
                // T opens the Diplomacy screen (D is map scrolling); the same rule as P about a focused text field.
                if (HumanPlayerId >= 0 && Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame
                    && !DiplomacyScreen.IsOpen && !PlanetsScreen.AnyTextFieldFocused())
                    ToggleDiplomacy();
                if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
                    return;

                // A panel already consumed this click (the Planet Detail fleet icon opening the
                // Fleet UI). Don't also process it as an outside-click this frame.
                if (Time.frameCount == _panelClickFrame)
                    return;

                var mousePosition = Mouse.current.position.ReadValue();

                // The fleet summary is a HUD screen of its own: a press on it must leave the open planet or fleet panel alone.
                if (FleetSummaryScreen.ContainsScreenPoint(mousePosition) || PlanetsScreen.ContainsScreenPoint(mousePosition) || DiplomacyScreen.ContainsScreenPoint(mousePosition) || GameSettingsScreen.ContainsScreenPoint(mousePosition))
                    return;

                if (FleetUIShowing())
                {
                    if (!_fleetUIController.ContainsScreenPoint(mousePosition) && !IsPointerOverPlanet(mousePosition))
                        HideFleetUI();
                    return;
                }

                // A click on a planet is handled by PlanetUIObject.OnPointerClick (which shows
                // that planet's detail), so only close here for a click that lands neither on
                // the detail panel nor on a planet -- same as pressing Escape.
                if (PlanetDetailShowing()
                    && !_planetDetailUIController.ContainsScreenPoint(mousePosition)
                    && !IsPointerOverPlanet(mousePosition))
                {
                    // Not closed at once: UI Toolkit delivers its pointer events around now, and a press on one of the panel's own controls (the
                    // Add item button, a row of a dialog) must never count as a click outside. LateUpdate decides.
                    _closePlanetDetailRequestFrame = Time.frameCount;
                }
            }

            // ── Hover popups (human match) ──────────────────────────────────

            private const float LineHoverTolerancePixels = 10f;
            private Vector2 _hoverPointer = new Vector2(float.NaN, float.NaN);
            private int _hoverTurn = -1;
            private LineDrawObject _hoverLine;
            private float _hoverLineWidth = 1f;
            private bool _iconHoverActive;   // a fleet icon's popup is up: the line hover leaves the popup alone

            /// <summary>Shows the hover popup for a fleet icon at the pointer (the human's own and rivals' icons alike).</summary>
            public void ShowIconHover(Vector2 screenPosition, IReadOnlyList<string> blocks)
            {
                _iconHoverActive = true;
                SetHoverLine(null);
                HumanHoverPopup.Show(_mainScreenUIController != null ? _mainScreenUIController.uiDocument.rootVisualElement : null, screenPosition, blocks);
            }

            public void HideIconHover()
            {
                _iconHoverActive = false;
                _hoverPointer = new Vector2(float.NaN, float.NaN);   // re-evaluate the lines at the next frame
                HumanHoverPopup.Hide();
            }

            // The popup for the order lines under the pointer: the human's own, and a rival's whose two end planets the human sees right now.
            // Re-evaluated only when the pointer moved or a turn passed; lines are LineRenderers with no colliders, so it is a screen-space test.
            private void UpdateLineHover()
            {
                if (HumanPlayerId < 0 || Mouse.current == null || _iconHoverActive) return;
                var pointer = Mouse.current.position.ReadValue();
                if (pointer == _hoverPointer && TurnNumber == _hoverTurn) return;
                _hoverPointer = pointer;
                _hoverTurn = TurnNumber;

                var root = _mainScreenUIController != null ? _mainScreenUIController.uiDocument.rootVisualElement : null;
                var overPanel = (PlanetDetailShowing() && _planetDetailUIController.ContainsScreenPoint(pointer))
                                || (FleetUIShowing() && _fleetUIController.ContainsScreenPoint(pointer));
                if (root == null || _camera == null || overPanel)
                {
                    SetHoverLine(null);
                    HumanHoverPopup.Hide();
                    return;
                }

                var candidates = new List<(GameAI.GameAIOrder item, IReadOnlyList<Vector2> points)>();
                foreach (var pair in _orderLines)
                {
                    var line = pair.Value;
                    if (!line || !line.lineRenderer || !line.lineRenderer.enabled || !line.gameObject.activeInHierarchy) continue;
                    if (FlatSpace.Human.HumanHoverText.Order(pair.Key) == null || !FlatSpace.Human.HumanHoverText.CanSee(pair.Key, HumanPlayerId, HumanSeesPlanet)) continue;
                    var renderer = line.lineRenderer;
                    var points = new List<Vector2>(renderer.positionCount);
                    for (var i = 0; i < renderer.positionCount; i++)
                    {
                        var position = renderer.GetPosition(i);
                        var world = renderer.useWorldSpace ? position : renderer.transform.TransformPoint(position);
                        points.Add(_camera.WorldToScreenPoint(world));
                    }
                    candidates.Add((pair.Key, points));
                }

                var picked = FlatSpace.Human.HumanHoverText.Pick(candidates, pointer, LineHoverTolerancePixels);
                if (picked.Count == 0)
                {
                    SetHoverLine(null);
                    HumanHoverPopup.Hide();
                    return;
                }
                SetHoverLine(_orderLines[picked[0]]);
                HumanHoverPopup.Show(root, pointer, picked.Select(o => FlatSpace.Human.HumanHoverText.Order(o, FlatSpace.Human.HumanHoverText.ShowFullFleetDetail(o.PlayerId, HumanPlayerId))).ToList());
            }

            // Thickens the nearest line while it is the one hovered, and restores the width it had when the hover leaves it.
            private void SetHoverLine(LineDrawObject line)
            {
                if (line == _hoverLine) return;
                if (_hoverLine && _hoverLine.lineRenderer) _hoverLine.lineRenderer.widthMultiplier = _hoverLineWidth;
                _hoverLine = line;
                if (!_hoverLine || !_hoverLine.lineRenderer) return;
                _hoverLineWidth = _hoverLine.lineRenderer.widthMultiplier;
                _hoverLine.lineRenderer.widthMultiplier = _hoverLineWidth * 2f;
            }

            // ── Fleet drag (human match) ────────────────────────────────────

            private LineDrawObject _dragLine;
            private string _dragOrigin;
            private int _dragFleet;
            private string _dragCheckedTarget;
            private Color32 _dragCheckColor;

            private static string PlanetUnderPointer(Vector2 screenPosition)
            {
                if (EventSystem.current == null) return null;
                var results = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screenPosition }, results);
                foreach (var result in results)
                {
                    var planet = result.gameObject.GetComponentInParent<PlanetUIObject>();
                    if (planet != null) return planet._planetName;
                }
                return null;
            }

            private FlatSpace.Human.HumanCommand FleetMoveCommand(string origin, int fleetId, string target)
            {
                var planet = GetPlanet(origin);
                return new FlatSpace.Human.HumanCommand
                {
                    type = "move", origin = origin, target = target,
                    ships = planet == null ? new List<int>() : FlatSpace.Human.HumanFleetView.ShipIndices(planet, HumanPlayerId, fleetId),
                };
            }

            /// <summary>Whether sending a whole fleet to a planet would be accepted now (null) or why not; nothing is applied.</summary>
            public string CheckFleetDrag(string origin, int fleetId, string targetPlanet)
            {
                if (HumanPlayerId < 0 || HumanPlayerId >= players.Count || players[HumanPlayerId].playerAI == null) return "NotYourPlanet";
                var command = FleetMoveCommand(origin, fleetId, targetPlanet);
                if (command.ships.Count == 0) return "UnknownFleet";
                var processor = new FlatSpace.Human.HumanCommandProcessor(GameAI.GameAIMap, players[HumanPlayerId].playerAI, TurnNumber) { PauseSettings = HumanPause };
                return processor.Check(command);
            }

            /// <summary>Sends a whole fleet to a planet: null when applied, else the refusal reason. The usual ship-transport orders result.</summary>
            public string TryDragFleet(string origin, int fleetId, string targetPlanet)
            {
                var command = FleetMoveCommand(origin, fleetId, targetPlanet);
                if (command.ships.Count == 0) return "UnknownFleet";
                var results = SubmitHumanCommands(new[] { command }, "FleetDrag");
                if (results.Count == 0) return "NotYourPlanet";
                return results[0].applied ? null : results[0].reason;
            }

            public void BeginFleetDrag(string origin, int fleetId)
            {
                if (HumanPlayerId < 0 || !_lineDrawObjectPrefab) return;
                HideIconHover();
                CancelFleetDrag();   // never leave an earlier line behind
                _dragOrigin = origin;
                _dragFleet = fleetId;
                _dragCheckedTarget = null;
                _dragLine = Instantiate<LineDrawObject>(_lineDrawObjectPrefab, transform);
            }

            /// <summary>Drops a fleet drag without sending anything: the line goes and the drag is forgotten.</summary>
            public void CancelFleetDrag()
            {
                if (_dragLine) Destroy(_dragLine.gameObject);
                _dragLine = null;
                _dragOrigin = null;
            }

            public void UpdateFleetDrag(Vector2 screenPosition)
            {
                if (!_dragLine || _camera == null) return;
                var origin = _planetUIObjects.Find(p => p._planetName == _dragOrigin);
                if (origin == null) return;

                // Green when the planet under the pointer would take the fleet, red when not; the check runs only when that planet changes.
                var target = PlanetUnderPointer(screenPosition);
                if (target != _dragCheckedTarget)
                {
                    _dragCheckedTarget = target;
                    _dragCheckColor = target == null || target == _dragOrigin
                        ? new Color32(255, 255, 255, 255)
                        : CheckFleetDrag(_dragOrigin, _dragFleet, target) == null ? new Color32(80, 255, 80, 255) : new Color32(255, 70, 70, 255);
                }

                var from = origin.transform.position;
                var to = _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, _camera.WorldToScreenPoint(from).z));
                var renderer = _dragLine.lineRenderer;
                var a = renderer.useWorldSpace ? from : _dragLine.transform.InverseTransformPoint(from);
                var b = renderer.useWorldSpace ? to : _dragLine.transform.InverseTransformPoint(to);
                _dragLine.SetPath(new List<Vector3> { a, b }, 1f);
                _dragLine.SetColor(_dragCheckColor);
            }

            /// <summary>Ends a fleet drag at the pointer: sends the fleet to the planet under it. Returns null when sent, "" when released on no planet
            /// (nothing happens), else the refusal reason, which is also shown as a message.</summary>
            public string EndFleetDrag(Vector2 screenPosition)
            {
                if (_dragLine) Destroy(_dragLine.gameObject);
                _dragLine = null;
                var origin = _dragOrigin;
                _dragOrigin = null;
                var target = PlanetUnderPointer(screenPosition);
                if (origin == null || target == null) return "";
                var reason = TryDragFleet(origin, _dragFleet, target);
                if (reason != null && _mainScreenUIController != null)
                    HumanUiKit.Toast(_mainScreenUIController.uiDocument.rootVisualElement, FlatSpace.Human.HumanHoverText.RefusalSentence(reason));
                return reason;
            }

            private int _closePlanetDetailRequestFrame = -1;

            void LateUpdate()
            {
                if (_closePlanetDetailRequestFrame < 0 || Time.frameCount <= _closePlanetDetailRequestFrame) return;
                var requested = _closePlanetDetailRequestFrame;
                _closePlanetDetailRequestFrame = -1;
                if (!PlanetDetailShowing()) return;
                if (_planetDetailUIController.LastPointerDownFrame >= requested) return;   // one of the panel's controls took that press
                HidePlanetDetail();
            }

            private static bool IsPointerOverPlanet(Vector2 screenPosition)
            {
                if (EventSystem.current == null) return false;
                var pointerData = new PointerEventData(EventSystem.current) { position = screenPosition };
                var results = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointerData, results);
                foreach (var result in results)
                {
                    if (result.gameObject.GetComponentInParent<PlanetUIObject>() != null)
                        return true;
                }
                return false;
            }

            private void Awake()
            {
                if (_instance == null)
                    _instance = this;

                _camera = Camera.main;
                _planetUIObjects.Clear();
                if (IntialBoardState)
                {
                    InitGame(IntialBoardState._planetSpawnData);
                }
                InitializeInputActions();
                _mainScreenUIController = GetComponentInChildren<MainScreenUIController>();
                _planetDetailUIController = GetComponentInChildren<PlanetDetailUIController>();
                _fleetUIController = GetComponentInChildren<FleetUIController>();

            }

            /// <summary>The human player's id, -1 when the match has none.</summary>
            public int HumanPlayerId => MatchSettings.HumanPlayerId;

            /// <summary>What the human last saw of each planet (owner and turn); the Planets screen's Owner column. Fresh per match, saved with the game.</summary>
            public FlatSpace.Human.PlanetSightMemory PlanetSights { get; private set; } = new FlatSpace.Human.PlanetSightMemory();

            // Planet -> the last turn the human's ships cut another player's orders there (presentation only, not saved: a load shows none until the next cut).
            private readonly Dictionary<string, int> _humanBlockadingSince = new Dictionary<string, int>();
            private const int HumanBlockadingTurns = 5;

            private void RecordHumanBlockading()
            {
                if (HumanPlayerId < 0 || GameAI == null) return;
                foreach (var cut in GameAI.LastBlockadeCuts)
                    if (cut.BlockerId == HumanPlayerId && cut.PlayerId != HumanPlayerId)
                        _humanBlockadingSince[cut.Planet] = TurnNumber;
            }

            /// <summary>The Planets screen's rows for the human: the planets it has seen and its own. Empty without a human.</summary>
            public List<FlatSpace.Human.PlanetRow> BuildPlanetRows()
            {
                if (HumanPlayerId < 0 || HumanPlayerId >= players.Count || players[HumanPlayerId].playerAI == null || GameAI == null)
                    return new List<FlatSpace.Human.PlanetRow>();
                var humanId = HumanPlayerId;
                var humanAI = players[humanId].playerAI;
                var map = GameAI.GameAIMap;
                // The human blockades a planet when its docked ships cut another player's orders there, as recently as HumanBlockadingTurns ago.
                // Stance does not matter (a blockade works at peace); docked ships that cut nothing are not hostile activity.
                return FlatSpace.Human.HumanPlanetTable.Build(map, humanAI, humanId, TurnNumber, HumanSeesPlanet, HumanOwnerVisible(), HumanHasSeenPlanet, PlanetSights, humanAI.CurrentBlockadeView,
                    p => _humanBlockadingSince.TryGetValue(p.PlanetName, out var turn) && TurnNumber - turn <= HumanBlockadingTurns);
            }

            /// <summary>The Diplomacy screen's rival list: the players the human has met. Empty without a human, or with diplomacy off.</summary>
            public List<FlatSpace.Human.RivalRow> BuildDiplomacyRows()
            {
                if (HumanPlayerId < 0 || GameAI == null) return new List<FlatSpace.Human.RivalRow>();
                GameAI.GameAIMap.Diplomacy.Turn = TurnNumber;   // GameAI sets it at the start of each turn; a fresh load and the time between turns leave it behind
                return FlatSpace.Human.HumanDiplomacyView.Rivals(GameAI.GameAIMap, HumanPlayerId);
            }

            /// <summary>
            /// The Diplomacy screen's pane for one rival (null without a human, with diplomacy off, or for a player not met). The button states come
            /// from the command processor's own Check, and a fleet shows on the planets whose ships the board shows (in sight, or next to a planet the human occupies or has a ship at: HumanOwnerVisible).
            /// </summary>
            public FlatSpace.Human.RivalView BuildRivalView(int rival)
            {
                if (HumanPlayerId < 0 || HumanPlayerId >= players.Count || players[HumanPlayerId].playerAI == null || GameAI == null) return null;
                var humanAI = players[HumanPlayerId].playerAI;
                var map = GameAI.GameAIMap;
                map.Diplomacy.Turn = TurnNumber;   // see BuildDiplomacyRows: the truce and IsAtWar tests read it
                var processor = new FlatSpace.Human.HumanCommandProcessor(map, humanAI, TurnNumber) { PauseSettings = HumanPause };
                var stats = new WarshipStats(humanAI.ResearchCatalog != null ? humanAI.ResearchCatalog.catalogItems : null);
                return FlatSpace.Human.HumanDiplomacyView.Build(map, HumanPlayerId, rival, HumanOwnerVisible(), stats,
                    (type, id) => processor.Check(new FlatSpace.Human.HumanCommand { type = type, rival = id }));
            }

            /// <summary>The strength of docked ships (FleetStrength.Of, with the human's research for the stats; any player's when no human plays). 0 without a game.</summary>
            public float FleetStrengthOf(IEnumerable<Ship> ships)
            {
                var ai = HumanPlayerId >= 0 && HumanPlayerId < players.Count ? players[HumanPlayerId].playerAI : players.Select(p => p.playerAI).FirstOrDefault(a => a != null);
                if (ai == null) return 0f;
                return FleetStrength.Of(ships, new WarshipStats(ai.ResearchCatalog != null ? ai.ResearchCatalog.catalogItems : null));
            }

            /// <summary>How long a surrender's truce lasts (the Diplomacy screen's confirmation names it).</summary>
            public int SurrenderTruceTurns
                => GameAI != null && GameAI.GameAIMap != null && GameAI.GameAIMap.GameAIConstants != null ? GameAI.GameAIMap.GameAIConstants.surrenderTruceTurns : 30;

            /// <summary>
            /// Whether the planet panel shows only name, owner and population: a human plays and the planet is neither theirs nor in their sight now
            /// (a dimmed planet). Everything is shown when no human plays.
            /// </summary>
            public bool PlanetDetailIsLimited(Planet planet)
                => HumanPlayerId >= 0 && planet != null && planet.Owner != HumanPlayerId && !HumanSeesPlanet(planet.PlanetName);

            /// <summary>Whether a limited panel may show the population: the planet is next to one the human occupies or has a ship at (its owner is seen too).</summary>
            public bool PlanetPopulationShown(Planet planet)
                => !PlanetDetailIsLimited(planet) || HumanOwnerVisible()(planet.PlanetName);

            /// <summary>"You", "Player 2", "Unowned"; out of sight the last owner seen and its age ("Player 2 (7 turns ago)"), "Unknown" if never seen.</summary>
            public string OwnerLabel(Planet planet)
            {
                if (planet == null) return "";
                if (HumanPlayerId < 0) return planet.Owner < 0 ? "Unowned" : $"Player {planet.Owner}";
                var now = planet.Owner == HumanPlayerId || HumanOwnerVisible()(planet.PlanetName);
                var last = default(FlatSpace.Human.PlanetSightMemory.Sighting);
                var seen = PlanetSights.TryGet(planet.PlanetName, out last);
                var text = FlatSpace.Human.HumanPlanetTable.OwnerText(HumanPlayerId, now ? planet.Owner : last.Owner, now, now || seen, now ? 0 : TurnNumber - last.Turn);
                return text != "" ? text : (now || seen) ? "Unowned" : "Unknown";
            }

            /// <summary>Opens or closes the Planets screen (the P key and the HUD button). Only while a human plays.</summary>
            public void TogglePlanets() => _mainScreenUIController?.TogglePlanets();

            /// <summary>Opens or closes the Diplomacy screen (the T key and the HUD button). Only while a human plays.</summary>
            public void ToggleDiplomacy() => _mainScreenUIController?.ToggleDiplomacy();

            /// <summary>
            /// Planets whose owner the human sees now: those in sight, and those next to a planet it occupies or has a ship at (its vision sources). The rest of an
            /// adjacent planet (rates, building) stays hidden; only the owner is read.
            /// </summary>
            private Func<string, bool> HumanOwnerVisible()
            {
                var near = new HashSet<string>();
                var map = GameAI.GameAIMap;
                foreach (var source in map.GetVisionSourcePlanets(HumanPlayerId))
                    foreach (var neighbour in map.GetNeighbours(source.Planet.PlanetName))
                        near.Add(neighbour);
                return name => near.Contains(name) || HumanSeesPlanet(name);
            }

            /// <summary>Records the planets in the human's sight now. Does nothing without a human.</summary>
            public void UpdatePlanetSights()
            {
                if (HumanPlayerId < 0 || GameAI == null || GameAI.GameAIMap == null) return;
                PlanetSights.Update(GameAI.GameAIMap.PlanetList, HumanOwnerVisible(), TurnNumber);
            }

            /// <summary>
            /// Makes the player the human one. Player components are recreated on every init, so this runs after each
            /// InitGame of a human match (ApplyMatchHumanChoice).
            /// </summary>
            public void SetHumanPlayer(int playerId, bool freshMatch = true)
            {
                if (playerId < 0 || playerId >= players.Count) return;
                foreach (var player in players)
                    player.isHuman = player.playerID == playerId;
                // Planets change hands under this rule from now on (manual for the human, automatic otherwise), and the human's
                // planets already held start manual. A loaded save keeps its own flags (GameAIMap.SetPlanetSimulationStats).
                Planet.HumanOwnerTest = id => id == MatchSettings.HumanPlayerId;
                // A manual worker split exists only on the human's planets, so every event is the human's (the owner is already the new one when it ends as OwnerChanged).
                Planet.WorkerEventSink = (planet, kind, detail) =>
                {
                    if (kind == "Cleared") AITuningLogger.LogWorkersCleared(TurnNumber, MatchSettings.HumanPlayerId, planet.PlanetName, detail);
                    else
                    {
                        var parts = detail.Split(' ');   // "Arrival Industry" / "Loss Food"
                        AITuningLogger.LogWorkersPlaced(TurnNumber, MatchSettings.HumanPlayerId, planet.PlanetName, parts[0], parts.Length > 1 ? parts[1] : "");
                    }
                };
                if (freshMatch)
                {
                    foreach (var planet in GameAI.GameAIMap.PlanetList)
                        if (planet.Owner == playerId)
                            planet.ManualDevelopment = true;
                    players[playerId].playerAI.ShipmentsAutomated = true;   // a new match starts with shipments automated; a loaded save keeps its own flag (an old one: off)
                }
                MatchSettings.HumanRequested = true;
                MatchSettings.HumanPlayerId = playerId;
                owningPlayerId = playerId;
                AITuningLogger.LogMatchSetup(playerId, MatchSettings.TurnLimit);
                // The human sees the board as that player does: its fog, its notifications. The debug fog views are locked.
                SetFogViewMode(FlatSpace.Fog.FogViewMode.Player, playerId);
                var buttons = GetComponentInChildren<GameButtonHandler>();
                buttons?.RefreshFogView();
                buttons?.ShowNotificationList();   // the notification list is how a human plays: show it from the start
                UpdatePlanetSights();   // only overwrites the planets in sight now, so it is safe after a load's restore too
            }

            // After a board is initialised: a chosen human (a loaded save, or a pick already made) is applied at once, a requested
            // but not yet chosen one opens the picker. A match with no human requested is left exactly as it was.
            private void ApplyMatchHumanChoice()
            {
                AITuningLogger.LogMatchSetup(-1, MatchSettings.TurnLimit);
                if (!MatchSettings.HumanRequested) return;
                if (MatchSettings.HumanPlayerId >= 0)
                {
                    SetHumanPlayer(MatchSettings.HumanPlayerId, freshMatch: false);   // a loaded save: its planet flags stand
                    return;
                }
                var canvas = GetComponentInParent<Canvas>();
                if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
                if (canvas == null)
                {
                    Debug.LogError("[Gameboard] no Canvas found for the human player picker; the match runs without a human");
                    MatchSettings.HumanRequested = false;
                    return;
                }
                var choices = new List<(int id, string homePlanet)>();
                for (var i = 0; i < players.Count; i++)
                    choices.Add((i, GetPlayerCapitol(i)?.PlanetName));
                HumanPlayerPicker.Show(canvas.rootCanvas, choices, id => SetHumanPlayer(id));
            }

            private void CreatePlayerData()
            {
                // InitGame can run more than once for a single match (Awake seeds from
                // IntialBoardState, then a designer/save load re-inits). Rebuild the list
                // from scratch each time — otherwise the new Player components are appended
                // and keep their default playerID of 0, so ProcessResults runs player 0
                // again for every stale entry.
                foreach (var player in players)
                    if (player) Destroy(player);
                players.Clear();

                for (var i = 0; i < NumPlayers; i++)
                {
                    players.Add(this.AddComponent<Player>());
                    players[i].playerID = i;
                }

            }

            private void ClearPlayerData()
            {
                foreach(var player in players)
                    player.Clear();
            }

            public bool InitGameFromDesignerConfig(string filePath, SaveLoadSystem.BoardDesignerSave boardData)
            {
                if (boardData == null)
                    return false;

                IntialBoardState = null;
                _boardDesignPath = filePath;
                ClearExistingGameState();
                InitGame(boardData);
                ApplyMatchHumanChoice();
                PlanetaryUIUpdate();
                BoardUIUpdate();

                return true;
                
            }
            public bool InitGameFromSave(SaveLoadSystem.GameSave gameSave)
            {
                if (gameSave == null || (string.IsNullOrEmpty(gameSave.initialBoardStatePath) && string.IsNullOrEmpty(gameSave.boardDesignDataPath)))
                    return false;

                if (!string.IsNullOrEmpty(gameSave.boardDesignDataPath))
                {
                    SaveLoadSystem.LoadDesignerContent(gameSave.boardDesignDataPath);   
                    SetSimulationStatus(gameSave);
                    InitCameraFromSave(gameSave);
                    PlanetaryUIUpdate();
                    BoardUIUpdate();
                    return true;
                }
                else if (!string.IsNullOrEmpty(gameSave.initialBoardStatePath))
                {
                    SaveLoadSystem.Instance.LoadBoardConfigAddressable(gameSave.initialBoardStatePath, asyncOp =>
                    {
                        if (asyncOp.Status == AsyncOperationStatus.Succeeded)
                        {
                            IntialBoardState = Instantiate<BoardConfiguration>(asyncOp.Result) ;
                            ClearExistingGameState();
                            InitGame(IntialBoardState._planetSpawnData);
                            SetSimulationStatus(gameSave);
                            InitCameraFromSave(gameSave);
                            PlanetaryUIUpdate();
                            BoardUIUpdate();
                        }                     
                    });
                    return true;
                }
                return false;
            }

            private void InitCameraFromSave(SaveLoadSystem.GameSave gameSave)
            {
                if (_camera)
                {
                    var prevOrthoSize = _camera.orthographicSize;
                    _camera.orthographicSize = gameSave.cameraOrtho;
                    orthoChange = prevOrthoSize - _camera.orthographicSize;
                    _scrollRect.normalizedPosition = gameSave.scrollRectPosition;
                    foreach (var planetUI in _planetUIObjects)
                    {
                        planetUI.UIUpdateForScroll(orthoChange);

                    }
                }
            }

            private void SetSimulationStatus(SaveLoadSystem.GameSave gameSave)
            {
                TurnNumber = gameSave.turnNumber;
                HumanPause = FlatSpace.Human.HumanPauseSettings.FromSave(gameSave.hasPauseEvents, gameSave.pauseEvents);
                PlanetSights.Restore(gameSave.humanSightings?.ConvertAll(s => (s.planet, s.owner, s.turn)));
                foreach (var playerSave in gameSave.players)
                {
                    players[playerSave.playerId].SetStrategy(playerSave.strategy);
                    players[playerSave.playerId].SetSavedCatalogCompleted(playerSave.researchCatalogSave);
                    players[playerSave.playerId].SetSavedCatalogCompleted(playerSave.productionCatalogSave);
                    players[playerSave.playerId].playerAI.researchTotal = playerSave.currentResearch;
                    players[playerSave.playerId].playerAI.currentResearch =
                        players[playerSave.playerId].playerAI.ResearchCatalog.catalogItems
                            .Find(x => x.itemName == playerSave.currentResearchItem);
                    GameAI.GameAIMap.Knowledge.SetKnownPlanets(
                        playerSave.playerId, playerSave.knownPlanets ?? new List<string>());
                    players[playerSave.playerId].playerAI.SetDistributionCenters(
                        "Food", playerSave.foodDistributionCenters ?? new List<string>());
                    players[playerSave.playerId].playerAI.SetDistributionCenters(
                        "Grotsits", playerSave.grotsitsDistributionCenters ?? new List<string>());
                    players[playerSave.playerId].playerAI.RestoreRememberedBlockades(
                        playerSave.rememberedBlockades?.ConvertAll(b => b.ToEntry()));
                    GameAI.GameAIMap.Diplomacy.Restore(playerSave.playerId,
                        playerSave.stances?.ConvertAll(s => s.ToEntry()));
                    players[playerSave.playerId].playerAI.SetResearchQueue(playerSave.researchQueue);
                    players[playerSave.playerId].playerAI.ShipmentsAutomated = playerSave.shipmentsAutomated;

                }
                GameAI.SetSimulationStats(gameSave);
                ApplyMatchHumanChoice();   // MatchSettings already hold the save's human and turn limit (SaveLoadSystem.LoadSavedGame)

                if (_fogOfWarSystem != null && _fogOfWarSystem.Ready)
                {
                    foreach (var playerSave in gameSave.players)
                    {
                        if (playerSave.exploredCols == _fogOfWarSystem.GridCols &&
                            playerSave.exploredRows == _fogOfWarSystem.GridRows)
                        {
                            _fogOfWarSystem.SetExploredPacked(
                                playerSave.playerId, FogExplored.Decode(playerSave.exploredGrid));
                        }
                    }
                    _fogOfWarSystem.Recompute();
                    FogUIUpdate();
                }
            }

            private void ClearExistingGameState()
            {
                if (GameAI != null)
                    GameAI.ClearGameAI();
                ClearGraphics();
            }

            private void ClearGraphics()
            {
                foreach(var planetUIObject in _planetUIObjects)
                    Destroy(planetUIObject.gameObject);
                _planetUIObjects.Clear();
                var lineDrawObjects = GetComponentsInChildren<LineDrawObject>();
                foreach (var linedrawObject in lineDrawObjects)
                    Destroy(linedrawObject.gameObject);
                // Pooled lines are disabled, so the search above skipped them; in-flight ones were
                // destroyed above. Drop every reference so nothing reuses a destroyed line.
                foreach (var pooled in _orderLinePool)
                    if (pooled)
                        Destroy(pooled.gameObject);
                _orderLinePool.Clear();
                _orderLines.Clear();
                if (_fogOfWarSystem != null)
                    _fogOfWarSystem.DestroyOverlay();
                if (_camera)
                {
                    _camera.orthographicSize = _cameraInitialOrtho;
                }
            }

            // The planetSpawnData actually used to start the current match, regardless of whether it came
            // from the scene's own IntialBoardState or a designer/file-loaded board (which nulls
            // IntialBoardState -- see InitGameFromDesignerConfig). Repeat-run resets read this instead of
            // IntialBoardState directly so they work for either entry point.
            private List<PlanetSpawnData> _lastInitPlanetSpawnData;

            private void InitGame(List<PlanetSpawnData> planetSpawnData)
            {
                _lastInitPlanetSpawnData = planetSpawnData;
                // A fresh match always starts at turn 1. TurnNumber has no other reset path (only
                // SingleUpdate increments it, or a save load sets it explicitly), so without this a
                // repeated run here -- or a designer-board reload mid-session -- would carry over the
                // previous match's turn count instead of starting over.
                TurnNumber = 1;
                TurnLimitReached = false;
                Planet.HumanOwnerTest = null;   // no human until SetHumanPlayer; planets start automatic
                Planet.WorkerEventSink = null;
                HumanPause = FlatSpace.Human.HumanPauseSettings.Defaults();
                _humanEvents = new FlatSpace.Human.HumanTurnEvents();
                LastHumanEvents = new List<FlatSpace.Human.HumanEvent>();
                PausedByEvent = null;
                _defeatReported = false;
                _turnLimitReported = false;
                HumanNotifications = new FlatSpace.Human.HumanNotificationList();
                PlanetSights = new FlatSpace.Human.PlanetSightMemory();
                _humanBlockadingSince.Clear();
                PlanetsScreen.Close();
                PlanetsScreen.ResetState();
                DiplomacyScreen.Close();
                DiplomacyScreen.ResetState();
                GameSettingsScreen.Close();
                _shippedFood.Clear();
                _shippedGrotsits.Clear();
                AITuningLogger.BeginMatch(_logAIEvents || AITuningLogger.EnabledViaMainMenu);
                // The scene's BoardConfiguration asset, or (InitGameFromDesignerConfig clears that first) the
                // designer JSON's file name, so a log can be tied back to the board it ran on.
                AITuningLogger.LogBoardConfig(AITuningLogger.DescribeBoard(
                    IntialBoardState != null ? IntialBoardState.name : null, _boardDesignPath));

                if (GameAI == null)
                    GameAI = this.AddComponent<GameAI>() as GameAI;

                NumPlayers = planetSpawnData.Count(x => x._planetType == Planet.PlanetType.PlanetTypePrime);
                CreatePlayerData();

                ClearPlayerData();
                GameAI.InitGameAI(planetSpawnData, gameAIConstants);
                // The chokepoints of this board (the last InitGame in a match is the real one, like BoardConfig).
                AITuningLogger.LogChokepoints(GameAI.GameAIMap.TopChokepoints(10));
                InitPlanetGraphics(planetSpawnData);
                InitPathGraphics();
                InitFogOfWar();
                var playerOneCapital = GetPlayerCapitol(0);
                if(playerOneCapital)
                    ViewPlanet(playerOneCapital);
            }

            private void InitFogOfWar()
            {
                if (_fogOfWarSettings == null)
                {
                    Debug.LogWarning("[Gameboard] No FogOfWarSettings assigned; fog of war disabled.");
                    return;
                }
                if (_fogOfWarSystem == null)
                    _fogOfWarSystem = this.gameObject.AddComponent<FlatSpace.Fog.FogOfWarSystem>();

                _fogOfWarSystem.Init(GameAI.GameAIMap, _fogOfWarSettings, NumPlayers);
                _fogOfWarSystem.SetViewMode(FlatSpace.Fog.FogViewMode.NoFog, 0);
                // Reset alongside the fog view: a save/board with fewer players than the previous
                // match could otherwise leave owningPlayerId indexing past the end of players[].
                owningPlayerId = 0;

                var handler = GetComponentInChildren<GameButtonHandler>();
                if (handler) handler.RefreshFogView();

                _fogOfWarSystem.Recompute();
                FogUIUpdate();
            }

            private bool InitGame(SaveLoadSystem.BoardDesignerSave boardData)
            {
                if (GameAI == null)
                    GameAI = this.AddComponent<GameAI>() as GameAI;
                var planetSpawnData = new List<PlanetSpawnData>();
                // TODO come up with the correct loading ai constants
                /*
                if (gameAIConstants.name != boardData.aiConstants)
                {
                    SaveLoadSystem.Instance.LoadAIConstantsAddressable(boardData.aiConstants, asyncOp =>
                    {
                        if (asyncOp.Status == AsyncOperationStatus.Succeeded)
                        {
                            gameAIConstants = asyncOp.Result;
                        }
                    });
                }*/

                foreach (var planetDesignData in boardData.planetEntries)
                {
                    var spawnData = ScriptableObject.CreateInstance<PlanetSpawnData>();
                    spawnData._planetName = planetDesignData.name;
                    spawnData._planetPosition =
                        new Vector3(planetDesignData.position.x, planetDesignData.position.y, 0.0f);
                    spawnData._planetType = planetDesignData.planetType;
                    spawnData._connections = planetDesignData.connections != null
                        ? new List<string>(planetDesignData.connections)
                        : new List<string>();
                    spawnData._resourceData =
                        gameAIConstants.resourceData.Find(x => x._planetType == planetDesignData.planetType);
                    planetSpawnData.Add(spawnData);
                }
                InitGame(planetSpawnData);
                
                return true;
            }

            private void InitPlanetGraphics(List<PlanetSpawnData> spawnDataList)
            {
                foreach (var spawnData in spawnDataList)
                {
                    InitUIElement(spawnData, transform);   
                    
                }
            }
            
                
            private void InitUIElement(PlanetSpawnData spawnData, Transform parentTransform)
            {
                var prefab = _PlanetUIPrefabs?[(int)spawnData._planetType] != null ? 
                    _PlanetUIPrefabs[(int)spawnData._planetType] : Gameboard.Instance._PlanetUIPrefab;
                if (!prefab)
                    return;
            
                var uiObject = Instantiate(prefab,parentTransform) as PlanetUIObject;
                uiObject.transform.localPosition += spawnData._planetPosition;
                InitializeUIObject(uiObject, spawnData);
            }
            
            private void InitializeUIObject(PlanetUIObject uiObject , PlanetSpawnData spawnData)
            {

                if (uiObject == null)
                    return;
                uiObject._nameTextField.text = spawnData._planetName;
                uiObject._planetName = spawnData._planetName;
                uiObject.SetPlanetColor(spawnData._planetType);
                uiObject.UIUpdate();        
                _planetUIObjects.Add(uiObject); 
            }

            private void InitPathGraphics()
            {
                List<(Vector3, Vector3)> connectionPoints = new List<(Vector3, Vector3)>();
                PathingSystem.Instance.ConnectionVectors(connectionPoints);

                var prefab = _lineDrawObjectPrefab;
                if (!prefab)
                    return;

                foreach (var linePoints in connectionPoints)
                {
                    LineDrawObject lineDrawObject = Instantiate<LineDrawObject>(prefab,transform) as LineDrawObject;

                    if (lineDrawObject)
                    {
                        lineDrawObject.SetPoints(linePoints);
                    }
                }
            }

            private static bool OrderHasGraphic(GameAI.GameAIOrder order)
            {
                var hasGraphicList = new GameAI.GameAIOrder.OrderType[4]
                {
                    GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport,
                    GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport,
                    GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport,
                    GameAI.GameAIOrder.OrderType.OrderTypeShipTransport,
                };
                
                return Array.Exists(hasGraphicList, t => t == order.Type);

            }

            // The player colors are dark and about half transparent, which makes small markers fade into the
            // board; keep the hue, scale the strongest channel to 255 and drop the transparency.
            private static Color32 BrightOpaque(Color32 color)
            {
                int max = Math.Max(color.r, Math.Max(color.g, color.b));
                if (max == 0)
                    return new Color32(255, 255, 255, 255);
                var scale = 255.0f / max;
                return new Color32((byte)Math.Min(255, Mathf.RoundToInt(color.r * scale)),
                    (byte)Math.Min(255, Mathf.RoundToInt(color.g * scale)),
                    (byte)Math.Min(255, Mathf.RoundToInt(color.b * scale)), 255);
            }

            private static Color32 ColorForOrderType(GameAI.GameAIOrder order)
            {
                Color color;
                switch (order.Type)
                {
                    case GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport:
                        color = new Color32(0, 255,0, 255 );
                        break;
                    case GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport:
                        color = new Color32(0, 255, 255, 255);
                        break;
                    case GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport:
                        color = new Color32(210, 105, 30, 255);
                        break;
                    case GameAI.GameAIOrder.OrderType.OrderTypeShipTransport:
                        color = FleetSummary.ColorFor(order.PlayerId);
                        break;
                    default:
                        color = new Color32(255, 255, 255, 255);
                        break;
                }
                return color;
            }
            private void DisplayOrderGraphics(List<GameAI.GameAIOrder> orders)
            {
                var prefab = _orderLineDrawObjectPrefab;
                if (!prefab)
                    return;

                // Orders still in flight keep their line (_orderLines, keyed by the order object);
                // lines of finished orders are disabled and parked in _orderLinePool for reuse.
                var drawn = new HashSet<GameAI.GameAIOrder>();
                foreach (var order in orders)
                {
                    if(!OrderHasGraphic(order))
                        continue;

                    float offset = 10.0f;
                    if (order.Type == GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport)
                        offset = -10.0f;
                    else if (order.Type == GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport)
                        offset = 20.0f;
                    else if (order.Type == GameAI.GameAIOrder.OrderType.OrderTypeShipTransport)
                        offset = 30.0f;

                    var pathPoints = new List<Vector3>();
                    foreach (var point in GameAI.GameAIMap.OrderPathPoints(order))
                        pathPoints.Add(new Vector3(point.x + offset, point.y + offset, 0.0f));
                    if (pathPoints.Count < 2)
                        continue;   // nothing to draw (no route and no pathed Origin to Target)

                    if (!_orderLines.TryGetValue(order, out var lineDrawObject) || !lineDrawObject)
                    {
                        lineDrawObject = null;
                        while (_orderLinePool.Count > 0 && !lineDrawObject)
                            lineDrawObject = _orderLinePool.Pop();   // skips any destroyed entry
                        if (!lineDrawObject)
                            lineDrawObject = Instantiate<LineDrawObject>(prefab, transform) as LineDrawObject;
                        if (!lineDrawObject)
                            continue;
                        _orderLines[order] = lineDrawObject;
                    }
                    drawn.Add(order);

                    lineDrawObject.gameObject.SetActive(true);
                    // Fog can leave the renderers off, and its captured base colors are stale
                    // once this object serves another order; both were fresh on a new object.
                    if (lineDrawObject.lineRenderer) lineDrawObject.lineRenderer.enabled = true;
                    if (lineDrawObject.spriteRenderer) lineDrawObject.spriteRenderer.enabled = true;
                    lineDrawObject.ResetFogCapture();

                    var progressAmount =
                        Math.Clamp(
                            Convert.ToSingle(order.TotalDelay - (order.TimingDelay)) /
                            Convert.ToSingle(order.TotalDelay), 0.15f, 0.85f);
                    lineDrawObject.SetPath(pathPoints, progressAmount);
                    lineDrawObject.SetColor(ColorForOrderType(order));
                    lineDrawObject.SetSpriteColor(BrightOpaque(FleetSummary.ColorFor(order.PlayerId)));
                    if (_fogOfWarSystem != null && _fogOfWarSystem.Ready)
                    {
                        var originPlanet = GetPlanet(order.Origin);
                        var targetPlanet = GetPlanet(order.Target);
                        var visible = FlatSpace.Fog.FogVisibility.Hidden;
                        if (originPlanet != null)
                            visible = Max(visible, _fogOfWarSystem.Classify(originPlanet.Position));
                        if (targetPlanet != null)
                            visible = Max(visible, _fogOfWarSystem.Classify(targetPlanet.Position));
                        var dim = _fogOfWarSettings != null ? _fogOfWarSettings.exploredObjectDim : 0.45f;
                        lineDrawObject.SetFogState(
                            visible == FlatSpace.Fog.FogVisibility.Hidden
                                ? FlatSpace.Fog.FogVisibility.Hidden
                                : FlatSpace.Fog.FogVisibility.Visible, dim);
                    }
                }

                // Orders that finished since the last call: disable their line and pool it.
                List<GameAI.GameAIOrder> finished = null;
                foreach (var pair in _orderLines)
                    if (!drawn.Contains(pair.Key))
                        (finished ??= new List<GameAI.GameAIOrder>()).Add(pair.Key);
                if (finished == null)
                    return;
                foreach (var order in finished)
                {
                    var line = _orderLines[order];
                    _orderLines.Remove(order);
                    if (!line)
                        continue;
                    line.gameObject.SetActive(false);
                    _orderLinePool.Push(line);
                }
            }

            public Planet GetPlanet(string planetName)
            {
                return GameAI.GetPlanet(planetName);
            }

            private Planet GetPlayerCapitol(int playerID)
            {
                return GameAI.GetPlayerCapitol(playerID);
            }

            public void ViewPlanet(string planetName)
            {
                var planet = GetPlanet(planetName);
                if (planet == null)
                    return;
                ViewPlanet(planet);
            }

            private void ViewPlanet(Planet planet)
            {
                if (_scrollRect)
                {
                    var normalizedWidth = planet.Position.x + (.5f * _scrollRect.content.sizeDelta.x);
                    normalizedWidth /= _scrollRect.content.sizeDelta.x;  
                    var normalizedHeight = planet.Position.y + (.5f * _scrollRect.content.sizeDelta.y);
                    normalizedHeight /= _scrollRect.content.sizeDelta.y;
                    _scrollRect.normalizedPosition = new Vector2(normalizedWidth, normalizedHeight);
                }
            }

            /// <summary>The planet panel's controller; the human UI hosts its sections and dialogs in it.</summary>
            public PlanetDetailUIController PlanetDetail => _planetDetailUIController;

            /// <summary>
            /// The human's planets that can ship food or grotsits to a destination, with the AI's surplus figure (last turn's results scaled by
            /// the planet's population fraction) and the raw stock. Empty when no human plays or before the first turn has produced results.
            /// </summary>
            public List<FlatSpace.Human.ShipmentSource> GetShipmentSources(bool food, string destination)
            {
                if (HumanPlayerId < 0) return new List<FlatSpace.Human.ShipmentSource>();
                return FlatSpace.Human.HumanShipmentList.Build(GameAI.GameAIMap, HumanPlayerId, food, destination, GameAI.LastTurnResults,
                    planet => planet.GetPopulationFraction(HumanPlayerId), food ? _shippedFood : _shippedGrotsits);
            }

            // What the human has shipped from each planet since the last turn ended (the shipping dialog subtracts it from the reported surplus).
            private readonly Dictionary<string, float> _shippedFood = new Dictionary<string, float>();
            private readonly Dictionary<string, float> _shippedGrotsits = new Dictionary<string, float>();

            /// <summary>Checks a batch of the human's commands as Apply would run it, applying none of it (see HumanCommandProcessor.CheckBatch).</summary>
            public List<FlatSpace.Human.HumanCommandResult> CheckHumanBatch(IEnumerable<FlatSpace.Human.HumanCommand> commands)
            {
                if (HumanPlayerId < 0 || HumanPlayerId >= players.Count || players[HumanPlayerId].playerAI == null)
                    return new List<FlatSpace.Human.HumanCommandResult>();
                return new FlatSpace.Human.HumanCommandProcessor(GameAI.GameAIMap, players[HumanPlayerId].playerAI, TurnNumber)
                    { PauseSettings = HumanPause }.CheckBatch(commands);
            }

            public void ShowPlanetDetail(string planetName)
            {
                HideFleetUI();
                _planetDetailUIController.SetPlanet(GetPlanet(planetName));
                _planetDetailUIController.enabled = true;
            }

            public void HidePlanetDetail()
            {
                _planetDetailUIController.enabled = false;
            }

            public bool PlanetDetailShowing()
            {
                return _planetDetailUIController.enabled;
            }

            // Shows the fleet panel for one player's ships on the planet (the clicked fleet icon's owner).
            public void ShowFleetUI(string planetName, int owner)
            {
                if (!_fleetUIController) return;
                // A rival's fleet panel opens only for a planet the human sees now: a dimmed (explored) planet shows its icons, nothing more.
                if (HumanPlayerId >= 0 && owner != HumanPlayerId && !HumanSeesPlanet(planetName)) return;
                _panelClickFrame = Time.frameCount;
                HidePlanetDetail();
                _fleetUIController.SetPlanet(GetPlanet(planetName), owner);
                _fleetUIController.enabled = true;
            }

            public void HideFleetUI()
            {
                if (!_fleetUIController) return;
                _fleetUIController.enabled = false;
            }

            public bool FleetUIShowing()
            {
                return _fleetUIController && _fleetUIController.enabled;
            }
            
            private void InitializeInputActions()
            {
                _mapInputActions = new MapInputActions();
            }

            private void OnEnable()
            {
                if (_mapInputActions != null)
                {
                    _mapInputActions.Enable();
                    _mapInputActions.MapActions.MapZoom.performed += OnScrollPerformed;
                    _mapInputActions.MapActions.OpenMainMenu.performed += OnOpenMainMenuButtonPressed;
                }
            }

            private void OnDisable()
            {
                _mapInputActions?.Disable();
            }

            #region Input
            private void OnScrollPerformed(InputAction.CallbackContext context)
            {
                var scrollValue = context.ReadValue<float>();
                if (scrollValue != 0)
                {
                    var prevOrthoSize = _camera.orthographicSize;
                    var targetSize = _camera.orthographicSize + (scrollValue * -_cameraOrthoStep);
                    _camera.orthographicSize = Math.Clamp(targetSize, _minCameraOrtho, _maxCameraOrtho);
                    orthoChange = prevOrthoSize - _camera.orthographicSize;
                    foreach (var planetUI in _planetUIObjects)
                    {
                        planetUI.UIUpdateForScroll(orthoChange);
                    }

                }
            }
            private void OnOpenMainMenuButtonPressed(InputAction.CallbackContext context)
            {
                Debug.Log("Open Main Menu");
                var gameButtonHandler = GetComponentInChildren<GameButtonHandler>();
                if (gameButtonHandler)
                    gameButtonHandler.EscapeButtonPressed();
            }
            #endregion
            #region UpdatePlanet Functions

            public void SingleUpdate()
            {
                if (TurnLimitReached)
                    return;
                // A human was asked for but not picked yet: no turn runs, or the AI would play the player they are about to choose.
                if (MatchSettings.HumanRequested && MatchSettings.HumanPlayerId < 0)
                    return;
                // add Ai Actions here
                _playerNotifications.Clear();
                PausedByEvent = null;   // the reason the last run stopped is over once a turn is played
                GameAI.GameAIUpdate();
                TurnNumber++;
                if (MatchSettings.TurnLimit > 0 && TurnNumber == MatchSettings.TurnLimit)
                    TurnLimitReached = true;
                if (_fogOfWarSystem != null && _fogOfWarSystem.Ready)
                    _fogOfWarSystem.Recompute();   // before the events: rival events are reported only for planets in sight
                UpdatePlanetSights();
                RecordHumanBlockading();
                CollectHumanEvents();
                ReportTurnLimit();
                PlanetaryUIUpdate();
                BoardUIUpdate();

            }

            /// <summary>
            /// A click on a notification: the board centres on its planet and the planet panel opens (read-only when the planet is no longer
            /// the human's). The list is not changed; it clears itself at the start of the next turn. An entry with no planet does nothing.
            /// </summary>
            public void GoToNotification(FlatSpace.Human.HumanNotificationEntry entry)
            {
                if (entry != null) GoToPlanet(entry.Planet);
            }

            /// <summary>
            /// A row of the fleet summary: the board centres on the planet (the destination for a fleet in flight) and nothing opens.
            /// </summary>
            public void GoToFleet(string planetName)
            {
                if (string.IsNullOrEmpty(planetName) || GetPlanet(planetName) == null) return;
                _panelClickFrame = Time.frameCount;
                ViewPlanet(planetName);
            }

            /// <summary>Centres the board on a planet and opens its panel (the pause banner and the notification list use it).</summary>
            public void GoToPlanet(string planetName)
            {
                if (string.IsNullOrEmpty(planetName) || GetPlanet(planetName) == null) return;
                _panelClickFrame = Time.frameCount;   // the click is on a HUD element: Update() must not read it as an outside click
                ViewPlanet(planetName);
                ShowPlanetDetail(planetName);
            }

            /// <summary>Whether the human has seen a planet: it is not hidden in the human's fog view (live or explored); the known planets when there is no fog system.</summary>
            public bool HumanHasSeenPlanet(string planetName)
            {
                var planet = GetPlanet(planetName);
                if (planet == null) return false;
                if (_fogOfWarSystem != null && _fogOfWarSystem.Ready)
                    return _fogOfWarSystem.Classify(planet.Position) != FlatSpace.Fog.FogVisibility.Hidden;
                return GameAI.GameAIMap.Knowledge.IsKnown(HumanPlayerId, planetName);
            }

            /// <summary>Whether a planet is in the human's sight right now (the fog view); the known planets when there is no fog system.</summary>
            private bool HumanSeesPlanet(string planetName)
            {
                var planet = GetPlanet(planetName);
                if (planet == null) return false;
                if (_fogOfWarSystem != null && _fogOfWarSystem.Ready)
                    return _fogOfWarSystem.Classify(planet.Position) == FlatSpace.Fog.FogVisibility.Visible;
                return GameAI.GameAIMap.Knowledge.IsKnown(HumanPlayerId, planetName);
            }

            /// <summary>
            /// Where every button of the human UI sends its commands. They are checked and turned into orders by the same
            /// HumanCommandProcessor as the JSON file's, and the orders execute at once (GameAI.ApplyOrdersNow), so the screens show the
            /// result immediately. Returns one result per command; empty when no human plays. screen labels the caller for the log.
            /// </summary>
            public List<FlatSpace.Human.HumanCommandResult> SubmitHumanCommands(IEnumerable<FlatSpace.Human.HumanCommand> commands, string screen)
            {
                var results = new List<FlatSpace.Human.HumanCommandResult>();
                if (HumanPlayerId < 0 || HumanPlayerId >= players.Count) return results;
                var humanAI = players[HumanPlayerId].playerAI;
                if (humanAI == null) return results;

                var commandList = commands.ToList();
                var processor = new FlatSpace.Human.HumanCommandProcessor(GameAI.GameAIMap, humanAI, TurnNumber) { PauseSettings = HumanPause };
                var orders = new List<GameAI.GameAIOrder>();
                results = processor.Apply(commandList, orders);
                humanAI.StartQueuedResearchIfIdle(orders);   // an item queued while nothing is in progress starts now
                AITuningLogger.LogNewOrders(TurnNumber, orders);   // the same start lines the AI's orders write, so arrivals have a start in the log
                GameAI.ApplyOrdersNow(orders);

                var refused = results.Count(r => !r.applied);
                AITuningLogger.LogHumanUiOrder(TurnNumber, HumanPlayerId, screen, results.Count, refused);
                for (var i = 0; i < results.Count; i++)
                {
                    var result = results[i];
                    if (!result.applied)
                    {
                        AITuningLogger.LogHumanCommandRefused(TurnNumber, HumanPlayerId, result.type, result.reason);
                        continue;
                    }
                    AITuningLogger.LogHumanCommand(TurnNumber, HumanPlayerId, result.type, "", "", result.detail);
                    // Remember what was shipped this turn: last turn's surplus report does not know about it.
                    if (result.type == "shipResource" && i < commandList.Count && commandList[i] != null)
                    {
                        var shipped = string.Equals(commandList[i].kind, "food", StringComparison.OrdinalIgnoreCase) ? _shippedFood : _shippedGrotsits;
                        shipped.TryGetValue(commandList[i].origin ?? "", out var before);
                        shipped[commandList[i].origin ?? ""] = before + commandList[i].count;
                    }
                }

                PlanetaryUIUpdate();
                BoardUIUpdate();   // also redraws the order lines and refreshes an open planet or fleet panel
                return results;
            }

            // The human has no populated planet, no colony ship and no colonist in flight: said once, the timer stops, the match goes on to watch.
            private void CheckHumanDefeat()
            {
                if (_defeatReported) return;
                if (!FlatSpace.Human.HumanStatus.IsDefeated(GameAI.GameAIMap, GameAI.CurrentAIOrders, HumanPlayerId,
                        out var populated, out var colonyShips, out var colonists))
                    return;
                _defeatReported = true;
                AITuningLogger.LogHumanDefeat(TurnNumber, HumanPlayerId, populated, colonyShips, colonists);
                StopTimedUpdate();
                ShowMatchEnd("Defeat", "You have no populated planets and no way to found a colony. The match goes on without you.");
            }

            // The match reached its turn limit: logged once for every match, a popup only when a human plays. No winner (scoring is a later feature).
            private void ReportTurnLimit()
            {
                if (!TurnLimitReached || _turnLimitReported) return;
                _turnLimitReported = true;
                AITuningLogger.LogTurnLimitReached(TurnNumber, MatchSettings.TurnLimit);
                if (HumanPlayerId < 0)
                    return;   // TimedUpdate stops a watch-only run at the limit itself, or starts the next repeat run
                StopTimedUpdate();
                ShowMatchEnd("Turn limit reached", $"The match ended at turn {TurnNumber}. There is no winner yet.");
            }

            private void ShowMatchEnd(string title, string message)
            {
                var canvas = GetComponentInParent<Canvas>();
                if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
                if (canvas != null) MatchEndPopup.Show(canvas.rootCanvas, title, message);
            }

            // The turn just played, as the human sees it. An enabled pause event stops the timer (stepping is unaffected); the event
            // that did is kept in PausedByEvent and logged.
            private void CollectHumanEvents()
            {
                PausedByEvent = null;
                _shippedFood.Clear();   // a new turn has produced new surplus reports
                _shippedGrotsits.Clear();
                if (HumanPlayerId < 0 || HumanPlayerId >= players.Count) return;
                var humanAI = players[HumanPlayerId].playerAI;
                if (humanAI == null) return;
                CheckHumanDefeat();

                var input = new FlatSpace.Human.HumanTurnInput
                {
                    Results = GameAI.LastTurnResults,
                    Executed = GameAI.LastExecutedOrders,
                    NewOrders = GameAI.LastNewOrders,
                    Cuts = GameAI.LastBlockadeCuts,
                    Conversions = GameAI.LastConversionReports,
                    // Only the blockades that matter to the human: its own planets, and planets where its orders were cut.
                    BlockadedPlanets = humanAI.CurrentBlockadeView != null
                        ? FlatSpace.Human.HumanTurnEvents.RelevantBlockades(GameAI.GameAIMap, HumanPlayerId, humanAI.CurrentBlockadeView.BlockadedNames,
                            humanAI.RememberedBlockades(TurnNumber).Select(e => e.Planet))
                        : new List<string>(),
                    ResearchCompleted = humanAI.TakeResearchCompleted(),
                    ShipmentsAutomated = humanAI.ShipmentsAutomated,
                    IsVisible = HumanSeesPlanet,
                };
                LastHumanEvents = _humanEvents.Collect(GameAI.GameAIMap, humanAI, input);
                var blockaded = new List<string>(input.BlockadedPlanets);
                var researchIdle = humanAI.ResearchIdle;
                var fresh = HumanNotifications.Update(TurnNumber, LastHumanEvents,
                    (kind, planet) => FlatSpace.Human.HumanOngoing.IsStillTrue(GameAI.GameAIMap, HumanPlayerId, blockaded, LastHumanEvents, kind, planet, researchIdle));
                // A shortage stops the timer only when something can be done about it: one of the human's planets could ship to it. It is
                // listed either way. With shipments automated the AI answers shortages itself, so they never stop the timer.
                fresh = fresh.FindAll(e => (e.Kind != FlatSpace.Human.HumanEventKind.ShortageFood && e.Kind != FlatSpace.Human.HumanEventKind.ShortageGrotsits)
                                           || (!humanAI.ShipmentsAutomated && GetShipmentSources(e.Kind == FlatSpace.Human.HumanEventKind.ShortageFood, e.Planet).Count > 0));
                if (HumanPause.ShouldPause(fresh, out var first))
                {
                    PausedByEvent = first;
                    AITuningLogger.LogHumanPause(TurnNumber, HumanPlayerId, first.Kind.ToString(), first.Planet);
                    StopTimedUpdate();
                }
            }

            private void PlanetaryUIUpdate()
            {
                foreach (var planetUI in _planetUIObjects)
                {
                    planetUI.UIUpdate();
                }
            }

            private void BoardUIUpdate()
            {
                DisplayOrderGraphics(GameAI.CurrentAIOrders);

                // When the debug fog view is locked to one player, scope the notification list to
                // that player too; "No Fog"/"All Players" show everyone's notifications as before.
                var scopedToOnePlayer = _fogOfWarSystem != null
                    && _fogOfWarSystem.ViewMode == FlatSpace.Fog.FogViewMode.Player;
                var notifications = scopedToOnePlayer
                    ? _playerNotifications.FindAll(n => n.PlayerName == owningPlayerId.ToString())
                    : _playerNotifications;
                // A human sees their own persistent list instead of the transient per-turn messages.
                if (HumanPlayerId >= 0)
                    notifications = HumanNotifications.Entries.Select(e => new PlayerNotification
                    {
                        PlayerName = HumanPlayerId.ToString(), Message = e.Text, ViewTarget = e.Planet, Source = e,
                    }).ToList();
                _mainScreenUIController?.SetNotifications(notifications);
                if (HumanPlayerId >= 0 && PausedByEvent.HasValue)
                    _mainScreenUIController?.SetPauseBanner(
                        "Stopped: " + FlatSpace.Human.HumanNotificationList.TextFor(PausedByEvent.Value, 1), PausedByEvent.Value.Planet);
                else
                    _mainScreenUIController?.SetPauseBanner(null, null);

                _mainScreenUIController?.SetStatus(Gameboard.Instance.TurnNumber, players[owningPlayerId].playerAI?.researchTotal ?? 0, players[owningPlayerId].playerAI?.currentResearch?.itemName ?? "", 0 );
                if(PlanetDetailShowing())
                    _planetDetailUIController.UpdatePlanetDetail();
                if (FleetUIShowing())
                    _fleetUIController.RefreshShipList();
                FogUIUpdate();
            }

            public void FogUIUpdate()
            {
                if (_fogOfWarSystem == null || !_fogOfWarSystem.Ready)
                    return;
                var dim = _fogOfWarSettings != null ? _fogOfWarSettings.exploredObjectDim : 0.45f;
                // The human sees the fleets on the planets next to its own (an explored planet shows its icons but not its stats).
                var nextToHuman = HumanPlayerId >= 0 && GameAI != null && GameAI.GameAIMap != null ? HumanOwnerVisible() : null;

                foreach (var planetUI in _planetUIObjects)
                {
                    var planet = GetPlanet(planetUI._planetName);
                    if (planet == null) continue;
                    planetUI.SetFogState(_fogOfWarSystem.Classify(planet.Position), dim, nextToHuman != null && nextToHuman(planetUI._planetName));
                }

                var lineDrawObjects = GetComponentsInChildren<LineDrawObject>();
                foreach (var line in lineDrawObjects)
                {
                    if (line.CompareTag("OrderLineDraw")) continue; // handled in DisplayOrderGraphics
                    var lr = line.lineRenderer;
                    if (lr == null || lr.positionCount < 2) continue;
                    var a = (Vector2)lr.GetPosition(0);
                    var b = (Vector2)lr.GetPosition(lr.positionCount - 1);
                    // Sample interior points, not the endpoints: a connection reads as what you can
                    // see ALONG it. Endpoints sit on planets (biased visible) and would light a long
                    // connection whose middle is unexplored; adjacency-explored segments read grey.
                    var state = Max(_fogOfWarSystem.Classify(Vector2.Lerp(a, b, 0.2f)),
                                Max(_fogOfWarSystem.Classify(Vector2.Lerp(a, b, 0.5f)),
                                    _fogOfWarSystem.Classify(Vector2.Lerp(a, b, 0.8f))));
                    line.SetFogState(state, dim);
                }
            }

            private static FlatSpace.Fog.FogVisibility Max(
                FlatSpace.Fog.FogVisibility x, FlatSpace.Fog.FogVisibility y)
                => (FlatSpace.Fog.FogVisibility)Mathf.Max((int)x, (int)y);

            public void SetFogViewMode(FlatSpace.Fog.FogViewMode mode, int playerIndex)
            {
                if (_fogOfWarSystem == null) return;
                _fogOfWarSystem.SetViewMode(mode, playerIndex);
                // Converge the debug view with "which player's HUD am I looking at": locking the
                // fog view to one player also scopes the status bar + notifications to them;
                // No Fog / All Players resets to player 0 (today's unfiltered default).
                owningPlayerId = mode == FlatSpace.Fog.FogViewMode.Player
                    ? Mathf.Clamp(playerIndex, 0, Mathf.Max(0, NumPlayers - 1))
                    : 0;
                BoardUIUpdate(); // refreshes fog visuals, order lines, status bar, and notifications immediately
            }

            private bool _timedUpdateRunning = false;
            private const int RepeatRunCount = 6;
            private int _repeatRunsStarted;

            public void StartTimedUpdate()
            {
                _repeatRunsStarted = 1;
                _timedUpdateRunning = true;
                StartCoroutine(TimedUpdate(.1f));
            }

            IEnumerator TimedUpdate(float waitTime)
            {
                while (_timedUpdateRunning)
                {
                    SingleUpdate();
                    yield return new WaitForSeconds(waitTime);
                    if (MatchSettings.TurnLimit > 0 && Gameboard.Instance.TurnNumber == MatchSettings.TurnLimit)
                    {
                        // A human match never restarts itself: the repeat runs are for unattended AI tuning.
                        if (HumanPlayerId >= 0)
                        {
                            Gameboard.Instance.StopTimedUpdate();
                            continue;
                        }
                        // Continue the SAME coroutine loop for the next run rather than starting a new
                        // one, so there's never a risk of two TimedUpdate coroutines running at once.
                        if (_repeatRunThreeTimes && _repeatRunsStarted < RepeatRunCount && _lastInitPlanetSpawnData != null)
                        {
                            _repeatRunsStarted++;
                            ClearExistingGameState();
                            InitGame(_lastInitPlanetSpawnData);
                            PlanetaryUIUpdate();
                            BoardUIUpdate();
                        }
                        else
                        {
                            Gameboard.Instance.StopTimedUpdate();
                        }
                    }
                }
            }

            public void StopTimedUpdate()
            {
                _timedUpdateRunning = false;
                // The run buttons show a run in progress until told otherwise: put them back so Next Turn and Start Run work at once.
                GetComponentInChildren<GameButtonHandler>()?.RunStopped();
            }

            public void CreateNotificationsForCompletedResearch(string completedResearch, int playerId)
            {
                if (!string.IsNullOrEmpty(completedResearch))
                {
                    _playerNotifications.Add(new PlayerNotification{
                        PlayerName = playerId.ToString(),
                        Message = "Player: " + playerId + " Completed research:  " + completedResearch,
                        ViewTarget = GameAI.GetPlayerCapitolName(playerId),
                    });
                }
            }
            public void CreateNotificationsForNewResearch(string newResearch, int playerId)
            {
                if (!string.IsNullOrEmpty(newResearch))
                {
                    _playerNotifications.Add(new PlayerNotification{
                        PlayerName = playerId.ToString(),
                        Message = "Player: " + playerId + " Starting research:  " + newResearch,
                        ViewTarget = GameAI.GetPlayerCapitolName(playerId),
                    });
                }
            }

            private static string FleetKindLabel(GameAI.GameAIOrder order)
                {
                if (order.Fleet == null) return "warships";
                switch (order.Fleet.Kind)
                {
                    case Ship.ShipKind.ColonyShip: return "colony ships";
                    case Ship.ShipKind.SiegeShip: return "siegeships";
                    default: return "warships";
                }
            }

            public void CreateNotificationsForExecutingOrders(List<GameAI.GameAIOrder> gameAIOrders)
            {
                foreach (var order in gameAIOrders)
                {
                    switch (order.Type)
                    {
                        case GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport:
                            _playerNotifications.Add(new PlayerNotification
                            {
                                PlayerName = order.PlayerId.ToString(),
                                Message = "Receiving " + order.Data + " Food From " + order.Origin + " at " +
                                          order.Target,
                                ViewTarget = order.Target,
                            });
                            break;
                        case GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport:
                            _playerNotifications.Add(new PlayerNotification
                            {
                                PlayerName = order.PlayerId.ToString(),
                                Message = "Receiving " + order.Data + " Grotsits From " + order.Origin + " at " +
                                          order.Target,
                                ViewTarget = order.Target,
                            });
                            break;
                        case GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport:
                            _playerNotifications.Add(new PlayerNotification
                            {
                                PlayerName = order.PlayerId.ToString(),
                                Message = "Colonizer arrived at " + order.Target + " From " + order.Origin,
                                ViewTarget = order.Target,
                            });
                            break;
                        case GameAI.GameAIOrder.OrderType.OrderTypeShipTransport:
                            _playerNotifications.Add(new PlayerNotification
                            {
                                PlayerName = order.PlayerId.ToString(),
                                Message = "Fleet of " + order.Data + " " + FleetKindLabel(order) + " arrived at " +
                                          order.Target + " from " + order.Origin,
                                ViewTarget = order.Target,
                            });
                            break;
                        default:
                            break;
                    }
                }
            }

            public void CreateNotificationsForNewOrders(List<GameAI.GameAIOrder> gameAIOrders)
            {
                foreach (var order in gameAIOrders)
                {
                    switch (order.Type)
                    {
                        case GameAI.GameAIOrder.OrderType.OrderTypeFoodTransport:
                             _playerNotifications.Add(new PlayerNotification 
                             {
                                 PlayerName = order.PlayerId.ToString(),
                                 Message = "Shipping " + order.Data + " Food From " + order.Origin + " to " + order.Target,
                                 ViewTarget = order.Origin,
                             });
                            break;
                         case GameAI.GameAIOrder.OrderType.OrderTypeGrotsitsTransport:
                             _playerNotifications.Add(new PlayerNotification
                             {
                                 PlayerName = order.PlayerId.ToString(),
                                 Message = "Shipping " + order.Data + " Grotsits From " + order.Origin + " to " + order.Target,
                                 ViewTarget = order.Origin,
                             });
                             break;
                         case GameAI.GameAIOrder.OrderType.OrderTypePopulationTransport:
                             _playerNotifications.Add(new PlayerNotification
                             {
                                 PlayerName = order.PlayerId.ToString(),
                                 Message = "Colonizing " + order.Target + " From " + order.Origin,
                                 ViewTarget = order.Origin,
                             });
                             break;
                         case GameAI.GameAIOrder.OrderType.OrderTypeIndustrySetProduction:
                            _playerNotifications.Add(new PlayerNotification
                            {
                                PlayerName = order.PlayerId.ToString(),
                                Message = "Add Production " + order.Data.ToString() + " At " + order.Origin,
                                ViewTarget = order.Origin,
                            });
                            break;
                         case GameAI.GameAIOrder.OrderType.OrderTypeShipTransport:
                             _playerNotifications.Add(new PlayerNotification
                             {
                                 PlayerName = order.PlayerId.ToString(),
                                 Message = "Fleet of " + order.Data + " " + FleetKindLabel(order) + " departing " +
                                           order.Origin + " for " + order.Target,
                                 ViewTarget = order.Origin,
                             });
                             break;
                         default:
                             break;
                    }
                }
            }

            #endregion
        }
    }
}
