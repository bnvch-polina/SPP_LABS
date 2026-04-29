using ProjectForTesting.Models;

namespace ProjectForTesting.Services;

    public class InMemoryTodoRepository : ITodoRepository
    {
        private readonly Dictionary<int, TodoItem> _items = new();
        private int _nextId = 1;
        
        public TodoItem GetById(int id)
        {
            _items.TryGetValue(id, out var item);
            return item;
        }
        
        public List<TodoItem> GetAll()
        {
            return _items.Values.ToList();
        }
        
        public void Add(TodoItem item)
        {
            if (item.Id == 0)
            {
                item.Id = GetNextId();
            }
            _items[item.Id] = item;
        }
        
        public void Update(TodoItem item)
        {
            if (_items.ContainsKey(item.Id))
            {
                _items[item.Id] = item;
            }
        }
        
        public void Delete(int id)
        {
            _items.Remove(id);
        }
        
        public async Task AddAsync(TodoItem item)
        {
            await Task.Delay(10); // имитация асинхронной операции
            Add(item);
        }
        
        public async Task UpdateAsync(TodoItem item)
        {
            await Task.Delay(10);
            Update(item);
        }
        
        public int GetNextId()
        {
            return _nextId++;
        }
    }