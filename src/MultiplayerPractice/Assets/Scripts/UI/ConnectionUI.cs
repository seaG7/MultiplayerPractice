using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public class ConnectionUI : MonoBehaviour
    {
        [SerializeField] private TMP_InputField nicknameInput;
        [SerializeField] private GameObject menuRoot;
        [SerializeField] private Button hostButton;
        [SerializeField] private Button clientButton;

        private bool callbacksRegistered;
        private CanvasGroup menuCanvasGroup;
        private NetworkManager networkManager;
        private LocalConnectionState clientState = LocalConnectionState.Stopped;
        private LocalConnectionState serverState = LocalConnectionState.Stopped;

        private void Awake()
        {
            menuRoot = gameObject;

            if (menuRoot == gameObject)
            {
                menuCanvasGroup = GetComponent<CanvasGroup>();
                if (menuCanvasGroup == null)
                {
                    menuCanvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            if (nicknameInput != null)
            {
                nicknameInput.text = LocalPlayerProfile.GetNickname();
            }

            if (hostButton != null)
            {
                hostButton.onClick.AddListener(StartHost);
            }

            if (clientButton != null)
            {
                clientButton.onClick.AddListener(StartClient);
            }
        }

        private void OnEnable()
        {
            ResolveNetworkManager();
            RegisterCallbacks();
            UpdateMenuVisibility(clientState != LocalConnectionState.Started && serverState != LocalConnectionState.Started);
        }

        private void OnDisable()
        {
            UnregisterCallbacks();
        }

        private void OnDestroy()
        {
            if (hostButton != null)
            {
                hostButton.onClick.RemoveListener(StartHost);
            }

            if (clientButton != null)
            {
                clientButton.onClick.RemoveListener(StartClient);
            }
        }

        public void StartHost()
        {
            SaveNickname();
            ResolveNetworkManager();
            if (networkManager == null)
            {
                return;
            }

            if (serverState != LocalConnectionState.Stopped || clientState != LocalConnectionState.Stopped)
            {
                return;
            }

            bool serverStarted = networkManager.ServerManager.StartConnection();
            bool clientStarted = networkManager.ClientManager.StartConnection();
            if (serverStarted && clientStarted)
            {
                UpdateMenuVisibility(false);
            }
        }

        public void StartClient()
        {
            SaveNickname();
            ResolveNetworkManager();
            if (networkManager == null || clientState != LocalConnectionState.Stopped)
            {
                return;
            }

            if (networkManager.ClientManager.StartConnection())
            {
                UpdateMenuVisibility(false);
            }
        }

        private void SaveNickname()
        {
            LocalPlayerProfile.SetNickname(nicknameInput == null ? string.Empty : nicknameInput.text);
        }

        private void RegisterCallbacks()
        {
            if (callbacksRegistered || networkManager == null)
            {
                return;
            }

            networkManager.ServerManager.OnServerConnectionState += HandleServerConnectionState;
            networkManager.ClientManager.OnClientConnectionState += HandleClientConnectionState;
            callbacksRegistered = true;
        }

        private void UnregisterCallbacks()
        {
            if (!callbacksRegistered || networkManager == null)
            {
                return;
            }

            networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
            networkManager.ClientManager.OnClientConnectionState -= HandleClientConnectionState;
            callbacksRegistered = false;
        }

        private void HandleServerConnectionState(ServerConnectionStateArgs args)
        {
            serverState = args.ConnectionState;
            UpdateMenuVisibility(clientState != LocalConnectionState.Started && serverState != LocalConnectionState.Started);
        }

        private void HandleClientConnectionState(ClientConnectionStateArgs args)
        {
            clientState = args.ConnectionState;
            UpdateMenuVisibility(clientState != LocalConnectionState.Started && serverState != LocalConnectionState.Started);
        }

        private void ResolveNetworkManager()
        {
            networkManager = InstanceFinder.NetworkManager ?? FindObjectOfType<NetworkManager>();
            if (networkManager == null)
            {
                clientState = LocalConnectionState.Stopped;
                serverState = LocalConnectionState.Stopped;
                return;
            }

            clientState = networkManager.TransportManager.Transport.GetConnectionState(false);
            serverState = networkManager.TransportManager.Transport.GetConnectionState(true);
        }

        private void UpdateMenuVisibility(bool visible)
        {
            if (menuRoot != null)
            {
                if (menuRoot == gameObject && menuCanvasGroup != null)
                {
                    menuCanvasGroup.alpha = visible ? 1f : 0f;
                    menuCanvasGroup.interactable = visible;
                    menuCanvasGroup.blocksRaycasts = visible;
                }
                else
                {
                    menuRoot.SetActive(visible);
                }
            }

            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = visible;
        }
    }
}
