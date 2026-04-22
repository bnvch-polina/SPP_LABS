namespace ProjectForTesting.Exceptions;

public class InvalidTodoOperationException : Exception
{
    public InvalidTodoOperationException() : base("Invalid todo operation") { }
        
    public InvalidTodoOperationException(string message) : base(message) { }
        
    public InvalidTodoOperationException(string message, Exception inner) : base(message, inner) { }
}