using TMPro;
using Unity.Netcode;
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

            nicknameInput.text = LocalPlayerProfile.GetNickname();
            hostButton.onClick.AddListener(StartHost);
            clientButton.onClick.AddListener(StartClient);
        }

        private void OnEnable()
        {
            RegisterCallbacks();
            UpdateMenuVisibility(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening);
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

            if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsListening)
            {
                return;
            }

            if (NetworkManager.Singleton.StartHost())
            {
                UpdateMenuVisibility(false);
            }
        }

        public void StartClient()
        {
            SaveNickname();

            if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsListening)
            {
                return;
            }

            if (NetworkManager.Singleton.StartClient())
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
            if (callbacksRegistered || NetworkManager.Singleton == null)
            {
                return;
            }

            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            NetworkManager.Singleton.OnServerStopped += HandleServerStopped;
            callbacksRegistered = true;
        }

        private void UnregisterCallbacks()
        {
            if (!callbacksRegistered || NetworkManager.Singleton == null)
            {
                return;
            }

            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            NetworkManager.Singleton.OnServerStopped -= HandleServerStopped;
            callbacksRegistered = false;
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (NetworkManager.Singleton == null || clientId != NetworkManager.Singleton.LocalClientId)
            {
                return;
            }

            UpdateMenuVisibility(false);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (NetworkManager.Singleton == null || clientId != NetworkManager.Singleton.LocalClientId)
            {
                return;
            }

            UpdateMenuVisibility(true);
        }

        private void HandleServerStopped(bool _)
        {
            UpdateMenuVisibility(true);
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
