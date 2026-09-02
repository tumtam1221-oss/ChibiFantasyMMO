namespace ChibiFantasy.Data
{
    /// <summary>
    /// A pluggable content check.
    /// </summary>
    /// <remarks>
    /// The extension point for specialised rules that this step deliberately does not
    /// write: that an equipment piece has a real slot and a resolvable rarity, that a
    /// monster's drop table exists, that a quest's objectives point at real targets. Those
    /// depend on content decisions that have not been made.
    ///
    /// A rule reports into the report and must not alter the definition it inspects.
    /// </remarks>
    public interface IDefinitionValidationRule
    {
        void Validate(IDefinition definition, IDefinitionLookup lookup, ValidationReport report);
    }
}
