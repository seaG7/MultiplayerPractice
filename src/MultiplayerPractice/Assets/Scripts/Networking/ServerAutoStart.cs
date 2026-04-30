using System.Collections;
using FishNet;
using FishNet.Managing;
using UnityEngine;

namespace Networking
{
    public sealed class ServerAutoStart : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Application.isBatchMode)
            {
                return;
            }

            GameObject runner = new("ServerAutoStart");
            DontDestroyOnLoad(runner);
            runner.AddComponent<ServerAutoStart>();
        }

        private IEnumerator Start()
        {
            NetworkManager networkManager = null;
            while (networkManager == null)
            {
                networkManager = InstanceFinder.NetworkManager ?? FindFirstObjectByType<NetworkManager>();
                yield return null;
            }

            NetworkLaunchOptions.Apply(networkManager, applyAddress: false);

            if (!networkManager.IsServerStarted)
            {
                Debug.Log("[Server] Headless mode detected. Starting FishNet server...");
                networkManager.ServerManager.StartConnection(NetworkLaunchOptions.GetPort());
            }
        }
    }
}
