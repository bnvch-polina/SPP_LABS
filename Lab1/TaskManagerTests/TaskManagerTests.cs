using TaskManagement;
using lab1_test_framework;

namespace TaskManagerTests;

public class TaskManagerTests
{
    [TestAttributes.TestSuite(Description = "Набор тестов для менеджера задач")]
    public class TaskManagerTestSuite
    {
        private TaskManager _taskManager = null!;
        private List<TaskItem> _createdTasks = new();

        [TestAttributes.Setup]
        public void Initialize()
        {
            _taskManager = new TaskManager();
            _createdTasks.Clear();
        }

        [TestAttributes.Teardown]
        public void Cleanup()
        {
            _taskManager = null!;
        }

        [TestAttributes.TestMethod(DisplayName = "Создание обычной задачи", Category = "Creation")]
        public void CreateSimpleTask_ShouldSucceed()
        {
            // Arrange
            string title = "Написать отчет";
            string category = "Работа";

            // Act
            var task = _taskManager.CreateTask(title, category);

            // Assert
            Assertions.IsNotNull(task);
            Assertions.AreEqual(title, task.Title);
            Assertions.AreEqual(category, task.Category);
            Assertions.AreEqual(1, task.Priority); // Приоритет по умолчанию
            Assertions.IsFalse(task.IsCompleted);
            _createdTasks.Add(task);
        }

        [TestAttributes.TestMethod(DisplayName = "Создание задачи с кастомным приоритетом", Category = "Creation")]
        public void CreateTaskWithPriority_ShouldSetPriority()
        {
            // Arrange
            string title = "Срочная задача";
            string category = "Работа";
            int priority = 5;

            // Act
            var task = _taskManager.CreateTask(title, category, priority);

            // Assert
            Assertions.IsNotNull(task);
            Assertions.AreEqual(priority, task.Priority);
            _createdTasks.Add(task);
        }

        [TestAttributes.TestMethod(DisplayName = "Проверка валидации пустого названия", Category = "Validation")]
        public void CreateTask_WithEmptyTitle_ShouldThrowException()
        {
            // Arrange
            string emptyTitle = "";
            string category = "Работа";

            // Act & Assert
            Assertions.Throws<TaskValidationException>(() =>
                _taskManager.CreateTask(emptyTitle, category));
        }

        [TestAttributes.TestMethod(DisplayName = "Проверка лимита задач в категории", Category = "Limits")]
        public void CreateTask_ExceedCategoryLimit_ShouldThrowException()
        {
            // Arrange
            string category = "Работа";

            // Создаем 10 задач (лимит для работы = 10)
            for (int i = 0; i < 10; i++)
            {
                var task = _taskManager.CreateTask($"Задача {i + 1}", category);
                _createdTasks.Add(task);
            }

            // Act & Assert
            Assertions.Throws<TaskLimitExceededException>(() =>
                _taskManager.CreateTask("Превышающая задача", category));
        }

        [TestAttributes.TestMethod(DisplayName = "Завершение существующей задачи", Category = "Completion")]
        public void CompleteTask_ExistingTask_ShouldMarkAsCompleted()
        {
            // Arrange
            var task = _taskManager.CreateTask("Тестовая задача", "Личное");
            _createdTasks.Add(task);

            // Act
            bool result = _taskManager.CompleteTask(task.Id);

            // Assert
            Assertions.IsTrue(result);
            Assertions.IsTrue(task.IsCompleted);
            Assertions.IsNotNull(task.CompletedAt);
            Assertions.AreEqual(1, _taskManager.TotalCompletedTasks);
        }

        [TestAttributes.TestMethod(DisplayName = "Завершение несуществующей задачи", Category = "Completion")]
        public void CompleteTask_NonExistingTask_ShouldReturnFalse()
        {
            // Act
            bool result = _taskManager.CompleteTask(999);

            // Assert
            Assertions.IsFalse(result);
        }

