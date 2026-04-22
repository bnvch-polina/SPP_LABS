namespace ProjectForTesting.Services;

public class NotificationService
{
    private readonly List<string> _sentNotifications = new();
    private readonly bool _shouldFail;
        
    public NotificationService(bool shouldFail = false)
    {
        _shouldFail = shouldFail;
    }
        
    public async Task NotifyAsync(string message)
    {
        await Task.Delay(50); // имитация отправки уведомления
            
        if (_shouldFail && message.Contains("fail"))
        {
            throw new InvalidOperationException("Notification service failed");
        }
            
        _sentNotifications.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
        
    public List<string> GetSentNotifications()
    {
        return _sentNotifications.ToList();
    }
        
    public void Clear()
    {
        _sentNotifications.Clear();
    }
        
    public int GetNotificationCount()
    {
        return _sentNotifications.Count;
    }
}