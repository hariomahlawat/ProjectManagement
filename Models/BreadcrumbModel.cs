namespace ProjectManagement.Models;

public record BreadcrumbModel(params (string Text, string? Url)[] Items);
