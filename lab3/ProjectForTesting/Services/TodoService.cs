using ProjectForTesting.Exceptions;
using ProjectForTesting.Models;

namespace ProjectForTesting.Services;

public class TodoService
{
        private readonly ITodoRepository _repository;
        private readonly NotificationService _notificationService;
        
        public TodoService(ITodoRepository repository, NotificationService notificationService)
        {
            _repository = repository;
            _notificationService = notificationService;
        }
        
        // Создание задачи
        public TodoItem CreateTodo(string title, string description = null, DateTime? dueDate = null)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new InvalidTodoOperationException("Title cannot be empty");
            
            if (title.Length > 100)
                throw new InvalidTodoOperationException("Title cannot exceed 100 characters");
            
            var todo = new TodoItem
            {
                Id = _repository.GetNextId(),
                Title = title.Trim(),
                Description = description,
                Status = TodoStatus.Pending,
                CreatedAt = DateTime.Now,
                DueDate = dueDate
            };
            
            _repository.Add(todo);
            _notificationService.NotifyAsync($"Task created: {title}").Wait();
            
            return todo;
        }
        
        // Асинхронное создание
        public async Task<TodoItem> CreateTodoAsync(string title, string description = null, DateTime? dueDate = null)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new InvalidTodoOperationException("Title cannot be empty");
            
            if (title.Length > 100)
                throw new InvalidTodoOperationException("Title cannot exceed 100 characters");
            
            var todo = new TodoItem
            {
                Id = _repository.GetNextId(),
                Title = title.Trim(),
                Description = description,
                Status = TodoStatus.Pending,
                CreatedAt = DateTime.Now,
                DueDate = dueDate
            };
            
            await _repository.AddAsync(todo);
            await _notificationService.NotifyAsync($"Task created: {title}");
            
            return todo;
        }
        
        // Завершение задачи
        public TodoItem CompleteTodo(int id)
        {
            var todo = _repository.GetById(id);
            if (todo == null)
                throw new TodoNotFoundException($"Todo with id {id} not found");
            
            if (todo.Status == TodoStatus.Completed)
                throw new InvalidTodoOperationException("Todo already completed");
            
            todo.Status = TodoStatus.Completed;
            todo.CompletedAt = DateTime.Now;
            _repository.Update(todo);
            
            _notificationService.NotifyAsync($"Task completed: {todo.Title}").Wait();
            
            return todo;
        }
        
        // Асинхронное завершение
        public async Task<TodoItem> CompleteTodoAsync(int id)
        {
            var todo = _repository.GetById(id);
            if (todo == null)
                throw new TodoNotFoundException($"Todo with id {id} not found");
            
            if (todo.Status == TodoStatus.Completed)
                throw new InvalidTodoOperationException("Todo already completed");
            
            todo.Status = TodoStatus.Completed;
            todo.CompletedAt = DateTime.Now;
            await _repository.UpdateAsync(todo);
            await _notificationService.NotifyAsync($"Task completed: {todo.Title}");
            
            return todo;
        }
        
        // Обновление статуса
        public TodoItem UpdateStatus(int id, TodoStatus newStatus)
        {
            var todo = _repository.GetById(id);
            if (todo == null)
                throw new TodoNotFoundException($"Todo with id {id} not found");
            
            if (todo.Status == TodoStatus.Completed && newStatus != TodoStatus.Completed)
                throw new InvalidTodoOperationException("Cannot change status of completed todo");
            
            todo.Status = newStatus;
            _repository.Update(todo);
            
            if (newStatus == TodoStatus.Completed)
            {
                todo.CompletedAt = DateTime.Now;
                _notificationService.NotifyAsync($"Task completed: {todo.Title}").Wait();
            }
            
            return todo;
        }
        
        // Получение по статусу
        public List<TodoItem> GetTodosByStatus(TodoStatus status)
        {
            return _repository.GetAll()
                .Where(t => t.Status == status)
                .OrderBy(t => t.CreatedAt)
                .ToList();
        }
        
        // Поиск по ключевому слову
        public List<TodoItem> SearchTodos(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return _repository.GetAll();
            
            return _repository.GetAll()
                .Where(t => t.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                           (t.Description != null && t.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(t => t.CreatedAt)
                .ToList();
        }
        
        // Получение просроченных задач
        public List<TodoItem> GetOverdueTodos()
        {
            return _repository.GetAll()
                .Where(t => t.DueDate.HasValue && 
                           t.DueDate.Value.Date < DateTime.Now.Date && 
                           t.Status != TodoStatus.Completed)
                .ToList();
        }
        
        // Удаление задачи
        public void DeleteTodo(int id)
        {
            var todo = _repository.GetById(id);
            if (todo == null)
                throw new TodoNotFoundException($"Todo with id {id} not found");
            
            if (todo.Status == TodoStatus.Completed)
                throw new InvalidTodoOperationException("Cannot delete completed todo");
            
            _repository.Delete(id);
            _notificationService.NotifyAsync($"Task deleted: {todo.Title}").Wait();
        }
        
        // Асинхронное удаление
        public async Task DeleteTodoAsync(int id)
        {
            var todo = _repository.GetById(id);
            if (todo == null)
                throw new TodoNotFoundException($"Todo with id {id} not found");
            
            if (todo.Status == TodoStatus.Completed)
                throw new InvalidTodoOperationException("Cannot delete completed todo");
            
            _repository.Delete(id);
            await _notificationService.NotifyAsync($"Task deleted: {todo.Title}");
        }
        
        // Получение всех задач
        public List<TodoItem> GetAllTodos()
        {
            return _repository.GetAll().OrderBy(t => t.CreatedAt).ToList();
        }
        
        // Получение статистики
        public (int total, int pending, int inProgress, int completed, int overdue) GetStatistics()
        {
            var all = _repository.GetAll();
            return (
                total: all.Count,
                pending: all.Count(t => t.Status == TodoStatus.Pending),
                inProgress: all.Count(t => t.Status == TodoStatus.InProgress),
                completed: all.Count(t => t.Status == TodoStatus.Completed),
                overdue: GetOverdueTodos().Count
            );
        }
        
        // Массовое завершение задач
        public async Task<int> BulkCompleteAsync(List<int> ids)
        {
            int completedCount = 0;
            
            foreach (var id in ids)
            {
                try
                {
                    await CompleteTodoAsync(id);
                    completedCount++;
                }
                catch (TodoNotFoundException)
                {
                    // пропускаем несуществующие
                }
                catch (InvalidTodoOperationException)
                {
                    // пропускаем уже завершенные
                }
            }
            
            return completedCount;
        }
        
        // Обновление описания
        public TodoItem UpdateDescription(int id, string newDescription)
        {
            var todo = _repository.GetById(id);
            if (todo == null)
                throw new TodoNotFoundException($"Todo with id {id} not found");
            
            todo.Description = newDescription;
            _repository.Update(todo);
            
            return todo;
        }
        
        // Перенос срока выполнения
        public TodoItem RescheduleDueDate(int id, DateTime newDueDate)
        {
            var todo = _repository.GetById(id);
            if (todo == null)
                throw new TodoNotFoundException($"Todo with id {id} not found");
            
            if (todo.Status == TodoStatus.Completed)
                throw new InvalidTodoOperationException("Cannot reschedule completed todo");
            
            todo.DueDate = newDueDate;
            
            if (newDueDate.Date < DateTime.Now.Date)
            {
                todo.Status = TodoStatus.Overdue;
            }
            
            _repository.Update(todo);
            
            return todo;
        }
        
        // Копирование задачи
        public TodoItem CloneTodo(int id)
        {
            var original = _repository.GetById(id);
            if (original == null)
                throw new TodoNotFoundException($"Todo with id {id} not found");
            
            return CreateTodo(
                $"{original.Title} (Copy)",
                original.Description,
                original.DueDate
            );
        }
        
        // Очистка завершенных задач
        public int CleanupCompletedTodos()
        {
            var completed = _repository.GetAll()
                .Where(t => t.Status == TodoStatus.Completed)
                .ToList();
            
            foreach (var todo in completed)
            {
                _repository.Delete(todo.Id);
            }
            
            return completed.Count;
        }
}