# PRISM Photos Consolidation — Url.Page Compile Hotfix

Fixes CS1061 in `Pages/Photos/Index.Albums.cs` by importing the ASP.NET Core MVC namespace that contains the `IUrlHelper.Page(...)` extension method.

Copy `Pages/Photos/Index.Albums.cs` over the current project file and rebuild.

No database or configuration change is required.
