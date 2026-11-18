using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Contracts.Chat;
using ProjectManagement.Data;
using ProjectManagement.Models.Chat;

namespace ProjectManagement.Services.Chat;

/// <summary>
/// Everything a message carries besides its text, added in one place to every message chat
/// returns or pushes: its files, the message it replies to, when it was edited, who it
/// mentions and its reactions. Batched: one query per kind of detail for a whole page.
/// None of it depends on who is asking, so the same message can be pushed to everyone.
/// The reply is an id only: the browser quotes the original from what it has loaded, so a
/// group member never receives text from before they joined through a reply.
/// </summary>
public static class ChatMessageDetails
{
    public static async Task<IReadOnlyList<ChatMessageDto>> WithDetailsAsync(
        ApplicationDbContext db,
        IReadOnlyList<ChatMessageDto> messages,
        CancellationToken cancellationToken)
    {
        var withFiles = await ChatAttachmentService.WithAttachmentsAsync(db, messages, cancellationToken);

        // Notices and unsent messages carry nothing more: an unsent message's text is gone, and
        // with it what it replied to, mentioned or was reacted to.
        var ids = withFiles.Where(m => m.Id > 0 && m.EventKind is null && !m.IsUnsent).Select(m => m.Id).ToList();
        if (ids.Count == 0)
        {
            return withFiles;
        }

        var extras = await db.ChatMessages
            .AsNoTracking()
            .Where(m => ids.Contains(m.Id) && (m.ReplyToMessageId != null || m.EditedUtc != null))
            .Select(m => new { m.Id, m.ReplyToMessageId, m.EditedUtc })
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        var mentions = (await db.ChatMessageMentions
                .AsNoTracking()
                .Where(m => ids.Contains(m.MessageId))
                .Select(m => new { m.MessageId, m.UserId })
                .ToListAsync(cancellationToken))
            .GroupBy(m => m.MessageId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(m => m.UserId).OrderBy(id => id, StringComparer.Ordinal).ToList());

        var reactions = Group(await db.ChatMessageReactions
            .AsNoTracking()
            .Where(r => ids.Contains(r.MessageId))
            .Select(r => new ReactionRow(r.MessageId, r.Kind, r.UserId, r.CreatedUtc))
            .ToListAsync(cancellationToken));

        if (extras.Count == 0 && mentions.Count == 0 && reactions.Count == 0)
        {
            return withFiles;
        }

        return withFiles
            .Select(m =>
            {
                if (m.Id <= 0 || m.EventKind is not null || m.IsUnsent)
                {
                    return m;
                }

                extras.TryGetValue(m.Id, out var extra);
                return m with
                {
                    ReplyToMessageId = extra?.ReplyToMessageId,
                    EditedUtc = extra?.EditedUtc,
                    MentionedUserIds = mentions.GetValueOrDefault(m.Id),
                    Reactions = reactions.GetValueOrDefault(m.Id),
                };
            })
            .ToList();
    }

    public static async Task<ChatMessageDto> WithDetailsAsync(ApplicationDbContext db, ChatMessageDto message, CancellationToken cancellationToken)
        => (await WithDetailsAsync(db, new[] { message }, cancellationToken))[0];

    /// <summary>The reactions of one message, kinds in their fixed order, people in the order they reacted.</summary>
    public static async Task<IReadOnlyList<ChatReactionDto>> ReactionsOfAsync(ApplicationDbContext db, long messageId, CancellationToken cancellationToken)
    {
        var rows = await db.ChatMessageReactions
            .AsNoTracking()
            .Where(r => r.MessageId == messageId)
            .Select(r => new ReactionRow(r.MessageId, r.Kind, r.UserId, r.CreatedUtc))
            .ToListAsync(cancellationToken);
        return Group(rows).GetValueOrDefault(messageId) ?? Array.Empty<ChatReactionDto>();
    }

    private static Dictionary<long, IReadOnlyList<ChatReactionDto>> Group(List<ReactionRow> rows)
        => rows
            .GroupBy(r => r.MessageId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ChatReactionDto>)ChatReactionKind.All
                    .Select(kind => new ChatReactionDto(
                        kind,
                        g.Where(r => r.Kind == kind)
                            .OrderBy(r => r.CreatedUtc)
                            .ThenBy(r => r.UserId, StringComparer.Ordinal)
                            .Select(r => r.UserId)
                            .ToList()))
                    .Where(r => r.UserIds.Count > 0)
                    .ToList());

    private sealed record ReactionRow(long MessageId, string Kind, string UserId, DateTimeOffset CreatedUtc);
}
