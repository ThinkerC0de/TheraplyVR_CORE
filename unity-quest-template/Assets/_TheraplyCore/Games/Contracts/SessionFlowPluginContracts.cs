using System.Collections.Generic;

namespace TheraplyCore.Games.Contracts
{
    /// <summary>
    /// Reusable action plugin contract.
    /// Plugins provide intent parsing + validate/apply behavior for one action type.
    /// </summary>
    public interface IActionPlugin
    {
        string ActionId { get; }
        string ChannelId { get; }

        bool TryCreateIntent(
            IReadOnlyDictionary<string, object> rawInput,
            out ActionIntent intent);

        ActionValidationResult Validate(
            ActionIntent intent,
            ActionContext context,
            AllowedActionDefinition allowedAction);

        ActionApplyResult Apply(
            ActionIntent intent,
            ActionContext context,
            AllowedActionDefinition allowedAction);
    }
}
