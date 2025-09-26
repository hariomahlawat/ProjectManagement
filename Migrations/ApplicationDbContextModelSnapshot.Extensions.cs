using Microsoft.EntityFrameworkCore;

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Provides a public entry point to the protected <see cref="ApplicationDbContextModelSnapshot.BuildModel"/> method so
    /// migration designer files can reuse the current model definition without duplicating code.
    /// </summary>
    public partial class ApplicationDbContextModelSnapshot
    {
        public void PopulateModel(ModelBuilder modelBuilder) => BuildModel(modelBuilder);
    }
}
