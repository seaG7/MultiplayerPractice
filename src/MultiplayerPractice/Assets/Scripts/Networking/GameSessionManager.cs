using System;
using System.Collections;
using System.Text;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace Networking
{
    public struct GameSessionStateBroadcast : IBroadcast
    {
        public int State;
        public int ConnectedPlayers;
        public int RequiredPlayers;
        public float MatchTimer;
        public string LiveScoreText;
        public string ResultText;
    }

    [DisallowMultipleComponent]
    public sealed class GameSessionManager : MonoBehaviour
    {
        public enum GameState
        {
            WaitingForPlayers = 0,
            InProgress = 1,
            ShowingResults = 2
        }

        [SerializeField] private int requiredPlayers = 2;
        [SerializeField] private float matchDuration = 60f;
        [SerializeField] private float resultsDuration = 5f;
        [SerializeField] private float lobbyRestartDelay = 3f;
        [SerializeField] private int scoreToWin = 3;

        private const float SessionBroadcastInterval = 0.25f;
        private GameState currentState = GameState.WaitingForPlayers;
        private int connectedPlayers;
        private float matchTimer = 60f;
        private string liveScoreText = string.Empty;
        private string resultText = string.Empty;
        private bool startMatchQueued;
        private float sessionBroadcastTimer;
        private NetworkManager networkManager;
        private bool serverCallbacksRegistered;
        private bool serverSessionInitialized;
        private Coroutine broadcastRegistrationRoutine;

        public static GameSessionManager Instance { get; private set; }
        public static event Action SessionChanged;

        public int RequiredPlayers => Mathf.Max(1, requiredPlayers);
        public int ConnectedPlayers => connectedPlayers;
        public float MatchTimer => matchTimer;
        public string LiveScoreText => liveScoreText;
        public string ResultText => resultText;
        public GameState CurrentState => currentState;
        public bool IsGameplayActive => CurrentState == GameState.InProgress;
        public static bool IsGameplayActiveGlobal => Instance != null && Instance.IsGameplayActive;

        public static void ApplyRemoteState(GameSessionStateBroadcast message)
        {
            if (Instance == null)
            {
                return;
            }

            Instance.ApplyRemoteStateInternal(message);
        }

        private void Awake()
        {
            Instance = this;
            matchTimer = matchDuration;
        }

        private void OnEnable()
        {
            broadcastRegistrationRoutine = StartCoroutine(RegisterClientBroadcastWhenReady());
        }

        private void OnDisable()
        {
            CancelInvoke();
            startMatchQueued = false;

            if (broadcastRegistrationRoutine != null)
            {
                StopCoroutine(broadcastRegistrationRoutine);
                broadcastRegistrationRoutine = null;
            }

            UnregisterNetworkManager();
            serverSessionInitialized = false;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void InitializeServerSession()
        {
            if (serverSessionInitialized)
            {
                return;
            }

            serverSessionInitialized = true;
            matchTimer = matchDuration;
            resultText = string.Empty;
            RefreshLiveScoreText();
            SetState(GameState.WaitingForPlayers);
            RefreshConnectedPlayers();
            TryQueueMatchStart();
            BroadcastSessionState();
        }

        private void Update()
        {
            if (!IsServerActive)
            {
                return;
            }

            if (CurrentState == GameState.InProgress)
            {
                matchTimer = Mathf.Max(0f, matchTimer - Time.deltaTime);
                if (matchTimer <= 0f)
                {
                    EndMatch();
                }
            }

            sessionBroadcastTimer -= Time.deltaTime;
            if (sessionBroadcastTimer <= 0f)
            {
                RefreshLiveScoreText();
                BroadcastSessionState();
            }
        }

        public void NotifyScoreChanged()
        {
            if (!IsServerActive || CurrentState != GameState.InProgress)
            {
                return;
            }

            RefreshLiveScoreText();
            BroadcastSessionState();

            NetworkPlayer leader = GetLeader();
            if (leader != null && scoreToWin > 0 && leader.Score >= scoreToWin)
            {
                EndMatch();
            }
        }

        public void NotifyPlayerSpawned()
        {
            if (!IsServerActive)
            {
                return;
            }

            RefreshConnectedPlayers();
            RefreshLiveScoreText();
            BroadcastSessionState();
            TryQueueMatchStart();
        }

        private void HandleRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            RefreshConnectedPlayers();

            if (args.ConnectionState == RemoteConnectionState.Stopped && CurrentState == GameState.InProgress && connectedPlayers < RequiredPlayers)
            {
                EndMatch();
                return;
            }

            TryQueueMatchStart();
        }

        private void RefreshConnectedPlayers()
        {
            if (networkManager == null)
            {
                return;
            }

            int activeConnections = 0;
            foreach (NetworkConnection connection in networkManager.ServerManager.Clients.Values)
            {
                if (connection != null && connection.IsActive)
                {
                    activeConnections++;
                }
            }

            int spawnedPlayers = 0;
            foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            {
                if (player != null && player.NetworkObject != null && player.NetworkObject.IsSpawned && player.Owner.IsValid)
                {
                    spawnedPlayers++;
                }
            }

            int count = Mathf.Max(activeConnections, spawnedPlayers);

            if (connectedPlayers == count)
            {
                return;
            }

            connectedPlayers = count;
            NotifySessionChangedAndPlayers();
            BroadcastSessionState();
        }

        private void TryQueueMatchStart()
        {
            if (!IsServerActive || CurrentState != GameState.WaitingForPlayers || connectedPlayers < RequiredPlayers || startMatchQueued)
            {
                return;
            }

            startMatchQueued = true;
            Invoke(nameof(StartMatch), lobbyRestartDelay);
        }

        private void StartMatch()
        {
            startMatchQueued = false;
            RefreshConnectedPlayers();
            if (CurrentState != GameState.WaitingForPlayers || connectedPlayers < RequiredPlayers)
            {
                return;
            }

            ResetPlayers(resetScore: true);
            resultText = string.Empty;
            matchTimer = matchDuration;
            RefreshLiveScoreText();
            SetState(GameState.InProgress);
            BroadcastSessionState();
            Debug.Log("[Server] Match started.");
        }

        private void EndMatch()
        {
            if (CurrentState == GameState.ShowingResults)
            {
                return;
            }

            CancelInvoke(nameof(StartMatch));
            startMatchQueued = false;
            RefreshLiveScoreText();
            resultText = BuildResultsText();
            SetState(GameState.ShowingResults);
            BroadcastSessionState();
            Debug.Log("[Server] Match ended. Showing results.");
            Invoke(nameof(ResetToLobby), resultsDuration);
        }

        private void ResetToLobby()
        {
            ResetPlayers(resetScore: true);
            matchTimer = matchDuration;
            resultText = string.Empty;
            RefreshLiveScoreText();
            SetState(GameState.WaitingForPlayers);
            RefreshConnectedPlayers();
            BroadcastSessionState();
            Debug.Log("[Server] Lobby reset.");
            TryQueueMatchStart();
        }

        private void ResetPlayers(bool resetScore)
        {
            foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            {
                player.ResetForMatchOnServer(resetScore);
            }
        }

        private NetworkPlayer GetLeader()
        {
            NetworkPlayer leader = null;
            foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            {
                if (leader == null || player.Score > leader.Score)
                {
                    leader = player;
                }
            }

            return leader;
        }

        private string BuildResultsText()
        {
            StringBuilder builder = new();
            builder.AppendLine("Итоговый счёт");
            builder.Append(BuildScoreText());
            return builder.ToString();
        }

        private void RefreshLiveScoreText()
        {
            liveScoreText = BuildScoreText();
        }

        private string BuildScoreText()
        {
            NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
            Array.Sort(players, (left, right) => right.Score.CompareTo(left.Score));

            StringBuilder builder = new();
            if (players.Length == 0)
            {
                builder.AppendLine("Нет подключённых игроков");
                return builder.ToString();
            }

            for (int i = 0; i < players.Length; i++)
            {
                NetworkPlayer player = players[i];
                string playerName = string.IsNullOrWhiteSpace(player.Nickname) ? $"Player {player.OwnerId + 1}" : player.Nickname;
                builder.AppendLine($"{i + 1}. {playerName}: {player.Score}");
            }

            return builder.ToString();
        }

        private void SetState(GameState state)
        {
            if (currentState == state)
            {
                return;
            }

            currentState = state;
            NotifySessionChangedAndPlayers();
        }

        private IEnumerator RegisterClientBroadcastWhenReady()
        {
            WaitForSeconds wait = new(0.25f);
            while (enabled)
            {
                NetworkManager foundNetworkManager = InstanceFinder.NetworkManager;
                if (foundNetworkManager != null && foundNetworkManager != networkManager)
                {
                    RegisterNetworkManager(foundNetworkManager);
                }

                UpdateServerRegistration();
                yield return wait;
            }
        }

        private void RegisterNetworkManager(NetworkManager newNetworkManager)
        {
            UnregisterNetworkManager();
            networkManager = newNetworkManager;
            networkManager.ClientManager.RegisterBroadcast<GameSessionStateBroadcast>(HandleSessionStateBroadcast);
            NotifySessionChanged();
            UpdateServerRegistration();
        }

        private void UnregisterNetworkManager()
        {
            if (networkManager == null)
            {
                return;
            }

            networkManager.ClientManager.UnregisterBroadcast<GameSessionStateBroadcast>(HandleSessionStateBroadcast);
            if (serverCallbacksRegistered)
            {
                networkManager.ServerManager.OnRemoteConnectionState -= HandleRemoteConnectionState;
                serverCallbacksRegistered = false;
            }

            networkManager = null;
        }

        private void UpdateServerRegistration()
        {
            if (networkManager == null)
            {
                return;
            }

            if (networkManager.IsServerStarted)
            {
                if (!serverCallbacksRegistered)
                {
                    networkManager.ServerManager.OnRemoteConnectionState += HandleRemoteConnectionState;
                    serverCallbacksRegistered = true;
                }

                InitializeServerSession();
                return;
            }

            if (serverCallbacksRegistered)
            {
                networkManager.ServerManager.OnRemoteConnectionState -= HandleRemoteConnectionState;
                serverCallbacksRegistered = false;
            }

            serverSessionInitialized = false;
        }

        private void HandleSessionStateBroadcast(GameSessionStateBroadcast message, Channel channel)
        {
            ApplyRemoteStateInternal(message);
        }

        private void BroadcastSessionState()
        {
            if (!IsServerActive)
            {
                return;
            }

            sessionBroadcastTimer = SessionBroadcastInterval;
            GameSessionStateBroadcast snapshot = CreateSnapshot();
            networkManager.ServerManager.Broadcast(snapshot, requireAuthenticated: false);
            SendTargetedSessionState(snapshot);
        }

        private GameSessionStateBroadcast CreateSnapshot()
        {
            return new GameSessionStateBroadcast
            {
                State = (int)currentState,
                ConnectedPlayers = connectedPlayers,
                RequiredPlayers = RequiredPlayers,
                MatchTimer = matchTimer,
                LiveScoreText = liveScoreText,
                ResultText = resultText
            };
        }

        private void SendTargetedSessionState(GameSessionStateBroadcast snapshot)
        {
            foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            {
                player.SendSessionStateToOwner(snapshot);
            }
        }

        private void ApplyRemoteStateInternal(GameSessionStateBroadcast message)
        {
            if (IsServerActive)
            {
                return;
            }

            currentState = ToGameState(message.State);
            connectedPlayers = message.ConnectedPlayers;
            requiredPlayers = Mathf.Max(1, message.RequiredPlayers);
            matchTimer = Mathf.Max(0f, message.MatchTimer);
            liveScoreText = message.LiveScoreText ?? string.Empty;
            resultText = message.ResultText ?? string.Empty;
            NotifySessionChangedAndPlayers();
        }

        private bool IsServerActive => networkManager != null && networkManager.IsServerStarted;

        private static GameState ToGameState(int state)
        {
            return Enum.IsDefined(typeof(GameState), state) ? (GameState)state : GameState.WaitingForPlayers;
        }

        private static void NotifySessionChangedAndPlayers()
        {
            NotifySessionChanged();
            foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            {
                player.RefreshSessionState();
            }
        }

        private static void NotifySessionChanged()
        {
            SessionChanged?.Invoke();
        }
    }
}
