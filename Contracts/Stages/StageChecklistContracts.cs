using System;
using System.Collections.Generic;

namespace ProjectManagement.Contracts.Stages;

public record StageChecklistTemplateDto(
    int Id,
    string Version,
    string StageCode,
    string? UpdatedByUserId,
    DateTimeOffset? UpdatedOn,
    byte[] RowVersion,
    IReadOnlyList<StageChecklistItemDto> Items);

public record StageChecklistItemDto(
    int Id,
    string Text,
    int Sequence,
    byte[] RowVersion,
    string? UpdatedByUserId,
    DateTimeOffset? UpdatedOn);

public record StageChecklistItemCreateRequest(
    string Text,
    byte[] TemplateRowVersion,
    int? Sequence = null);

public record StageChecklistItemUpdateRequest(
    string Text,
    byte[] TemplateRowVersion,
    byte[] ItemRowVersion);

public record StageChecklistItemDeleteRequest(
    byte[] TemplateRowVersion,
    byte[] ItemRowVersion);

public record StageChecklistReorderRequest(
    byte[] TemplateRowVersion,
    IReadOnlyList<StageChecklistReorderItem> Items);

public record StageChecklistReorderItem(
    int ItemId,
    int Sequence,
    byte[] RowVersion);
