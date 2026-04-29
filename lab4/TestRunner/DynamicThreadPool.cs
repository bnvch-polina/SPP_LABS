using System.Collections.Generic;
using System.Diagnostics;

namespace SPP_LAB_1_TRIAL;

public sealed class DynamicThreadPool : IDisposable
{
    public sealed class PoolOptions
    {
        public int MinThreads { get; init; } = 2;
        public int MaxThreads { get; init; } = 8;
        //Если задача ждёт дольше - создаётся новый поток.
        public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(4);
        //максимальное время существования потока
        public TimeSpan QueueWaitThreshold { get; init; } = TimeSpan.FromMilliseconds(500);
        //	Если поток занят задачей дольше - считается зависшим.
        public TimeSpan WorkerHangTimeout { get; init; } = TimeSpan.FromSeconds(15);
        //частота проверки 
        public TimeSpan MonitorInterval { get; init; } = TimeSpan.FromMilliseconds(300);
    }

    public sealed class PoolStats
    {
        public int QueueLength { get; init; }      // Задач в очереди
        public int ActiveWorkers { get; init; }    // Всего потоков
        public int BusyWorkers { get; init; }      // Занятых потоков
        public long CompletedTasks { get; init; }  // Успешно выполнено
        public long FailedTasks { get; init; }     // С ошибками
        public long SpawnedWorkers { get; init; }  // Всего создано потоков
        public long RetiredWorkers { get; init; }  // Всего уволено
        public long RecoveredHungWorkers { get; init; } // Восстановлено зависших
    }

    private sealed class WorkItem
    {
        //какое действие(пока задача именно в очереди)
        public Action Action { get; init; } = null!;
        //когда поставлено в очередь
        public DateTime EnqueuedAtUtc { get; init; }
        //для логов
        public string Description { get; init; } = string.Empty;
    }

    private sealed class WorkerState
    {
        public int WorkerId { get; init; }
        public Thread Thread { get; init; } = null!;
        public bool Busy { get; set; }
        public DateTime LastActivityUtc { get; set; }
        public DateTime? StartedCurrentTaskUtc { get; set; }
        //запрос на выход из пула
        public bool RetireRequested { get; set; }
    }

    private readonly Queue<WorkItem> _queue = new();
    private readonly Dictionary<int, WorkerState> _workers = new();
    private readonly object _sync = new();
    private readonly Thread _monitorThread;
    private readonly PoolOptions _options;

    private bool _shutdownRequested;
    private int _nextWorkerId;
    private long _completedTasks;
    private long _failedTasks;
    private long _spawnedWorkers;
    private long _retiredWorkers;
    private long _recoveredHungWorkers;

    public event Action<string>? OnLog;
    public event Action<PoolStats>? OnStatsChanged;
    public event Action? OnPoolStarted;
    public event Action? OnPoolShutdown;
    public event Action<int, string>? OnWorkerSpawned; // workerId, reason
    public event Action<int, string>? OnWorkerRetired;  // workerId, reason
    public event Action<string>? OnTaskEnqueued;        // description
    public event Action<int, string>? OnTaskStarted;   // workerId, description
    public event Action<int, string>? OnTaskCompleted; // workerId, description
    public event Action<int, string, string>? OnTaskFailed; // workerId, description, error

    public DynamicThreadPool(PoolOptions? options = null)
    {
        _options = options ?? new PoolOptions();
        if (_options.MinThreads < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MinThreads must be >= 1.");
        }

        if (_options.MaxThreads < _options.MinThreads)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxThreads must be >= MinThreads.");
        }

        for (int i = 0; i < _options.MinThreads; i++)
        {
            SpawnWorker_Locked("initial warm-up");
        }

        _monitorThread = new Thread(MonitorLoop)
        {
            IsBackground = true,
            Name = "DynamicThreadPool-Monitor"
        };
        _monitorThread.Start();

