using System.Reflection;
using CatsAndMouseApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CatsAndMouseApi.Tests;

public sealed class HubExceptionUnwrappingFilterTests
{
    private readonly HubExceptionUnwrappingFilter _filter = new();

    [Fact]
    public async Task Rethrows_the_hub_exception_that_reflection_wrapped_synchronously()
    {
        var exception = await Assert.ThrowsAsync<HubException>(async () =>
            await _filter.InvokeMethodAsync(null!, _ => throw new TargetInvocationException(new HubException("It's not your turn"))));

        Assert.Equal("It's not your turn", exception.Message);
    }

    [Fact]
    public async Task Rethrows_the_hub_exception_from_a_faulted_invocation()
    {
        var exception = await Assert.ThrowsAsync<HubException>(async () =>
            await _filter.InvokeMethodAsync(null!, _ => ValueTask.FromException<object?>(new TargetInvocationException(new HubException("Game is over")))));

        Assert.Equal("Game is over", exception.Message);
    }

    [Fact]
    public async Task Passes_results_and_other_exceptions_through_untouched()
    {
        Assert.Equal("result", await _filter.InvokeMethodAsync(null!, _ => ValueTask.FromResult<object?>("result")));

        var original = new InvalidOperationException("boom");
        Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _filter.InvokeMethodAsync(null!, _ => throw original)));
    }
}
