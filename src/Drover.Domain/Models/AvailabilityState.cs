namespace Drover.Domain.Models;

/// <summary>Whether a model or tool is still offered. One that disappears keeps its row, marked missing.</summary>
public enum AvailabilityState
{
    Available,
    Missing,
}
