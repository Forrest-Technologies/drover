namespace Drover.Data.Repositories;

/// <summary>
/// People's own sign-ins to model connections, as rows. Row-level security shows a transaction the rows of its own
/// person and nobody else's, and never a row for everyone: so nothing here filters by person to keep people apart,
/// and there is no way to read another person's sign-in, or to fall back to it. Runs inside the caller's scope.
/// </summary>
public sealed class ModelConnectionCredentialRepository(DroverDbContext db)
{
    #region Public Methods

    public void Add(ModelConnectionCredential credential) => db.ModelConnectionCredentials.Add(credential);

    public void Remove(ModelConnectionCredential credential) => db.ModelConnectionCredentials.Remove(credential);

    /// <summary>The person's own sign-in to the connection, tracked, or null when they have none.</summary>
    public Task<ModelConnectionCredential?> GetCredential(Guid connectionId, Guid userId, CancellationToken cancellationToken) =>
        db.ModelConnectionCredentials.SingleOrDefaultAsync(
            credential => credential.ModelConnectionId == connectionId && credential.AudienceUserId == userId,
            cancellationToken);

    /// <summary>One sign-in by its id, tracked, when the transaction may see it.</summary>
    public Task<ModelConnectionCredential?> GetCredential(Guid credentialId, CancellationToken cancellationToken) =>
        db.ModelConnectionCredentials.SingleOrDefaultAsync(credential => credential.Id == credentialId, cancellationToken);

    /// <summary>The transaction's own person's sign-ins to these connections.</summary>
    public Task<List<ModelConnectionCredential>> ListCredentials(IReadOnlyCollection<Guid> connectionIds, CancellationToken cancellationToken) =>
        db.ModelConnectionCredentials.AsNoTracking()
            .Where(credential => connectionIds.Contains(credential.ModelConnectionId))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The active sign-ins with an end, in every space, last renewed before <paramref name="notRenewedSince"/>. This
    /// crosses spaces, so it goes through <c>sys.model_sign_ins_due</c>, which returns ids only: the caller then
    /// refreshes each one inside its own space's tenant scope, as the person it belongs to. One taken up and not
    /// renewed since <paramref name="notTriedSince"/> is left out, and those never taken up come first. The spaces
    /// take turns, one from each before a second from any.
    /// </summary>
    public Task<List<ModelSignInDue>> ListSignInsDue(
        DateTimeOffset notRenewedSince,
        DateTimeOffset notTriedSince,
        int limit,
        CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<ModelSignInDue>(
                $"""
                SELECT space_id, audience_user_id, model_connection_id, credential_id
                FROM sys.model_sign_ins_due({notRenewedSince}, {notTriedSince}, {limit})
                """)
            .ToListAsync(cancellationToken);

    #endregion
}

/// <summary>A person's sign-in to a model connection that is due for renewal: ids only.</summary>
public sealed record ModelSignInDue(Guid SpaceId, Guid AudienceUserId, Guid ModelConnectionId, Guid CredentialId);
