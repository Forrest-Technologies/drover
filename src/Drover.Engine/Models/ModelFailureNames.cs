namespace Drover.Engine.Models;

public static class ModelFailureNames
{
    /// <summary>The failure in the snake_case the API documents: invalid_key, retry_later, and so on.</summary>
    public static string ToWireName(this ModelFailure failure) =>
        failure switch
        {
            ModelFailure.InvalidKey => "invalid_key",
            ModelFailure.RetryLater => "retry_later",
            ModelFailure.OutOfCredit => "out_of_credit",
            ModelFailure.Unreachable => "unreachable",
            _ => "rejected",
        };
}
