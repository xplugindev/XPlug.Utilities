using SharpOnvifServer;

namespace Screen2VMS.Onvif;

/// <summary>
/// The single ONVIF account (spec 24).
/// </summary>
/// <remarks>
/// One account is all Profile S needs for the MVP. User management is out of
/// scope (spec 3), so there is no store behind this - just the credentials the
/// operator configured.
/// </remarks>
public sealed class OnvifUserRepository : IUserRepository
{
    private readonly UserInfo user;

    public OnvifUserRepository(string userName, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(userName);
        ArgumentException.ThrowIfNullOrEmpty(password);

        user = new UserInfo(userName, password);
    }

    public UserInfo? GetUser(string userName) =>
        string.Equals(userName, user.UserName, StringComparison.Ordinal) ? user : null;

    public Task<UserInfo?> GetUserAsync(string userName) => Task.FromResult(GetUser(userName));

    /// <summary>
    /// Pre-hashed digest lookup, used by RFC 7616 clients.
    /// </summary>
    /// <remarks>
    /// The password is held in plaintext here, so there is no stored hash to
    /// return and the caller falls back to hashing it itself.
    /// </remarks>
    public UserInfo? GetUserByHash(string algorithm, string userName, string realm) => GetUser(userName);

    public Task<UserInfo?> GetUserByHashAsync(string algorithm, string userName, string realm) =>
        Task.FromResult(GetUserByHash(algorithm, userName, realm));
}
