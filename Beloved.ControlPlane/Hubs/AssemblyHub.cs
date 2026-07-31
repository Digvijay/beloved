using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace Beloved.ControlPlane.Hubs;

public class AssemblyHub : Hub
{
    public async Task JoinJobGroup(string jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, jobId);
    }
}
