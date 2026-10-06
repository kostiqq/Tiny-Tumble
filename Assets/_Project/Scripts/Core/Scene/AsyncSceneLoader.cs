using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AsyncSceneLoader : ISceneLoader
{
    public async UniTask LoadSceneAsync(string sceneName)
    {
        AsyncOperation sceneLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

        await sceneLoad.ToUniTask();
    }
}
