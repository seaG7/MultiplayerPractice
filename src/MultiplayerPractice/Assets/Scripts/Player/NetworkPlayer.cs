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

    private PlayerController playerController;
    private PlayerMovementPrediction playerMovementPrediction;
    private Attack attack;
    private PlayerShooting playerShooting;
    private TextMeshPro infoLabel;
    private Coroutine respawnCoroutine;
    private uint nextServerAttackTick;
    private float nextLocalAttackTime;

    public string Nickname => nickname.Value;
    public int CurrentHealth => health.Value;
    public int MaxHealth => maxHealth;
    public bool IsAlive => isAlive.Value;

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
    }

    public override void OnStartNetwork()
    {
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
        if (base.Owner.IsLocalClient)
        {
            RefreshInfoLabel();
        }

        UpdateInfoLabelFacing();
    }

    public bool TryPerformAttack()
    {
        if (!IsClientInitialized || !base.IsOwner || !IsAlive || attack == null || !attack.CanPlayAttackAnimation())
        {
            return false;
        }

        if (Time.time < nextLocalAttackTime)
        {
            return false;
        }

        nextLocalAttackTime = Time.time + attackCooldown;
        attack.PlayAttackAnimation();
        RequestAttackServerRpc();
        return true;
    }

    public bool TryApplyDamageOnServer(int damage, int attackerClientId, Vector3 attackDirection)
    {
        if (!IsServerInitialized || !IsAlive || attackerClientId == OwnerId)
        {
            return false;
        }

        health.Value = Mathf.Max(0, health.Value - damage);

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
        if (!IsServerInitialized || !IsAlive || health.Value >= maxHealth)
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

    [ServerRpc]
    private void RequestAttackServerRpc()
    {
        if (!IsAlive)
        {
            return;
        }

        if (TimeManager.Tick < nextServerAttackTick)
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

    [ObserversRpc(ExcludeOwner = true)]
    private void PlayAttackObserversRpc()
    {
        attack?.PlayAttackAnimation(true);
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
        bool canControl = base.IsOwner && IsAlive;
        bool shouldEnableController = IsAlive;

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
                return playerShooting != null
                    ? $"HP: {CurrentHealth} | Ammo: {playerShooting.CurrentAmmo}/{playerShooting.MaxAmmo}"
                    : $"HP: {CurrentHealth}";
            }

            uint currentTick = TimeManager.Tick;
            double remainingSeconds = respawnEndTick.Value > currentTick
                ? TimeManager.TicksToTime(respawnEndTick.Value - currentTick)
                : 0d;

            return $"Respawn: {remainingSeconds:0.0}s";
        }

        return IsAlive ? $"HP: {CurrentHealth}" : "DEAD";
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
