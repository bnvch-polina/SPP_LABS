using TestingFramework;
using ProjectForTesting.Exceptions;
using ProjectForTesting.Models;
using ProjectForTesting.Services;

namespace TestsForProject;

[Atributes.TestClass("Todo")]
public class Test
{
    private TodoService _todoService;
    private ITodoRepository _todoRepository;
    private NotificationService _notificationService;

    [Atributes.Setup]
    public void Initialize()
    {
        //setup
        _todoRepository = new InMemoryTodoRepository();
        _notificationService = new NotificationService();
        _todoService = new TodoService(_todoRepository, _notificationService);
        SharedContextStore.Set("service", _todoService);
        SharedContextStore.Set("setup-at", DateTime.UtcNow);
        //setup end
    }

    [Atributes.TearDown]
    public void Cleanup()
    {
        _todoService = null;
        _todoRepository = null;
        _notificationService = null;
    }

    [Atributes.TestMethod]
    [Atributes.SharedContext("service")]
    public void SharedContext_ContainsTodoService()
    {
        var fromContext = SharedContextStore.GetRequired<TodoService>("service");
        Asserts.IsNotNull(fromContext, "Shared context should keep TodoService instance");
    }

    [Atributes.TestMethodAttribute("Создание задачи с валидными выходными данными")]
    public void Test_CreateTodo_ValidData()
    {
        string title = "Test Title1";
        string description = "Test Description1";
        DateTime dueDate = DateTime.Now.AddDays(7);
        var todoItem = _todoService.CreateTodo(title, description, dueDate);
        Asserts.IsNotNull(todoItem, "CreateTodo вернул null");
        Asserts.AreEqual(todoItem.Title, title);
        Asserts.AreEqual(todoItem.Description, description);
        Asserts.IsGreaterThenZero(todoItem.Id);
        Asserts.AreEqual(todoItem.Status, TodoStatus.Pending);
        Asserts.AreEqual(todoItem.DueDate, dueDate);
        Asserts.IsNotNull(todoItem.CreatedAt);
    }
    
    [Atributes.TestCase("Test Title1", "Test Description1")]
    [Atributes.TestCase("Test Title2", "Test Description2")]
    [Atributes.TestCase("Test Title3", "Test Description3", "2025-04-03")]
    [Atributes.TestMethodAttribute("Создание задач с разными выходными данными")]
    public void Test_CreateTodo_DifferentData(string title, string? description = null, string? dueDateStr = null)
    {
        DateTime? dueDate = null;
        if (!string.IsNullOrEmpty(dueDateStr))
        {
            dueDate = DateTime.Parse(dueDateStr);
        }
        var todoItem = dueDate.HasValue ? 
            _todoService.CreateTodo(title, description, dueDate) : 
            _todoService.CreateTodo(title, description);
        Asserts.IsNotNull(todoItem);
        Asserts.AreEqual(todoItem.Title, title);
        Asserts.AreEqual(todoItem.Description, description);
        Asserts.IsGreaterThenZero(todoItem.Id);
        Asserts.AreEqual(todoItem.Status, TodoStatus.Pending);
        Asserts.AreEqual(todoItem.DueDate, dueDate);
        Asserts.IsNotNull(todoItem.CreatedAt);
    }

    [Atributes.TestMethodAttribute("Создание задачи с пустым названием")]
    public void Test_CreateTodo_Empty_Title()
    {
        string title = "";
        string description = "";
        DateTime dueDate = DateTime.Now.AddDays(7);
        Asserts.IsThrownException<InvalidTodoOperationException>(() =>
            _todoService.CreateTodo(title, description, dueDate));
    }

    [Atributes.TestMethodAttribute("Создание задачи с названием длиннее 100 символов")]
    public void Test_CreateTodo_TitleWithBigLenght()
    {
        string longTitle = new string('a', 101);
        string description = "Test Description1";
        Asserts.IsThrownException<InvalidTodoOperationException>(() => _todoService.CreateTodo(longTitle, description));
    }

    [Atributes.TestMethodAttribute("Завершение существующей задачи")]
    public void CompleteTodo_ExistingTodo_UpdatesStatusAndCompletedAt()
    {
        var todo = _todoService.CreateTodo("Завершить задачу", "Описание");
        var completed = _todoService.CompleteTodo(todo.Id);
        Asserts.AreEqual(TodoStatus.Completed, completed.Status);
        Asserts.IsNotNull(completed.CompletedAt);
        Asserts.IsTrue(completed.CompletedAt <= DateTime.Now);
    }

    [Atributes.TestMethodAttribute("Завершение несуществующей задачи выбрасывает исключение")]
    public void CompleteTodo_NonExistentTodo_ThrowsException()
    {
        Asserts.IsThrownException<TodoNotFoundException>(
            () => _todoService.CompleteTodo(999)
        );
    }

