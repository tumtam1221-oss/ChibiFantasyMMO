namespace ChibiFantasy.Data
{
    /// <summary>
    /// Elemental affinity used for offence and resistance matching.
    /// </summary>
    /// <remarks>
    /// Modelled as an enum rather than a definition because element interactions form a
    /// fixed matrix that damage resolution must handle explicitly; adding an element is a
    /// code change either way. If elements later need authored per-element data such as
    /// icons, names or resistance curves, this becomes an ElementDefinition and the field
    /// becomes a DefinitionId.
    /// </remarks>
    public enum ElementType
    {
        Neutral = 0,
        Fire = 1,
        Water = 2,
        Earth = 3,
        Wind = 4,
        Light = 5,
        Dark = 6
    }
}
