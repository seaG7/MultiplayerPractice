using FishNet;
using FishNet.Managing;
using Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public sealed class GameSessionUI : MonoBehaviour
    {
        private Canvas canvas;
        private GameObject overlay;
        private TMP_Text titleText;
        private TMP_Text bodyText;
        private TMP_Text hudText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isBatchMode || FindFirstObjectByType<GameSessionUI>() != null)
            {
                return;
            }

            GameObject runner = new("GameSessionUI");
            runner.AddComponent<GameSessionUI>();
        }

        private void Awake()
        {
            BuildUi();
        }

        private void OnEnable()
        {
            GameSessionManager.SessionChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            GameSessionManager.SessionChanged -= Refresh;
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            GameSessionManager session = GameSessionManager.Instance;
            bool networkRunning = IsNetworkRunning();
            if (session == null || !networkRunning)
            {
                SetVisible(false, false);
                return;
            }

            switch (session.CurrentState)
            {
                case GameSessionManager.GameState.WaitingForPlayers:
                    SetVisible(true, false);
                    titleText.text = "Ожидание игроков";
                    bodyText.text = $"Игроки: {session.ConnectedPlayers}/{session.RequiredPlayers}\nМатч начнётся автоматически";
                    break;
                case GameSessionManager.GameState.InProgress:
                    SetVisible(false, true);
                    hudText.text = BuildHud(session);
                    break;
                case GameSessionManager.GameState.ShowingResults:
                    SetVisible(true, false);
                    titleText.text = "Результаты";
                    bodyText.text = $"{session.ResultText}\nВозврат в лобби...";
                    break;
            }
        }

        private static bool IsNetworkRunning()
        {
            NetworkManager networkManager = InstanceFinder.NetworkManager;
            return networkManager != null && (networkManager.IsClientStarted || networkManager.IsServerStarted);
        }

        private static string BuildHud(GameSessionManager session)
        {
            int seconds = Mathf.CeilToInt(session.MatchTimer);
            string scores = string.IsNullOrWhiteSpace(session.LiveScoreText)
                ? BuildLocalScoreText()
                : session.LiveScoreText.TrimEnd();

            return $"Время: {seconds:00}\nСчёт\n{scores}";
        }

        private static string BuildLocalScoreText()
        {
            string scores = string.Empty;
            foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            {
                string playerName = string.IsNullOrWhiteSpace(player.Nickname) ? $"Player {player.OwnerId + 1}" : player.Nickname;
                scores += $"{playerName}: {player.Score}\n";
            }

            return string.IsNullOrWhiteSpace(scores) ? "Нет подключённых игроков" : scores.TrimEnd();
        }

        private void SetVisible(bool overlayVisible, bool hudVisible)
        {
            if (overlay != null)
            {
                overlay.SetActive(overlayVisible);
            }

            if (hudText != null)
            {
                hudText.gameObject.SetActive(hudVisible);
            }
        }

        private void BuildUi()
        {
            canvas = new GameObject("SessionCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.transform.SetParent(transform, false);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            overlay = new GameObject("SessionOverlay", typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(canvas.transform, false);
            RectTransform overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.68f);

            titleText = CreateText("Title", overlay.transform, 46, TextAlignmentOptions.Center, new Vector2(0.5f, 0.58f), new Vector2(900f, 90f));
            bodyText = CreateText("Body", overlay.transform, 30, TextAlignmentOptions.Center, new Vector2(0.5f, 0.45f), new Vector2(1100f, 260f));
            hudText = CreateText("Hud", canvas.transform, 26, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(520f, 240f));

            RectTransform hudRect = hudText.GetComponent<RectTransform>();
            hudRect.pivot = new Vector2(0f, 1f);
            hudRect.anchoredPosition = new Vector2(24f, -24f);
        }

        private static TMP_Text CreateText(string name, Transform parent, int fontSize, TextAlignmentOptions alignment, Vector2 anchor, Vector2 size)
        {
            GameObject textObject = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);

            RectTransform rectTransform = textObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = anchor;
            rectTransform.anchorMax = anchor;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = size;
            rectTransform.anchoredPosition = Vector2.zero;

            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }
    }
}
