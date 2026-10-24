namespace Game.UI.MainGameScreenUI
{
    public class PlayerNotification
    {
        public string PlayerName { get; set; }
        public string Message { get; set; }
        public string ViewTarget { get; set; }
        // The human notification entry this line shows (null for the per-turn messages); clicking it goes through Gameboard.GoToNotification.
        public object Source { get; set; }
    }
}
