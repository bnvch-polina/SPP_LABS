using ProjectForTesting.Exceptions;
using ProjectForTesting.Models;
using ProjectForTesting.Services;
using TestingFramework;

namespace TestsForProject;

[Atributes.TestClass("FilterDemo")]
public class FilterDemoTests
{
    private TodoService _todoService = null!;
    private ITodoRepository _todoRepository = null!;
    private NotificationService _notificationService = null!;

    [Atributes.Setup]
    public void Initialize()
    {
        _todoRepository = new InMemoryTodoRepository();
        _notificationService = new NotificationService();
        _todoService = new TodoService(_todoRepository, _notificationService);

        SharedContextStore.Set("service", _todoService);
    }

    [Atributes.TearDown]
    public void Cleanup()
    {
        _todoService = null!;
        _todoRepository = null!;
        _notificationService = null!;
    }

    [Atributes.TestMethod("Todo creation - test cases via yield return")]
    [Atributes.TestMeta(category: "FilterDemo", priority: 3, author: "Alice")]
    [Atributes.TestCaseSource(nameof(GeneratedTodoCases))]
    public void CreateTodo_YieldReturnCases(string title, string description)
    {
        var todoItem = _todoService.CreateTodo(title, description);

        Asserts.IsNotNull(todoItem);
        Asserts.AreEqual(title.Trim(), todoItem.Title);
        Asserts.AreEqual(description, todoItem.Description);
        Asserts.AreEqual(TodoStatus.Pending, todoItem.Status);
        Asserts.IsNotNull(todoItem.CreatedAt);
    }

    private static System.Collections.Generic.IEnumerable<object[]> GeneratedTodoCases()
    {
        // Генерация кейсов с yield return — требование ЛР4.
        for (int i = 1; i <= 25; i++)
        {
            yield return new object[]
            {
                $"FilterDemo Todo {i}",
                $"FilterDemo Description {i}"
            };
        }
    }

    [Atributes.TestMethod("Expression tree debug output (intentional failure)")]
    [Atributes.TestMeta(category: "FilterDemo", priority: 3, author: "Alice")]
    public void ExpressionTreeAssert_ShowsDetailedDebug_OnFailure()
    {
        int x = 5;
        int y = 6;

        // intentionally false: тест должен упасть и показать подробный разбор expression tree.
        Asserts.That(() => x == y, "Demo: expression tree debug should describe operand values and operator.");
    }

    [Atributes.TestMethod("Should be excluded by priority/author filter")]
    [Atributes.TestMeta(category: "FilterDemo", priority: 1, author: "Bob")]
    public void ExcludedByFilter_Demo()
    {
        Asserts.IsTrue(true);
    }
}

