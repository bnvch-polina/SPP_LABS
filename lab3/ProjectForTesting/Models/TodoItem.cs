namespace ProjectForTesting.Models;

public class TodoItem
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public TodoStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? DueDate { get; set; }
        
    public TodoItem()
    {
        CreatedAt = DateTime.Now;
        Status = TodoStatus.Pending;
    }
        
    public override string ToString()
    {
        return $"[{Id}] {Title} - {Status}";
    }
}