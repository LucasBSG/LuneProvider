using Microsoft.AspNetCore.SignalR;

namespace LuneProvisioner.Api.Infrastructure.SignalR;

public sealed class JobStatusHub : Hub
{
    public static string GroupName(Guid jobId) => $"job:{jobId:N}";

    public Task JoinJobGroup(Guid jobId)
        => Groups.AddToGroupAsync(Context.ConnectionId, GroupName(jobId));

    public Task LeaveJobGroup(Guid jobId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(jobId));
}
