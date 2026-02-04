namespace DeploymentStatusService;

/// <summary>
/// Represents a deployment in a Kubernetes cluster
/// </summary>
public record DeploymentInfo(
    string Name,
    string Namespace,
    string ClusterName
);

/// <summary>
/// Status returned from the cluster API
/// </summary>
public record DeploymentStatus(
    string Name,
    string Namespace,
    string ClusterName,
    int DesiredReplicas,
    int ReadyReplicas,
    DeploymentPhase Phase,
    DateTime LastUpdated
);

public enum DeploymentPhase
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Unknown
}

/// <summary>
/// Aggregated result containing status for multiple deployments
/// </summary>
public class AggregatedStatusResult
{
    public List<DeploymentStatus> Statuses { get; } = new();
    public List<StatusError> Errors { get; } = new();
    public DateTime AggregatedAt { get; init; } = DateTime.UtcNow;
}

public record StatusError(
    DeploymentInfo Deployment,
    string ErrorMessage
);
