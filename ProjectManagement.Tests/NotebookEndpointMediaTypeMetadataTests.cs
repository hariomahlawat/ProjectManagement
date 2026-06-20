using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Controllers.Api;

namespace ProjectManagement.Tests;

public sealed class NotebookEndpointMediaTypeMetadataTests
{
    public static IEnumerable<object[]> JsonMutationActions()
    {
        // SECTION: Notebook JSON mutation endpoint names that must reject non-JSON request bodies.
        foreach (var name in new[]
        {
            nameof(NotebookController.Create),
            nameof(NotebookController.Update),
            nameof(NotebookController.Pin),
            nameof(NotebookController.Archive),
            nameof(NotebookController.Complete),
            nameof(NotebookController.Reopen),
            nameof(NotebookController.Delete),
            nameof(NotebookController.Restore),
            nameof(NotebookController.ShowCheckboxes),
            nameof(NotebookController.HideCheckboxes),
            nameof(NotebookController.ToggleChecklistItem)
        })
        {
            yield return new object[] { name };
        }
    }

    [Theory]
    [MemberData(nameof(JsonMutationActions))]
    public void Notebook_mutations_declare_application_json_contract(string actionName)
    {
        // SECTION: Endpoint media-type metadata proves the server expects JSON instead of accepting arbitrary bodies.
        var method = typeof(NotebookController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(candidate => candidate.Name == actionName);

        var consumes = method.GetCustomAttribute<ConsumesAttribute>();

        Assert.NotNull(consumes);
        Assert.Contains("application/json", consumes!.ContentTypes);
    }
}
