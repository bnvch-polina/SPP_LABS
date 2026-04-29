namespace TestingFramework.Exeptions;

public class AssertExpression: Exception
{
    public AssertExpression(string message): base(message)
    {
        
    }
}