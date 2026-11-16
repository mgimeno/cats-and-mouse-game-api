using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.SignalR;

namespace CatsAndMouseApi.Hubs;

/// <summary>
/// Restores, under Native AOT, what the hub does on the JIT when a method throws.
/// </summary>
/// <remarks>
/// Without dynamic code SignalR invokes hub methods through reflection, and reflection wraps
/// anything a method throws synchronously in a <see cref="TargetInvocationException"/>. The
/// dispatcher only passes a <see cref="HubException"/>'s message on to the caller, so every rule
/// violation ("It's not your turn", "Game password is invalid") reached the client as "An
/// unexpected error occurred" and was logged as the wrapper. Found by scripts/smoke-test.sh against
/// the AOT build; the JIT build, and so every unit test, never sees the wrapper.
/// </remarks>
public sealed class HubExceptionUnwrappingFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            // Rethrows the original with its own stack trace, as if nothing had wrapped it.
            ExceptionDispatchInfo.Throw(exception.InnerException);
            throw;
        }
    }
}
