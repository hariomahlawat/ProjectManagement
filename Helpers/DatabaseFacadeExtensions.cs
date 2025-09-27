using System;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ProjectManagement.Helpers
{
    public static class DatabaseFacadeExtensions
    {
        private const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";

        public static bool IsSqlServer(this DatabaseFacade databaseFacade)
        {
            if (databaseFacade == null)
            {
                throw new ArgumentNullException(nameof(databaseFacade));
            }

            return string.Equals(databaseFacade.ProviderName, SqlServerProviderName, StringComparison.Ordinal);
        }
    }
}
