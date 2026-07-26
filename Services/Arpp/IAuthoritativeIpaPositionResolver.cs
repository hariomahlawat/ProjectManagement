namespace ProjectManagement.Services.Arpp;

public interface IAuthoritativeIpaPositionResolver
{
    Task<AuthoritativeIpaPosition?> ResolveAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, AuthoritativeIpaPosition>> ResolveManyAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default);
}
