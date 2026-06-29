using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Features.MediaLibrary.Data;

#nullable disable

namespace ProjectManagement.Features.MediaLibrary.Data.Migrations;

[DbContext(typeof(MediaLibraryDbContext))]
[Migration("20260628213000_AddClassificationDecisionPipeline")]
public sealed class AddClassificationDecisionPipeline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // --- MediaAsset prediction and decision state ---
        migrationBuilder.AddColumn<string>(name: "PredictedClassification", table: "MediaAssets", type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Unknown");
        migrationBuilder.AddColumn<decimal>(name: "PredictedClassificationScore", table: "MediaAssets", type: "numeric(5,4)", precision: 5, scale: 4, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<string>(name: "ClassificationDecisionStatus", table: "MediaAssets", type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "NotProcessed");
        migrationBuilder.AddColumn<string>(name: "ClassificationDecisionReasonCode", table: "MediaAssets", type: "character varying(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>(name: "AutomaticClassificationSignalsJson", table: "MediaAssets", type: "jsonb", nullable: true);
        migrationBuilder.AddColumn<string>(name: "AutomaticClassificationScoresJson", table: "MediaAssets", type: "jsonb", nullable: true);
        migrationBuilder.AddColumn<string>(name: "AutomaticClassificationMetricsJson", table: "MediaAssets", type: "jsonb", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ClassificationReviewedByUserId", table: "MediaAssets", type: "character varying(450)", maxLength: 450, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "ClassificationReviewedAt", table: "MediaAssets", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ClassificationReviewReason", table: "MediaAssets", type: "character varying(1024)", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "ClassificationConcurrencyToken", table: "MediaAssets", type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()");

        // --- Classification audit evidence preservation ---
        migrationBuilder.AddColumn<string>(name: "AutomaticPredictedClassification", table: "MediaClassificationAudits", type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Unknown");
        migrationBuilder.AddColumn<decimal>(name: "AutomaticPredictedScore", table: "MediaClassificationAudits", type: "numeric(5,4)", precision: 5, scale: 4, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<string>(name: "PreviousDecisionStatus", table: "MediaClassificationAudits", type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "NotProcessed");
        migrationBuilder.AddColumn<string>(name: "NewDecisionStatus", table: "MediaClassificationAudits", type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "NotProcessed");
        migrationBuilder.AddColumn<string>(name: "CorrelationId", table: "MediaClassificationAudits", type: "character varying(128)", maxLength: 128, nullable: true);

        // --- Append-only automatic classification run history ---
        migrationBuilder.CreateTable(
            name: "MediaClassificationRuns",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MediaAssetId = table.Column<long>(type: "bigint", nullable: false),
                ClassifierVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PredictedClassification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                PredictedScore = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                EffectiveClassification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                DecisionStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                DecisionReasonCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                CategoryScoresJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                SignalsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                MetricsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                ProcessingDurationMilliseconds = table.Column<int>(type: "integer", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                FailureReason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MediaClassificationRuns", x => x.Id);
                table.ForeignKey("FK_MediaClassificationRuns_MediaAssets_MediaAssetId", x => x.MediaAssetId, "MediaAssets", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.Sql("""
            UPDATE "MediaAssets"
            SET "PredictedClassification" = CASE WHEN "ClassificationIsManual" THEN 'Unknown' ELSE "Classification" END,
                "PredictedClassificationScore" = CASE WHEN "ClassificationIsManual" THEN 0 ELSE LEAST(GREATEST(COALESCE("ClassificationConfidence", 0), 0), 1) END,
                "ClassificationDecisionStatus" = CASE
                    WHEN "ClassificationIsManual" = TRUE THEN 'ManuallyCorrected'
                    WHEN "Kind" <> 'Photo' THEN 'NotApplicable'
                    WHEN "AnalysisStatus" = 'Failed' THEN 'ProcessingFailed'
                    ELSE 'NeedsReview'
                END,
                "ClassificationDecisionReasonCode" = CASE
                    WHEN "ClassificationIsManual" = TRUE THEN 'MIGRATED_MANUAL_DECISION'
                    WHEN "AnalysisStatus" = 'Failed' THEN 'CLASSIFIER_PROCESSING_FAILED'
                    ELSE 'MIGRATED_REQUIRES_RECLASSIFICATION'
                END,
                "AutomaticClassificationSignalsJson" = COALESCE("AnalysisSignalsJson", '[]'::jsonb),
                "AutomaticClassificationScoresJson" = '{}'::jsonb,
                "AutomaticClassificationMetricsJson" = '{}'::jsonb,
                "ClassificationReviewedByUserId" = CASE WHEN "ClassificationIsManual" THEN "ClassificationUpdatedByUserId" ELSE NULL END,
                "ClassificationReviewedAt" = CASE WHEN "ClassificationIsManual" THEN "ClassifiedAtUtc" ELSE NULL END;
            """);

        migrationBuilder.CreateIndex(name: "IX_MediaAssets_ClassificationDecisionStatus", table: "MediaAssets", column: "ClassificationDecisionStatus");
        migrationBuilder.CreateIndex(name: "IX_MediaAssets_PredictedClassification", table: "MediaAssets", column: "PredictedClassification");
        migrationBuilder.CreateIndex(name: "IX_MediaAssets_Classification", table: "MediaAssets", column: "Classification");
        migrationBuilder.CreateIndex(name: "IX_MediaAssets_ClassificationIsManual", table: "MediaAssets", column: "ClassificationIsManual");
        migrationBuilder.CreateIndex(name: "IX_MediaAssets_ClassifierVersion", table: "MediaAssets", column: "ClassifierVersion");
        migrationBuilder.CreateIndex(name: "IX_MediaAssets_ClassificationReviewedAt", table: "MediaAssets", column: "ClassificationReviewedAt");
        migrationBuilder.CreateIndex(name: "IX_MediaAssets_ClassificationConcurrencyToken", table: "MediaAssets", column: "ClassificationConcurrencyToken");
        migrationBuilder.CreateIndex(name: "IX_MediaClassificationRuns_Asset_CompletedAt", table: "MediaClassificationRuns", columns: new[] { "MediaAssetId", "CompletedAt" });
        migrationBuilder.CreateIndex(name: "IX_MediaClassificationRuns_ClassifierVersion", table: "MediaClassificationRuns", column: "ClassifierVersion");
        migrationBuilder.CreateIndex(name: "IX_MediaClassificationRuns_PredictedClassification", table: "MediaClassificationRuns", column: "PredictedClassification");
        migrationBuilder.CreateIndex(name: "IX_MediaClassificationRuns_DecisionStatus", table: "MediaClassificationRuns", column: "DecisionStatus");
        migrationBuilder.CreateIndex(name: "IX_MediaClassificationRuns_Succeeded", table: "MediaClassificationRuns", column: "Succeeded");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MediaClassificationRuns");
        migrationBuilder.DropIndex(name: "IX_MediaAssets_ClassificationDecisionStatus", table: "MediaAssets");
        migrationBuilder.DropIndex(name: "IX_MediaAssets_PredictedClassification", table: "MediaAssets");
        migrationBuilder.DropIndex(name: "IX_MediaAssets_Classification", table: "MediaAssets");
        migrationBuilder.DropIndex(name: "IX_MediaAssets_ClassificationIsManual", table: "MediaAssets");
        migrationBuilder.DropIndex(name: "IX_MediaAssets_ClassifierVersion", table: "MediaAssets");
        migrationBuilder.DropIndex(name: "IX_MediaAssets_ClassificationReviewedAt", table: "MediaAssets");
        migrationBuilder.DropIndex(name: "IX_MediaAssets_ClassificationConcurrencyToken", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "PredictedClassification", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "PredictedClassificationScore", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "ClassificationDecisionStatus", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "ClassificationDecisionReasonCode", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "AutomaticClassificationSignalsJson", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "AutomaticClassificationScoresJson", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "AutomaticClassificationMetricsJson", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "ClassificationReviewedByUserId", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "ClassificationReviewedAt", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "ClassificationReviewReason", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "ClassificationConcurrencyToken", table: "MediaAssets");
        migrationBuilder.DropColumn(name: "AutomaticPredictedClassification", table: "MediaClassificationAudits");
        migrationBuilder.DropColumn(name: "AutomaticPredictedScore", table: "MediaClassificationAudits");
        migrationBuilder.DropColumn(name: "PreviousDecisionStatus", table: "MediaClassificationAudits");
        migrationBuilder.DropColumn(name: "NewDecisionStatus", table: "MediaClassificationAudits");
        migrationBuilder.DropColumn(name: "CorrelationId", table: "MediaClassificationAudits");
    }
}
