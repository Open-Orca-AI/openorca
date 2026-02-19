using OpenOrca.Core.Configuration;

namespace OpenOrca.Core.Permissions;

public enum PermissionDecision
{
    Approved,
    Denied,
    ApproveAll
}

public sealed class PermissionManager
{
    private readonly OrcaConfig _config;
    private readonly HashSet<string> _sessionApprovedTools = new(StringComparer.OrdinalIgnoreCase);

    // Callback for interactive approval prompt (set by CLI layer)
    public Func<string, string, Task<PermissionDecision>>? PromptForApproval { get; set; }

    public PermissionManager(OrcaConfig config)
    {
        _config = config;
    }

    public async Task<bool> CheckPermissionAsync(string toolName, string riskLevel)
    {
        // Disabled tools
        if (_config.Permissions.DisabledTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
            return false;

        // Auto-approve all
        if (_config.Permissions.AutoApproveAll)
            return true;

        // Always-approved tools
        if (_config.Permissions.AlwaysApprove.Contains(toolName, StringComparer.OrdinalIgnoreCase))
            return true;

        // Session-level approval
        if (_sessionApprovedTools.Contains(toolName))
            return true;

        // Risk-based auto-approval
        if (riskLevel == "ReadOnly" && _config.Permissions.AutoApproveReadOnly)
            return true;

        if (riskLevel == "Moderate" && _config.Permissions.AutoApproveModerate)
            return true;

        // Need interactive approval
        if (PromptForApproval is null)
            return false;

        var decision = await PromptForApproval(toolName, riskLevel);

        switch (decision)
        {
            case PermissionDecision.Approved:
                return true;
            case PermissionDecision.ApproveAll:
                _sessionApprovedTools.Add(toolName);
                return true;
            case PermissionDecision.Denied:
            default:
                return false;
        }
    }

    public void ResetSessionApprovals()
    {
        _sessionApprovedTools.Clear();
    }
}
