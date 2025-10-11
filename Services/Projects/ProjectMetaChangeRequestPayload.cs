using System.Text.Json.Serialization;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectMetaChangeRequestPayload
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("caseFileNumber")]
    public string? CaseFileNumber { get; set; }

    [JsonPropertyName("categoryId")]
    public int? CategoryId { get; set; }

    [JsonPropertyName("technicalCategoryId")]
    public int? TechnicalCategoryId { get; set; }

    [JsonPropertyName("sponsoringUnitId")]
    public int? SponsoringUnitId { get; set; }

    [JsonPropertyName("sponsoringLineDirectorateId")]
    public int? SponsoringLineDirectorateId { get; set; }
}
