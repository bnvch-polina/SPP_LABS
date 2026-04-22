namespace TestingFramework;

public class Atributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class TestClassAttribute : Attribute
    {
        public string? Category { get; }

        public TestClassAttribute()
        {
        }

        public TestClassAttribute(string category)
        {
            Category = category;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class TestMethodAttribute : Attribute
    {
        public string Description { get; }

        public TestMethodAttribute()
        {
            Description = "Test without description";
        }

        public TestMethodAttribute(string description)
        {
            Description = description;
        }
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class SetupAttribute : Attribute{}
    [AttributeUsage(AttributeTargets.Method)]
    public class TearDownAttribute : Attribute{}

    [AttributeUsage(AttributeTargets.Method)]
    public class TimeoutAttribute : Attribute
    {
        public int Milliseconds { get; }

        public TimeoutAttribute(int milliseconds)
        {
            if (milliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(milliseconds), "Timeout must be greater than zero.");
            }

            Milliseconds = milliseconds;
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class SharedContextAttribute : Attribute
    {
        public string Key { get; }

        public SharedContextAttribute(string key)
        {
            Key = key;
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class TestCaseAttribute(params object[] parameters) : Attribute
    {
        public object[] Parameters = parameters;
    }
}