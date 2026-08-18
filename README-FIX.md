# PRISM Photos Albums CS0173 Hotfix

Replace `Pages/Photos/Index.cshtml.cs` with the supplied file.

The fix explicitly types nullable route values used in anonymous objects and route dictionaries:
- `bool?` for optional boolean query-string values.
- `int?` for optional page numbers.

This resolves the CS0173 errors at the reported album URL-builder lines. No database migration or configuration change is required.