        [TestAttributes.TestMethod(DisplayName = "Асинхронный поиск задач", Category = "Async")]
        public async Task SearchTasksAsync_WithValidTerm_ShouldReturnMatches()
        {
            // Arrange
            _taskManager.CreateTask("Купить продукты", "Личное");
            _taskManager.CreateTask("Купить книгу", "Обучение");
            _taskManager.CreateTask("Сходить в спортзал", "Личное");

            // Act
            var results = await _taskManager.SearchTasksAsync("купить");

            // Assert
            Assertions.IsNotNull(results);
            Assertions.CollectionCount(results, 2);
            Assertions.IsTrue(results.All(t => t.Title.Contains("Купить")));
        }

        [TestAttributes.TestMethod(DisplayName = "Проверка статистики", Category = "Statistics")]
        public void GetStatistics_AfterOperations_ShouldReturnCorrectData()
        {
            // Arrange
            _taskManager.CreateTask("Задача 1", "Работа", 3);
            _taskManager.CreateTask("Задача 2", "Работа", 2);
            _taskManager.CreateTask("Задача 3", "Личное", 1);

            var tasks = _taskManager.GetTasksByCategory("Работа");
            _taskManager.CompleteTask(tasks[0].Id);

            // Act
            var stats = _taskManager.GetStatistics();

            // Assert
            Assertions.AreEqual(3, stats.TotalTasks);
            Assertions.AreEqual(1, stats.CompletedTasks);
            Assertions.AreEqual(2, stats.ActiveTasks);
            Assertions.AreEqual(2, stats.TasksByCategory["Работа"]);
            Assertions.AreEqual(1, stats.TasksByCategory["Личное"]);
            Assertions.IsGreaterThan(stats.AveragePriority, 1.9);
        }

        [TestAttributes.TestMethod(DisplayName = "Удаление завершенной задачи", Category = "Deletion")]
        public void DeleteTask_CompletedTask_ShouldSucceed()
        {
            // Arrange
            var task = _taskManager.CreateTask("Удаляемая задача", "Личное");
            _taskManager.CompleteTask(task.Id);

            // Act
            bool result = _taskManager.DeleteTask(task.Id);

            // Assert
            Assertions.IsTrue(result);

            // Проверяем, что задача действительно удалена
            var stats = _taskManager.GetStatistics();
            Assertions.AreEqual(0, stats.TotalTasks);
        }

        [TestAttributes.TestMethod(DisplayName = "Удаление активной задачи", Category = "Deletion")]
        public void DeleteTask_ActiveTask_ShouldReturnFalse()
        {
            // Arrange
            var task = _taskManager.CreateTask("Активная задача", "Личное");

            // Act
            bool result = _taskManager.DeleteTask(task.Id);

            // Assert
            Assertions.IsFalse(result);

            // Задача должна остаться
            var stats = _taskManager.GetStatistics();
            Assertions.AreEqual(1, stats.TotalTasks);
        }

        [TestAttributes.TestCase("Работа", 10)]
        [TestAttributes.TestCase("Личное", 5)]
        [TestAttributes.TestCase("Обучение", 3)]
        [TestAttributes.TestMethod(DisplayName = "Проверка лимитов категорий", Category = "Limits")]
        public void CategoryLimits_ShouldBeConfiguredCorrectly(string category, int expectedLimit)
        {
            // Act
            // Пытаемся создать задач больше лимита
            var tasks = new List<TaskItem>();

            for (int i = 0; i < expectedLimit; i++)
            {
                var task = _taskManager.CreateTask($"Задача {i + 1}", category);
                tasks.Add(task);
            }

            // Assert - должны успешно создаться
            Assertions.CollectionCount(tasks, expectedLimit);

            // Попытка создать еще одну должна провалиться
            Assertions.Throws<TaskLimitExceededException>(() =>
                _taskManager.CreateTask("Превышение", category));
        }
    }
}