using System.Linq;

/// <summary>
/// Match setup chosen in the menu before the Flatspace scene loads: the turn limit and whether (and which) player is human.
/// A plain static class, not a MonoBehaviour: Unity only clears statics on a domain reload, not on a scene load, and
/// Gameboard.Awake() reads these before any sceneLoaded callback would fire (same precedent as AITuningLogger.EnabledViaMainMenu).
/// </summary>
public static class MatchSettings
{
    /// <summary>The selectable turn limits; 0 is Unlimited.</summary>
    public static readonly int[] TurnLimitPresets = { 100, 200, 400, 0 };

    public const int DefaultTurnLimit = 400;

    /// <summary>Turn at which the match ends; 0 = Unlimited.</summary>
    public static int TurnLimit = DefaultTurnLimit;

    /// <summary>The menu asked for a human player; the player id is picked after the board loads.</summary>
    public static bool HumanRequested;

    /// <summary>The human's player id, -1 while none is chosen.</summary>
    public static int HumanPlayerId = -1;

    public static int ClampTurnLimit(int value) => TurnLimitPresets.Contains(value) ? value : DefaultTurnLimit;

    public static void Reset()
    {
        TurnLimit = DefaultTurnLimit;
        HumanRequested = false;
        HumanPlayerId = -1;
    }
}
