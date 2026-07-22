using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Admin.MasterData;

public interface IAdminHierarchyValidationService
{
    Task<AdminOperationResult> ValidateProjectCategoryParentAsync(
        int? categoryId,
        int? parentId,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> ValidateTechnicalCategoryParentAsync(
        int? categoryId,
        int? parentId,
        CancellationToken cancellationToken = default);
}

public sealed class AdminHierarchyValidationService : IAdminHierarchyValidationService
{
    private readonly ApplicationDbContext _db;

    public AdminHierarchyValidationService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<AdminOperationResult> ValidateProjectCategoryParentAsync(
        int? categoryId,
        int? parentId,
        CancellationToken cancellationToken = default)
    {
        var relations = await _db.ProjectCategories
            .AsNoTracking()
            .Select(item => new HierarchyRelation(item.Id, item.ParentId))
            .ToListAsync(cancellationToken);

        return Validate(relations, categoryId, parentId, "project category");
    }

    public async Task<AdminOperationResult> ValidateTechnicalCategoryParentAsync(
        int? categoryId,
        int? parentId,
        CancellationToken cancellationToken = default)
    {
        var relations = await _db.TechnicalCategories
            .AsNoTracking()
            .Select(item => new HierarchyRelation(item.Id, item.ParentId))
            .ToListAsync(cancellationToken);

        return Validate(relations, categoryId, parentId, "technical category");
    }

    internal static AdminOperationResult Validate(
        IReadOnlyCollection<HierarchyRelation> relations,
        int? categoryId,
        int? parentId,
        string itemLabel)
    {
        if (parentId.HasValue && !relations.Any(item => item.Id == parentId.Value))
        {
            return AdminOperationResult.Failure(
                "The selected parent no longer exists.",
                "ParentNotFound");
        }

        if (categoryId.HasValue && categoryId.Value == parentId)
        {
            return AdminOperationResult.Failure(
                $"A {itemLabel} cannot be its own parent.",
                "SelfParent");
        }

        var proposed = relations.ToDictionary(item => item.Id, item => item.ParentId);

        if (categoryId.HasValue && parentId.HasValue)
        {
            var visited = new HashSet<int>();
            var current = parentId.Value;
            while (visited.Add(current) && proposed.TryGetValue(current, out var ancestorId) && ancestorId.HasValue)
            {
                if (current == categoryId.Value)
                {
                    return AdminOperationResult.Failure(
                        $"A {itemLabel} cannot be moved under one of its descendants.",
                        "DescendantParent");
                }

                current = ancestorId.Value;
            }

            if (current == categoryId.Value)
            {
                return AdminOperationResult.Failure(
                    $"A {itemLabel} cannot be moved under one of its descendants.",
                    "DescendantParent");
            }
        }

        if (categoryId.HasValue)
        {
            if (!proposed.ContainsKey(categoryId.Value))
            {
                return AdminOperationResult.Failure(
                    $"The selected {itemLabel} no longer exists.",
                    "NotFound");
            }

            proposed[categoryId.Value] = parentId;
        }

        if (DetectCycle(proposed))
        {
            return AdminOperationResult.Failure(
                $"The proposed {itemLabel} hierarchy would contain a cycle.",
                "HierarchyCycleDetected");
        }

        return AdminOperationResult.Success();
    }

    private static bool DetectCycle(IReadOnlyDictionary<int, int?> parents)
    {
        foreach (var id in parents.Keys)
        {
            var visited = new HashSet<int>();
            var current = id;

            while (parents.TryGetValue(current, out var parentId) && parentId.HasValue)
            {
                if (!visited.Add(current))
                {
                    return true;
                }

                current = parentId.Value;
            }
        }

        return false;
    }

    internal sealed record HierarchyRelation(int Id, int? ParentId);
}
