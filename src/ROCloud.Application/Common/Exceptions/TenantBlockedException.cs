namespace ROCloud.Application.Common.Exceptions;

/// <summary>
/// Thrown when a non-owner tries to start a session on a blocked workspace (suspended, or cancelled past
/// its paid period). Only the owner can clear the block by paying, so a staff session would be a
/// dead-end: every request 401s and nothing the user can do fixes it. The message names who to ask.
/// </summary>
public class TenantBlockedException : Exception
{
    public TenantBlockedException(string message) : base(message) { }
}
