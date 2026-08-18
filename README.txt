PRISM Photos Razor compile fix

Paste these files over the project root, preserving paths:
  Pages/Photos/Index.cshtml
  wwwroot/css/pages/photos-library.css

Root cause fixed:
The Photos summary inside an @if Razor code block mixed markup elements with bare text after implicit Razor expressions. At code-block scope Razor parsed the trailing words/HTML as C#, causing CS1056/CS0103/CS1525/CS1002 cascade errors at lines 207/211.

After copying, clean and rebuild:
  dotnet clean
  Remove-Item -Recurse -Force .\bin, .\obj -ErrorAction SilentlyContinue
  dotnet build .\ProjectManagement.csproj
  dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

The ProjectManagement.Tests CS0006 error is downstream of the main ProjectManagement build failure and should disappear after the Razor file compiles.
