namespace PMT.Domain.Enums;

/// <summary>
/// Author of a persisted chat message. Stored as a string in
/// dbo.AiChatMessage.Role (varchar(16)).
/// </summary>
/// <remarks>
/// Intermediate "tool" turns are deliberately not part of this enum. They are
/// transient prompt-construction details and are audited in dbo.AiAgentToolCall
/// instead of being written to the visible transcript.
/// </remarks>
public enum AiChatRole
{
    User,
    Assistant,
    System
}
