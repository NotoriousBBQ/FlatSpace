namespace FlatSpace.Fog
{
    public enum FogVisibility { Hidden, Explored, Visible }

    public enum FogViewMode { NoFog, AllPlayers, Player }

    public struct FogSample
    {
        public float Strength;
        public bool Explored;
    }
}
