using System.Collections;
using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Networking;
using Player;
using TMPro;
using UI;
using UnityEngine;

[RequireComponent(typeof(FishNet.Object.NetworkObject))]
public class NetworkPlayer : NetworkBehaviour
{
    [Header("Stats")]
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private int attackDamage = 20;
    [SerializeField] private float attackRange = 2.25f;
    [SerializeField] private float attackCooldown = 0.6f;

    [Header("Respawn")]
    [SerializeField] private float respawnDelay = 3f;

    [Header("View")]
    [SerializeField] private Vector3 infoLabelOffset = new(0f, 2.1f, 0f);
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private GameObject visualRoot;

    private readonly SyncVar<string> nickname = new(string.Empty);
    private readonly SyncVar<int> health = new(100);
    private readonly SyncVar<bool> isAlive = new(true);
    private readonly SyncVar<uint> respawnEndTick = new(0u);
    private readonly SyncVar<int> score = new(0);

    private PlayerController playerController;
    private PlayerMovementPrediction playerMovementPrediction;
    private Attack attack;
    private PlayerShooting playerShooting;
    private TextMeshPro infoLabel;
    private Coroutine respawnCoroutine;
    private uint nextServerAttackTick;
    private float nextLocalAttackTime;
    private bool ownershipStateInitialized;
    private bool lastAppliedCanAct;
    private bool lastAppliedAlive;

    public string Nickname => nickname.Value;
    public int CurrentHealth => health.Value;
    public int MaxHealth => maxHealth;
    public bool IsAlive => isAlive.Value;
    public int Score => score.Value;
    public bool CanAct => IsAlive && GameSessionManager.IsGameplayActiveGlobal;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        playerMovementPrediction = GetComponent<PlayerMovementPrediction>();
        attack = GetComponent<Attack>();
        playerShooting = GetComponent<PlayerShooting>();

        nickname.OnChange += HandleNicknameChanged;
        health.OnChange += HandleHealthChanged;
        isAlive.OnChange += HandleAliveChanged;
        respawnEndTick.OnChange += HandleRespawnEndTickChanged;
        score.OnChange += HandleScoreChanged;

