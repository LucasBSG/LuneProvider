namespace LuneProvisioner.Api.Domain;

public enum JobStatus
{
    Pending = 1,
    Running = 2,
    PendingApproval = 3,
    Succeeded = 4,
    Failed = 5
}
