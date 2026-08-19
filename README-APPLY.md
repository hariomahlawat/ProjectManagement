# Apply

Copy the package contents over the project root, preserving paths:

`E:\Dot Net Web Development\ProjectManagement\`

Then run:

```powershell
dotnet clean
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

Restart PRISM after a successful build. No database migration or configuration change is required.
