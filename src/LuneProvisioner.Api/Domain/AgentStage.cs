namespace LuneProvisioner.Api.Domain;

public enum AgentStage
{
    Plan = 1,
    Validate = 2,
    DryRun = 3,
    Apply = 4,
    Output = 5
}
