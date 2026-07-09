using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ROCloud.Application.Common.Behaviours;

/// <summary>Logs each request's name and duration.</summary>
public class LoggingBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehaviour<TRequest, TResponse>> _logger;

    public LoggingBehaviour(ILogger<LoggingBehaviour<TRequest, TResponse>> logger) => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await next();
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation("Handled {RequestName} in {ElapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
        }
    }
}
