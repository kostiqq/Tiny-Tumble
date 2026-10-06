using UnityEngine;
using VContainer;

public class Bootstraper : MonoBehaviour
{
    [Inject]
    private ISceneLoader _sceneLoader;
        
    public void Awake()
    {
        
        //instntiate gamescope
        _sceneLoader.LoadSceneAsync("MainMenu");
        //connect to server todo
    }
}
