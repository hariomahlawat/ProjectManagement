using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Features.Users;

internal static class MentionApi
{
    public static void MapMentionApi(this WebApplication app)
    {
        app.MapGet("/api/users/mentions", SearchMentionsAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> SearchMentionsAsync(
        [FromQuery(Name = "q")] string? query,
        [FromQuery(Name = "limit")] int? limit,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var term = query?.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            return Results.Ok(Array.Empty<MentionUserDto>());
        }

        var normalized = term.ToLowerInvariant();
        var size = Math.Clamp(limit.GetValueOrDefault(5), 1, 20);
        var fetchSize = Math.Max(size * 5, size);

        var candidates = await db.Users
            .AsNoTracking()
            .Where(u => !u.IsDisabled && !u.PendingDeletion)
            .Where(u =>
                (!string.IsNullOrEmpty(u.FullName) && u.FullName!.ToLower().Contains(normalized)) ||
                (!string.IsNullOrEmpty(u.UserName) && u.UserName!.ToLower().Contains(normalized)))
            .Select(u => new { u.Id, u.FullName, u.UserName, u.Email })
            .Take(fetchSize)
            .ToListAsync(cancellationToken);

        var results = candidates
            .Select(user =>
            {
                var display = ResolveDisplayName(user.FullName, user.UserName, user.Email, user.Id);
                return new MentionUserDto(user.Id, display, BuildInitials(display));
            })
            .OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(user => user.Id, StringComparer.Ordinal)
            .Take(size)
            .ToArray();

        return Results.Ok(results);
    }

    private static string ResolveDisplayName(string? fullName, string? userName, string? email, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName!;
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            return userName!;
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            return email!;
        }

        return fallback;
    }

    private static string BuildInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        var parts = name
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return "?";
        }

        if (parts.Length == 1)
        {
            var word = parts[0];
            if (word.Length >= 2)
            {
                return string.Concat(char.ToUpperInvariant(word[0]), char.ToUpperInvariant(word[1]));
            }

            return char.ToUpperInvariant(word[0]).ToString();
        }

        var first = char.ToUpperInvariant(parts[0][0]);
        var last = char.ToUpperInvariant(parts[^1][0]);
        return string.Concat(first, last);
    }

    private sealed record MentionUserDto(string Id, string DisplayName, string Initials);
}
