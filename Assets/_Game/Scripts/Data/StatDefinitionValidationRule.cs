namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that a stat's own bounds make sense.
    /// </summary>
    /// <remarks>
    /// Plugs into the existing <see cref="DefinitionValidator"/> through
    /// <see cref="IDefinitionValidationRule"/>, so a malformed stat is caught in the same
    /// content pass as everything else.
    ///
    /// A stat whose maximum sits below its minimum admits no legal value at all, which
    /// would make every character holding it fail validation for a reason that is not their
    /// fault. Catching it on the definition points at the actual mistake.
    /// </remarks>
    public sealed class StatDefinitionValidationRule : IDefinitionValidationRule
    {
        public void Validate(IDefinition definition, IDefinitionLookup lookup, ValidationReport report)
        {
            var stat = definition as StatDefinition;

            if (stat == null)
            {
                return;
            }

            if (stat.MaxValue < stat.MinValue)
            {
                report.AddError(ValidationCode.InvalidConfiguration, stat.Id,
                    "Maximum value " + stat.MaxValue + " is below minimum value "
                    + stat.MinValue + ", so no value is legal.");
            }

            if (stat.MinValue < 0)
            {
                report.AddError(ValidationCode.InvalidConfiguration, stat.Id,
                    "Minimum value " + stat.MinValue
                    + " is negative, but a character base stat cannot be.");
            }
        }
    }
}
