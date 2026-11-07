namespace ProjectManagement.Data.DocRepo;

/// <summary>
/// The shape shared by the repository's document types and offices, so one service
/// (Services/DocRepo/DocRepoListService) maintains both lists. No schema meaning.
/// </summary>
public interface IDocRepoListEntry
{
    int Id { get; }

    string Name { get; set; }

    bool IsActive { get; set; }

    int SortOrder { get; set; }
}
