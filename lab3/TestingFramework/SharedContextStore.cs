namespace TestingFramework;

public static class SharedContextStore
{
    private static readonly Dictionary<string, object> Context = new();

    public static void Set(string key, object value)
    {
        Context[key] = value;
    }

    public static bool TryGet<T>(string key, out T? value)
    {
        if (Context.TryGetValue(key, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    public static T GetRequired<T>(string key)
    {
        if (!TryGet<T>(key, out var value) || value == null)
        {
            throw new KeyNotFoundException($"Shared context key '{key}' was not found.");
        }

        return value;
    }

    public static void Clear()
    {
        Context.Clear();
    }
}
