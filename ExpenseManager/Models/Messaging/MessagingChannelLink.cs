namespace ExpenseManager.Models.Messaging;

public sealed class MessagingChannelLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public MessagingChannel Channel { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public DateTime LinkedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
