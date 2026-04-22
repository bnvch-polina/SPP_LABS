using ProjectForTesting.Models;

namespace ProjectForTesting.Services;

public interface ITodoRepository
{
    TodoItem GetById(int id);
    List<TodoItem> GetAll();
    void Add(TodoItem item);
    void Update(TodoItem item);
    void Delete(int id);
    Task AddAsync(TodoItem item);
    Task UpdateAsync(TodoItem item);
    int GetNextId();
}