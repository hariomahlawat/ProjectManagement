using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "ProjectCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
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
                    table.CheckConstraint("CK_StageChangeLogs_Action", "\"Action\" IN ('Requested','Approved','Rejected','DirectApply','Applied','Superseded')");
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
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CaseFileNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false),
                    ActivePlanVersionNo = table.Column<int>(type: "integer", nullable: true),
                    CategoryId = table.Column<int>(type: "integer", nullable: true),
                    HodUserId = table.Column<string>(type: "text", nullable: true),
                    LeadPoUserId = table.Column<string>(type: "text", nullable: true),
                    PlanApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PlanApprovedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
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
                        name: "FK_Projects_ProjectCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "ProjectCategories",
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
                    table.ForeignKey(
                        name: "FK_PlanVersions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
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
                    table.ForeignKey(
                        name: "FK_ProjectAonFacts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    table.ForeignKey(
                        name: "FK_ProjectBenchmarkFacts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
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
                    table.ForeignKey(
                        name: "FK_ProjectCommercialFacts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    table.ForeignKey(
                        name: "FK_ProjectIpaFacts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    table.ForeignKey(
                        name: "FK_ProjectPlanDurations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    table.ForeignKey(
                        name: "FK_ProjectPlanSnapshots_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    table.ForeignKey(
                        name: "FK_ProjectPncFacts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    RequiresBackfill = table.Column<bool>(type: "boolean", nullable: false)
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
                    table.ForeignKey(
                        name: "FK_PlanApprovalLogs_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    table.ForeignKey(
                        name: "FK_ProjectPlanSnapshotRows_ProjectPlanSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "ProjectPlanSnapshots",
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
                    table.ForeignKey(
                        name: "FK_ProjectComments_ProjectStages_ProjectStageId",
                        column: x => x.ProjectStageId,
                        principalTable: "ProjectStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectComments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    table.ForeignKey(
                        name: "FK_ProjectCommentAttachments_ProjectComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "ProjectComments",
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
                    table.ForeignKey(
                        name: "FK_ProjectCommentMentions_ProjectComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "ProjectComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_Holidays_Date",
                table: "Holidays",
                column: "Date",
                unique: true);

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
                name: "IX_ProjectIpaFacts_ProjectId",
                table: "ProjectIpaFacts",
                column: "ProjectId");

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
                name: "IX_Projects_HodUserId",
                table: "Projects",
                column: "HodUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_LeadPoUserId",
                table: "Projects",
                column: "LeadPoUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_PlanApprovedByUserId",
                table: "Projects",
                column: "PlanApprovedByUserId");

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
                name: "IX_StageChangeLogs_ProjectId_StageCode_At",
                table: "StageChangeLogs",
                columns: new[] { "ProjectId", "StageCode", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_StageChangeRequests_ProjectId_StageCode",
                table: "StageChangeRequests",
                columns: new[] { "ProjectId", "StageCode" },
                unique: true,
                filter: "\"DecisionStatus\" = 'Pending'");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "Holidays");

            migrationBuilder.DropTable(
                name: "PlanApprovalLogs");

            migrationBuilder.DropTable(
                name: "ProjectAonFacts");

            migrationBuilder.DropTable(
                name: "ProjectBenchmarkFacts");

            migrationBuilder.DropTable(
                name: "ProjectCommentAttachments");

            migrationBuilder.DropTable(
                name: "ProjectCommentMentions");

            migrationBuilder.DropTable(
                name: "ProjectCommercialFacts");

            migrationBuilder.DropTable(
                name: "ProjectIpaFacts");

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
                name: "StageChangeLogs");

            migrationBuilder.DropTable(
                name: "StageChangeRequests");

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
                name: "WorkflowStatuses");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "ProjectComments");

            migrationBuilder.DropTable(
                name: "ProjectPlanSnapshots");

            migrationBuilder.DropTable(
                name: "PlanVersions");

            migrationBuilder.DropTable(
                name: "Statuses");

            migrationBuilder.DropTable(
                name: "Workflows");

            migrationBuilder.DropTable(
                name: "ProjectStages");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "ProjectCategories");
        }
    }
}
