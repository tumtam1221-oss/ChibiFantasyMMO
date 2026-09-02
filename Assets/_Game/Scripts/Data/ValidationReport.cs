using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// The findings from one validation run.
    /// </summary>
    /// <remarks>
    /// Accumulates rather than throwing, so a content set can be checked in full and every
    /// problem reported at once. Stopping at the first fault would make fixing a broken
    /// content drop an iterative guessing game.
    ///
    /// Messages are kept in the order they were reported, which combined with deterministic
    /// iteration in the validator makes a report reproducible between runs and therefore
    /// diffable in CI.
    /// </remarks>
    public sealed class ValidationReport
    {
        private readonly List<ValidationMessage> _messages = new List<ValidationMessage>();
        private int _errorCount;
        private int _warningCount;

        /// <summary>Every finding, in report order.</summary>
        public IReadOnlyList<ValidationMessage> Messages => _messages;

        /// <summary>True when nothing was reported at error severity. Warnings do not fail.</summary>
        public bool IsValid => _errorCount == 0;

        public int ErrorCount => _errorCount;

        public int WarningCount => _warningCount;

        public void Add(ValidationMessage message)
        {
            _messages.Add(message);

            if (message.Severity == ValidationSeverity.Error)
            {
                _errorCount++;
            }
            else
            {
                _warningCount++;
            }
        }

        public void AddError(ValidationCode code, DefinitionId definitionId, string message)
        {
            Add(new ValidationMessage(ValidationSeverity.Error, code, definitionId, message));
        }

        public void AddWarning(ValidationCode code, DefinitionId definitionId, string message)
        {
            Add(new ValidationMessage(ValidationSeverity.Warning, code, definitionId, message));
        }
    }
}
