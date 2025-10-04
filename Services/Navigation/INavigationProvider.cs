using System.Collections.Generic;
using System.Threading.Tasks;
using ProjectManagement.Models.Navigation;

namespace ProjectManagement.Services.Navigation;

public interface INavigationProvider
{
    Task<IReadOnlyList<NavigationItem>> GetNavigationAsync();
}
