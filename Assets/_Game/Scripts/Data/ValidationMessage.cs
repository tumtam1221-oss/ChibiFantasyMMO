using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>How much a validation finding matters.</summary>
    /// <remarks>
    /// Severity is decided by the rule that reports the finding, not implied by its
    /// <see cref="ValidationCode"/>. The same fact can be fatal in one content set and
    /// tolerable in another; keeping the two independent avoids baking one project's
    /// policy into the shared vocabulary.
    /// </remarks>
    public enum ValidationSeverity
    {
        Warning = 0,
        Error = 1
    }

    /// <summary>
    /// Machine-readable classification of a validation finding.
    /// </summary>
    /// <remarks>
    /// Deliberately short. Only the codes this step can actually produce are defined;
    /// specialised content rules will add their own as they are written, and an enum that
    /// anticipates them would be a list of guesses.
    /// </remarks>
    public enum ValidationCode
    {
        None = 0,

        /// <summary>A null entry appeared where a definition was expected.</summary>
        NullDefinition = 1,

        /// <summary>A definition carries no usable identity.</summary>
        MissingDefinitionId = 2,

        /// <summary>Two definitions in the same scope claim the same identity.</summary>
        DuplicateDefinitionId = 3,

        /// <summary>A definition points at an identity that does not resolve.</summary>
        MissingReference = 4
    }

    /// <summary>
    /// One validation finding.
    /// </summary>
    /// <remarks>
    /// Structured rather than a bare string so findings can be counted, filtered and acted
    /// on by tooling. The human-readable text is for the reader; the code and severity are
    /// for the build.
    /// </remarks>
    public readonly struct ValidationMessage
    {
        public ValidationMessage(ValidationSeverity severity, ValidationCode code,
            DefinitionId definitionId, string message)
        {
            Severity = severity;
            Code = code;
            DefinitionId = definitionId;
            Message = message;
        }

        public ValidationSeverity Severity { get; }

        public ValidationCode Code { get; }

        /// <summary>The definition the finding concerns. May be
        /// <see cref="DefinitionId.None"/> when the definition had no usable identity.</summary>
        public DefinitionId DefinitionId { get; }

        public string Message { get; }

        public override string ToString()
        {
            string id = DefinitionId.IsValid ? DefinitionId.ToString() : "<no id>";
            return Severity + " " + Code + " [" + id + "] " + Message;
        }
    }
}
