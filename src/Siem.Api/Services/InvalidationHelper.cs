namespace Siem.Api.Services;

internal static class InvalidationHelper
{
    /// <summary>
    /// Signals invalidation with a short synchronous retry.
    /// If all attempts fail, logs an error. The coordinator's startup
    /// reload and next signal will eventually catch up.
    /// </summary>
    public static void SignalWithRetry(
        IRecompilationCoordinator coordinator,
        InvalidationSignal signal,
        ILogger logger,
        int maxAttempts = 3,
        int delayMs = 50)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            if (coordinator.SignalInvalidation(signal))
                return;

            if (i < maxAttempts - 1)
                Thread.Sleep(delayMs);
        }

        logger.LogError(
            "Failed to signal invalidation after {Attempts} attempts: {Reason} {EntityId}. " +
            "Rules engine may be stale until next signal or restart.",
            maxAttempts, signal.Reason, signal.EntityId);
    }
}