    [Atributes.TestMethodAttribute("Повторное завершение задачи выбрасывает исключение")]
    public void CompleteTodo_AlreadyCompletedTodo_ThrowsException()
    {
        var todo = _todoService.CreateTodo("Задача", "Описание");
        _todoService.CompleteTodo(todo.Id);
        Asserts.IsThrownException<InvalidTodoOperationException>(
            () => _todoService.CompleteTodo(todo.Id)
        );
    }
    
    [Atributes.TestMethodAttribute("Обновление статуса задачи")]
    public void UpdateStatus_ChangesStatus()
    {
        var todo = _todoService.CreateTodo("Задача", "Описание");
        var updated = _todoService.UpdateStatus(todo.Id, TodoStatus.InProgress);
        Asserts.AreEqual(TodoStatus.InProgress, updated.Status);
    }

    [Atributes.TestMethodAttribute("Обновление статуса завершенной задачи выбрасывает исключение")]
    public void UpdateStatus_CompletedTodo_ThrowsException()
    {
        var todo = _todoService.CreateTodo("Задача", "Описание");
        _todoService.CompleteTodo(todo.Id);
        Asserts.IsThrownException<InvalidTodoOperationException>(
            () => _todoService.UpdateStatus(todo.Id, TodoStatus.Pending)
        );
    }

    [Atributes.TestMethodAttribute("Обновление описания задачи")]
    public void UpdateDescription_ChangesDescription()
    {
        var todo = _todoService.CreateTodo("Задача", "Старое описание");
        string newDescription = "Новое описание";
        var updated = _todoService.UpdateDescription(todo.Id, newDescription);
        Asserts.AreEqual(newDescription, updated.Description);
    }

    [Atributes.TestMethodAttribute("Поиск задач по статусу")]
    public void GetTodosByStatus_ReturnsCorrectTodos()
    {
        _todoService.CreateTodo("Задача 1", "Описание");
        _todoService.CreateTodo("Задача 2", "Описание");
        var inProgress = _todoService.CreateTodo("Задача 3", "Описание");
        _todoService.UpdateStatus(inProgress.Id, TodoStatus.InProgress);
        var pendingTodos = _todoService.GetTodosByStatus(TodoStatus.Pending);
        var progressTodos = _todoService.GetTodosByStatus(TodoStatus.InProgress);
        Asserts.AreEqual(2, pendingTodos.Count);
        Asserts.AreEqual(1, progressTodos.Count);
        Asserts.All(pendingTodos, t => Asserts.AreEqual(TodoStatus.Pending, t.Status));
    }

    [Atributes.TestMethodAttribute("Поиск задач по ключевому слову")]
    public void SearchTodos_ByKeyword_ReturnsMatchingTodos()
    {
        _todoService.CreateTodo("Купить молоко", "Купить в магазине");
        _todoService.CreateTodo("Написать код", "Написать тесты");
        _todoService.CreateTodo("Купить хлеб", "Купить в пекарне");

        var results = _todoService.SearchTodos("купить");

        Asserts.AreEqual(2, results.Count);
        Asserts.Contains(results, t => t.Title.Contains("молоко"));
        Asserts.Contains(results, t => t.Title.Contains("хлеб"));
    }

    [Atributes.TestMethodAttribute("Поиск с пустым ключевым словом возвращает все задачи")]
    public void SearchTodos_EmptyKeyword_ReturnsAllTodos()
    {
        _todoService.CreateTodo("Задача 1", "Описание");
        _todoService.CreateTodo("Задача 2", "Описание");
        _todoService.CreateTodo("Задача 3", "Описание");
        var results = _todoService.SearchTodos("");
        Asserts.AreEqual(3, results.Count);
    }

    [Atributes.TestMethodAttribute("Получение просроченных задач")]
    public void GetOverdueTodos_ReturnsOverdueTodos()
    {
        _todoService.CreateTodo("Срочная задача", "Описание", DateTime.Now.AddDays(-1));
        _todoService.CreateTodo("Обычная задача", "Описание", DateTime.Now.AddDays(7));
        _todoService.CreateTodo("Еще просроченная", "Описание", DateTime.Now.AddDays(-2));

        var overdue = _todoService.GetOverdueTodos();

        Asserts.AreEqual(2, overdue.Count);
        Asserts.All(overdue, t => Asserts.IsTrue(t.DueDate < DateTime.Now));
    }
    
