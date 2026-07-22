using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AdminAuditPayloadParserTests
{
    private readonly AdminAuditPayloadParser _parser = new();

    [Fact]
    public void Parse_StructuredPayload_SeparatesActorAndAffectedRecord()
    {
        const string json = """
            {
              "EntityType": "ApplicationUser",
              "EntityId": "user-27",
              "ActorUserId": "admin-1",
              "ActorName": "administrator",
              "Reason": "Posting completed"
            }
            """;

        var result = _parser.Parse(json);

        Assert.Equal("ApplicationUser", result.EntityType);
        Assert.Equal("user-27", result.EntityId);
        Assert.Equal("admin-1", result.ActorUserId);
        Assert.Equal("administrator", result.ActorName);
        Assert.Equal("ApplicationUser · user-27", result.AffectedRecord);
    }

    [Fact]
    public void Parse_InvalidJson_PreservesRawValueWithoutThrowing()
    {
        var result = _parser.Parse("not-json");

        Assert.Equal("not-json", result.RawPrettyJson);
        Assert.Null(result.EntityId);
    }
}
