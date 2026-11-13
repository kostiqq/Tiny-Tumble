namespace TinyTumble.Core.Network
{
    /// <summary>
    /// Приглашение друзей в текущую сессию (оверлей Steam). UI видит только этот интерфейс, без Steam API.
    /// </summary>
    public interface ISessionInvites
    {
        /// <summary>Есть сессия, в которую можно пригласить: Steam запущен и лобби для друзей создано.</summary>
        bool CanInvite { get; }

        /// <summary>Открывает окно приглашения. false — приглашать пока некуда.</summary>
        bool OpenInviteDialog();
    }
}
