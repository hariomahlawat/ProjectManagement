using ProjectManagement.Models.Navigation;

namespace ProjectManagement.Services.Navigation.ModuleNav;

public static class AdminModuleNavDefinition
{
    public static IReadOnlyList<NavigationItem> Build() =>
        AdminNavigationCatalog.BuildModuleItems();
}
