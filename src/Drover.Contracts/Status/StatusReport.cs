namespace Drover.Contracts.Status;

/// <summary>
/// What a space's owner or admin sees on the status page. <see cref="ServerRowsShown"/> is false where the deployment
/// keeps the server's own rows for its operator; the space's rows are always there.
/// </summary>
public sealed record StatusReport(bool ServerRowsShown, IReadOnlyList<StatusRow> Rows);

/// <summary>
/// One row. <c>Area</c> is "server" or "space". <c>State</c> is "ok", "warning", "problem", or "off". <c>Summary</c>
/// says what was found in plain words and, when something is wrong, what to do. <c>CheckedAt</c> is when the
/// thing was last seen working or failing, where that is known.
/// </summary>
public sealed record StatusRow(string Area, string Name, string State, string Summary, DateTimeOffset? CheckedAt);
