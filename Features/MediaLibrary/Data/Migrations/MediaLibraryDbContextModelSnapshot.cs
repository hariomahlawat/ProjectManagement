using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ProjectManagement.Features.MediaLibrary.Data;

#nullable disable

namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;

[DbContext(typeof(MediaLibraryDbContext))]
public sealed class MediaLibraryDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.19");
        MediaLibraryModelConfiguration.Configure(modelBuilder);
    }
}
