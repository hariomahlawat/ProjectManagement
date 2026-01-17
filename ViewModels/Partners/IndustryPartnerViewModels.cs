using System;

namespace ProjectManagement.ViewModels.Partners;

// SECTION: List view models
public sealed record IndustryPartnerSummaryVm(
    int Id,
    string FirmName,
    string Status,
    string? PartnerType,
    string? City,
    string? PrimaryContactName,
    string? PrimaryContactDesignation,
    string? PrimaryEmail,
    string? PrimaryPhone,
    int ProjectCount);

// SECTION: Lookup options
public sealed record IndustryPartnerOptionVm(int Id, string FirmName, string Status);

// SECTION: Project lookup options
public sealed record ProjectOptionVm(int Id, string Name, string LifecycleStatus);

// SECTION: Project association view model
public sealed record ProjectIndustryPartnerVm(
    int ProjectId,
    int PartnerId,
    string FirmName,
    string Role,
    string Status,
    DateOnly? FromDate,
    DateOnly? ToDate,
    string? Notes);