    [Atributes.TestMethodAttribute("Удаление существующей задачи")]
    public void DeleteTodo_ExistingTodo_RemovesTodo()
    {
        var todo = _todoService.CreateTodo("Удалить меня", "Описание");
        Asserts.AreEqual(1, _todoService.GetAllTodos().Count);
        _todoService.DeleteTodo(todo.Id);
        Asserts.AreEqual(0, _todoService.GetAllTodos().Count);
    }

    [Atributes.TestMethodAttribute("Удаление несуществующей задачи выбрасывает исключение")]
    public void DeleteTodo_NonExistentTodo_ThrowsException()
    {
        Asserts.IsThrownException<TodoNotFoundException>(
            () => _todoService.DeleteTodo(999)
        );
    }

    [Atributes.TestMethodAttribute("Удаление завершенной задачи выбрасывает исключение")]
    public void DeleteTodo_CompletedTodo_ThrowsException()
    {
        var todo = _todoService.CreateTodo("Задача", "Описание");
        _todoService.CompleteTodo(todo.Id);
        Asserts.IsThrownException<InvalidTodoOperationException>(
            () => _todoService.DeleteTodo(todo.Id)
        );
    }
    
    [Atributes.TestMethodAttribute("Получение статистики")]
    public void GetStatistics_ReturnsCorrectCounts()
    {
        _todoService.CreateTodo("Задача 1", "Описание");
        _todoService.CreateTodo("Задача 2", "Описание");
        var todo3 = _todoService.CreateTodo("Задача 3", "Описание");
        _todoService.UpdateStatus(todo3.Id, TodoStatus.InProgress);
        var todo4 = _todoService.CreateTodo("Задача 4", "Описание");
        _todoService.CompleteTodo(todo4.Id);
        var stats = _todoService.GetStatistics();
        Asserts.AreEqual(4, stats.total);
        Asserts.AreEqual(2, stats.pending);
        Asserts.AreEqual(1, stats.inProgress);
        Asserts.AreEqual(1, stats.completed);
    }

    [Atributes.TestMethodAttribute("Клонирование задачи")]
    public void CloneTodo_CreatesCopyWithSuffix()
    {
        var original = _todoService.CreateTodo("Оригинал", "Описание", DateTime.Now.AddDays(5));
        var cloned = _todoService.CloneTodo(original.Id);
        Asserts.AreNotEqual(original.Id, cloned.Id);
        Asserts.AreEqual("Оригинал (Copy)", cloned.Title);
        Asserts.AreEqual(original.Description, cloned.Description);
        Asserts.AreEqual(original.DueDate, cloned.DueDate);
    }

    [Atributes.TestMethodAttribute("Очистка завершенных задач")]
    public void CleanupCompletedTodos_RemovesAllCompleted()
    {
        _todoService.CreateTodo("Задача 1", "Описание");
        var todo2 = _todoService.CreateTodo("Задача 2", "Описание");
        _todoService.CompleteTodo(todo2.Id);
        var todo3 = _todoService.CreateTodo("Задача 3", "Описание");
        _todoService.CompleteTodo(todo3.Id);

        Asserts.AreEqual(3, _todoService.GetAllTodos().Count);
        int deletedCount = _todoService.CleanupCompletedTodos();
        Asserts.AreEqual(2, deletedCount);
        Asserts.AreEqual(1, _todoService.GetAllTodos().Count);
    }

    [Atributes.TestMethodAttribute("Перенос срока выполнения на прошедшую дату делает задачу просроченной")]
    public void RescheduleDueDate_ToPastDate_MarksOverdue()
    {
        var todo = _todoService.CreateTodo("Задача", "Описание", DateTime.Now.AddDays(7));
        var updated = _todoService.RescheduleDueDate(todo.Id, DateTime.Now.AddDays(-1));
        Asserts.AreEqual(TodoStatus.Overdue, updated.Status);
        Asserts.IsTrue(updated.DueDate < DateTime.Now);
    }

    [Atributes.TestMethodAttribute("Асинхронное создание задачи")]
    public async Task CreateTodoAsync_ValidData_ReturnsPendingTodo()
    {
        var todo = await _todoService.CreateTodoAsync("Async task", "description");
        Asserts.IsNotNull(todo);
        Asserts.AreEqual("Async task", todo.Title);
        Asserts.AreEqual(TodoStatus.Pending, todo.Status);
    }

    [Atributes.TestMethodAttribute("Асинхронное завершение задачи")]
    public async Task CompleteTodoAsync_ExistingTodo_UpdatesCompletionFields()
    {
        var todo = _todoService.CreateTodo("Complete async", "description");
        var completed = await _todoService.CompleteTodoAsync(todo.Id);
        Asserts.AreEqual(TodoStatus.Completed, completed.Status);
        Asserts.IsNotNull(completed.CompletedAt);
    }
}