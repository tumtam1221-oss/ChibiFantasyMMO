namespace ChibiFantasy.Data
{
    /// <summary>
    /// The gender a player chose for a character.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="GenderAvailability"/>, despite the overlapping words.
    /// That enum answers "which genders may select this class" and lives on
    /// <see cref="ClassDefinition"/> as authored content; its Any value is a permission,
    /// not a person. This enum answers "what did this player pick", and Any would be
    /// meaningless here. Reusing one for the other would let a class-availability rule be
    /// stored as a character's gender and never be caught by the compiler.
    ///
    /// Unspecified exists so an unset or partially deserialized value does not silently
    /// read as Male, which is what a zero-valued Male would cause.
    /// </remarks>
    public enum CharacterGender
    {
        Unspecified = 0,
        Male = 1,
        Female = 2
    }
}
