# Proliferation Reports v5 — EF Core translation fix

## Corrected defect

`ResolveScopeAsync` projected `Project` rows into the `ProjectInfo` record and then ordered by `ProjectInfo.Name` inside the database query. EF Core/Npgsql could not translate the resulting constructor-member `OrderBy` expression.

The query now orders by the mapped entity property `Project.Name` before projecting into `ProjectInfo`:

```csharp
var projects = await query
    .OrderBy(x => x.Name)
    .Select(x => new ProjectInfo(
        x.Id,
        x.Name,
        x.CaseFileNumber,
        x.TechnicalCategoryId,
        x.TechnicalCategory != null ? x.TechnicalCategory.Name : "Not categorised"))
    .ToListAsync(cancellationToken);
```

This restores all common report scopes:

- All proliferation
- Technical category
- Selected simulators

No database migration, package, route, JavaScript, or service-registration change is required.
