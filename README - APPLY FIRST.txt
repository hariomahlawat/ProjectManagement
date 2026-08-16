PRISM PublicationsRuntimeIntegrationTests compile fix
===================================================

Replace:
ProjectManagement.Tests\Publications\PublicationsRuntimeIntegrationTests.cs

Root cause:
The test uses ILoggerFactory in TestAuthHandler but does not import
Microsoft.Extensions.Logging. The test project uses Microsoft.NET.Sdk,
whose implicit usings do not include that namespace.

Fix:
Added: using Microsoft.Extensions.Logging;

No production code or DI registration change is required.
