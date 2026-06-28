# PRISM Media Synchronizer Options Fix

## Cause

`PrismMediaCatalogueSynchronizer` referenced `_options.Classification.Enabled` in two places, but the class did not declare or initialize `_options`.

## Correction

The synchronizer now receives `IOptions<MediaLibraryOptions>` through constructor injection and stores the resolved options value in a private readonly field.

No service-registration change is required because `MediaLibraryOptions` is already registered through the Options pattern in `AddMediaLibrary`.

## Replaced file

`Features/MediaLibrary/Services/PrismMediaCatalogueSynchronizer.cs`

## Deployment

Copy the `Features` folder into the application root, then run:

```powershell
dotnet clean
dotnet build
dotnet test
```

No database migration is required.
