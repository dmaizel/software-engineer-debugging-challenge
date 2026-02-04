namespace DeploymentStatusService;

/// <summary>
/// Configurable retry policy with exponential backoff.
/// Used for resilient API calls to cluster endpoints.
/// </summary>
public class RetryPolicy
{
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly double _backoffMultiplier;
    private int _currentAttempt;

    public RetryPolicy(int maxRetries = 3, TimeSpan? baseDelay = null, double backoffMultiplier = 2.0)
    {
        _maxRetries = maxRetries;
        _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(100);
        _backoffMultiplier = backoffMultiplier;
        _currentAttempt = 0;
    }

    /// <summary>
    /// Executes the given operation with retry logic.
    /// On transient failures, retries with exponential backoff.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        _currentAttempt = 0;
        Exception? lastException = null;

        while (_currentAttempt <= _maxRetries)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await operation();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TransientException ex)
            {
                lastException = ex;
                _currentAttempt++;

                if (_currentAttempt > _maxRetries)
                {
                    break;
                }

                var delay = CalculateDelay();
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception)
            {
                // Non-transient exceptions should not be retried
                throw;
            }
        }

        throw new RetryExhaustedException(
            $"Operation failed after {_maxRetries} retries",
            lastException
        );
    }

    private TimeSpan CalculateDelay()
    {
        var delayMs = _baseDelay.TotalMilliseconds * Math.Pow(_backoffMultiplier, _currentAttempt - 1);
        delayMs = Math.Min(delayMs, 30000);
        return TimeSpan.FromMilliseconds(delayMs);
    }
}

/// <summary>
/// Indicates a transient failure that may succeed on retry
/// </summary>
public class TransientException : Exception
{
    public TransientException(string message) : base(message) { }
    public TransientException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when all retry attempts have been exhausted
/// </summary>
public class RetryExhaustedException : Exception
{
    public RetryExhaustedException(string message, Exception? inner) : base(message, inner) { }
}
