namespace DeploymentStatusService;

/// <summary>
/// Aggregates deployment status from multiple clusters.
/// Provides a unified view of deployment health across the infrastructure.
/// </summary>
public class DeploymentStatusAggregator
{
    private readonly IClusterApiClient _clusterClient;
    private readonly RetryPolicy _retryPolicy;

    public DeploymentStatusAggregator(IClusterApiClient clusterClient, RetryPolicy? retryPolicy = null)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
        // Use a shared retry policy for consistent behavior across all calls
        _retryPolicy = retryPolicy ?? new RetryPolicy(maxRetries: 3);
    }

    /// <summary>
    /// Gets the status of multiple deployments concurrently.
    /// Results are aggregated into a single response for efficiency.
    /// </summary>
    public async Task<AggregatedStatusResult> GetStatusAsync(
        IEnumerable<DeploymentInfo> deployments,
        CancellationToken cancellationToken = default)
    {
        var result = new AggregatedStatusResult();
        var deploymentList = deployments.ToList();

        if (deploymentList.Count == 0)
        {
            return result;
        }

        // Fetch all deployment statuses concurrently for better performance
        var tasks = deploymentList.Select(deployment => 
            FetchSingleDeploymentStatusAsync(deployment, cancellationToken)
        ).ToList();

        var outcomes = await Task.WhenAll(tasks);

        // Aggregate results
        foreach (var outcome in outcomes)
        {
            if (outcome.Status != null)
            {
                result.Statuses.Add(outcome.Status);
            }
            else if (outcome.Error != null)
            {
                result.Errors.Add(outcome.Error);
            }
        }

        return result;
    }

    /// <summary>
    /// Fetches status for a single deployment with retry logic
    /// </summary>
    private async Task<FetchOutcome> FetchSingleDeploymentStatusAsync(
        DeploymentInfo deployment,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await _retryPolicy.ExecuteAsync(
                async () => await _clusterClient.GetDeploymentStatusAsync(
                    deployment.ClusterName,
                    deployment.Namespace,
                    deployment.Name,
                    cancellationToken
                ),
                cancellationToken
            );

            return new FetchOutcome(status, null);
        }
        catch (RetryExhaustedException ex)
        {
            return new FetchOutcome(null, new StatusError(deployment, $"Failed after retries: {ex.Message}"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new FetchOutcome(null, new StatusError(deployment, ex.Message));
        }
    }

    private record FetchOutcome(DeploymentStatus? Status, StatusError? Error);
}

/// <summary>
/// Interface for cluster API communication.
/// In production, this wraps the Kubernetes API client.
/// </summary>
public interface IClusterApiClient
{
    Task<DeploymentStatus> GetDeploymentStatusAsync(
        string clusterName,
        string @namespace,
        string deploymentName,
        CancellationToken cancellationToken = default
    );
}