        OnPoolStarted?.Invoke();
    }
    //-
    public void Enqueue(Action action, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_sync)
        {
            if (_shutdownRequested)
            {
                throw new InvalidOperationException("Pool is shutting down.");
            }

            _queue.Enqueue(new WorkItem
            {
                Action = action,
                EnqueuedAtUtc = DateTime.UtcNow,
                Description = description ?? "task"
            });

            OnTaskEnqueued?.Invoke(description ?? "task");
            ScaleUpIfNeeded_Locked();
            PulseAllAndPublishStats_Locked();
        }
    }
    //-
    public void Dispose()
    {
        List<Thread> workerThreads;
        lock (_sync)
        {
            if (_shutdownRequested)
            {
                return;
            }

            _shutdownRequested = true;
            OnPoolShutdown?.Invoke();
            workerThreads = _workers.Values.Select(w => w.Thread).ToList();
            Monitor.PulseAll(_sync);
            PublishStatsUnsafe_Locked();
        }

        foreach (var thread in workerThreads)
        {
            thread.Join();
        }

        _monitorThread.Join();
    }
    //-
    private void MonitorLoop()
    {
        while (true)
        {
            Thread.Sleep(_options.MonitorInterval);
            lock (_sync)
            {
                if (_shutdownRequested)
                {
                    return;
                }

                ScaleUpIfNeeded_Locked();
                ShrinkIfIdle_Locked();
                RecoverHungWorkers_Locked();
                PulseAllAndPublishStats_Locked();
            }
        }
    }
    //-
    private void WorkerLoop(int workerId)
    {
        while (true)
        {
            WorkItem? item;
            lock (_sync)
            {
                while (!_shutdownRequested && _queue.Count == 0 && _workers.TryGetValue(workerId, out var current) && !current.RetireRequested)
                {
                    var idleLimit = current.LastActivityUtc + _options.IdleTimeout;
                    var waitFor = idleLimit - DateTime.UtcNow;
                    if (waitFor <= TimeSpan.Zero)
                    {
                        break;
                    }

                    Monitor.Wait(_sync, waitFor);
                }

                if (_shutdownRequested)
                {
                    RetireWorker_Locked(workerId, "shutdown");
                    return;
                }

                if (!_workers.TryGetValue(workerId, out var worker))
                {
                    return;
                }

                if (worker.RetireRequested || (_queue.Count == 0 && _workers.Count > _options.MinThreads && DateTime.UtcNow - worker.LastActivityUtc >= _options.IdleTimeout))
                {
                    RetireWorker_Locked(workerId, worker.RetireRequested ? "requested retirement" : "idle timeout");
                    return;
                }

                if (_queue.Count == 0)
                {
                    continue;
                }

                item = _queue.Dequeue();
                worker.Busy = true;
                worker.StartedCurrentTaskUtc = DateTime.UtcNow;
                worker.LastActivityUtc = DateTime.UtcNow;
                PublishStatsUnsafe_Locked();

                OnTaskStarted?.Invoke(workerId, item.Description);
            }

            try
            {
                item.Action();
                OnTaskCompleted?.Invoke(workerId, item.Description);
                lock (_sync)
                {
                    _completedTasks++;
                }
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _failedTasks++;
                }

                OnLog?.Invoke($"[pool] Task failed: {item.Description}. Error: {ex.GetType().Name}: {ex.Message}");
                OnTaskFailed?.Invoke(workerId, item.Description, $"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                lock (_sync)
                {
                    if (_workers.TryGetValue(workerId, out var worker))
                    {
                        worker.Busy = false;
                        worker.StartedCurrentTaskUtc = null;
                        worker.LastActivityUtc = DateTime.UtcNow;
                    }

                    PulseAllAndPublishStats_Locked();
                }
            }
        }
    }
    //-
    private void ScaleUpIfNeeded_Locked()
    {
        if (_workers.Count >= _options.MaxThreads || _queue.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var oldestItem = _queue.Peek();
        var queueWait = now - oldestItem.EnqueuedAtUtc;
        var busyWorkers = _workers.Values.Count(w => w.Busy);
        var needMoreWorkers = _queue.Count > busyWorkers || queueWait >= _options.QueueWaitThreshold;

        if (needMoreWorkers)
        {
            SpawnWorker_Locked(queueWait >= _options.QueueWaitThreshold
                ? $"queue wait threshold exceeded ({queueWait.TotalMilliseconds:F0}ms)"
                : "queue length exceeded busy workers");
        }
    }
    //-
    private void ShrinkIfIdle_Locked()
    {
        if (_workers.Count <= _options.MinThreads)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var idleCandidates = _workers.Values
            .Where(w => !w.Busy && !w.RetireRequested && now - w.LastActivityUtc >= _options.IdleTimeout)
            .ToList();

        foreach (var worker in idleCandidates)
        {
            if (_workers.Count <= _options.MinThreads)
            {
                break;
            }

            worker.RetireRequested = true;
            OnLog?.Invoke($"[pool] Worker #{worker.WorkerId} scheduled for retirement due to idleness.");
        }
    }
    //-
    private void RecoverHungWorkers_Locked()
    {
        foreach (var worker in _workers.Values.ToList())
        {
            if (!worker.Busy || worker.StartedCurrentTaskUtc == null)
            {
                continue;
            }

            var busyFor = DateTime.UtcNow - worker.StartedCurrentTaskUtc.Value;
            if (busyFor < _options.WorkerHangTimeout)
            {
                continue;
            }

            _recoveredHungWorkers++;
            worker.RetireRequested = true;

            if (_workers.Count < _options.MaxThreads)
            {
                SpawnWorker_Locked($"replacing hung worker #{worker.WorkerId}, busy for {busyFor.TotalMilliseconds:F0}ms");
            }
            else
            {
                OnLog?.Invoke($"[pool] Hung worker #{worker.WorkerId} detected but no room to spawn replacement.");
            }
        }
    }
    //-
    private void SpawnWorker_Locked(string reason)
    {
        var workerId = ++_nextWorkerId;
        var thread = new Thread(() => WorkerLoop(workerId))
        {
            IsBackground = true,
            Name = $"DynamicPool-Worker-{workerId}"
        };

        _workers[workerId] = new WorkerState
        {
            WorkerId = workerId,
            Thread = thread,
            LastActivityUtc = DateTime.UtcNow
        };

        _spawnedWorkers++;
        thread.Start();
        OnLog?.Invoke($"[pool] Worker #{workerId} started ({reason}). Active workers: {_workers.Count}.");
        OnWorkerSpawned?.Invoke(workerId, reason);
    }
    //-
    private void RetireWorker_Locked(int workerId, string reason)
    {
        if (_workers.Remove(workerId))
        {
            _retiredWorkers++;
            OnLog?.Invoke($"[pool] Worker #{workerId} retired ({reason}). Active workers: {_workers.Count}.");
            OnWorkerRetired?.Invoke(workerId, reason);
        }
    }
    //-
    private void PulseAllAndPublishStats_Locked()
    {
        Monitor.PulseAll(_sync);
        PublishStatsUnsafe_Locked();
    }
    //-
    private void PublishStatsUnsafe_Locked()
    {
        OnStatsChanged?.Invoke(new PoolStats
        {
            QueueLength = _queue.Count,
            ActiveWorkers = _workers.Count,
            BusyWorkers = _workers.Values.Count(w => w.Busy),
            CompletedTasks = _completedTasks,
            FailedTasks = _failedTasks,
            SpawnedWorkers = _spawnedWorkers,
            RetiredWorkers = _retiredWorkers,
            RecoveredHungWorkers = _recoveredHungWorkers
        });
    }
}
