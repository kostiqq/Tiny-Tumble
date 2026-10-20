using Cysharp.Threading.Tasks;

namespace TinyTumble.Core.Network
{
    /// <summary>
    /// Команды главного меню: создать игру или подключиться и перейти в лобби.
    /// </summary>
    public interface ISessionLauncher
    {
        bool IsBusy { get; }

        UniTask<SessionStartResult> HostAsync();

        UniTask<SessionStartResult> JoinAsync(string address);
    }
}
