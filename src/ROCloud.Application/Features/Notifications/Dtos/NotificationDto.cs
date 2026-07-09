namespace ROCloud.Application.Features.Notifications.Dtos;

/// <summary>A single in-app notification. The portal renders a translated label from Type + Count.</summary>
public sealed record NotificationDto(
    Guid Id,
    string Type,
    int Count,
    string Title,
    string? Link,
    bool IsRead,
    DateTime CreatedAt);

/// <summary>The owner's bell feed: the unread badge count plus the notification list.</summary>
public sealed record NotificationFeedDto(
    int UnreadCount,
    IReadOnlyList<NotificationDto> Items);
