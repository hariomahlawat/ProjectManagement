# PRISM Media Catalogue Consistency Build Fix

## Corrected error

`MediaCatalogueConsistencyService` projected only `SourceEntityId` and `IsAvailable`, but later referenced `AvailabilityStatus`. The projection now includes `AvailabilityStatus`.

## Additional semantic correction

Catalogue membership and source availability are now evaluated independently:

- **Missing from catalogue** means an authoritative PRISM source record has no catalogue record.
- **Unavailable** means the catalogue record exists, but its source is not currently readable.

Previously, an unavailable asset could incorrectly be counted as missing from the catalogue.

## Deployment

No database migration is required.

```powershell
dotnet clean
dotnet build
dotnet test
```
