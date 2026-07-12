using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Admin.MasterData;

public sealed record CategoryCreateCommand(string Name, int? ParentId, bool IsActive);
public sealed record CategoryUpdateCommand(int Id, string Name, int? ParentId, bool IsActive, byte[] RowVersion);
public sealed record FlatLookupCreateCommand(string Name, int SortOrder);
public sealed record FlatLookupUpdateCommand(int Id, string Name, int SortOrder, byte[] RowVersion);

public interface IAdminMasterDataCommandService
{
    Task<AdminOperationResult<ProjectCategory>> CreateProjectCategoryAsync(CategoryCreateCommand command, CancellationToken cancellationToken = default);
    Task<AdminOperationResult<ProjectCategory>> UpdateProjectCategoryAsync(CategoryUpdateCommand command, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> DeleteProjectCategoryAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> ToggleProjectCategoryAsync(int id, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> MoveProjectCategoryAsync(int id, int offset, CancellationToken cancellationToken = default);

    Task<AdminOperationResult<TechnicalCategory>> CreateTechnicalCategoryAsync(CategoryCreateCommand command, CancellationToken cancellationToken = default);
    Task<AdminOperationResult<TechnicalCategory>> UpdateTechnicalCategoryAsync(CategoryUpdateCommand command, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> DeleteTechnicalCategoryAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> ToggleTechnicalCategoryAsync(int id, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> MoveTechnicalCategoryAsync(int id, int offset, CancellationToken cancellationToken = default);

    Task<AdminOperationResult<ProjectType>> CreateProjectTypeAsync(FlatLookupCreateCommand command, CancellationToken cancellationToken = default);
    Task<AdminOperationResult<ProjectType>> UpdateProjectTypeAsync(FlatLookupUpdateCommand command, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> SetProjectTypeActiveAsync(int id, bool isActive, byte[] rowVersion, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> MoveProjectTypeAsync(int id, int offset, CancellationToken cancellationToken = default);

    Task<AdminOperationResult<SponsoringUnit>> CreateSponsoringUnitAsync(FlatLookupCreateCommand command, CancellationToken cancellationToken = default);
    Task<AdminOperationResult<SponsoringUnit>> UpdateSponsoringUnitAsync(FlatLookupUpdateCommand command, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> SetSponsoringUnitActiveAsync(int id, bool isActive, byte[] rowVersion, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> MoveSponsoringUnitAsync(int id, int offset, CancellationToken cancellationToken = default);

    Task<AdminOperationResult<LineDirectorate>> CreateLineDirectorateAsync(FlatLookupCreateCommand command, CancellationToken cancellationToken = default);
    Task<AdminOperationResult<LineDirectorate>> UpdateLineDirectorateAsync(FlatLookupUpdateCommand command, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> SetLineDirectorateActiveAsync(int id, bool isActive, byte[] rowVersion, CancellationToken cancellationToken = default);
    Task<AdminOperationResult> MoveLineDirectorateAsync(int id, int offset, CancellationToken cancellationToken = default);
}

public sealed class AdminMasterDataCommandService : IAdminMasterDataCommandService
{
    private readonly ApplicationDbContext _db;
    private readonly IAdminHierarchyValidationService _hierarchy;
    private readonly IAdminAuditService _audit;
    private readonly IAuthorizationService _authorization;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IClock _clock;

    public AdminMasterDataCommandService(
        ApplicationDbContext db,
        IAdminHierarchyValidationService hierarchy,
        IAdminAuditService audit,
        IAuthorizationService authorization,
        IHttpContextAccessor httpContextAccessor,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<AdminOperationResult<ProjectCategory>> CreateProjectCategoryAsync(
        CategoryCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        var auth = await EnsureAuthorizedAsync(cancellationToken);
        if (!auth.Succeeded)
        {
            return AdminOperationResult<ProjectCategory>.Failure(auth.UserMessage!, auth.ErrorCode, auth.TraceId);
        }

        var nameResult = ValidateName(command.Name, 120);
        if (!nameResult.Succeeded)
        {
            return AdminOperationResult<ProjectCategory>.Failure(nameResult.UserMessage!, nameResult.ErrorCode);
        }

        var hierarchy = await _hierarchy.ValidateProjectCategoryParentAsync(null, command.ParentId, cancellationToken);
        if (!hierarchy.Succeeded)
        {
            return AdminOperationResult<ProjectCategory>.Failure(hierarchy.UserMessage!, hierarchy.ErrorCode);
        }

        var name = MasterDataName.Normalize(command.Name);
        if (await ProjectCategoryDuplicateExistsAsync(name, command.ParentId, null, cancellationToken))
        {
            return Duplicate<ProjectCategory>("A project category with this name already exists under the selected parent.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var category = new ProjectCategory
            {
                Name = name,
                ParentId = command.ParentId,
                IsActive = command.IsActive,
                CreatedByUserId = RequireActorUserId(),
                CreatedAt = _clock.UtcNow.UtcDateTime,
                SortOrder = await NextProjectCategorySortOrderAsync(command.ParentId, null, cancellationToken)
            };

            _db.ProjectCategories.Add(category);
            await _db.SaveChangesAsync(cancellationToken);
            await AuditAsync("MasterData.ProjectCategoryCreated", "ProjectCategory", category.Id, null, Snapshot(category), cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return AdminOperationResult<ProjectCategory>.Success(category, $"Created '{category.Name}'.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Duplicate<ProjectCategory>("A project category with this name already exists under the selected parent.");
        }
    }

    public async Task<AdminOperationResult<ProjectCategory>> UpdateProjectCategoryAsync(
        CategoryUpdateCommand command,
        CancellationToken cancellationToken = default)
    {
        var auth = await EnsureAuthorizedAsync(cancellationToken);
        if (!auth.Succeeded)
        {
            return AdminOperationResult<ProjectCategory>.Failure(auth.UserMessage!, auth.ErrorCode, auth.TraceId);
        }

        var nameResult = ValidateName(command.Name, 120);
        if (!nameResult.Succeeded)
        {
            return AdminOperationResult<ProjectCategory>.Failure(nameResult.UserMessage!, nameResult.ErrorCode);
        }

        var hierarchy = await _hierarchy.ValidateProjectCategoryParentAsync(command.Id, command.ParentId, cancellationToken);
        if (!hierarchy.Succeeded)
        {
            return AdminOperationResult<ProjectCategory>.Failure(hierarchy.UserMessage!, hierarchy.ErrorCode);
        }

        var category = await _db.ProjectCategories.SingleOrDefaultAsync(item => item.Id == command.Id, cancellationToken);
        if (category is null)
        {
            return NotFound<ProjectCategory>("Project category");
        }

        var name = MasterDataName.Normalize(command.Name);
        if (await ProjectCategoryDuplicateExistsAsync(name, command.ParentId, command.Id, cancellationToken))
        {
            return Duplicate<ProjectCategory>("A project category with this name already exists under the selected parent.");
        }

        if (!HasRowVersion(command.RowVersion))
        {
            return MissingRowVersion<ProjectCategory>();
        }

        var before = Snapshot(category);
        SetOriginalRowVersion(category, command.RowVersion);
        var parentChanged = category.ParentId != command.ParentId;

        category.Name = name;
        category.ParentId = command.ParentId;
        category.IsActive = command.IsActive;
        if (parentChanged)
        {
            category.SortOrder = await NextProjectCategorySortOrderAsync(command.ParentId, category.Id, cancellationToken);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await AuditAsync("MasterData.ProjectCategoryUpdated", "ProjectCategory", category.Id, before, Snapshot(category), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdminOperationResult<ProjectCategory>.Success(category, $"Updated '{category.Name}'.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Concurrency<ProjectCategory>();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Duplicate<ProjectCategory>("A project category with this name already exists under the selected parent.");
        }
    }

    public Task<AdminOperationResult> DeleteProjectCategoryAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default) =>
        DeleteCategoryAsync(
            id,
            rowVersion,
            isTechnical: false,
            cancellationToken);

    public Task<AdminOperationResult> ToggleProjectCategoryAsync(int id, CancellationToken cancellationToken = default) =>
        ToggleCategoryAsync(id, isTechnical: false, cancellationToken);

    public Task<AdminOperationResult> MoveProjectCategoryAsync(int id, int offset, CancellationToken cancellationToken = default) =>
        MoveCategoryAsync(id, offset, isTechnical: false, cancellationToken);

    public async Task<AdminOperationResult<TechnicalCategory>> CreateTechnicalCategoryAsync(
        CategoryCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        var auth = await EnsureAuthorizedAsync(cancellationToken);
        if (!auth.Succeeded)
        {
            return AdminOperationResult<TechnicalCategory>.Failure(auth.UserMessage!, auth.ErrorCode, auth.TraceId);
        }

        var nameResult = ValidateName(command.Name, 120);
        if (!nameResult.Succeeded)
        {
            return AdminOperationResult<TechnicalCategory>.Failure(nameResult.UserMessage!, nameResult.ErrorCode);
        }

        var hierarchy = await _hierarchy.ValidateTechnicalCategoryParentAsync(null, command.ParentId, cancellationToken);
        if (!hierarchy.Succeeded)
        {
            return AdminOperationResult<TechnicalCategory>.Failure(hierarchy.UserMessage!, hierarchy.ErrorCode);
        }

        var name = MasterDataName.Normalize(command.Name);
        if (await TechnicalCategoryDuplicateExistsAsync(name, command.ParentId, null, cancellationToken))
        {
            return Duplicate<TechnicalCategory>("A technical category with this name already exists under the selected parent.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var category = new TechnicalCategory
            {
                Name = name,
                ParentId = command.ParentId,
                IsActive = command.IsActive,
                CreatedByUserId = RequireActorUserId(),
                CreatedAt = _clock.UtcNow.UtcDateTime,
                SortOrder = await NextTechnicalCategorySortOrderAsync(command.ParentId, null, cancellationToken)
            };

            _db.TechnicalCategories.Add(category);
            await _db.SaveChangesAsync(cancellationToken);
            await AuditAsync("MasterData.TechnicalCategoryCreated", "TechnicalCategory", category.Id, null, Snapshot(category), cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return AdminOperationResult<TechnicalCategory>.Success(category, $"Created '{category.Name}'.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Duplicate<TechnicalCategory>("A technical category with this name already exists under the selected parent.");
        }
    }

    public async Task<AdminOperationResult<TechnicalCategory>> UpdateTechnicalCategoryAsync(
        CategoryUpdateCommand command,
        CancellationToken cancellationToken = default)
    {
        var auth = await EnsureAuthorizedAsync(cancellationToken);
        if (!auth.Succeeded)
        {
            return AdminOperationResult<TechnicalCategory>.Failure(auth.UserMessage!, auth.ErrorCode, auth.TraceId);
        }

        var nameResult = ValidateName(command.Name, 120);
        if (!nameResult.Succeeded)
        {
            return AdminOperationResult<TechnicalCategory>.Failure(nameResult.UserMessage!, nameResult.ErrorCode);
        }

        var hierarchy = await _hierarchy.ValidateTechnicalCategoryParentAsync(command.Id, command.ParentId, cancellationToken);
        if (!hierarchy.Succeeded)
        {
            return AdminOperationResult<TechnicalCategory>.Failure(hierarchy.UserMessage!, hierarchy.ErrorCode);
        }

        var category = await _db.TechnicalCategories.SingleOrDefaultAsync(item => item.Id == command.Id, cancellationToken);
        if (category is null)
        {
            return NotFound<TechnicalCategory>("Technical category");
        }

        var name = MasterDataName.Normalize(command.Name);
        if (await TechnicalCategoryDuplicateExistsAsync(name, command.ParentId, command.Id, cancellationToken))
        {
            return Duplicate<TechnicalCategory>("A technical category with this name already exists under the selected parent.");
        }

        if (!HasRowVersion(command.RowVersion))
        {
            return MissingRowVersion<TechnicalCategory>();
        }

        var before = Snapshot(category);
        SetOriginalRowVersion(category, command.RowVersion);
        var parentChanged = category.ParentId != command.ParentId;

        category.Name = name;
        category.ParentId = command.ParentId;
        category.IsActive = command.IsActive;
        if (parentChanged)
        {
            category.SortOrder = await NextTechnicalCategorySortOrderAsync(command.ParentId, category.Id, cancellationToken);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await AuditAsync("MasterData.TechnicalCategoryUpdated", "TechnicalCategory", category.Id, before, Snapshot(category), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdminOperationResult<TechnicalCategory>.Success(category, $"Updated '{category.Name}'.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Concurrency<TechnicalCategory>();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Duplicate<TechnicalCategory>("A technical category with this name already exists under the selected parent.");
        }
    }

    public Task<AdminOperationResult> DeleteTechnicalCategoryAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default) =>
        DeleteCategoryAsync(id, rowVersion, isTechnical: true, cancellationToken);

    public Task<AdminOperationResult> ToggleTechnicalCategoryAsync(int id, CancellationToken cancellationToken = default) =>
        ToggleCategoryAsync(id, isTechnical: true, cancellationToken);

    public Task<AdminOperationResult> MoveTechnicalCategoryAsync(int id, int offset, CancellationToken cancellationToken = default) =>
        MoveCategoryAsync(id, offset, isTechnical: true, cancellationToken);

    public Task<AdminOperationResult<ProjectType>> CreateProjectTypeAsync(FlatLookupCreateCommand command, CancellationToken cancellationToken = default) =>
        CreateFlatLookupAsync(
            command,
            _db.ProjectTypes,
            "ProjectType",
            "project type",
            "MasterData.ProjectTypeCreated",
            name => new ProjectType { Name = name, SortOrder = command.SortOrder, IsActive = true },
            item => item.Id,
            Snapshot,
            cancellationToken);

    public Task<AdminOperationResult<ProjectType>> UpdateProjectTypeAsync(FlatLookupUpdateCommand command, CancellationToken cancellationToken = default) =>
        UpdateFlatLookupAsync(
            command,
            _db.ProjectTypes,
            "ProjectType",
            "project type",
            "MasterData.ProjectTypeUpdated",
            item => item.Name,
            (item, name) => { item.Name = name; item.SortOrder = command.SortOrder; },
            Snapshot,
            cancellationToken);

    public Task<AdminOperationResult> SetProjectTypeActiveAsync(int id, bool isActive, byte[] rowVersion, CancellationToken cancellationToken = default) =>
        SetFlatLookupActiveAsync(
            id,
            isActive,
            rowVersion,
            _db.ProjectTypes,
            "ProjectType",
            "project type",
            "MasterData.ProjectTypeStatusChanged",
            item => item.Name,
            item => item.IsActive,
            (item, value) => item.IsActive = value,
            idValue => _db.Projects.AnyAsync(project => project.ProjectTypeId == idValue, cancellationToken),
            Snapshot,
            cancellationToken);

    public Task<AdminOperationResult> MoveProjectTypeAsync(int id, int offset, CancellationToken cancellationToken = default) =>
        MoveFlatLookupAsync(id, offset, _db.ProjectTypes, "ProjectType", "MasterData.ProjectTypeReordered", item => item.Id, item => item.Name, item => item.SortOrder, (item, value) => item.SortOrder = value, Snapshot, cancellationToken);

    public Task<AdminOperationResult<SponsoringUnit>> CreateSponsoringUnitAsync(FlatLookupCreateCommand command, CancellationToken cancellationToken = default) =>
        CreateFlatLookupAsync(
            command,
            _db.SponsoringUnits,
            "SponsoringUnit",
            "sponsoring unit",
            "MasterData.SponsoringUnitCreated",
            name => new SponsoringUnit { Name = name, SortOrder = command.SortOrder, IsActive = true, CreatedUtc = _clock.UtcNow.UtcDateTime, UpdatedUtc = _clock.UtcNow.UtcDateTime },
            item => item.Id,
            Snapshot,
            cancellationToken);

    public Task<AdminOperationResult<SponsoringUnit>> UpdateSponsoringUnitAsync(FlatLookupUpdateCommand command, CancellationToken cancellationToken = default) =>
        UpdateFlatLookupAsync(
            command,
            _db.SponsoringUnits,
            "SponsoringUnit",
            "sponsoring unit",
            "MasterData.SponsoringUnitUpdated",
            item => item.Name,
            (item, name) => { item.Name = name; item.SortOrder = command.SortOrder; item.UpdatedUtc = _clock.UtcNow.UtcDateTime; },
            Snapshot,
            cancellationToken);

    public Task<AdminOperationResult> SetSponsoringUnitActiveAsync(int id, bool isActive, byte[] rowVersion, CancellationToken cancellationToken = default) =>
        SetFlatLookupActiveAsync(
            id,
            isActive,
            rowVersion,
            _db.SponsoringUnits,
            "SponsoringUnit",
            "sponsoring unit",
            "MasterData.SponsoringUnitStatusChanged",
            item => item.Name,
            item => item.IsActive,
            (item, value) => { item.IsActive = value; item.UpdatedUtc = _clock.UtcNow.UtcDateTime; },
            idValue => _db.Projects.AnyAsync(project => project.SponsoringUnitId == idValue, cancellationToken),
            Snapshot,
            cancellationToken);

    public Task<AdminOperationResult> MoveSponsoringUnitAsync(int id, int offset, CancellationToken cancellationToken = default) =>
        MoveFlatLookupAsync(id, offset, _db.SponsoringUnits, "SponsoringUnit", "MasterData.SponsoringUnitReordered", item => item.Id, item => item.Name, item => item.SortOrder, (item, value) => { item.SortOrder = value; item.UpdatedUtc = _clock.UtcNow.UtcDateTime; }, Snapshot, cancellationToken);

    public Task<AdminOperationResult<LineDirectorate>> CreateLineDirectorateAsync(FlatLookupCreateCommand command, CancellationToken cancellationToken = default) =>
        CreateFlatLookupAsync(
            command,
            _db.LineDirectorates,
            "LineDirectorate",
            "line directorate",
            "MasterData.LineDirectorateCreated",
            name => new LineDirectorate { Name = name, SortOrder = command.SortOrder, IsActive = true, CreatedUtc = _clock.UtcNow.UtcDateTime, UpdatedUtc = _clock.UtcNow.UtcDateTime },
            item => item.Id,
            Snapshot,
            cancellationToken);

    public Task<AdminOperationResult<LineDirectorate>> UpdateLineDirectorateAsync(FlatLookupUpdateCommand command, CancellationToken cancellationToken = default) =>
        UpdateFlatLookupAsync(
            command,
            _db.LineDirectorates,
            "LineDirectorate",
            "line directorate",
            "MasterData.LineDirectorateUpdated",
            item => item.Name,
            (item, name) => { item.Name = name; item.SortOrder = command.SortOrder; item.UpdatedUtc = _clock.UtcNow.UtcDateTime; },
            Snapshot,
            cancellationToken);

    public Task<AdminOperationResult> SetLineDirectorateActiveAsync(int id, bool isActive, byte[] rowVersion, CancellationToken cancellationToken = default) =>
        SetFlatLookupActiveAsync(
            id,
            isActive,
            rowVersion,
            _db.LineDirectorates,
            "LineDirectorate",
            "line directorate",
            "MasterData.LineDirectorateStatusChanged",
            item => item.Name,
            item => item.IsActive,
            (item, value) => { item.IsActive = value; item.UpdatedUtc = _clock.UtcNow.UtcDateTime; },
            idValue => _db.Projects.AnyAsync(project => project.SponsoringLineDirectorateId == idValue, cancellationToken),
            Snapshot,
            cancellationToken);

    public Task<AdminOperationResult> MoveLineDirectorateAsync(int id, int offset, CancellationToken cancellationToken = default) =>
        MoveFlatLookupAsync(id, offset, _db.LineDirectorates, "LineDirectorate", "MasterData.LineDirectorateReordered", item => item.Id, item => item.Name, item => item.SortOrder, (item, value) => { item.SortOrder = value; item.UpdatedUtc = _clock.UtcNow.UtcDateTime; }, Snapshot, cancellationToken);

    private async Task<AdminOperationResult> DeleteCategoryAsync(
        int id,
        byte[] rowVersion,
        bool isTechnical,
        CancellationToken cancellationToken)
    {
        var auth = await EnsureAuthorizedAsync(cancellationToken);
        if (!auth.Succeeded)
        {
            return auth;
        }

        if (!HasRowVersion(rowVersion))
        {
            return MissingRowVersion();
        }

        if (isTechnical)
        {
            var category = await _db.TechnicalCategories.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (category is null)
            {
                return NotFound("Technical category");
            }

            if (await _db.TechnicalCategories.AnyAsync(item => item.ParentId == id, cancellationToken))
            {
                return AdminOperationResult.Failure("Reassign or remove the child technical categories first.", "EntityInUse");
            }

            if (await _db.Projects.AnyAsync(project => project.TechnicalCategoryId == id, cancellationToken))
            {
                return AdminOperationResult.Failure("This technical category is assigned to existing projects and cannot be deleted.", "EntityInUse");
            }

            SetOriginalRowVersion(category, rowVersion);
            var before = Snapshot(category);
            _db.TechnicalCategories.Remove(category);

            return await SaveDeleteAsync("MasterData.TechnicalCategoryDeleted", "TechnicalCategory", category.Id, category.Name, before, cancellationToken);
        }

        var projectCategory = await _db.ProjectCategories.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (projectCategory is null)
        {
            return NotFound("Project category");
        }

        if (await _db.ProjectCategories.AnyAsync(item => item.ParentId == id, cancellationToken))
        {
            return AdminOperationResult.Failure("Reassign or remove the child project categories first.", "EntityInUse");
        }

        if (await _db.Projects.AnyAsync(project => project.CategoryId == id, cancellationToken))
        {
            return AdminOperationResult.Failure("This project category is assigned to existing projects and cannot be deleted.", "EntityInUse");
        }

        SetOriginalRowVersion(projectCategory, rowVersion);
        var projectBefore = Snapshot(projectCategory);
        _db.ProjectCategories.Remove(projectCategory);
        return await SaveDeleteAsync("MasterData.ProjectCategoryDeleted", "ProjectCategory", projectCategory.Id, projectCategory.Name, projectBefore, cancellationToken);
    }

    private async Task<AdminOperationResult> SaveDeleteAsync(
        string action,
        string entityType,
        int id,
        string name,
        object before,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await AuditAsync(action, entityType, id, before, null, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdminOperationResult.Success($"Deleted '{name}'.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Concurrency();
        }
    }

    private async Task<AdminOperationResult> ToggleCategoryAsync(int id, bool isTechnical, CancellationToken cancellationToken)
    {
        var auth = await EnsureAuthorizedAsync(cancellationToken);
        if (!auth.Succeeded)
        {
            return auth;
        }

        if (isTechnical)
        {
            var category = await _db.TechnicalCategories.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (category is null)
            {
                return NotFound("Technical category");
            }

            var before = Snapshot(category);
            category.IsActive = !category.IsActive;
            return await SaveStatusAsync("MasterData.TechnicalCategoryStatusChanged", "TechnicalCategory", category.Id, category.Name, category.IsActive, before, Snapshot(category), cancellationToken);
        }

        var projectCategory = await _db.ProjectCategories.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (projectCategory is null)
        {
            return NotFound("Project category");
        }

        var projectBefore = Snapshot(projectCategory);
        projectCategory.IsActive = !projectCategory.IsActive;
        return await SaveStatusAsync("MasterData.ProjectCategoryStatusChanged", "ProjectCategory", projectCategory.Id, projectCategory.Name, projectCategory.IsActive, projectBefore, Snapshot(projectCategory), cancellationToken);
    }

    private async Task<AdminOperationResult> SaveStatusAsync(
        string action,
        string entityType,
        int id,
        string name,
        bool isActive,
        object before,
        object after,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await AuditAsync(action, entityType, id, before, after, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdminOperationResult.Success(isActive ? $"Activated '{name}'." : $"Deactivated '{name}'.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Concurrency();
        }
    }

    private async Task<AdminOperationResult> MoveCategoryAsync(int id, int offset, bool isTechnical, CancellationToken cancellationToken)
    {
        if (offset == 0)
        {
            return AdminOperationResult.Success("No changes made.");
        }

        var auth = await EnsureAuthorizedAsync(cancellationToken);
        if (!auth.Succeeded)
        {
            return auth;
        }

        if (isTechnical)
        {
            var current = await _db.TechnicalCategories.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (current is null)
            {
                return NotFound("Technical category");
            }

            var siblings = await _db.TechnicalCategories
                .Where(item => item.ParentId == current.ParentId)
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Name)
                .ToListAsync(cancellationToken);

            return await ReorderAsync(siblings, current, offset, item => item.Id, item => item.Name, item => item.SortOrder, (item, value) => item.SortOrder = value, "TechnicalCategory", "MasterData.TechnicalCategoryReordered", Snapshot, cancellationToken);
        }

        var projectCurrent = await _db.ProjectCategories.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (projectCurrent is null)
        {
            return NotFound("Project category");
        }

        var projectSiblings = await _db.ProjectCategories
            .Where(item => item.ParentId == projectCurrent.ParentId)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);

        return await ReorderAsync(projectSiblings, projectCurrent, offset, item => item.Id, item => item.Name, item => item.SortOrder, (item, value) => item.SortOrder = value, "ProjectCategory", "MasterData.ProjectCategoryReordered", Snapshot, cancellationToken);
    }

    private async Task<AdminOperationResult<T>> CreateFlatLookupAsync<T>(
        FlatLookupCreateCommand command,
        DbSet<T> set,
        string entityType,
        string label,
        string auditAction,
        Func<string, T> factory,
        Func<T, int> getId,
        Func<T, object> snapshot,
        CancellationToken cancellationToken) where T : class
    {
        var auth = await EnsureAuthorizedAsync(cancellationToken);
        if (!auth.Succeeded)
        {
            return AdminOperationResult<T>.Failure(auth.UserMessage!, auth.ErrorCode, auth.TraceId);
        }

        var nameResult = ValidateName(command.Name, 200);
        if (!nameResult.Succeeded)
        {
            return AdminOperationResult<T>.Failure(nameResult.UserMessage!, nameResult.ErrorCode);
        }

        var name = MasterDataName.Normalize(command.Name);
        var canonical = MasterDataName.Canonical(name);
        if (await set.AsNoTracking().AnyAsync(item => EF.Property<string>(item, "Name").ToUpper() == canonical, cancellationToken))
        {
            return Duplicate<T>($"A {label} with this name already exists.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var entity = factory(name);
            set.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);
            var id = getId(entity);
            await AuditAsync(auditAction, entityType, id, null, snapshot(entity), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdminOperationResult<T>.Success(entity, $"Created '{name}'.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Duplicate<T>($"A {label} with this name already exists.");
        }
    }

    private async Task<AdminOperationResult<T>> UpdateFlatLookupAsync<T>(
        FlatLookupUpdateCommand command,
        DbSet<T> set,
        string entityType,
        string label,
        string auditAction,
        Func<T, string> getName,
        Action<T, string> apply,
        Func<T, object> snapshot,
        CancellationToken cancellationToken) where T : class
    {
        var auth = await EnsureAuthorizedAsync(cancellationToken);
        if (!auth.Succeeded)
        {
            return AdminOperationResult<T>.Failure(auth.UserMessage!, auth.ErrorCode, auth.TraceId);
        }

        var nameResult = ValidateName(command.Name, 200);
        if (!nameResult.Succeeded)
        {
            return AdminOperationResult<T>.Failure(nameResult.UserMessage!, nameResult.ErrorCode);
        }

        var entity = await set.SingleOrDefaultAsync(item => EF.Property<int>(item, "Id") == command.Id, cancellationToken);
        if (entity is null)
        {
            return NotFound<T>(char.ToUpperInvariant(label[0]) + label[1..]);
        }

        var name = MasterDataName.Normalize(command.Name);
        var canonical = MasterDataName.Canonical(name);
        if (await set.AsNoTracking().AnyAsync(item => EF.Property<int>(item, "Id") != command.Id && EF.Property<string>(item, "Name").ToUpper() == canonical, cancellationToken))
        {
            return Duplicate<T>($"A {label} with this name already exists.");
        }

        if (!HasRowVersion(command.RowVersion))
        {
            return MissingRowVersion<T>();
        }

        var before = snapshot(entity);
        _db.Entry(entity).Property("RowVersion").OriginalValue = command.RowVersion;
        apply(entity, name);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await AuditAsync(auditAction, entityType, command.Id, before, snapshot(entity), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdminOperationResult<T>.Success(entity, $"Updated '{getName(entity)}'.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Concurrency<T>();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Duplicate<T>($"A {label} with this name already exists.");
        }
    }

    private async Task<AdminOperationResult> SetFlatLookupActiveAsync<T>(
        int id,
        bool isActive,
        byte[] rowVersion,
        DbSet<T> set,
        string entityType,
        string label,
        string auditAction,
        Func<T, string> getName,
        Func<T, bool> getActive,
        Action<T, bool> setActive,
        Func<int, Task<bool>> isInUse,
        Func<T, object> snapshot,
        CancellationToken cancellationToken) where T : class
    {
        var auth = await EnsureAuthorizedAsync(cancellationToken);
        if (!auth.Succeeded)
        {
            return auth;
        }

        var entity = await set.SingleOrDefaultAsync(item => EF.Property<int>(item, "Id") == id, cancellationToken);
        if (entity is null)
        {
            return NotFound(char.ToUpperInvariant(label[0]) + label[1..]);
        }

        if (!isActive && await isInUse(id))
        {
            return AdminOperationResult.Failure($"This {label} is assigned to existing projects and cannot be deactivated.", "EntityInUse");
        }

        if (getActive(entity) == isActive)
        {
            return AdminOperationResult.Success(isActive ? $"'{getName(entity)}' is already active." : $"'{getName(entity)}' is already inactive.");
        }

        if (!HasRowVersion(rowVersion))
        {
            return MissingRowVersion();
        }

        var before = snapshot(entity);
        _db.Entry(entity).Property("RowVersion").OriginalValue = rowVersion;
        setActive(entity, isActive);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await AuditAsync(auditAction, entityType, id, before, snapshot(entity), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdminOperationResult.Success(isActive ? $"Reactivated '{getName(entity)}'." : $"Deactivated '{getName(entity)}'.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Concurrency();
        }
    }

    private async Task<AdminOperationResult> MoveFlatLookupAsync<T>(
        int id,
        int offset,
        DbSet<T> set,
        string entityType,
        string auditAction,
        Func<T, int> getId,
        Func<T, string> getName,
        Func<T, int> getSortOrder,
        Action<T, int> setSortOrder,
        Func<T, object> snapshot,
        CancellationToken cancellationToken) where T : class
    {
        if (offset == 0)
        {
            return AdminOperationResult.Success("No changes made.");
        }

        var auth = await EnsureAuthorizedAsync(cancellationToken);
        if (!auth.Succeeded)
        {
            return auth;
        }

        var items = await set.OrderBy(item => EF.Property<int>(item, "SortOrder"))
            .ThenBy(item => EF.Property<string>(item, "Name"))
            .ToListAsync(cancellationToken);

        var current = items.FirstOrDefault(item => getId(item) == id);
        if (current is null)
        {
            return AdminOperationResult.Failure("The selected master-data item no longer exists.", "NotFound");
        }

        return await ReorderAsync(items, current, offset, getId, getName, getSortOrder, setSortOrder, entityType, auditAction, snapshot, cancellationToken);
    }

    private async Task<AdminOperationResult> ReorderAsync<T>(
        IList<T> items,
        T current,
        int offset,
        Func<T, int> getId,
        Func<T, string> getName,
        Func<T, int> getSortOrder,
        Action<T, int> setSortOrder,
        string entityType,
        string auditAction,
        Func<T, object> snapshot,
        CancellationToken cancellationToken) where T : class
    {
        var currentIndex = items.IndexOf(current);
        var targetIndex = Math.Clamp(currentIndex + offset, 0, items.Count - 1);
        if (targetIndex == currentIndex)
        {
            return AdminOperationResult.Success(offset < 0 ? "Already at the top." : "Already at the bottom.");
        }

        var before = items.Select(snapshot).ToArray();
        items.RemoveAt(currentIndex);
        items.Insert(targetIndex, current);
        for (var index = 0; index < items.Count; index++)
        {
            setSortOrder(items[index], index);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await AuditAsync(auditAction, entityType, getId(current), before, items.Select(snapshot).ToArray(), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdminOperationResult.Success(offset < 0 ? $"Moved '{getName(current)}' up." : $"Moved '{getName(current)}' down.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Concurrency();
        }
    }

    private async Task<AdminOperationResult> EnsureAuthorizedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal is null || !(await _authorization.AuthorizeAsync(principal, null, AdminPolicies.MasterDataManage)).Succeeded)
        {
            return AdminOperationResult.Failure("You are not authorised to manage master data.", "Forbidden", TraceId);
        }

        return AdminOperationResult.Success();
    }

    private static AdminOperationResult ValidateName(string? name, int maxLength)
    {
        var normalized = MasterDataName.Normalize(name);
        if (normalized.Length == 0)
        {
            return AdminOperationResult.Failure("Name is required.", "ValidationFailed");
        }

        return normalized.Length > maxLength
            ? AdminOperationResult.Failure($"Name must be {maxLength} characters or fewer.", "ValidationFailed")
            : AdminOperationResult.Success();
    }

    private string RequireActorUserId()
    {
        var userId = _httpContextAccessor.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrWhiteSpace(userId) ? "system" : userId;
    }

    private string? TraceId => _httpContextAccessor.HttpContext?.TraceIdentifier;

    private Task AuditAsync(string action, string entityType, int id, object? before, object? after, CancellationToken cancellationToken) =>
        _audit.RecordAsync(new AdminAuditEntry(
            action,
            entityType,
            id.ToString(),
            before,
            after,
            Origin: "Admin.MasterData"), cancellationToken);

    private async Task<bool> ProjectCategoryDuplicateExistsAsync(string name, int? parentId, int? excludeId, CancellationToken cancellationToken)
    {
        var canonical = MasterDataName.Canonical(name);
        return await _db.ProjectCategories.AsNoTracking().AnyAsync(
            item => item.ParentId == parentId && (!excludeId.HasValue || item.Id != excludeId.Value) && item.Name.ToUpper() == canonical,
            cancellationToken);
    }

    private async Task<bool> TechnicalCategoryDuplicateExistsAsync(string name, int? parentId, int? excludeId, CancellationToken cancellationToken)
    {
        var canonical = MasterDataName.Canonical(name);
        return await _db.TechnicalCategories.AsNoTracking().AnyAsync(
            item => item.ParentId == parentId && (!excludeId.HasValue || item.Id != excludeId.Value) && item.Name.ToUpper() == canonical,
            cancellationToken);
    }

    private async Task<int> NextProjectCategorySortOrderAsync(int? parentId, int? excludeId, CancellationToken cancellationToken) =>
        (await _db.ProjectCategories
            .Where(item => item.ParentId == parentId && (!excludeId.HasValue || item.Id != excludeId.Value))
            .MaxAsync(item => (int?)item.SortOrder, cancellationToken) ?? -1) + 1;

    private async Task<int> NextTechnicalCategorySortOrderAsync(int? parentId, int? excludeId, CancellationToken cancellationToken) =>
        (await _db.TechnicalCategories
            .Where(item => item.ParentId == parentId && (!excludeId.HasValue || item.Id != excludeId.Value))
            .MaxAsync(item => (int?)item.SortOrder, cancellationToken) ?? -1) + 1;

    private void SetOriginalRowVersion<T>(T entity, byte[] rowVersion) where T : class =>
        _db.Entry(entity).Property("RowVersion").OriginalValue = rowVersion;

    private static bool HasRowVersion(byte[]? rowVersion) => rowVersion is { Length: > 0 };

    private static AdminOperationResult<T> MissingRowVersion<T>() =>
        AdminOperationResult<T>.Failure(
            "The record version is missing. Reload the page and try again.",
            "ConcurrencyTokenMissing");

    private static AdminOperationResult MissingRowVersion() =>
        AdminOperationResult.Failure(
            "The record version is missing. Reload the page and try again.",
            "ConcurrencyTokenMissing");

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation;

    private static AdminOperationResult<T> Duplicate<T>(string message) =>
        AdminOperationResult<T>.Failure(message, "DuplicateName");

    private static AdminOperationResult<T> NotFound<T>(string label) =>
        AdminOperationResult<T>.Failure($"{label} not found.", "NotFound");

    private static AdminOperationResult NotFound(string label) =>
        AdminOperationResult.Failure($"{label} not found.", "NotFound");

    private static AdminOperationResult<T> Concurrency<T>() =>
        AdminOperationResult<T>.Failure("This record was changed by another administrator. Reload the page and try again.", "ConcurrencyConflict");

    private static AdminOperationResult Concurrency() =>
        AdminOperationResult.Failure("This record was changed by another administrator. Reload the page and try again.", "ConcurrencyConflict");

    private static object Snapshot(ProjectCategory item) => new { item.Id, item.Name, item.ParentId, item.IsActive, item.SortOrder };
    private static object Snapshot(TechnicalCategory item) => new { item.Id, item.Name, item.ParentId, item.IsActive, item.SortOrder };
    private static object Snapshot(ProjectType item) => new { item.Id, item.Name, item.IsActive, item.SortOrder };
    private static object Snapshot(SponsoringUnit item) => new { item.Id, item.Name, item.IsActive, item.SortOrder, item.UpdatedUtc };
    private static object Snapshot(LineDirectorate item) => new { item.Id, item.Name, item.IsActive, item.SortOrder, item.UpdatedUtc };
}
