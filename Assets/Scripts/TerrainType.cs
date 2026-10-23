/// <summary>
/// Terrain categories stored in the GridManager's 2D array.
/// Plain must stay first (value 0) so a freshly allocated array defaults to Plain.
/// </summary>
public enum TerrainType
{
    Plain = 0,
    Muddy,
    Stony,
    Water,
    OutOfBounds
}