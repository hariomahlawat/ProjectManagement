using System.Reflection;
using ProjectManagement.Contracts.Notebook;
using ProjectManagement.Services.Notebook;

namespace ProjectManagement.Tests;

public sealed class NotebookUpdateContractTests
{
    [Fact]
    public void Update_request_is_content_only_and_does_not_inherit_create_contract()
    {
        // SECTION: Update autosave contract must not inherit create-only or command-owned fields.
        Assert.NotEqual(typeof(CreateNotebookItemRequest), typeof(UpdateNotebookItemRequest).BaseType);

        var properties = typeof(UpdateNotebookItemRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(UpdateNotebookItemRequest.Version), properties);
        Assert.DoesNotContain("ClientRequestId", properties);
        Assert.DoesNotContain("IsPinned", properties);
        Assert.DoesNotContain("IsFavorite", properties);
        Assert.DoesNotContain("Status", properties);
        Assert.DoesNotContain("OwnerId", properties);
        Assert.DoesNotContain("CreatedAtUtc", properties);
    }

    [Fact]
    public void Update_service_input_excludes_version_and_command_owned_state()
    {
        // SECTION: Expected version is supplied separately from content-owned update input.
        var properties = typeof(NotebookUpdateInput)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Version", properties);
        Assert.DoesNotContain("ClientRequestId", properties);
        Assert.DoesNotContain("IsPinned", properties);
        Assert.DoesNotContain("IsFavorite", properties);
        Assert.DoesNotContain("Status", properties);
    }
}
