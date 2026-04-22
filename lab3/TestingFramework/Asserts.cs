using TestingFramework.Exeptions;

namespace TestingFramework;

public class Asserts
{
    public static void AreEqual(object expected, object actual, string message = "")
    {
        if (!Equals(expected, actual))
        {
            throw new AssertExpression($"{message} - Expected: {expected} Actual: {actual}\n");
        }
    }

    public static void IsNotNull(object obj, string? message = null)
    {
        if (obj == null)
        {
            throw new AssertExpression(message ?? "Expected non-null value");
        }
    }

    public static void IsGreaterThenZero(int value, string? message = null)
    {
        if (value <= 0)
        {
            throw new AssertExpression(message ?? "Expected greater than zero");
        }
    }

    public static TException IsThrownException<TException>(Action action, string? message = null) where TException  : Exception
    {
        try
        {
            action();
            throw new AssertExpression(message ?? "Expected exception");
        }
        catch(TException  ex)
        {
            return ex;
        }
    }

    public static void IsTrue(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new AssertExpression(message ?? "Expected true");
        }
    }
    
    public static void AreNotEqual(object expected, object actual)
    {
        if (Equals(expected, actual))
        {
            throw new AssertExpression(
                $"Assert.AreNotEqual failed. Expected: {expected}, Actual: {actual}"
            );
        }
    }

    //do all elements satisfy to condition
    public static void All<T>(IEnumerable<T> collection, Action<T> action, string? message = null)
    {
        var failedItems = new List<(T item, int index, string message)>();
        int index = 0;
        foreach (var item in collection)
        {
            try
            {
                action(item);
            }
            catch (AssertExpression ex)
            {
                failedItems.Add((item, index, ex.Message));
            }
            catch (Exception ex)
            {
                failedItems.Add((item, index, $"Unexpected error: {ex.Message}"));
            }
            index++;
        }

        if (failedItems.Count > 0)
        {
            string errorMessage = "Expected all items to satisfy condition, but:";
            int itemsToShow = Math.Min(failedItems.Count, 10);
            for (int i = 0; i < itemsToShow; i++)
            {
                errorMessage += $"  {failedItems[i]}\n";
            }
            throw new AssertExpression(errorMessage);
        }
    }

    //does any element satisfy to condition 
    public static void Contains<T>(IEnumerable<T> collection, Func<T, bool> predicate, string? message = null)
    {
        var matchingItems = collection.Where(predicate).ToList();
        if (!matchingItems.Any())
        {
            throw new AssertExpression(message ?? "Expected mathing with at least one item");
        }
    }
}