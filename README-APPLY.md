# Apply instructions

Copy the package contents over the ProjectManagement project root, preserving folders and overwriting the matching files.

Recommended verification from the project root:

```powershell
dotnet clean
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
node --check .\wwwroot\js\pages\photos-library.js
node --check .\wwwroot\js\pages\photos-curation.js
node --check .\wwwroot\js\pages\photos-person-profile.js
node --test .\wwwroot\js\pages\photos-person-profile-contract.test.js
```

No EF migration or appsettings change is required.
