# ProjectManagement ToT compile fix v2

This is a cumulative replacement package. It includes both corrections needed
for the reported build failures.

## Replace

Copy these files into the project root while preserving their paths:

- `ProjectManagement.csproj`
- `ViewComponents/ProjectTotCommandCardViewComponent.cs`

## Corrections

1. `ProjectManagement.csproj` excludes retained `ReadyToReplace/**` source from
   the Web SDK's recursive C#, Razor, content, and resource discovery. This
   resolves the earlier duplicate-type and duplicate-member errors.
2. `ProjectTotCommandCardViewComponent.cs` uses the record constructor's exact
   named parameter, `CanManage`. C# named arguments are case-sensitive.

`CS0006` in `ProjectManagement.Tests` is consequential: the test project cannot
find `ProjectManagement.dll` because the main project did not compile. It
requires no separate test-project change.

## Verify

From the project directory:

```powershell
dotnet clean .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet build .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

If Visual Studio still displays stale diagnostics after a successful command
line build, close and reopen the solution to refresh its design-time build.
