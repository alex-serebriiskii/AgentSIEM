namespace Siem.Api.Notifications;

public interface INotificationRetryWorker
{
    void EnqueueRetry(PendingNotification notification);
}
