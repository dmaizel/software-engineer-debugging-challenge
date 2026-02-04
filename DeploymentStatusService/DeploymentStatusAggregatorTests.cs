using Xunit;

namespace DeploymentStatusService.Tests;

public class DeploymentStatusAggregatorTests
{
    [Fact]
    public async Task SingleDeployment_ReturnsCorrectStatus()
    {
        // Arrange
        var mockClient = new MockClusterApiClient();
        mockClient.SetupDeployment("prod-cluster", "app-namespace", "web-api", 
            replicas: 3, ready: 3, phase: DeploymentPhase.Running);

        var aggregator = new DeploymentStatusAggregator(mockClient);
        var deployments = new[]
        {
            new DeploymentInfo("web-api", "app-namespace", "prod-cluster")
        };

        // Act
        var result = await aggregator.GetStatusAsync(deployments);

        // Assert
        Assert.Single(result.Statuses);
        Assert.Empty(result.Errors);
        Assert.Equal("web-api", result.Statuses[0].Name);
        Assert.Equal(3, result.Statuses[0].ReadyReplicas);
    }

    [Fact]
    public async Task MultipleDeployments_Sequential_ReturnsAllStatuses()
    {
        // Arrange
        var mockClient = new MockClusterApiClient();
        mockClient.SetupDeployment("cluster-a", "ns1", "service-1", 2, 2, DeploymentPhase.Running);
        mockClient.SetupDeployment("cluster-b", "ns2", "service-2", 5, 5, DeploymentPhase.Running);

        var aggregator = new DeploymentStatusAggregator(mockClient);

        // Act
        var result1 = await aggregator.GetStatusAsync(new[] 
        { 
            new DeploymentInfo("service-1", "ns1", "cluster-a") 
        });
        var result2 = await aggregator.GetStatusAsync(new[] 
        { 
            new DeploymentInfo("service-2", "ns2", "cluster-b") 
        });

        // Assert
        Assert.Single(result1.Statuses);
        Assert.Equal(2, result1.Statuses[0].ReadyReplicas);
        Assert.Single(result2.Statuses);
        Assert.Equal(5, result2.Statuses[0].ReadyReplicas);
    }

    [Fact]
    public async Task TransientFailure_RetriesAndSucceeds()
    {
        // Arrange
        var mockClient = new MockClusterApiClient();
        mockClient.SetupDeployment("cluster", "ns", "flaky-service", 3, 3, DeploymentPhase.Running);
        mockClient.SetupTransientFailures("cluster", "ns", "flaky-service", failCount: 2);

        var retryPolicy = new RetryPolicy(maxRetries: 3, baseDelay: TimeSpan.FromMilliseconds(10));
        var aggregator = new DeploymentStatusAggregator(mockClient, retryPolicy);

        // Act
        var result = await aggregator.GetStatusAsync(new[]
        {
            new DeploymentInfo("flaky-service", "ns", "cluster")
        });

        // Assert
        Assert.Single(result.Statuses);
        Assert.Empty(result.Errors);
        Assert.Equal("flaky-service", result.Statuses[0].Name);
    }

