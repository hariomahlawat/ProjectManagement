PRISM Search V2 - SearchEngine CS9006/CS1733 build hotfix

Apply to the ProjectManagement project root and overwrite:
  Services/SearchV2/Query/SearchEngine.cs

Root cause:
BuildSearchSql() is a single-$ interpolated raw string. The SQL empty-json
fallback used '{{}}', which is invalid brace syntax in this raw interpolated
string and causes CS9006/CS1733.

Fix:
Both facet fallbacks now use PostgreSQL jsonb_build_object(), which returns
an empty jsonb object without literal C# interpolation braces.

The ProjectManagement.Tests CS0006 error is downstream: the main project did
not emit ProjectManagement.dll because SearchEngine.cs failed compilation.
After replacing this file, Clean/Rebuild the solution.
