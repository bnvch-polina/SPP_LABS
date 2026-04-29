using TestingFramework.Exeptions;
using System.Linq.Expressions;
using System.Text;

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

    // Новый метод для проверки boolean-expression с подробным разбором дерева при падении.
    // Пример: Asserts.That(() => x == y);
    public static void That(Expression<Func<bool>> condition, string? message = null)
    {
        if (condition == null) throw new ArgumentNullException(nameof(condition));

        bool result;
        try
        {
            // В рамках лабораторной считаем, что expression не параметризован.
            result = condition.Compile().Invoke();
        }
        catch (Exception ex)
        {
            throw new AssertExpression(
                (message != null ? message + "\n" : string.Empty) +
                $"Failed to evaluate assertion expression: {ex.GetType().Name}: {ex.Message}\nExpression: {condition}"
            );
        }

        if (result)
        {
            return;
        }

        var details = ExpressionDebugView.Render(condition.Body, maxDepth: 8);
        throw new AssertExpression(
            (message ?? "Assertion failed") +
            $"\nExpression: {condition}\nDetails:\n{details}"
        );
    }

    private static class ExpressionDebugView
    {
        public static string Render(Expression expression, int maxDepth)
        {
            var sb = new StringBuilder();
            AppendNode(sb, expression, depth: 0, maxDepth);
            return sb.ToString();
        }

        private static void AppendNode(StringBuilder sb, Expression expression, int depth, int maxDepth)
        {
            if (depth > maxDepth)
            {
                sb.AppendLine($"{Indent(depth)}... (max depth reached)");
                return;
            }

            var value = TryEvaluate(expression);
            sb.AppendLine($"{Indent(depth)}- {expression.NodeType}: {expression} => {FormatValue(value)}");

            switch (expression)
            {
                case BinaryExpression be:
                    sb.AppendLine($"{Indent(depth)}  operator: {BinaryOperatorText(be.NodeType)}");
                    sb.AppendLine($"{Indent(depth)}  left  => {FormatValue(TryEvaluate(be.Left))}");
                    sb.AppendLine($"{Indent(depth)}  right => {FormatValue(TryEvaluate(be.Right))}");
                    AppendNode(sb, be.Left, depth + 1, maxDepth);
                    AppendNode(sb, be.Right, depth + 1, maxDepth);
                    break;
                case UnaryExpression ue:
                    sb.AppendLine($"{Indent(depth)}  operator: {UnaryOperatorText(ue.NodeType)}");
                    AppendNode(sb, ue.Operand, depth + 1, maxDepth);
                    break;
                case MethodCallExpression mce:
                    sb.AppendLine($"{Indent(depth)}  call: {mce.Method.DeclaringType?.Name}.{mce.Method.Name}");
                    foreach (var arg in mce.Arguments)
                    {
                        AppendNode(sb, arg, depth + 1, maxDepth);
                    }
                    break;
                default:
                    break;
            }
        }

        private static string Indent(int depth) => new string(' ', depth * 2);

        private static string BinaryOperatorText(ExpressionType nodeType) =>
            nodeType switch
            {
                ExpressionType.Equal => "==",
                ExpressionType.NotEqual => "!=",
                ExpressionType.GreaterThan => ">",
                ExpressionType.GreaterThanOrEqual => ">=",
                ExpressionType.LessThan => "<",
                ExpressionType.LessThanOrEqual => "<=",
                ExpressionType.AndAlso => "&&",
                ExpressionType.OrElse => "||",
                ExpressionType.Add => "+",
                ExpressionType.Subtract => "-",
                ExpressionType.Multiply => "*",
                ExpressionType.Divide => "/",
                _ => nodeType.ToString()
            };

        private static string UnaryOperatorText(ExpressionType nodeType) =>
            nodeType switch
            {
                ExpressionType.Not => "!",
                ExpressionType.Negate => "-",
                _ => nodeType.ToString()
            };

        private static object? TryEvaluate(Expression expression)
        {
            try
            {
                if (expression.NodeType == ExpressionType.Parameter)
                {
                    return "<parameter>";
                }

                var boxed = Expression.Convert(expression, typeof(object));
                var lambda = Expression.Lambda<Func<object?>>(boxed);
                return lambda.Compile().Invoke();
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static string FormatValue(object? value)
        {
            if (value == null) return "null";
            if (value is string s) return $"\"{s}\"";
            return value.ToString() ?? "<unknown>";
        }
    }
}