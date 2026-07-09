namespace ROCloud.Application.Common.Sanitisation;

/// <summary>
/// Marks a string command/request property as user-provided rich text that must be HTML-
/// sanitised before reaching the handler (guide §10.5). Applied by SanitizationBehaviour.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SanitizeHtmlAttribute : Attribute;
