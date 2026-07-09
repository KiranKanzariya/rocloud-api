using System.Collections.Concurrent;
using System.Reflection;
using MediatR;
using ROCloud.Application.Common.Sanitisation;

namespace ROCloud.Application.Common.Behaviours;

/// <summary>
/// Runs after validation: HTML-sanitises every request string property marked with
/// <see cref="SanitizeHtmlAttribute"/> before the handler sees it (guide §10.5), so stored
/// rich text (customer/invoice notes, service-request descriptions) can never carry XSS.
/// </summary>
public class SanitizationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    // One reflection pass per request type, cached.
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Cache = new();

    private readonly IHtmlSanitizer _sanitizer;

    public SanitizationBehaviour(IHtmlSanitizer sanitizer) => _sanitizer = sanitizer;

    public Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var props = Cache.GetOrAdd(typeof(TRequest), static t => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string)
                        && p.CanWrite
                        && p.GetCustomAttribute<SanitizeHtmlAttribute>() is not null)
            .ToArray());

        foreach (var prop in props)
        {
            if (prop.GetValue(request) is string value && !string.IsNullOrEmpty(value))
            {
                var cleaned = _sanitizer.Sanitize(value);
                if (!string.Equals(cleaned, value, StringComparison.Ordinal))
                    prop.SetValue(request, cleaned);
            }
        }

        return next();
    }
}