    [Fact]
    public async Task MultipleDeployments_ShouldReturnCorrectStatusForEach()
    {
        // Arrange
        var mockClient = new MockClusterApiClient();
        
        mockClient.SetupDeployment("cluster", "ns", "deploy-1", 1, 1, DeploymentPhase.Running);
        mockClient.SetupDeployment("cluster", "ns", "deploy-2", 2, 2, DeploymentPhase.Running);
        mockClient.SetupDeployment("cluster", "ns", "deploy-3", 3, 3, DeploymentPhase.Running);
        mockClient.SetupDeployment("cluster", "ns", "deploy-4", 4, 4, DeploymentPhase.Running);
        mockClient.SetupDeployment("cluster", "ns", "deploy-5", 5, 5, DeploymentPhase.Running);

        mockClient.SetupTransientFailures("cluster", "ns", "deploy-2", failCount: 2);
        mockClient.SetupTransientFailures("cluster", "ns", "deploy-4", failCount: 1);

        var retryPolicy = new RetryPolicy(maxRetries: 3, baseDelay: TimeSpan.FromMilliseconds(10));
        var aggregator = new DeploymentStatusAggregator(mockClient, retryPolicy);

        var deployments = new[]
        {
            new DeploymentInfo("deploy-1", "ns", "cluster"),
            new DeploymentInfo("deploy-2", "ns", "cluster"),
            new DeploymentInfo("deploy-3", "ns", "cluster"),
            new DeploymentInfo("deploy-4", "ns", "cluster"),
            new DeploymentInfo("deploy-5", "ns", "cluster"),
        };

        // Act
        var result = await aggregator.GetStatusAsync(deployments);

        // Assert
        Assert.Empty(result.Errors);
        Assert.Equal(5, result.Statuses.Count);

        foreach (var status in result.Statuses)
        {
            var expectedReplicas = int.Parse(status.Name.Split('-')[1]);
            Assert.Equal(expectedReplicas, status.ReadyReplicas);
        }
    }

    [Fact]
    public async Task HighVolume_AllShouldSucceed()
    {
        // Arrange
        var mockClient = new MockClusterApiClient();
        var deployments = new List<DeploymentInfo>();
        
        for (int i = 1; i <= 20; i++)
        {
            mockClient.SetupDeployment("cluster", "ns", $"svc-{i}", i, i, DeploymentPhase.Running);
            deployments.Add(new DeploymentInfo($"svc-{i}", "ns", "cluster"));
            
            if (i % 3 == 0)
            {
                mockClient.SetupTransientFailures("cluster", "ns", $"svc-{i}", failCount: 2);
            }
        }

        var retryPolicy = new RetryPolicy(maxRetries: 3, baseDelay: TimeSpan.FromMilliseconds(5));
        var aggregator = new DeploymentStatusAggregator(mockClient, retryPolicy);

        // Act
        var result = await aggregator.GetStatusAsync(deployments);

        // Assert
        Assert.Empty(result.Errors);
        Assert.Equal(20, result.Statuses.Count);
    }
}

/// <summary>
/// Mock implementation of cluster API client for testing.
/// </summary>
public class MockClusterApiClient : IClusterApiClient
{
    private readonly Dictionary<string, DeploymentStatus> _deployments = new();
    private readonly Dictionary<string, int> _failureCounts = new();
    private readonly Dictionary<string, int> _currentFailures = new();
    private readonly object _lock = new();

    public void SetupDeployment(string cluster, string ns, string name, 
        int replicas, int ready, DeploymentPhase phase)
    {
        var key = GetKey(cluster, ns, name);
        _deployments[key] = new DeploymentStatus(
            name, ns, cluster, replicas, ready, phase, DateTime.UtcNow
        );
    }

    public void SetupTransientFailures(string cluster, string ns, string name, int failCount)
    {
        var key = GetKey(cluster, ns, name);
        _failureCounts[key] = failCount;
        _currentFailures[key] = 0;
    }

    public async Task<DeploymentStatus> GetDeploymentStatusAsync(
        string clusterName, string @namespace, string deploymentName,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(Random.Shared.Next(5, 20), cancellationToken);

        var key = GetKey(clusterName, @namespace, deploymentName);

        lock (_lock)
        {
            if (_failureCounts.TryGetValue(key, out var maxFailures))
            {
                _currentFailures.TryGetValue(key, out var current);
                if (current < maxFailures)
                {
                    _currentFailures[key] = current + 1;
                    throw new TransientException($"Transient failure for {deploymentName}");
                }
            }
        }

        if (_deployments.TryGetValue(key, out var status))
        {
            return status;
        }

        throw new InvalidOperationException($"Deployment not found: {deploymentName}");
    }

    private static string GetKey(string cluster, string ns, string name) 
        => $"{cluster}/{ns}/{name}";
}
