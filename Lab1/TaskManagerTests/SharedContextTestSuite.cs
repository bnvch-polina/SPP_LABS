using TaskManagement;
using lab1_test_framework;

namespace TaskManagerTests;

public class SharedContextTestSuite
{
    
        private TaskManager _taskManager = null!;

        [TestAttributes.Setup]
        public void Init()
        {
            _taskManager = new TaskManager();
        }

        [TestAttributes.Teardown]
        public void Clean()
        {
            _taskManager = null!;
        }

        [TestAttributes.SharedContext(ContextId = "WORKFLOW_001", Order = 1)]
        [TestAttributes.TestMethod(DisplayName = "Шаг 1: Создание рабочих задач")]
        public void Step1_CreateWorkTasks()
        {
            // Создаем несколько рабочих задач
            var task1 = _taskManager.CreateTask("Подготовить презентацию", "Работа", 4);
            var task2 = _taskManager.CreateTask("Отправить отчет", "Работа", 3);
            
            Assertions.IsNotNull(task1);
            Assertions.IsNotNull(task2);
            
            var stats = _taskManager.GetStatistics();
            Assertions.AreEqual(2, stats.TasksByCategory["Работа"]);
        }

        [TestAttributes.SharedContext(ContextId = "WORKFLOW_001", Order = 2)]
        [TestAttributes.TestMethod(DisplayName = "Шаг 2: Выполнение задач")]
        public void Step2_CompleteSomeTasks()
        {
            var workTasks = _taskManager.GetTasksByCategory("Работа");
            
            // Завершаем первую задачу
            bool completed = _taskManager.CompleteTask(workTasks[0].Id);
            
            Assertions.IsTrue(completed);
            Assertions.AreEqual(1, _taskManager.TotalCompletedTasks);
        }

        [TestAttributes.SharedContext(ContextId = "WORKFLOW_001", Order = 3)]
        [TestAttributes.TestMethod(DisplayName = "Шаг 3: Проверка статистики после операций")]
        public void Step3_VerifyFinalState()
        {
            var stats = _taskManager.GetStatistics();
            
            Assertions.AreEqual(2, stats.TotalTasks);
            Assertions.AreEqual(1, stats.CompletedTasks);
            Assertions.AreEqual(1, stats.ActiveTasks);
            Assertions.AreEqual(2, stats.TasksByCategory["Работа"]);
        }

        [TestAttributes.SharedContext(ContextId = "PRIORITY_TEST", Order = 1)]
        [TestAttributes.TestMethod(DisplayName = "Создание задач с разным приоритетом")]
        public void PriorityTest_Step1_CreateTasks()
        {
            _taskManager.CreateTask("Низкий приоритет", "Личное", 1);
            _taskManager.CreateTask("Средний приоритет", "Личное", 3);
            _taskManager.CreateTask("Высокий приоритет", "Личное", 5);
            
            var tasks = _taskManager.GetTasksByCategory("Личное");
            
            Assertions.CollectionCount(tasks, 3);
            Assertions.AreEqual(5, tasks[0].Priority); // Должны быть отсортированы по приоритету
            Assertions.AreEqual(3, tasks[1].Priority);
            Assertions.AreEqual(1, tasks[2].Priority);
        }

        [TestAttributes.SharedContext(ContextId = "PRIORITY_TEST", Order = 2)]
        [TestAttributes.TestMethod(DisplayName = "Проверка сортировки по приоритету")]
        public void PriorityTest_Step2_AddNewHighPriority()
        {
            _taskManager.CreateTask("Сверхважная задача", "Личное", 5);
            
            var tasks = _taskManager.GetTasksByCategory("Личное");
            
            Assertions.CollectionCount(tasks, 4);
            
            // Проверяем, что порядок сортировки сохраняется
            for (int i = 0; i < tasks.Count - 1; i++)
            {
                Assertions.IsTrue(tasks[i].Priority >= tasks[i + 1].Priority);
            }
        }
}