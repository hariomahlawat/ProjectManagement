using System;
using System.Runtime.CompilerServices;

namespace ProjectManagement.Tests;

/// <summary>
/// Runs Npgsql in the same timestamp mode as the application, which sets this switch at
/// startup (Program.cs). Npgsql reads the switch once, the first time it is used in the
/// process, so it has to be set before any test touches Npgsql. Setting it from a test
/// class's static constructor was too late whenever another PostgreSQL test ran first,
/// which made those tests fail or pass depending on test order.
/// </summary>
internal static class NpgsqlTestModeInitializer
{
    [ModuleInitializer]
    internal static void EnableLegacyTimestampBehavior()
        => AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
}
