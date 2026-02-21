namespace FabCopilot.Contracts.Enums;

/// <summary>
/// Classified query intent for routing search strategies.
/// </summary>
public enum QueryIntent
{
    General,
    Error,
    Procedure,
    Part,
    Definition,
    Spec,
    Comparison
}
