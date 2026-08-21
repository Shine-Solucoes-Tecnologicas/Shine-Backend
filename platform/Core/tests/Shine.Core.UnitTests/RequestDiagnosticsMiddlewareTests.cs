using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shine.Api;

namespace Shine.UnitTests;

public sealed class RequestDiagnosticsMiddlewareTests
{
    [Fact]
    public async Task Creates_and_returns_a_correlation_id_when_header_is_missing()
    {
        var context = new DefaultHttpContext();
        var middleware = new RequestDiagnosticsMiddleware(next: _ => Task.CompletedTask, NullLogger<RequestDiagnosticsMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        var value = context.Response.Headers[RequestDiagnosticsMiddleware.HeaderName].ToString();
        Assert.True(Guid.TryParse(value, out _));
        Assert.Equal(value, context.Items[RequestDiagnosticsMiddleware.HeaderName]);
    }

    [Fact]
    public async Task Preserves_a_valid_incoming_correlation_id()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var context = new DefaultHttpContext();
        context.Request.Headers[RequestDiagnosticsMiddleware.HeaderName] = correlationId;
        var middleware = new RequestDiagnosticsMiddleware(next: _ => Task.CompletedTask, NullLogger<RequestDiagnosticsMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(correlationId, context.Response.Headers[RequestDiagnosticsMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task Keeps_correlation_header_when_the_next_delegate_fails()
    {
        var context = new DefaultHttpContext();
        var middleware = new RequestDiagnosticsMiddleware(
            next: _ => throw new InvalidOperationException("expected"),
            NullLogger<RequestDiagnosticsMiddleware>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
        Assert.True(Guid.TryParse(context.Response.Headers[RequestDiagnosticsMiddleware.HeaderName].ToString(), out _));
    }
}
