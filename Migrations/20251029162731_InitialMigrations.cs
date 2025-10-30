using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    rec RECORD;
                BEGIN
                    FOR rec IN (
                        SELECT tablename
                        FROM pg_tables
                        WHERE schemaname = 'public'
                          AND tablename <> '__EFMigrationsHistory'
                    ) LOOP
                        EXECUTE format('DROP TABLE IF EXISTS "%I" CASCADE;', rec.tablename);
                    END LOOP;
                END $$;
                """
            );

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "boolean", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    Rank = table.Column<string>(type: "text", nullable: false),
                    LastLoginUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LoginCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDisabled = table.Column<bool>(type: "boolean", nullable: false),
                    DisabledUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DisabledByUserId = table.Column<string>(type: "text", nullable: true),
                    PendingDeletion = table.Column<bool>(type: "boolean", nullable: false),
                    DeletionRequestedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DeletionRequestedByUserId = table.Column<string>(type: "text", nullable: true),
                    ShowCelebrationsInCalendar = table.Column<bool>(type: "boolean", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TimeUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    UserName = table.Column<string>(type: "text", nullable: true),
                    Ip = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    Message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DataJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    WhenUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Event = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ip = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Celebrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<byte>(type: "smallint", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SpouseName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Day = table.Column<byte>(type: "smallint", nullable: false),
                    Month = table.Column<byte>(type: "smallint", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: true),
                    CreatedById = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Celebrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyLoginStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyLoginStats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<byte>(type: "smallint", nullable: false),
                    Location = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    StartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsAllDay = table.Column<bool>(type: "boolean", nullable: false),
                    RecurrenceRule = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RecurrenceUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecurrenceExDates = table.Column<string>(type: "text", nullable: true),
                    CreatedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UpdatedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FfcCountries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsoCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FfcCountries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Holidays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LineDirectorates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineDirectorates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDispatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecipientUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Module = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ScopeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ScopeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProjectId = table.Column<int>(type: "integer", nullable: true),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Route = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    DispatchedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDispatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectCategories_ProjectCategories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "ProjectCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProliferationGranular",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    UnitName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProliferationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    ApprovalStatus = table.Column<int>(type: "integer", nullable: false),
                    SubmittedByUserId = table.Column<string>(type: "text", nullable: false),
                    ApprovedByUserId = table.Column<string>(type: "text", nullable: true),
                    ApprovedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastUpdatedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProliferationGranular", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProliferationYearly",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    TotalQuantity = table.Column<int>(type: "integer", nullable: false),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    ApprovalStatus = table.Column<int>(type: "integer", nullable: false),
                    SubmittedByUserId = table.Column<string>(type: "text", nullable: false),
                    ApprovedByUserId = table.Column<string>(type: "text", nullable: true),
                    ApprovedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastUpdatedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProliferationYearly", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProliferationYearPreference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    SetByUserId = table.Column<string>(type: "text", nullable: false),
                    SetOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProliferationYearPreference", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialMediaEventTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialMediaEventTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialMediaPlatforms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialMediaPlatforms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SponsoringUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SponsoringUnits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StageChangeLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    FromActualStart = table.Column<DateOnly>(type: "date", nullable: true),
                    ToActualStart = table.Column<DateOnly>(type: "date", nullable: true),
                    FromCompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ToCompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageChangeLogs", x => x.Id);
                    table.CheckConstraint("CK_StageChangeLogs_Action", "\"Action\" IN ('Requested','Approved','Rejected','DirectApply','Applied','Superseded','AutoBackfill','Backfill')");
                });

            migrationBuilder.CreateTable(
                name: "StageChangeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequestedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RequestedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecisionStatus = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false, defaultValue: "Pending"),
                    DecidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DecidedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageChangeRequests", x => x.Id);
                    table.CheckConstraint("CK_StageChangeRequests_DecisionStatus", "\"DecisionStatus\" IN ('Pending','Approved','Rejected','Superseded')");
                });

            migrationBuilder.CreateTable(
                name: "StageDependencyTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "text", nullable: false),
                    FromStageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DependsOnStageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageDependencyTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StageShiftLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OldForecastDue = table.Column<DateOnly>(type: "date", nullable: true),
                    NewForecastDue = table.Column<DateOnly>(type: "date", nullable: false),
                    DeltaDays = table.Column<int>(type: "integer", nullable: false),
                    CauseStageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CauseType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageShiftLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StageTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Optional = table.Column<bool>(type: "boolean", nullable: false),
                    ParallelGroup = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Statuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Statuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TechnicalCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TechnicalCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TechnicalCategories_TechnicalCategories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "TechnicalCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TodoItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Priority = table.Column<byte>(type: "smallint", nullable: false),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false, defaultValue: (byte)0),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TodoItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainingRankCategoryMap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Rank = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingRankCategoryMap", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainingTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserNotificationPreferences",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Allow = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotificationPreferences", x => new { x.UserId, x.Kind });
                });

            migrationBuilder.CreateTable(
                name: "VisitTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workflows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workflows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActivityTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityTypes_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityTypes_AspNetUsers_LastModifiedByUserId",
                        column: x => x.LastModifiedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StageChecklistTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageChecklistTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageChecklistTemplates_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FfcRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CountryId = table.Column<long>(type: "bigint", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    IpaYes = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IpaDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IpaRemarks = table.Column<string>(type: "text", nullable: true),
                    GslYes = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    GslDate = table.Column<DateOnly>(type: "date", nullable: true),
                    GslRemarks = table.Column<string>(type: "text", nullable: true),
                    DeliveryYes = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DeliveryRemarks = table.Column<string>(type: "text", nullable: true),
                    InstallationYes = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    InstallationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InstallationRemarks = table.Column<string>(type: "text", nullable: true),
                    OverallRemarks = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FfcRecords", x => x.Id);
                    table.CheckConstraint("CK_FfcRecords_DeliveryDateRequiresFlag", "\"DeliveryDate\" IS NULL OR \"DeliveryYes\" = TRUE");
                    table.CheckConstraint("CK_FfcRecords_GslDateRequiresFlag", "\"GslDate\" IS NULL OR \"GslYes\" = TRUE");
                    table.CheckConstraint("CK_FfcRecords_InstallationDateRequiresFlag", "\"InstallationDate\" IS NULL OR \"InstallationYes\" = TRUE");
                    table.CheckConstraint("CK_FfcRecords_IpaDateRequiresFlag", "\"IpaDate\" IS NULL OR \"IpaYes\" = TRUE");
                    table.ForeignKey(
                        name: "FK_FfcRecords_FfcCountries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "FfcCountries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecipientUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Module = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ScopeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ScopeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProjectId = table.Column<int>(type: "integer", nullable: true),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Route = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    SeenUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReadUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SourceDispatchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_NotificationDispatches_SourceDispatchId",
                        column: x => x.SourceDispatchId,
                        principalTable: "NotificationDispatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Trainings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TrainingMonth = table.Column<int>(type: "integer", nullable: true),
                    TrainingYear = table.Column<int>(type: "integer", nullable: true),
                    LegacyOfficerCount = table.Column<int>(type: "integer", nullable: false),
                    LegacyJcoCount = table.Column<int>(type: "integer", nullable: false),
                    LegacyOrCount = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trainings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trainings_TrainingTypes_TrainingTypeId",
                        column: x => x.TrainingTypeId,
                        principalTable: "TrainingTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    WorkflowId = table.Column<int>(type: "integer", nullable: false),
                    StatusId = table.Column<int>(type: "integer", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowStatuses_Statuses_StatusId",
                        column: x => x.StatusId,
                        principalTable: "Statuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowStatuses_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Activities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Location = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ScheduledStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ScheduledEndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActivityTypeId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Activities_ActivityTypes_ActivityTypeId",
                        column: x => x.ActivityTypeId,
                        principalTable: "ActivityTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Activities_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Activities_AspNetUsers_DeletedByUserId",
                        column: x => x.DeletedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Activities_AspNetUsers_LastModifiedByUserId",
                        column: x => x.LastModifiedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StageChecklistItemTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TemplateId = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UpdatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageChecklistItemTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageChecklistItemTemplates_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StageChecklistItemTemplates_StageChecklistTemplates_Templat~",
                        column: x => x.TemplateId,
                        principalTable: "StageChecklistTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FfcAttachments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FfcRecordId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Caption = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FfcAttachments", x => x.Id);
                    table.CheckConstraint("CK_FfcAttachments_SizeBytes", "\"SizeBytes\" >= 0");
                    table.ForeignKey(
                        name: "FK_FfcAttachments_FfcRecords_FfcRecordId",
                        column: x => x.FfcRecordId,
                        principalTable: "FfcRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingCounters",
                columns: table => new
                {
                    TrainingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Officers = table.Column<int>(type: "integer", nullable: false),
                    JuniorCommissionedOfficers = table.Column<int>(type: "integer", nullable: false),
                    OtherRanks = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingCounters", x => x.TrainingId);
                    table.ForeignKey(
                        name: "FK_TrainingCounters_Trainings_TrainingId",
                        column: x => x.TrainingId,
                        principalTable: "Trainings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingDeleteRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DecidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingDeleteRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingDeleteRequests_Trainings_TrainingId",
                        column: x => x.TrainingId,
                        principalTable: "Trainings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingTrainees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrainingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArmyNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Rank = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UnitName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<byte>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingTrainees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingTrainees_Trainings_TrainingId",
                        column: x => x.TrainingId,
                        principalTable: "Trainings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActivityAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActivityId = table.Column<int>(type: "integer", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityAttachments_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityAttachments_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityDeleteRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActivityId = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    ApprovedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RejectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityDeleteRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityDeleteRequests_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityDeleteRequests_AspNetUsers_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityDeleteRequests_AspNetUsers_RejectedByUserId",
                        column: x => x.RejectedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityDeleteRequests_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StageChecklistAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TemplateId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    PerformedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    PerformedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageChecklistAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageChecklistAudits_StageChecklistItemTemplates_ItemId",
                        column: x => x.ItemId,
                        principalTable: "StageChecklistItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StageChecklistAudits_StageChecklistTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "StageChecklistTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FfcProjects",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FfcRecordId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    LinkedProjectId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FfcProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FfcProjects_FfcRecords_FfcRecordId",
                        column: x => x.FfcRecordId,
                        principalTable: "FfcRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IprAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IprRecordId = table.Column<int>(type: "integer", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IprAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IprAttachments_AspNetUsers_ArchivedByUserId",
                        column: x => x.ArchivedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_IprAttachments_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IprRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IprFilingNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FiledBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FiledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GrantedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProjectId = table.Column<int>(type: "integer", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IprRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanApprovalLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanVersionId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PerformedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PerformedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanApprovalLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanApprovalLogs_AspNetUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    VersionNo = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    SubmittedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ApprovedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RejectedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectionNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AnchorStageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AnchorDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SkipWeekends = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    TransitionRule = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PncApplicable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanVersions_AspNetUsers_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlanVersions_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanVersions_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlanVersions_AspNetUsers_RejectedByUserId",
                        column: x => x.RejectedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlanVersions_AspNetUsers_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StagePlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanVersionId = table.Column<int>(type: "integer", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PlannedStart = table.Column<DateOnly>(type: "date", nullable: true),
                    PlannedDue = table.Column<DateOnly>(type: "date", nullable: true),
                    DurationDays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StagePlans_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectAonFacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AonCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectAonFacts", x => x.Id);
                    table.CheckConstraint("ck_aonfact_amount", "\"AonCost\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ProjectAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PerformedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PerformedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    MetadataJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectAudits_AspNetUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectBenchmarkFacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BenchmarkCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectBenchmarkFacts", x => x.Id);
                    table.CheckConstraint("ck_bmfact_amount", "\"BenchmarkCost\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ProjectCommentAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommentId = table.Column<int>(type: "integer", nullable: false),
                    StoredFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UploadedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCommentAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectCommentAttachments_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectCommentMentions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommentId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCommentMentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectCommentMentions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    ProjectStageId = table.Column<int>(type: "integer", nullable: true),
                    ParentCommentId = table.Column<int>(type: "integer", nullable: true),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Pinned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    EditedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    EditedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectComments_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectComments_AspNetUsers_EditedByUserId",
                        column: x => x.EditedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProjectComments_ProjectComments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "ProjectComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectCommercialFacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    L1Cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCommercialFacts", x => x.Id);
                    table.CheckConstraint("ck_l1fact_amount", "\"L1Cost\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ProjectDocumentRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    StageId = table.Column<int>(type: "integer", nullable: true),
                    DocumentId = table.Column<int>(type: "integer", nullable: true),
                    TotId = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Draft"),
                    RequestType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Upload"),
                    TempStorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    ReviewedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewerNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDocumentRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectDocumentRequests_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectDocumentRequests_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProjectDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    StageId = table.Column<int>(type: "integer", nullable: true),
                    RequestId = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Published"),
                    FileStamp = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TotId = table.Column<int>(type: "integer", nullable: true),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDocuments", x => x.Id);
                    table.CheckConstraint("ck_projectdocuments_filesize", "\"FileSize\" >= 0");
                    table.ForeignKey(
                        name: "FK_ProjectDocuments_AspNetUsers_ArchivedByUserId",
                        column: x => x.ArchivedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProjectDocuments_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectIpaFacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IpaCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectIpaFacts", x => x.Id);
                    table.CheckConstraint("ck_ipafact_amount", "\"IpaCost\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ProjectMetaChangeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    ChangeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    RequestNote = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DecisionStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DecisionNote = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RequestedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DecidedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OriginalName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OriginalDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OriginalCategoryId = table.Column<int>(type: "integer", nullable: true),
                    OriginalTechnicalCategoryId = table.Column<int>(type: "integer", nullable: true),
                    TechnicalCategoryId = table.Column<int>(type: "integer", nullable: true),
                    OriginalCaseFileNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OriginalRowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, nullable: true),
                    OriginalSponsoringUnitId = table.Column<int>(type: "integer", nullable: true),
                    OriginalSponsoringLineDirectorateId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMetaChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectMetaChangeRequests_TechnicalCategories_TechnicalCate~",
                        column: x => x.TechnicalCategoryId,
                        principalTable: "TechnicalCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Caption = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TotId = table.Column<int>(type: "integer", nullable: true),
                    IsCover = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPhotos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectPlanDurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPlanDurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectPlanSnapshotRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SnapshotId = table.Column<int>(type: "integer", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlannedStart = table.Column<DateOnly>(type: "date", nullable: true),
                    PlannedDue = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPlanSnapshotRows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectPlanSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    TakenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TakenByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPlanSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectPlanSnapshots_AspNetUsers_TakenByUserId",
                        column: x => x.TakenByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectPncFacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PncCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPncFacts", x => x.Id);
                    table.CheckConstraint("ck_pncfact_amount", "\"PncCost\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LifecycleStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Active"),
                    IsLegacy = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletedYear = table.Column<int>(type: "integer", nullable: true),
                    CancelledOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CaseFileNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false),
                    ActivePlanVersionNo = table.Column<int>(type: "integer", nullable: true),
                    CategoryId = table.Column<int>(type: "integer", nullable: true),
                    TechnicalCategoryId = table.Column<int>(type: "integer", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DeleteReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DeleteMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DeleteApprovedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    SponsoringUnitId = table.Column<int>(type: "integer", nullable: true),
                    SponsoringLineDirectorateId = table.Column<int>(type: "integer", nullable: true),
                    HodUserId = table.Column<string>(type: "text", nullable: true),
                    LeadPoUserId = table.Column<string>(type: "text", nullable: true),
                    PlanApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PlanApprovedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CoverPhotoId = table.Column<int>(type: "integer", nullable: true),
                    CoverPhotoVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    FeaturedVideoId = table.Column<int>(type: "integer", nullable: true),
                    FeaturedVideoVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_AspNetUsers_HodUserId",
                        column: x => x.HodUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Projects_AspNetUsers_LeadPoUserId",
                        column: x => x.LeadPoUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Projects_AspNetUsers_PlanApprovedByUserId",
                        column: x => x.PlanApprovedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Projects_LineDirectorates_SponsoringLineDirectorateId",
                        column: x => x.SponsoringLineDirectorateId,
                        principalTable: "LineDirectorates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Projects_ProjectCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "ProjectCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Projects_ProjectPhotos_CoverPhotoId",
                        column: x => x.CoverPhotoId,
                        principalTable: "ProjectPhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Projects_SponsoringUnits_SponsoringUnitId",
                        column: x => x.SponsoringUnitId,
                        principalTable: "SponsoringUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Projects_TechnicalCategories_TechnicalCategoryId",
                        column: x => x.TechnicalCategoryId,
                        principalTable: "TechnicalCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectScheduleSettings",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    IncludeWeekends = table.Column<bool>(type: "boolean", nullable: false),
                    SkipHolidays = table.Column<bool>(type: "boolean", nullable: false),
                    NextStageStartPolicy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "NextWorkingDay"),
                    AnchorStart = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectScheduleSettings", x => x.ProjectId);
                    table.ForeignKey(
                        name: "FK_ProjectScheduleSettings_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectSowFacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SponsoringUnit = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SponsoringLineDirectorate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectSowFacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectSowFacts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectStages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlannedStart = table.Column<DateOnly>(type: "date", nullable: true),
                    PlannedDue = table.Column<DateOnly>(type: "date", nullable: true),
                    ForecastStart = table.Column<DateOnly>(type: "date", nullable: true),
                    ForecastDue = table.Column<DateOnly>(type: "date", nullable: true),
                    ActualStart = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IsAutoCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    AutoCompletedFromCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    RequiresBackfill = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectStages", x => x.Id);
                    table.CheckConstraint("CK_ProjectStages_CompletedHasDate", "\"Status\" <> 'Completed' OR (\"CompletedOn\" IS NOT NULL AND \"ActualStart\" IS NOT NULL) OR \"RequiresBackfill\" IS TRUE");
                    table.ForeignKey(
                        name: "FK_ProjectStages_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectSupplyOrderFacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupplyOrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectSupplyOrderFacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectSupplyOrderFacts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectTotRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    ProposedStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProposedStartedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ProposedCompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ProposedMetDetails = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ProposedMetCompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ProposedFirstProductionModelManufactured = table.Column<bool>(type: "boolean", nullable: true),
                    ProposedFirstProductionModelManufacturedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SubmittedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    SubmittedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DecisionState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DecidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DecidedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTotRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectTotRequests_AspNetUsers_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectTotRequests_AspNetUsers_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectTotRequests_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectTots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    MetDetails = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    MetCompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    FirstProductionModelManufactured = table.Column<bool>(type: "boolean", nullable: true),
                    FirstProductionModelManufacturedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LastApprovedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastApprovedOnUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectTots_AspNetUsers_LastApprovedByUserId",
                        column: x => x.LastApprovedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectTots_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectVideos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Ordinal = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    PosterStorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    PosterContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectVideos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectVideos_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Remarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    AuthorRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "General"),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StageRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StageNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastEditedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DeletedByRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Remarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Remarks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingProjects",
                columns: table => new
                {
                    TrainingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    AllocationShare = table.Column<decimal>(type: "numeric(9,4)", nullable: false, defaultValue: 0m),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingProjects", x => new { x.TrainingId, x.ProjectId });
                    table.ForeignKey(
                        name: "FK_TrainingProjects_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingProjects_Trainings_TrainingId",
                        column: x => x.TrainingId,
                        principalTable: "Trainings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserProjectMutes",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProjectMutes", x => new { x.UserId, x.ProjectId });
                    table.ForeignKey(
                        name: "FK_UserProjectMutes_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RemarkAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RemarkId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SnapshotType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SnapshotScope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "General"),
                    SnapshotAuthorRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotAuthorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    SnapshotEventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SnapshotStageRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SnapshotStageName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SnapshotBody = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SnapshotCreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SnapshotLastEditedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SnapshotIsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    SnapshotDeletedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SnapshotDeletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    SnapshotDeletedByRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SnapshotProjectId = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ActorRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActionAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Meta = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemarkAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemarkAudits_Remarks_RemarkId",
                        column: x => x.RemarkId,
                        principalTable: "Remarks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RemarkMentions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RemarkId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemarkMentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemarkMentions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RemarkMentions_Remarks_RemarkId",
                        column: x => x.RemarkId,
                        principalTable: "Remarks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SocialMediaEventPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SocialMediaEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, defaultValue: ""),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    Caption = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    VersionStamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsCover = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialMediaEventPhotos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialMediaEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SocialMediaEventTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DateOfEvent = table.Column<DateOnly>(type: "date", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SocialMediaPlatformId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CoverPhotoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialMediaEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialMediaEvents_SocialMediaEventPhotos_CoverPhotoId",
                        column: x => x.CoverPhotoId,
                        principalTable: "SocialMediaEventPhotos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SocialMediaEvents_SocialMediaEventTypes_SocialMediaEventTyp~",
                        column: x => x.SocialMediaEventTypeId,
                        principalTable: "SocialMediaEventTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SocialMediaEvents_SocialMediaPlatforms_SocialMediaPlatformId",
                        column: x => x.SocialMediaPlatformId,
                        principalTable: "SocialMediaPlatforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VisitPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    Caption = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    VersionStamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitPhotos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Visits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DateOfVisit = table.Column<DateOnly>(type: "date", nullable: false),
                    VisitorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Strength = table.Column<int>(type: "integer", nullable: false),
                    Remarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CoverPhotoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Visits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Visits_VisitPhotos_CoverPhotoId",
                        column: x => x.CoverPhotoId,
                        principalTable: "VisitPhotos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Visits_VisitTypes_VisitTypeId",
                        column: x => x.VisitTypeId,
                        principalTable: "VisitTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[]
                {
                    "Id",
                    "AccessFailedCount",
                    "ConcurrencyStamp",
                    "CreatedUtc",
                    "DeletionRequestedByUserId",
                    "DeletionRequestedUtc",
                    "DisabledByUserId",
                    "DisabledUtc",
                    "Email",
                    "EmailConfirmed",
                    "FullName",
                    "IsDisabled",
                    "LastLoginUtc",
                    "LockoutEnabled",
                    "LockoutEnd",
                    "LoginCount",
                    "MustChangePassword",
                    "NormalizedEmail",
                    "NormalizedUserName",
                    "PasswordHash",
                    "PendingDeletion",
                    "PhoneNumber",
                    "PhoneNumberConfirmed",
                    "Rank",
                    "SecurityStamp",
                    "ShowCelebrationsInCalendar",
                    "TwoFactorEnabled",
                    "UserName"
                },
                values: new object[]
                {
                    "system",
                    0,
                    "bb6d6cb5-52dd-432c-95d4-6b6a92d6a0d3",
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    null,
                    null,
                    null,
                    null,
                    "system@example.local",
                    true,
                    "System Account",
                    false,
                    null,
                    false,
                    null,
                    0,
                    false,
                    "SYSTEM@EXAMPLE.LOCAL",
                    "SYSTEM",
                    null,
                    false,
                    null,
                    false,
                    "System",
                    "c3f1e44d-21c7-4cd1-8d3a-2212333e2ef2",
                    true,
                    false,
                    "system"
                });

            migrationBuilder.InsertData(
                table: "ActivityTypes",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "Description", "IsActive", "LastModifiedAtUtc", "LastModifiedByUserId", "Name", "RowVersion" },
                values: new object[,]
                {
                    { 1, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "All types of administrative tasks/ events or activities.", true, null, null, "Adm Activities", new byte[] { 216, 208, 195, 169, 249, 15, 192, 73, 139, 118, 95, 193, 29, 92, 16, 222 } },
                    { 2, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "All internal and external inspections.", true, null, null, "Inspections", new byte[] { 141, 95, 207, 27, 182, 148, 187, 79, 137, 223, 209, 223, 15, 158, 156, 66 } },
                    { 3, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Engagements with academic institutions and partners.", true, null, null, "Academia Interaction", new byte[] { 122, 28, 155, 220, 131, 79, 23, 77, 142, 55, 159, 210, 102, 90, 30, 59 } },
                    { 4, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Collaboration with industry stakeholders and forums.", true, null, null, "Industry Interaction", new byte[] { 248, 153, 190, 112, 55, 17, 127, 78, 158, 99, 47, 8, 107, 92, 104, 77 } },
                    { 5, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Educational seminars, lectures, and talks.", true, null, null, "Seminar/ Lecture", new byte[] { 146, 250, 117, 199, 45, 67, 185, 73, 143, 98, 111, 142, 234, 12, 126, 155 } },
                    { 6, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Activities that do not fit other defined categories.", true, null, null, "Misc", new byte[] { 140, 13, 2, 92, 75, 122, 79, 79, 136, 34, 159, 108, 242, 8, 136, 127 } }
                });

            migrationBuilder.InsertData(
                table: "SocialMediaEventTypes",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "Description", "IsActive", "LastModifiedAtUtc", "LastModifiedByUserId", "Name", "RowVersion" },
                values: new object[,]
                {
                    { new Guid("0b35f77a-4ef6-4a0a-85f9-9fa0b1b0c353"), new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Stories focused on community outreach and engagement.", true, null, null, "Community Engagement", new byte[] { 156, 101, 26, 107, 203, 244, 144, 76, 138, 54, 143, 246, 185, 53, 94, 125 } },
                    { new Guid("9ddf8646-7070-4f7a-9fa0-8cb19f4a0d5b"), new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Coverage for new campaign announcements and kick-off posts.", true, null, null, "Campaign Launch", new byte[] { 245, 68, 156, 111, 242, 153, 246, 71, 148, 115, 208, 182, 163, 36, 184, 37 } },
                    { new Guid("fa2f60fa-7d4f-4f60-a84b-e8f64dce0b73"), new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Highlights of major delivery milestones shared online.", true, null, null, "Milestone Update", new byte[] { 40, 230, 184, 11, 72, 157, 218, 71, 147, 5, 43, 110, 111, 141, 165, 198 } }
                });

            migrationBuilder.InsertData(
                table: "TrainingRankCategoryMap",
                columns: new[] { "Id", "Category", "CreatedAtUtc", "CreatedByUserId", "IsActive", "LastModifiedAtUtc", "LastModifiedByUserId", "Rank", "RowVersion" },
                values: new object[,]
                {
                    { 1, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Lt", new byte[] { 61, 159, 111, 95, 255, 34, 143, 74, 158, 242, 124, 179, 241, 169, 5, 19 } },
                    { 2, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Capt", new byte[] { 166, 160, 205, 214, 86, 106, 52, 75, 157, 190, 166, 144, 181, 140, 219, 223 } },
                    { 3, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Maj", new byte[] { 138, 141, 101, 86, 59, 29, 154, 77, 191, 53, 248, 240, 167, 241, 218, 107 } },
                    { 4, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Lt Col", new byte[] { 52, 39, 155, 167, 206, 55, 160, 74, 143, 167, 30, 67, 213, 198, 166, 244 } },
                    { 5, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Col", new byte[] { 122, 117, 252, 30, 252, 4, 34, 78, 134, 229, 44, 201, 190, 58, 129, 215 } },
                    { 6, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Brig", new byte[] { 199, 169, 246, 168, 209, 95, 83, 76, 142, 42, 194, 185, 241, 176, 240, 215 } },
                    { 7, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Maj Gen", new byte[] { 52, 18, 159, 45, 142, 51, 74, 77, 191, 103, 58, 193, 48, 150, 164, 184 } },
                    { 8, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Lt Gen", new byte[] { 28, 41, 139, 79, 38, 47, 105, 75, 155, 111, 67, 59, 33, 22, 177, 217 } },
                    { 9, 0, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Gen", new byte[] { 186, 33, 192, 201, 174, 145, 193, 66, 185, 232, 147, 14, 16, 183, 196, 126 } },
                    { 10, 1, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Naib Subedar", new byte[] { 120, 166, 215, 245, 236, 210, 75, 77, 164, 229, 44, 77, 86, 243, 241, 180 } },
                    { 11, 1, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Subedar", new byte[] { 176, 137, 22, 166, 168, 68, 64, 71, 148, 81, 162, 169, 99, 159, 77, 157 } },
                    { 12, 1, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Subedar Major", new byte[] { 217, 181, 194, 227, 175, 18, 172, 71, 182, 159, 104, 232, 230, 245, 211, 193 } },
                    { 13, 2, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Sepoy", new byte[] { 94, 47, 125, 10, 180, 187, 216, 75, 183, 61, 148, 165, 8, 42, 77, 12 } },
                    { 14, 2, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Lance Naik", new byte[] { 142, 76, 159, 154, 18, 10, 95, 78, 144, 27, 151, 20, 252, 183, 217, 194 } },
                    { 15, 2, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Naik", new byte[] { 195, 247, 229, 178, 21, 125, 226, 78, 155, 241, 12, 104, 75, 39, 220, 233 } },
                    { 16, 2, new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", true, null, null, "Havildar", new byte[] { 188, 242, 150, 141, 212, 18, 244, 67, 141, 107, 43, 176, 121, 72, 243, 217 } }
                });

            migrationBuilder.InsertData(
                table: "TrainingTypes",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "Description", "DisplayOrder", "IsActive", "LastModifiedAtUtc", "LastModifiedByUserId", "Name", "RowVersion" },
                values: new object[,]
                {
                    { new Guid("39f0d83c-5322-4a6d-bd1c-1b4dfbb5887b"), new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Drone operator and maintenance training.", 2, true, null, null, "Drone", new byte[] { 188, 145, 243, 210, 164, 100, 54, 76, 146, 24, 26, 59, 169, 189, 234, 249 } },
                    { new Guid("f4a9b1c7-0a3c-46da-92ff-39b861fd4c91"), new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Simulator-based training sessions.", 1, true, null, null, "Simulator", new byte[] { 117, 111, 155, 77, 150, 141, 212, 71, 157, 65, 159, 79, 74, 14, 166, 121 } }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_ActivityTypeId",
                table: "Activities",
                column: "ActivityTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_CreatedAtUtc",
                table: "Activities",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_CreatedByUserId",
                table: "Activities",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_DeletedByUserId",
                table: "Activities",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_LastModifiedByUserId",
                table: "Activities",
                column: "LastModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_ScheduledStartUtc",
                table: "Activities",
                column: "ScheduledStartUtc");

            migrationBuilder.CreateIndex(
                name: "UX_Activities_ActivityTypeId_Title",
                table: "Activities",
                columns: new[] { "ActivityTypeId", "Title" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAttachments_ActivityId",
                table: "ActivityAttachments",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAttachments_UploadedAtUtc",
                table: "ActivityAttachments",
                column: "UploadedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAttachments_UploadedByUserId",
                table: "ActivityAttachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityDeleteRequests_ApprovedByUserId",
                table: "ActivityDeleteRequests",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityDeleteRequests_RejectedByUserId",
                table: "ActivityDeleteRequests",
                column: "RejectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityDeleteRequests_RequestedAtUtc",
                table: "ActivityDeleteRequests",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityDeleteRequests_RequestedByUserId",
                table: "ActivityDeleteRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ActivityDeleteRequests_ActivityId_Pending",
                table: "ActivityDeleteRequests",
                column: "ActivityId",
                unique: true,
                filter: "\"ApprovedAtUtc\" IS NULL AND \"RejectedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityTypes_CreatedByUserId",
                table: "ActivityTypes",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityTypes_LastModifiedByUserId",
                table: "ActivityTypes",
                column: "LastModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ActivityTypes_Name",
                table: "ActivityTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Ip",
                table: "AuditLogs",
                column: "Ip");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Level",
                table: "AuditLogs",
                column: "Level");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TimeUtc",
                table: "AuditLogs",
                column: "TimeUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserName",
                table: "AuditLogs",
                column: "UserName");

            migrationBuilder.CreateIndex(
                name: "IX_AuthEvents_Event_WhenUtc",
                table: "AuthEvents",
                columns: new[] { "Event", "WhenUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Celebrations_DeletedUtc",
                table: "Celebrations",
                column: "DeletedUtc",
                filter: "\"DeletedUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Celebrations_EventType_Month_Day",
                table: "Celebrations",
                columns: new[] { "EventType", "Month", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyLoginStats_Date",
                table: "DailyLoginStats",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_EndUtc",
                table: "Events",
                column: "EndUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Events_StartUtc",
                table: "Events",
                column: "StartUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FfcAttachments_FfcRecordId",
                table: "FfcAttachments",
                column: "FfcRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_FfcAttachments_Kind",
                table: "FfcAttachments",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "UX_FfcCountries_Name",
                table: "FfcCountries",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FfcProjects_FfcRecordId",
                table: "FfcProjects",
                column: "FfcRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_FfcProjects_LinkedProjectId",
                table: "FfcProjects",
                column: "LinkedProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_FfcRecords_CountryId_Year",
                table: "FfcRecords",
                columns: new[] { "CountryId", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_FfcRecords_StatusFlags",
                table: "FfcRecords",
                columns: new[] { "IpaYes", "GslYes", "DeliveryYes", "InstallationYes" });

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_Date",
                table: "Holidays",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IprAttachments_ArchivedByUserId",
                table: "IprAttachments",
                column: "ArchivedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IprAttachments_IprRecordId",
                table: "IprAttachments",
                column: "IprRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_IprAttachments_UploadedByUserId",
                table: "IprAttachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IprRecords_IprFilingNumber",
                table: "IprRecords",
                column: "IprFilingNumber");

            migrationBuilder.CreateIndex(
                name: "IX_IprRecords_ProjectId",
                table: "IprRecords",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_IprRecords_Status",
                table: "IprRecords",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_IprRecords_Type",
                table: "IprRecords",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "UX_IprRecords_FilingNumber_Type",
                table: "IprRecords",
                columns: new[] { "IprFilingNumber", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LineDirectorates_Name",
                table: "LineDirectorates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_ActorUserId_DispatchedUtc",
                table: "NotificationDispatches",
                columns: new[] { "ActorUserId", "DispatchedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_DispatchedUtc",
                table: "NotificationDispatches",
                column: "DispatchedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_Fingerprint",
                table: "NotificationDispatches",
                column: "Fingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_Module_EventType_DispatchedUtc",
                table: "NotificationDispatches",
                columns: new[] { "Module", "EventType", "DispatchedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_ProjectId_DispatchedUtc",
                table: "NotificationDispatches",
                columns: new[] { "ProjectId", "DispatchedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_RecipientUserId_Kind_DispatchedUtc",
                table: "NotificationDispatches",
                columns: new[] { "RecipientUserId", "Kind", "DispatchedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatches_ScopeType_ScopeId_DispatchedUtc",
                table: "NotificationDispatches",
                columns: new[] { "ScopeType", "ScopeId", "DispatchedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Fingerprint",
                table: "Notifications",
                column: "Fingerprint",
                filter: "\"Fingerprint\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_CreatedUtc",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_ReadUtc_CreatedUtc",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "ReadUtc", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_SeenUtc_CreatedUtc",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "SeenUtc", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SourceDispatchId",
                table: "Notifications",
                column: "SourceDispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanApprovalLogs_PerformedByUserId",
                table: "PlanApprovalLogs",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanApprovalLogs_PlanVersionId",
                table: "PlanApprovalLogs",
                column: "PlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_ApprovedByUserId",
                table: "PlanVersions",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_CreatedByUserId",
                table: "PlanVersions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_OwnerUserId",
                table: "PlanVersions",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_ProjectId_OwnerUserId",
                table: "PlanVersions",
                columns: new[] { "ProjectId", "OwnerUserId" },
                unique: true,
                filter: "\"Status\" = 'Draft' AND \"OwnerUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_ProjectId_VersionNo",
                table: "PlanVersions",
                columns: new[] { "ProjectId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_RejectedByUserId",
                table: "PlanVersions",
                column: "RejectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_SubmittedByUserId",
                table: "PlanVersions",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAonFacts_ProjectId",
                table: "ProjectAonFacts",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAudit_ProjectId_PerformedAt",
                table: "ProjectAudits",
                columns: new[] { "ProjectId", "PerformedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAudits_PerformedByUserId",
                table: "ProjectAudits",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBenchmarkFacts_ProjectId",
                table: "ProjectBenchmarkFacts",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCategories_ParentId_Name",
                table: "ProjectCategories",
                columns: new[] { "ParentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCommentAttachments_CommentId",
                table: "ProjectCommentAttachments",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCommentAttachments_UploadedByUserId",
                table: "ProjectCommentAttachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCommentMentions_CommentId_UserId",
                table: "ProjectCommentMentions",
                columns: new[] { "CommentId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCommentMentions_UserId",
                table: "ProjectCommentMentions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComments_CreatedByUserId",
                table: "ProjectComments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComments_EditedByUserId",
                table: "ProjectComments",
                column: "EditedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComments_ParentCommentId",
                table: "ProjectComments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComments_ProjectId_CreatedOn",
                table: "ProjectComments",
                columns: new[] { "ProjectId", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComments_ProjectStageId",
                table: "ProjectComments",
                column: "ProjectStageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCommercialFacts_ProjectId",
                table: "ProjectCommercialFacts",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocumentRequests_ProjectId",
                table: "ProjectDocumentRequests",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocumentRequests_ProjectId_Status",
                table: "ProjectDocumentRequests",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocumentRequests_ProjectId_TotId",
                table: "ProjectDocumentRequests",
                columns: new[] { "ProjectId", "TotId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocumentRequests_RequestedByUserId",
                table: "ProjectDocumentRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocumentRequests_ReviewedByUserId",
                table: "ProjectDocumentRequests",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocumentRequests_StageId",
                table: "ProjectDocumentRequests",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocumentRequests_TotId",
                table: "ProjectDocumentRequests",
                column: "TotId");

            migrationBuilder.CreateIndex(
                name: "ux_projectdocumentrequests_pending_document",
                table: "ProjectDocumentRequests",
                column: "DocumentId",
                unique: true,
                filter: "\"DocumentId\" IS NOT NULL AND \"Status\" IN ('Draft', 'Submitted')");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_ArchivedByUserId",
                table: "ProjectDocuments",
                column: "ArchivedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_ProjectId",
                table: "ProjectDocuments",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_ProjectId_StageId_IsArchived",
                table: "ProjectDocuments",
                columns: new[] { "ProjectId", "StageId", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_ProjectId_TotId",
                table: "ProjectDocuments",
                columns: new[] { "ProjectId", "TotId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_StageId",
                table: "ProjectDocuments",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_TotId",
                table: "ProjectDocuments",
                column: "TotId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_UploadedByUserId",
                table: "ProjectDocuments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectIpaFacts_ProjectId",
                table: "ProjectIpaFacts",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMetaChangeRequests_TechnicalCategoryId",
                table: "ProjectMetaChangeRequests",
                column: "TechnicalCategoryId");

            migrationBuilder.CreateIndex(
                name: "ux_projectmetachangerequests_pending",
                table: "ProjectMetaChangeRequests",
                column: "ProjectId",
                unique: true,
                filter: "\"DecisionStatus\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhotos_ProjectId_Ordinal",
                table: "ProjectPhotos",
                columns: new[] { "ProjectId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhotos_ProjectId_TotId",
                table: "ProjectPhotos",
                columns: new[] { "ProjectId", "TotId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhotos_TotId",
                table: "ProjectPhotos",
                column: "TotId");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectPhotos_Cover",
                table: "ProjectPhotos",
                column: "ProjectId",
                unique: true,
                filter: "\"IsCover\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPlanDurations_ProjectId_StageCode",
                table: "ProjectPlanDurations",
                columns: new[] { "ProjectId", "StageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPlanSnapshotRows_SnapshotId",
                table: "ProjectPlanSnapshotRows",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPlanSnapshots_ProjectId_TakenAt",
                table: "ProjectPlanSnapshots",
                columns: new[] { "ProjectId", "TakenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPlanSnapshots_TakenByUserId",
                table: "ProjectPlanSnapshots",
                column: "TakenByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPncFacts_ProjectId",
                table: "ProjectPncFacts",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CategoryId",
                table: "Projects",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CompletedYear",
                table: "Projects",
                column: "CompletedYear");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CoverPhotoId",
                table: "Projects",
                column: "CoverPhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_FeaturedVideoId",
                table: "Projects",
                column: "FeaturedVideoId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_HodUserId",
                table: "Projects",
                column: "HodUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_IsDeleted_Filtered",
                table: "Projects",
                column: "IsDeleted",
                filter: "\"IsDeleted\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_IsDeleted_IsArchived",
                table: "Projects",
                columns: new[] { "IsDeleted", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_IsLegacy",
                table: "Projects",
                column: "IsLegacy");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_LeadPoUserId",
                table: "Projects",
                column: "LeadPoUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_LifecycleStatus",
                table: "Projects",
                column: "LifecycleStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_PlanApprovedByUserId",
                table: "Projects",
                column: "PlanApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_SponsoringLineDirectorateId",
                table: "Projects",
                column: "SponsoringLineDirectorateId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_SponsoringUnitId",
                table: "Projects",
                column: "SponsoringUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TechnicalCategoryId",
                table: "Projects",
                column: "TechnicalCategoryId");

            migrationBuilder.CreateIndex(
                name: "UX_Projects_CaseFileNumber",
                table: "Projects",
                column: "CaseFileNumber",
                unique: true,
                filter: "\"CaseFileNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectSowFacts_ProjectId",
                table: "ProjectSowFacts",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectStages_ProjectId_StageCode",
                table: "ProjectStages",
                columns: new[] { "ProjectId", "StageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectSupplyOrderFacts_ProjectId",
                table: "ProjectSupplyOrderFacts",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTotRequests_DecidedByUserId",
                table: "ProjectTotRequests",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTotRequests_ProjectId",
                table: "ProjectTotRequests",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTotRequests_SubmittedByUserId",
                table: "ProjectTotRequests",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTots_LastApprovedByUserId",
                table: "ProjectTots",
                column: "LastApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTots_ProjectId",
                table: "ProjectTots",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVideos_ProjectId_Ordinal",
                table: "ProjectVideos",
                columns: new[] { "ProjectId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProliferationGranular_ProjectId_Source_ProliferationDate",
                table: "ProliferationGranular",
                columns: new[] { "ProjectId", "Source", "ProliferationDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ProlifYearly_Project_Source_Year",
                table: "ProliferationYearly",
                columns: new[] { "ProjectId", "Source", "Year" });

            migrationBuilder.CreateIndex(
                name: "UX_ProlifYearPref_Project_Source_Year",
                table: "ProliferationYearPreference",
                columns: new[] { "ProjectId", "Source", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemarkAudits_RemarkId",
                table: "RemarkAudits",
                column: "RemarkId");

            migrationBuilder.CreateIndex(
                name: "IX_RemarkMentions_RemarkId_UserId",
                table: "RemarkMentions",
                columns: new[] { "RemarkId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemarkMentions_UserId",
                table: "RemarkMentions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Remarks_ProjectId_IsDeleted_CreatedAtUtc",
                table: "Remarks",
                columns: new[] { "ProjectId", "IsDeleted", "CreatedAtUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Remarks_ProjectId_IsDeleted_Scope_CreatedAtUtc",
                table: "Remarks",
                columns: new[] { "ProjectId", "IsDeleted", "Scope", "CreatedAtUtc" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Remarks_ProjectId_IsDeleted_Type_EventDate",
                table: "Remarks",
                columns: new[] { "ProjectId", "IsDeleted", "Type", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SocialMediaEventPhotos_EventId_CreatedAtUtc",
                table: "SocialMediaEventPhotos",
                columns: new[] { "SocialMediaEventId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_SocialMediaEventPhotos_IsCover",
                table: "SocialMediaEventPhotos",
                columns: new[] { "SocialMediaEventId", "IsCover" },
                unique: true,
                filter: "\"IsCover\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_SocialMediaEvents_CoverPhotoId",
                table: "SocialMediaEvents",
                column: "CoverPhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialMediaEvents_DateOfEvent",
                table: "SocialMediaEvents",
                column: "DateOfEvent");

            migrationBuilder.CreateIndex(
                name: "IX_SocialMediaEvents_SocialMediaEventTypeId",
                table: "SocialMediaEvents",
                column: "SocialMediaEventTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialMediaEvents_SocialMediaPlatformId",
                table: "SocialMediaEvents",
                column: "SocialMediaPlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialMediaEventTypes_Name",
                table: "SocialMediaEventTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocialMediaPlatforms_Name",
                table: "SocialMediaPlatforms",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SponsoringUnits_Name",
                table: "SponsoringUnits",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageChangeLogs_ProjectId_StageCode_At",
                table: "StageChangeLogs",
                columns: new[] { "ProjectId", "StageCode", "At" });

            migrationBuilder.CreateIndex(
                name: "ux_stagechangerequests_pending",
                table: "StageChangeRequests",
                columns: new[] { "ProjectId", "StageCode" },
                unique: true,
                filter: "\"DecisionStatus\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_StageChecklistAudits_ItemId",
                table: "StageChecklistAudits",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StageChecklistAudits_TemplateId",
                table: "StageChecklistAudits",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_StageChecklistItemTemplates_TemplateId_Sequence",
                table: "StageChecklistItemTemplates",
                columns: new[] { "TemplateId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageChecklistItemTemplates_UpdatedByUserId",
                table: "StageChecklistItemTemplates",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StageChecklistTemplates_UpdatedByUserId",
                table: "StageChecklistTemplates",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StageChecklistTemplates_Version_StageCode",
                table: "StageChecklistTemplates",
                columns: new[] { "Version", "StageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageDependencyTemplates_Version_FromStageCode_DependsOnSta~",
                table: "StageDependencyTemplates",
                columns: new[] { "Version", "FromStageCode", "DependsOnStageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StagePlans_PlanVersionId_StageCode",
                table: "StagePlans",
                columns: new[] { "PlanVersionId", "StageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageShiftLogs_ProjectId_StageCode_CreatedOn",
                table: "StageShiftLogs",
                columns: new[] { "ProjectId", "StageCode", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_StageTemplates_Version_Code",
                table: "StageTemplates",
                columns: new[] { "Version", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Statuses_Name",
                table: "Statuses",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TechnicalCategories_ParentId_Name",
                table: "TechnicalCategories",
                columns: new[] { "ParentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TodoItems_DeletedUtc",
                table: "TodoItems",
                column: "DeletedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TodoItems_OwnerId_OrderIndex",
                table: "TodoItems",
                columns: new[] { "OwnerId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_TodoItems_OwnerId_Status_IsPinned_DueAtUtc",
                table: "TodoItems",
                columns: new[] { "OwnerId", "Status", "IsPinned", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingDeleteRequests_Status",
                table: "TrainingDeleteRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingDeleteRequests_TrainingId",
                table: "TrainingDeleteRequests",
                column: "TrainingId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingProjects_ProjectId",
                table: "TrainingProjects",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingRankCategoryMap_Rank",
                table: "TrainingRankCategoryMap",
                column: "Rank",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trainings_EndDate",
                table: "Trainings",
                column: "EndDate");

            migrationBuilder.CreateIndex(
                name: "IX_Trainings_StartDate",
                table: "Trainings",
                column: "StartDate");

            migrationBuilder.CreateIndex(
                name: "IX_Trainings_TrainingTypeId",
                table: "Trainings",
                column: "TrainingTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Trainings_TrainingYear",
                table: "Trainings",
                column: "TrainingYear");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingTrainees_TrainingId",
                table: "TrainingTrainees",
                column: "TrainingId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingTrainees_TrainingId_ArmyNumber",
                table: "TrainingTrainees",
                columns: new[] { "TrainingId", "ArmyNumber" },
                unique: true,
                filter: "\"ArmyNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingTypes_Name",
                table: "TrainingTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProjectMutes_ProjectId",
                table: "UserProjectMutes",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitPhotos_VisitId_CreatedAtUtc",
                table: "VisitPhotos",
                columns: new[] { "VisitId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_CoverPhotoId",
                table: "Visits",
                column: "CoverPhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_DateOfVisit",
                table: "Visits",
                column: "DateOfVisit");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_VisitTypeId",
                table: "Visits",
                column: "VisitTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitTypes_Name",
                table: "VisitTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStatuses_Name_WorkflowId",
                table: "WorkflowStatuses",
                columns: new[] { "Name", "WorkflowId" },
                filter: "\"Name\" IS NOT NULL AND \"WorkflowId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStatuses_StatusId",
                table: "WorkflowStatuses",
                column: "StatusId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStatuses_WorkflowId",
                table: "WorkflowStatuses",
                column: "WorkflowId");

            migrationBuilder.AddForeignKey(
                name: "FK_FfcProjects_Projects_LinkedProjectId",
                table: "FfcProjects",
                column: "LinkedProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_IprAttachments_IprRecords_IprRecordId",
                table: "IprAttachments",
                column: "IprRecordId",
                principalTable: "IprRecords",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_IprRecords_Projects_ProjectId",
                table: "IprRecords",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PlanApprovalLogs_PlanVersions_PlanVersionId",
                table: "PlanApprovalLogs",
                column: "PlanVersionId",
                principalTable: "PlanVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PlanVersions_Projects_ProjectId",
                table: "PlanVersions",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectAonFacts_Projects_ProjectId",
                table: "ProjectAonFacts",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectAudits_Projects_ProjectId",
                table: "ProjectAudits",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectBenchmarkFacts_Projects_ProjectId",
                table: "ProjectBenchmarkFacts",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectCommentAttachments_ProjectComments_CommentId",
                table: "ProjectCommentAttachments",
                column: "CommentId",
                principalTable: "ProjectComments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectCommentMentions_ProjectComments_CommentId",
                table: "ProjectCommentMentions",
                column: "CommentId",
                principalTable: "ProjectComments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectComments_ProjectStages_ProjectStageId",
                table: "ProjectComments",
                column: "ProjectStageId",
                principalTable: "ProjectStages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectComments_Projects_ProjectId",
                table: "ProjectComments",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectCommercialFacts_Projects_ProjectId",
                table: "ProjectCommercialFacts",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectDocumentRequests_ProjectDocuments_DocumentId",
                table: "ProjectDocumentRequests",
                column: "DocumentId",
                principalTable: "ProjectDocuments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectDocumentRequests_ProjectStages_StageId",
                table: "ProjectDocumentRequests",
                column: "StageId",
                principalTable: "ProjectStages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectDocumentRequests_ProjectTots_TotId",
                table: "ProjectDocumentRequests",
                column: "TotId",
                principalTable: "ProjectTots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectDocumentRequests_Projects_ProjectId",
                table: "ProjectDocumentRequests",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectDocuments_ProjectStages_StageId",
                table: "ProjectDocuments",
                column: "StageId",
                principalTable: "ProjectStages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectDocuments_ProjectTots_TotId",
                table: "ProjectDocuments",
                column: "TotId",
                principalTable: "ProjectTots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectDocuments_Projects_ProjectId",
                table: "ProjectDocuments",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectIpaFacts_Projects_ProjectId",
                table: "ProjectIpaFacts",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectMetaChangeRequests_Projects_ProjectId",
                table: "ProjectMetaChangeRequests",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectPhotos_ProjectTots_TotId",
                table: "ProjectPhotos",
                column: "TotId",
                principalTable: "ProjectTots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectPhotos_Projects_ProjectId",
                table: "ProjectPhotos",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectPlanDurations_Projects_ProjectId",
                table: "ProjectPlanDurations",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectPlanSnapshotRows_ProjectPlanSnapshots_SnapshotId",
                table: "ProjectPlanSnapshotRows",
                column: "SnapshotId",
                principalTable: "ProjectPlanSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectPlanSnapshots_Projects_ProjectId",
                table: "ProjectPlanSnapshots",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectPncFacts_Projects_ProjectId",
                table: "ProjectPncFacts",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_ProjectVideos_FeaturedVideoId",
                table: "Projects",
                column: "FeaturedVideoId",
                principalTable: "ProjectVideos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SocialMediaEventPhotos_SocialMediaEvents_SocialMediaEventId",
                table: "SocialMediaEventPhotos",
                column: "SocialMediaEventId",
                principalTable: "SocialMediaEvents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VisitPhotos_Visits_VisitId",
                table: "VisitPhotos",
                column: "VisitId",
                principalTable: "Visits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_AspNetUsers_HodUserId",
                table: "Projects");

            migrationBuilder.DropForeignKey(
                name: "FK_Projects_AspNetUsers_LeadPoUserId",
                table: "Projects");

            migrationBuilder.DropForeignKey(
                name: "FK_Projects_AspNetUsers_PlanApprovedByUserId",
                table: "Projects");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectTots_AspNetUsers_LastApprovedByUserId",
                table: "ProjectTots");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectPhotos_Projects_ProjectId",
                table: "ProjectPhotos");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectTots_Projects_ProjectId",
                table: "ProjectTots");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectVideos_Projects_ProjectId",
                table: "ProjectVideos");

            migrationBuilder.DropForeignKey(
                name: "FK_SocialMediaEventPhotos_SocialMediaEvents_SocialMediaEventId",
                table: "SocialMediaEventPhotos");

            migrationBuilder.DropForeignKey(
                name: "FK_VisitPhotos_Visits_VisitId",
                table: "VisitPhotos");

            migrationBuilder.DropTable(
                name: "ActivityAttachments");

            migrationBuilder.DropTable(
                name: "ActivityDeleteRequests");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "AuthEvents");

            migrationBuilder.DropTable(
                name: "Celebrations");

            migrationBuilder.DropTable(
                name: "DailyLoginStats");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "FfcAttachments");

            migrationBuilder.DropTable(
                name: "FfcProjects");

            migrationBuilder.DropTable(
                name: "Holidays");

            migrationBuilder.DropTable(
                name: "IprAttachments");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "PlanApprovalLogs");

            migrationBuilder.DropTable(
                name: "ProjectAonFacts");

            migrationBuilder.DropTable(
                name: "ProjectAudits");

            migrationBuilder.DropTable(
                name: "ProjectBenchmarkFacts");

            migrationBuilder.DropTable(
                name: "ProjectCommentAttachments");

            migrationBuilder.DropTable(
                name: "ProjectCommentMentions");

            migrationBuilder.DropTable(
                name: "ProjectCommercialFacts");

            migrationBuilder.DropTable(
                name: "ProjectDocumentRequests");

            migrationBuilder.DropTable(
                name: "ProjectIpaFacts");

            migrationBuilder.DropTable(
                name: "ProjectMetaChangeRequests");

            migrationBuilder.DropTable(
                name: "ProjectPlanDurations");

            migrationBuilder.DropTable(
                name: "ProjectPlanSnapshotRows");

            migrationBuilder.DropTable(
                name: "ProjectPncFacts");

            migrationBuilder.DropTable(
                name: "ProjectScheduleSettings");

            migrationBuilder.DropTable(
                name: "ProjectSowFacts");

            migrationBuilder.DropTable(
                name: "ProjectSupplyOrderFacts");

            migrationBuilder.DropTable(
                name: "ProjectTotRequests");

            migrationBuilder.DropTable(
                name: "ProliferationGranular");

            migrationBuilder.DropTable(
                name: "ProliferationYearly");

            migrationBuilder.DropTable(
                name: "ProliferationYearPreference");

            migrationBuilder.DropTable(
                name: "RemarkAudits");

            migrationBuilder.DropTable(
                name: "RemarkMentions");

            migrationBuilder.DropTable(
                name: "StageChangeLogs");

            migrationBuilder.DropTable(
                name: "StageChangeRequests");

            migrationBuilder.DropTable(
                name: "StageChecklistAudits");

            migrationBuilder.DropTable(
                name: "StageDependencyTemplates");

            migrationBuilder.DropTable(
                name: "StagePlans");

            migrationBuilder.DropTable(
                name: "StageShiftLogs");

            migrationBuilder.DropTable(
                name: "StageTemplates");

            migrationBuilder.DropTable(
                name: "TodoItems");

            migrationBuilder.DropTable(
                name: "TrainingCounters");

            migrationBuilder.DropTable(
                name: "TrainingDeleteRequests");

            migrationBuilder.DropTable(
                name: "TrainingProjects");

            migrationBuilder.DropTable(
                name: "TrainingRankCategoryMap");

            migrationBuilder.DropTable(
                name: "TrainingTrainees");

            migrationBuilder.DropTable(
                name: "UserNotificationPreferences");

            migrationBuilder.DropTable(
                name: "UserProjectMutes");

            migrationBuilder.DropTable(
                name: "WorkflowStatuses");

            migrationBuilder.DropTable(
                name: "Activities");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "FfcRecords");

            migrationBuilder.DropTable(
                name: "IprRecords");

            migrationBuilder.DropTable(
                name: "NotificationDispatches");

            migrationBuilder.DropTable(
                name: "ProjectComments");

            migrationBuilder.DropTable(
                name: "ProjectDocuments");

            migrationBuilder.DropTable(
                name: "ProjectPlanSnapshots");

            migrationBuilder.DropTable(
                name: "Remarks");

            migrationBuilder.DropTable(
                name: "StageChecklistItemTemplates");

            migrationBuilder.DropTable(
                name: "PlanVersions");

            migrationBuilder.DropTable(
                name: "Trainings");

            migrationBuilder.DropTable(
                name: "Statuses");

            migrationBuilder.DropTable(
                name: "Workflows");

            migrationBuilder.DropTable(
                name: "ActivityTypes");

            migrationBuilder.DropTable(
                name: "FfcCountries");

            migrationBuilder.DropTable(
                name: "ProjectStages");

            migrationBuilder.DropTable(
                name: "StageChecklistTemplates");

            migrationBuilder.DropTable(
                name: "TrainingTypes");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "LineDirectorates");

            migrationBuilder.DropTable(
                name: "ProjectCategories");

            migrationBuilder.DropTable(
                name: "ProjectPhotos");

            migrationBuilder.DropTable(
                name: "ProjectVideos");

            migrationBuilder.DropTable(
                name: "SponsoringUnits");

            migrationBuilder.DropTable(
                name: "TechnicalCategories");

            migrationBuilder.DropTable(
                name: "ProjectTots");

            migrationBuilder.DropTable(
                name: "SocialMediaEvents");

            migrationBuilder.DropTable(
                name: "SocialMediaEventPhotos");

            migrationBuilder.DropTable(
                name: "SocialMediaEventTypes");

            migrationBuilder.DropTable(
                name: "SocialMediaPlatforms");

            migrationBuilder.DropTable(
                name: "Visits");

            migrationBuilder.DropTable(
                name: "VisitPhotos");

            migrationBuilder.DropTable(
                name: "VisitTypes");
        }
    }
}
