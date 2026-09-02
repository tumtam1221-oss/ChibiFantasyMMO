namespace ChibiFantasy.Data
{
    /// <summary>
    /// Which part of a character an appearance option applies to.
    /// </summary>
    /// <remarks>
    /// A structural category, not content. The forbidden shape would be an enum listing
    /// the options themselves (MaleFace01, FemaleHair03); this lists the places options
    /// plug into, each of which a renderer has to handle explicitly. Adding Face02 is
    /// authoring an asset; adding Eyebrow is a code change either way.
    ///
    /// Future slots such as eyebrow, mouth, beard, tattoo, scar, accessory or body
    /// proportion extend this enum without touching any state, definition or validation
    /// code, because nothing switches on the full set.
    /// </remarks>
    public enum AppearanceSlot
    {
        None = 0,
        Face = 1,
        Eyes = 2,
        Hair = 3,
        HairColor = 4,
        SkinTone = 5
    }
}
