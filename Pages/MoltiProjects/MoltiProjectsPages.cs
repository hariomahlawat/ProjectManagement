using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Pages.ItProjects;
using ProjectManagement.Services;
using ProjectManagement.Services.ItProjects;

namespace ProjectManagement.Pages.MoltiProjects;

// MOLTI projects (docs/ux/molti-sre-projects.md): a module of its own for the user, on the IT projects
// engine. Each page is the shared page model with this module's code and policy; the markup is
// the shared partial under Pages/ItProjects.

/// <summary>The MOLTI projects board (<c>/MoltiProjects</c>).</summary>
[Authorize(Policy = Policies.MoltiProjects.View)]
public class IndexModel : ItProjectBoardPageModel
{
    public IndexModel(IItProjectReadService reads)
        : base(reads)
    {
    }

    public override string ModuleCode => ItProjectModules.Molti;
}

/// <summary>New MOLTI project (<c>/MoltiProjects/Create</c>): Admin and HoD.</summary>
[Authorize(Policy = Policies.MoltiProjects.Manage)]
public class CreateModel : ItProjectCreatePageModel
{
    public CreateModel(IItProjectCommandService commands, IClock clock)
        : base(commands, clock)
    {
    }

    public override string ModuleCode => ItProjectModules.Molti;
}

/// <summary>One MOLTI project (<c>/MoltiProjects/{id}</c>).</summary>
[Authorize(Policy = Policies.MoltiProjects.View)]
public class DetailsModel : ItProjectDetailsPageModel
{
    public DetailsModel(IItProjectReadService reads, IItProjectCommandService commands, IItProjectRemarkService remarks, IItProjectAttachmentService attachments, IClock clock)
        : base(reads, commands, remarks, attachments, clock)
    {
    }

    public override string ModuleCode => ItProjectModules.Molti;
}
