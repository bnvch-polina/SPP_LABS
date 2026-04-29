using TestingFramework;

namespace TestsForProject;

[Atributes.TestClass("Parallel")]
public class ParallelAndTimeoutTests
{
    [Atributes.TestMethod("Slow test A (1s)")]
    public async Task SlowTestA()
    {
        await Task.Delay(1000);
        Asserts.IsTrue(true);
    }

    [Atributes.TestMethod("Slow test B (1s)")]
    public async Task SlowTestB()
    {
        await Task.Delay(1000);
        Asserts.IsTrue(true);
    }

    [Atributes.TestMethod("Slow test C (1s)")]
    public async Task SlowTestC()
    {
        await Task.Delay(1000);
        Asserts.IsTrue(true);
    }

    [Atributes.TestMethod("Timeout demo test")]
    [Atributes.Timeout(500)]
    public async Task TimeoutDemo()
    {
        await Task.Delay(2000);
    }
}
