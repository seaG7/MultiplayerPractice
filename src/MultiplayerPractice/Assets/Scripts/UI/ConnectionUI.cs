using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using Networking;
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
        [SerializeField] private TMP_InputField addressInput;
        [SerializeField] private TMP_InputField portInput;
        [SerializeField] private TMP_Text endpointStatusText;
        [SerializeField] private bool createMissingEndpointInputs = true;

        private bool callbacksRegistered;
        private CanvasGroup menuCanvasGroup;
        private NetworkManager networkManager;
        private LocalConnectionState clientState = LocalConnectionState.Stopped;
        private LocalConnectionState serverState = LocalConnectionState.Stopped;
        private const string AddressPrefsKey = "mp_server_address";
        private const string PortPrefsKey = "mp_server_port";

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

            EnsureEndpointInputs();
            InitializeEndpointInputs();

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

            ApplyEndpoint(applyAddress: true, forceAddress: "127.0.0.1");
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

            ApplyEndpoint(applyAddress: true);
            if (networkManager.ClientManager.StartConnection())
            {
                UpdateMenuVisibility(false);
            }
        }

        private void SaveNickname()
        {
            LocalPlayerProfile.SetNickname(nicknameInput == null ? string.Empty : nicknameInput.text);
        }

        private void InitializeEndpointInputs()
        {
            ResolveNetworkManager();

            string commandLineAddress = NetworkLaunchOptions.GetAddress();
            string savedAddress = PlayerPrefs.GetString(AddressPrefsKey, string.Empty);
            string fallbackAddress = networkManager != null && networkManager.TransportManager.Transport != null
                ? networkManager.TransportManager.Transport.GetClientAddress()
                : "127.0.0.1";

            if (addressInput != null)
            {
                addressInput.text = FirstNonEmpty(commandLineAddress, savedAddress, fallbackAddress, "127.0.0.1");
            }

            int savedPort = PlayerPrefs.GetInt(PortPrefsKey, NetworkLaunchOptions.DefaultPort);
            ushort commandLinePort = NetworkLaunchOptions.GetPort((ushort)savedPort);
            if (portInput != null)
            {
                portInput.text = commandLinePort.ToString();
            }

            UpdateEndpointStatus();
        }

        private void ApplyEndpoint(bool applyAddress, string forceAddress = null)
        {
            if (networkManager == null)
            {
                return;
            }

            string address = string.IsNullOrWhiteSpace(forceAddress) ? GetAddressFromUi() : forceAddress;
            ushort port = GetPortFromUi();
            NetworkLaunchOptions.Apply(networkManager, applyAddress, address, port);

            if (!string.IsNullOrWhiteSpace(address))
            {
                PlayerPrefs.SetString(AddressPrefsKey, address);
            }

            PlayerPrefs.SetInt(PortPrefsKey, port);
            PlayerPrefs.Save();
            UpdateEndpointStatus();
        }

        private string GetAddressFromUi()
        {
            if (addressInput != null && !string.IsNullOrWhiteSpace(addressInput.text))
            {
                return addressInput.text.Trim();
            }

            return FirstNonEmpty(NetworkLaunchOptions.GetAddress(), PlayerPrefs.GetString(AddressPrefsKey, string.Empty), "127.0.0.1");
        }

        private ushort GetPortFromUi()
        {
            if (portInput != null && ushort.TryParse(portInput.text, out ushort port))
            {
                return port;
            }

            return NetworkLaunchOptions.GetPort((ushort)PlayerPrefs.GetInt(PortPrefsKey, NetworkLaunchOptions.DefaultPort));
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private void UpdateEndpointStatus()
        {
            if (endpointStatusText == null)
            {
                return;
            }

            endpointStatusText.text = $"Server: {GetAddressFromUi()}:{GetPortFromUi()}";
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
            networkManager = InstanceFinder.NetworkManager ?? FindFirstObjectByType<NetworkManager>();
            if (networkManager == null)
            {
                clientState = LocalConnectionState.Stopped;
                serverState = LocalConnectionState.Stopped;
                return;
            }

            clientState = networkManager.TransportManager.Transport.GetConnectionState(false);
            serverState = networkManager.TransportManager.Transport.GetConnectionState(true);
        }

        private void EnsureEndpointInputs()
        {
            if (!createMissingEndpointInputs || menuRoot == null)
            {
                return;
            }

            Transform parent = menuRoot.transform;
            if (addressInput == null)
            {
                addressInput = CreateInput(parent, "ServerAddressInput", "Server IP", new Vector2(17f, -185f), "127.0.0.1");
            }

            if (portInput == null)
            {
                portInput = CreateInput(parent, "ServerPortInput", "Port", new Vector2(17f, -250f), NetworkLaunchOptions.DefaultPort.ToString());
            }

            if (endpointStatusText == null)
            {
                endpointStatusText = CreateLabel(parent, "EndpointStatus", new Vector2(17f, -305f));
            }
        }

        private static TMP_InputField CreateInput(Transform parent, string name, string placeholder, Vector2 anchoredPosition, string value)
        {
            GameObject inputObject = new(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            inputObject.layer = parent.gameObject.layer;
            inputObject.transform.SetParent(parent, false);

            RectTransform rectTransform = inputObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = new Vector2(340f, 48f);

            Image image = inputObject.GetComponent<Image>();
            image.color = Color.white;

            RectTransform textViewport = CreateChildRect(inputObject.transform, "Text Area", Vector2.zero, Vector2.one, new Vector2(14f, 7f), new Vector2(-14f, -7f));
            textViewport.gameObject.AddComponent<RectMask2D>();

            TMP_Text text = CreateInputText(textViewport, "Text", Color.black, TextAlignmentOptions.MidlineLeft);
            TMP_Text placeholderText = CreateInputText(textViewport, "Placeholder", new Color(0f, 0f, 0f, 0.45f), TextAlignmentOptions.MidlineLeft);
            placeholderText.text = placeholder;

            TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
            input.targetGraphic = image;
            input.textViewport = textViewport;
            input.textComponent = text;
            input.placeholder = placeholderText;
            input.text = value;
            return input;
        }

        private static TMP_Text CreateLabel(Transform parent, string name, Vector2 anchoredPosition)
        {
            GameObject labelObject = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.layer = parent.gameObject.layer;
            labelObject.transform.SetParent(parent, false);

            RectTransform rectTransform = labelObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = new Vector2(520f, 44f);

            TMP_Text text = labelObject.GetComponent<TMP_Text>();
            text.fontSize = 20f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            return text;
        }

        private static TMP_Text CreateInputText(RectTransform parent, string name, Color color, TextAlignmentOptions alignment)
        {
            GameObject textObject = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.layer = parent.gameObject.layer;
            textObject.transform.SetParent(parent, false);

            RectTransform rectTransform = textObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.fontSize = 22f;
            text.alignment = alignment;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        private static RectTransform CreateChildRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject child = new(name, typeof(RectTransform));
            child.layer = parent.gameObject.layer;
            child.transform.SetParent(parent, false);

            RectTransform rectTransform = child.GetComponent<RectTransform>();
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = offsetMin;
            rectTransform.offsetMax = offsetMax;
            return rectTransform;
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
