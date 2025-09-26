using System;
using System.Collections.Generic;

namespace ProjectManagement.ViewModels;

public sealed class AssignRolesVm
{
    public int ProjectId { get; init; }
    public byte[] RowVersion { get; init; } = Array.Empty<byte>();

    public string? HodUserId { get; init; }
    public string? PoUserId { get; init; }

    public IReadOnlyList<(string Id, string Name)> HodOptions { get; init; } = Array.Empty<(string, string)>();
    public IReadOnlyList<(string Id, string Name)> PoOptions { get; init; } = Array.Empty<(string, string)>();
}
