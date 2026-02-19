namespace TaskManagement
{
    public class TaskManager
    {
        private readonly Dictionary<int, TaskItem> _tasks = new();
        private readonly Dictionary<string, int> _categoryLimits = new();
        private int _nextId = 1;
        private readonly object _lock = new object();

        public int TotalCompletedTasks { get; private set; }
        public int ActiveTaskCount => _tasks.Count(t => !t.Value.IsCompleted);
        
        public TaskManager()
        {
            // Категории и лимиты задач по умолчанию
            _categoryLimits["Работа"] = 10;
            _categoryLimits["Личное"] = 5;
            _categoryLimits["Обучение"] = 3;
        }

        public TaskItem CreateTask(string title, string category, int priority = 1)
        {
            ValidateTaskInput(title, category, priority);

            lock (_lock)
            {
                if (_categoryLimits.TryGetValue(category, out int limit))
                {
                    var categoryTaskCount = _tasks.Values.Count(t => t.Category == category && !t.IsCompleted);
                    if (categoryTaskCount >= limit)
                        throw new TaskLimitExceededException($"Достигнут лимит задач в категории {category}: {limit}");
                }

                var task = new TaskItem
                {
                    Id = _nextId++,
                    Title = title,
                    Category = category,
                    Priority = priority,
                    CreatedAt = DateTime.UtcNow,
                    IsCompleted = false
                };

                _tasks[task.Id] = task;
                return task;
            }
        }

        private void ValidateTaskInput(string title, string category, int priority)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new TaskValidationException("Название задачи не может быть пустым");
            
            if (title.Length > 200)
                throw new TaskValidationException("Название задачи слишком длинное (макс. 200 символов)");
            
            if (string.IsNullOrWhiteSpace(category))
                throw new TaskValidationException("Категория не может быть пустой");
            
            if (priority < 1 || priority > 5)
                throw new TaskValidationException("Приоритет должен быть от 1 до 5");
        }

        public bool CompleteTask(int taskId)
        {
            lock (_lock)
            {
                if (!_tasks.TryGetValue(taskId, out var task))
                    return false;

                if (!task.IsCompleted)
                {
                    task.IsCompleted = true;
                    task.CompletedAt = DateTime.UtcNow;
                    TotalCompletedTasks++;
                    return true;
                }
                
                return false;
            }
        }

        public List<TaskItem> GetTasksByCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                throw new TaskValidationException("Категория не может быть пустой");

            return _tasks.Values
                .Where(t => t.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.CreatedAt)
                .ToList();
        }

        public async Task<List<TaskItem>> SearchTasksAsync(string searchTerm)
        {
            await Task.Delay(50); // Имитация асинхронной операции
            
            if (string.IsNullOrWhiteSpace(searchTerm))
                return new List<TaskItem>();

            return _tasks.Values
                .Where(t => t.Title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.CreatedAt)
                .ToList();
        }

        public TaskStatistics GetStatistics()
        {
            return new TaskStatistics
            {
                TotalTasks = _tasks.Count,
                CompletedTasks = TotalCompletedTasks,
                ActiveTasks = ActiveTaskCount,
                TasksByCategory = _tasks.Values
                    .GroupBy(t => t.Category)
                    .ToDictionary(g => g.Key, g => g.Count()),
                AveragePriority = _tasks.Values.Any() ? _tasks.Values.Average(t => t.Priority) : 0
            };
        }

        public bool DeleteTask(int taskId)
        {
            lock (_lock)
            {
                if (!_tasks.TryGetValue(taskId, out var task))
                    return false;
                
                if (!task.IsCompleted)
                {
                    return false; // Нельзя удалить активную задачу
                }

                return _tasks.Remove(taskId);
            }
        }

        public void SetCategoryLimit(string category, int limit)
        {
            if (limit < 1 || limit > 100)
                throw new TaskValidationException("Лимит должен быть от 1 до 100");
            
            _categoryLimits[category] = limit;
        }
    }

    public class TaskItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int Priority { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool IsCompleted { get; set; }
        public TimeSpan? CompletionTime => CompletedAt.HasValue ? CompletedAt - CreatedAt : null;
    }

    public class TaskStatistics
    {
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int ActiveTasks { get; set; }
        public Dictionary<string, int> TasksByCategory { get; set; } = new();
        public double AveragePriority { get; set; }
    }

    // Кастомные исключения
    public class TaskValidationException : Exception
    {
        public string ErrorCode { get; } = "VALIDATION_ERROR";
        
        public TaskValidationException(string message) : base(message) { }
    }

    public class TaskLimitExceededException : Exception
    {
        public string ErrorCode { get; } = "LIMIT_EXCEEDED";
        
        public TaskLimitExceededException(string message) : base(message) { }
    }

    public class TaskNotFoundException : Exception
    {
        public int TaskId { get; }
        public string ErrorCode { get; } = "NOT_FOUND";
        
        public TaskNotFoundException(int taskId) 
            : base($"Задача с ID {taskId} не найдена")
        {
            TaskId = taskId;
        }
    }
}