        EnsureCameraTarget();
        EnsureVisualRoot();
        EnsureInfoLabel();
    }

    private void OnDestroy()
    {
        nickname.OnChange -= HandleNicknameChanged;
        health.OnChange -= HandleHealthChanged;
        isAlive.OnChange -= HandleAliveChanged;
        respawnEndTick.OnChange -= HandleRespawnEndTickChanged;
        score.OnChange -= HandleScoreChanged;
    }

    public override void OnStartNetwork()
    {
        GameSessionManager.SessionChanged += HandleSessionChanged;

        if (IsServerInitialized)
        {
            health.Value = maxHealth;
            isAlive.Value = true;
            respawnEndTick.Value = 0u;
            nextServerAttackTick = 0u;

            if (string.IsNullOrWhiteSpace(nickname.Value))
            {
                SetNicknameInternal(string.Empty);
            }

            playerShooting?.RefillAmmoOnServer();
        }

        ApplyAliveState(isAlive.Value);
        RefreshInfoLabel();

        if (base.Owner.IsLocalClient)
        {
            SubmitNicknameServerRpc(LocalPlayerProfile.GetNickname());
            BindCamera();
        }
    }

    public override void OnStopNetwork()
    {
        GameSessionManager.SessionChanged -= HandleSessionChanged;

        if (base.Owner.IsLocalClient)
        {
            ClearCamera();
        }

        if (IsServerInitialized && respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
            respawnCoroutine = null;
        }
    }

    private void LateUpdate()
    {
        RefreshSessionStateIfNeeded();

        if (base.Owner.IsLocalClient)
        {
            RefreshInfoLabel();
        }

        UpdateInfoLabelFacing();
    }

    public bool TryQueuePredictedAttack()
    {
        if (!IsClientInitialized || !base.IsOwner || !CanAct || attack == null || !attack.CanPlayAttackAnimation())
        {
            return false;
        }

        if (Time.time < nextLocalAttackTime)
        {
            return false;
        }

        nextLocalAttackTime = Time.time + attackCooldown;
        return true;
    }

    public void ProcessPredictedAttack(bool allowPresentationEvents)
    {
        if (!CanAct || attack == null || playerController == null)
        {
            return;
        }

        if (IsServerInitialized && TimeManager.Tick < nextServerAttackTick)
        {
            return;
        }

        if (!playerController.CanStartAttack(requireLocalControl: false))
        {
            return;
        }

        attack.BeginAttackMovementLock();

        if (allowPresentationEvents && base.IsOwner)
        {
            attack.PlayAttackAnimation(ignoreStateChecks: true, applyMovementLock: false);
        }

        if (!IsServerInitialized)
        {
            return;
        }

        nextServerAttackTick = TimeManager.Tick + TimeManager.TimeToTicks(attackCooldown, TickRounding.RoundUp);
        PlayAttackObserversRpc();

        NetworkPlayer target = FindAttackTarget();
        if (target == null || target == this)
        {
            return;
        }

        Vector3 attackDirection = target.transform.position - transform.position;
        if (attackDirection.sqrMagnitude <= 0.001f)
        {
            attackDirection = transform.forward;
        }

        target.TryApplyDamageOnServer(attackDamage, OwnerId, attackDirection.normalized);
    }

    public bool TryApplyDamageOnServer(int damage, int attackerClientId, Vector3 attackDirection)
    {
        if (!IsServerInitialized || !CanAct || attackerClientId == OwnerId)
        {
            return false;
        }

        int previousHealth = health.Value;
        health.Value = Mathf.Max(0, health.Value - damage);
        if (previousHealth > 0 && health.Value <= 0)
        {
            AwardScoreToAttacker(attackerClientId);
        }

        if (attackDirection.sqrMagnitude <= 0.001f)
        {
            attackDirection = transform.forward;
        }

        playerController?.ApplyDMG(attackDirection.normalized, 250f);
        PlayHitObserversRpc(attackDirection.normalized);
        return true;
    }

    public bool TryRestoreHealthOnServer(int amount)
    {
        if (!IsServerInitialized || !CanAct || health.Value >= maxHealth)
        {
            return false;
        }

        health.Value = Mathf.Min(maxHealth, health.Value + amount);
        return true;
    }

    [ServerRpc]
    private void SubmitNicknameServerRpc(string requestedNickname)
    {
        SetNicknameInternal(requestedNickname);
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void PlayAttackObserversRpc()
    {
        attack?.PlayAttackAnimation(ignoreStateChecks: true, applyMovementLock: false);
    }

    [ObserversRpc(ExcludeServer = true)]
    private void PlayHitObserversRpc(Vector3 attackDirection)
    {
        playerController?.ApplyDMG(attackDirection, 250f);
    }

    [TargetRpc]
    private void ApplyRespawnTransformTargetRpc(NetworkConnection target, Vector3 position, Quaternion rotation)
    {
        playerMovementPrediction?.TeleportTo(position, rotation);
        playerController?.TeleportTo(position, rotation);
    }

    public void SendSessionStateToOwner(GameSessionStateBroadcast snapshot)
    {
        if (!IsServerInitialized || !base.Owner.IsValid || NetworkObject == null || !NetworkObject.IsSpawned)
        {
            return;
        }

        ReceiveSessionStateTargetRpc(
            base.Owner,
            snapshot.State,
            snapshot.ConnectedPlayers,
            snapshot.RequiredPlayers,
            snapshot.MatchTimer,
            snapshot.LiveScoreText,
            snapshot.ResultText);
    }

    [TargetRpc]
    private void ReceiveSessionStateTargetRpc(
        NetworkConnection target,
        int state,
        int connectedPlayers,
        int requiredPlayers,
        float matchTimer,
        string liveScoreText,
        string resultText)
    {
        GameSessionManager.ApplyRemoteState(new GameSessionStateBroadcast
        {
            State = state,
            ConnectedPlayers = connectedPlayers,
            RequiredPlayers = requiredPlayers,
            MatchTimer = matchTimer,
            LiveScoreText = liveScoreText,
            ResultText = resultText
        });
    }

    private void HandleNicknameChanged(string previousValue, string newValue, bool asServer)
    {
        RefreshInfoLabel();
    }

    private void HandleHealthChanged(int previousValue, int newValue, bool asServer)
    {
        RefreshInfoLabel();

        if (!IsServerInitialized || newValue > 0 || !isAlive.Value)
        {
            return;
        }

        HandleDeathOnServer();
    }

    private void HandleAliveChanged(bool previousValue, bool newValue, bool asServer)
    {
        ApplyAliveState(newValue);
        RefreshInfoLabel();
    }

    private void HandleRespawnEndTickChanged(uint previousValue, uint newValue, bool asServer)
    {
        RefreshInfoLabel();
    }

    private void HandleScoreChanged(int previousValue, int newValue, bool asServer)
    {
        RefreshInfoLabel();
    }

    private void HandleSessionChanged()
    {
        RefreshSessionState();
    }

    public void RefreshSessionState()
    {
        SetOwnershipState();
        RefreshInfoLabel();
    }

    public void ResetForMatchOnServer(bool resetScore)
    {
        if (!IsServerInitialized)
        {
            return;
        }

        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
            respawnCoroutine = null;
        }

        if (resetScore)
        {
            score.Value = 0;
        }

        GetRespawnPose(out Vector3 respawnPosition, out Quaternion respawnRotation);
        TeleportForRespawn(respawnPosition, respawnRotation);

        playerShooting?.RefillAmmoOnServer();
        health.Value = maxHealth;
        respawnEndTick.Value = 0u;
        isAlive.Value = true;
    }

    private void HandleDeathOnServer()
    {
        respawnEndTick.Value = TimeManager.Tick + TimeManager.TimeToTicks(respawnDelay, TickRounding.RoundUp);
        isAlive.Value = false;

        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
        }

        respawnCoroutine = StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnDelay);

        GetRespawnPose(out Vector3 respawnPosition, out Quaternion respawnRotation);
        TeleportForRespawn(respawnPosition, respawnRotation);

        playerShooting?.RefillAmmoOnServer();
        health.Value = maxHealth;
        respawnEndTick.Value = 0u;
        isAlive.Value = true;
        respawnCoroutine = null;
    }

    private void TeleportForRespawn(Vector3 position, Quaternion rotation)
    {
        playerMovementPrediction?.TeleportTo(position, rotation);
        playerController?.TeleportTo(position, rotation);

        if (base.Owner.IsLocalClient)
        {
            return;
        }

        ApplyRespawnTransformTargetRpc(base.Owner, position, rotation);
    }

    private void GetRespawnPose(out Vector3 position, out Quaternion rotation)
    {
        PlayerSpawnManager spawnManager = PlayerSpawnManager.Instance;
        if (spawnManager != null)
        {
            spawnManager.TryGetSpawnPose(out position, out rotation);
            return;
        }

        position = transform.position;
        rotation = transform.rotation;
    }

    private void ApplyAliveState(bool alive)
    {
        EnsureVisualRoot();
        if (visualRoot != null)
        {
            visualRoot.SetActive(alive);
        }

        if (alive)
        {
            playerController?.ResetMotionState();
        }

        SetOwnershipState();
    }

    private void SetOwnershipState()
    {
        bool gameplayActive = GameSessionManager.IsGameplayActiveGlobal;
        bool canControl = base.IsOwner && IsAlive && gameplayActive;
        bool shouldEnableController = IsAlive && gameplayActive;

        if (playerController != null)
        {
            playerController.SetLocalControl(canControl);
            playerController.SetControllerEnabled(shouldEnableController);
        }

        if (attack != null)
        {
            attack.SetLocalInputEnabled(canControl);
        }

        if (playerShooting != null)
        {
            playerShooting.SetLocalInputEnabled(canControl);
        }

        ownershipStateInitialized = true;
        lastAppliedCanAct = CanAct;
        lastAppliedAlive = IsAlive;
    }

    private void RefreshSessionStateIfNeeded()
    {
        bool currentCanAct = CanAct;
        bool currentAlive = IsAlive;
        if (!ownershipStateInitialized || currentCanAct != lastAppliedCanAct || currentAlive != lastAppliedAlive)
        {
            SetOwnershipState();
        }
    }

    public void RefreshInfoLabel()
    {
        EnsureInfoLabel();
        if (infoLabel == null)
        {
            return;
        }

        string playerName = string.IsNullOrWhiteSpace(Nickname) ? GetDefaultNickname() : Nickname;
        string status = BuildStatusText();
        infoLabel.text = $"{playerName}\n{status}";
    }

    private string BuildStatusText()
    {
        if (base.IsOwner)
        {
            if (IsAlive)
            {
                string status = playerShooting != null
                    ? $"HP: {CurrentHealth} | Ammo: {playerShooting.CurrentAmmo}/{playerShooting.MaxAmmo}"
                    : $"HP: {CurrentHealth}";
                return $"{status} | Score: {Score}";
            }

            uint currentTick = TimeManager.Tick;
            double remainingSeconds = respawnEndTick.Value > currentTick
                ? TimeManager.TicksToTime(respawnEndTick.Value - currentTick)
                : 0d;

            return $"Respawn: {remainingSeconds:0.0}s | Score: {Score}";
        }

        return IsAlive ? $"HP: {CurrentHealth} | Score: {Score}" : $"DEAD | Score: {Score}";
    }

    private void EnsureInfoLabel()
    {
        if (infoLabel != null)
        {
            return;
        }

        GameObject labelObject = new("PlayerInfoLabel");
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = infoLabelOffset;
        labelObject.transform.localScale = Vector3.one * 0.2f;

        infoLabel = labelObject.AddComponent<TextMeshPro>();
        infoLabel.alignment = TextAlignmentOptions.Center;
        infoLabel.fontSize = 4f;
        infoLabel.textWrappingMode = TextWrappingModes.NoWrap;
        infoLabel.color = Color.white;
        infoLabel.text = "Player\nHP: 0";
    }

    private void UpdateInfoLabelFacing()
    {
        if (infoLabel == null || Camera.main == null)
        {
            return;
        }

        Transform labelTransform = infoLabel.transform;
        Vector3 directionToCamera = Camera.main.transform.position - labelTransform.position;
        if (directionToCamera.sqrMagnitude > 0.001f)
        {
            labelTransform.rotation = Quaternion.LookRotation(directionToCamera.normalized) * Quaternion.Euler(0f, 180f, 0f);
        }
    }

    private void EnsureVisualRoot()
    {
        if (visualRoot != null)
        {
            return;
        }

        if (playerController != null && playerController.childPlayer != null)
        {
            visualRoot = playerController.childPlayer;
            return;
        }

        Transform childTransform = transform.Find("unitychan");
        if (childTransform != null)
        {
            visualRoot = childTransform.gameObject;
        }
    }

    private void EnsureCameraTarget()
    {
        if (cameraTarget != null)
        {
            return;
        }

        Transform pivot = transform.Find("Pivot");
        cameraTarget = pivot != null ? pivot : transform;
    }

    private void BindCamera()
    {
        EnsureCameraTarget();

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return;
        }

        playerController?.AssignCamera(mainCamera);

        CameraOrbit orbit = mainCamera.GetComponent<CameraOrbit>();
        if (orbit != null)
        {
            orbit.SetTarget(cameraTarget);
        }
    }

    private void ClearCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return;
        }

        CameraOrbit orbit = mainCamera.GetComponent<CameraOrbit>();
        if (orbit != null)
        {
            orbit.ClearTarget();
        }
    }

    private NetworkPlayer FindAttackTarget()
    {
        NetworkPlayer closestTarget = null;
        float closestDistance = attackRange;

        foreach (NetworkPlayer otherPlayer in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (otherPlayer == this || otherPlayer.OwnerId == OwnerId || !otherPlayer.IsAlive)
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, otherPlayer.transform.position);
            if (distance > closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            closestTarget = otherPlayer;
        }

        return closestTarget;
    }

    private void AwardScoreToAttacker(int attackerClientId)
    {
        foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.OwnerId != attackerClientId)
            {
                continue;
            }

            player.AddScoreOnServer(1);
            GameSessionManager.Instance?.NotifyScoreChanged();
            return;
        }
    }

    private void AddScoreOnServer(int amount)
    {
        if (!IsServerInitialized)
        {
            return;
        }

        score.Value = Mathf.Max(0, score.Value + amount);
    }

    private void SetNicknameInternal(string requestedNickname)
    {
        string nicknameValue = requestedNickname == null ? string.Empty : requestedNickname.Trim();
        nickname.Value = string.IsNullOrWhiteSpace(nicknameValue)
            ? GetDefaultNickname()
            : nicknameValue;
    }

    private string GetDefaultNickname()
    {
        return $"Player {OwnerId + 1}";
    }
}
