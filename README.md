# PRISM Publications Phase 32 — CS1503 Hotfix

## Build error fixed

`Services/Compendiums/CompendiumDossierPaginationPlanner.cs`, line 100:

```csharp
ResolveIdealResidualSpace(specifications.Count, programmeModuleCount)
```

was invalid because `specifications` in `Resolve(...)` is a `string[]`. `Count` therefore binds to the LINQ extension-method group rather than an `int` property.

It is corrected to:

```csharp
ResolveIdealResidualSpace(specifications.Length, programmeModuleCount)
```

No behavioural change is intended beyond resolving the compile error.

## Apply
Copy `Services/Compendiums/CompendiumDossierPaginationPlanner.cs` over the same file in the project and rebuild.
