using System.Collections;
using Networking;
using Player;
using TMPro;
using UI;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
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
    [SerializeField] private Vector3 infoLabelOffset = new Vector3(0f, 2.1f, 0f);
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private GameObject visualRoot;

    private readonly NetworkVariable<FixedString64Bytes> nickname =
        new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> health =
        new(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> isAlive =
        new(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<double> respawnEndTime =
        new(0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private PlayerController playerController;
    private Attack attack;
    private PlayerShooting playerShooting;
    private OwnerNetworkTransform ownerNetworkTransform;
    private TextMeshPro infoLabel;
    private Coroutine respawnCoroutine;
    private double nextServerAttackTime;
    private float nextLocalAttackTime;
    private Vector3 spawnDebugInitialPosition;
    private bool watchForUnexpectedOriginSnap;
    private bool unexpectedOriginSnapLogged;

    public string Nickname => nickname.Value.ToString();
    public int CurrentHealth => health.Value;
    public int MaxHealth => maxHealth;
    public bool IsAlive => isAlive.Value;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        attack = GetComponent<Attack>();
        playerShooting = GetComponent<PlayerShooting>();
        ownerNetworkTransform = GetComponent<OwnerNetworkTransform>();
        EnsureCameraTarget();
        EnsureVisualRoot();
        EnsureInfoLabel();
    }

    public override void OnNetworkSpawn()
    {
        nickname.OnValueChanged += HandleNicknameChanged;
        health.OnValueChanged += HandleHealthChanged;
        isAlive.OnValueChanged += HandleAliveChanged;
        respawnEndTime.OnValueChanged += HandleRespawnEndTimeChanged;

        if (IsServer)
        {
            health.Value = maxHealth;
            isAlive.Value = true;
            respawnEndTime.Value = 0d;
            if (nickname.Value.Length == 0)
            {
                SetNicknameInternal(default);
            }

            playerShooting?.RefillAmmoOnServer();
        }

        ApplyAliveState(isAlive.Value);
        RefreshInfoLabel();

        if (IsOwner)
        {
            SubmitNicknameServerRpc(new FixedString64Bytes(LocalPlayerProfile.GetNickname()));
            BindCamera();
            spawnDebugInitialPosition = transform.position;
            watchForUnexpectedOriginSnap = transform.position.sqrMagnitude > 25f;
            unexpectedOriginSnapLogged = false;
        }

        Debug.Log(
            $"NetworkPlayer.OnNetworkSpawn owner={OwnerClientId} local={NetworkManager.LocalClientId} " +
            $"isServer={IsServer} isOwner={IsOwner} transform={transform.position}");
    }

    public override void OnNetworkDespawn()
    {
        nickname.OnValueChanged -= HandleNicknameChanged;
        health.OnValueChanged -= HandleHealthChanged;
        isAlive.OnValueChanged -= HandleAliveChanged;
        respawnEndTime.OnValueChanged -= HandleRespawnEndTimeChanged;

        if (IsOwner)
        {
            ClearCamera();
        }

        if (IsServer && respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
            respawnCoroutine = null;
        }

        watchForUnexpectedOriginSnap = false;
    }

    private void LateUpdate()
    {
        if (watchForUnexpectedOriginSnap && !unexpectedOriginSnapLogged && transform.position.sqrMagnitude < 4f)
        {
            unexpectedOriginSnapLogged = true;
            Debug.LogWarning(
                $"NetworkPlayer unexpected move near origin. owner={OwnerClientId} local={NetworkManager.LocalClientId} " +
                $"current={transform.position} initial={spawnDebugInitialPosition}");
        }

        if (IsOwner)
        {
            RefreshInfoLabel();
        }

        UpdateInfoLabelFacing();
    }

    public bool TryPerformAttack()
    {
        if (!IsSpawned || !IsOwner || !IsAlive || attack == null || !attack.CanPlayAttackAnimation())
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

    public bool TryApplyDamageOnServer(int damage, ulong attackerClientId, Vector3 attackDirection)
    {
        if (!IsServer || !IsAlive || attackerClientId == OwnerClientId)
        {
            return false;
        }

        health.Value = Mathf.Max(0, health.Value - damage);

        if (attackDirection.sqrMagnitude <= 0.001f)
        {
            attackDirection = transform.forward;
        }

        PlayHitClientRpc(attackDirection.normalized);
        return true;
    }

    public bool TryRestoreHealthOnServer(int amount)
    {
        if (!IsServer || !IsAlive || health.Value >= maxHealth)
        {
            return false;
        }

        health.Value = Mathf.Min(maxHealth, health.Value + amount);
        return true;
    }

    [ServerRpc]
    private void SubmitNicknameServerRpc(FixedString64Bytes requestedNickname, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        SetNicknameInternal(requestedNickname);
    }

    [ServerRpc]
    private void RequestAttackServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || !IsAlive)
        {
            return;
        }

        double serverTime = NetworkManager.ServerTime.Time;
        if (serverTime < nextServerAttackTime)
        {
            return;
        }

        nextServerAttackTime = serverTime + attackCooldown;
        PlayAttackClientRpc();

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

        target.TryApplyDamageOnServer(attackDamage, OwnerClientId, attackDirection.normalized);
    }

    [ClientRpc]
    private void PlayAttackClientRpc()
    {
        if (IsOwner || attack == null)
        {
            return;
        }

        attack.PlayAttackAnimation(true);
    }

    [ClientRpc]
    private void PlayHitClientRpc(Vector3 attackDirection)
    {
        if (playerController != null)
        {
            playerController.ApplyDMG(attackDirection, 250f);
        }
    }

    [ClientRpc]
    private void ApplyRespawnTransformClientRpc(Vector3 position, Quaternion rotation, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
        {
            return;
        }

        playerController?.SetNetworkTransform(position, rotation, true);
        playerController?.ResetMotionState();

        if (ownerNetworkTransform != null)
        {
            ownerNetworkTransform.TeleportTo(position, rotation);
        }
        else
        {
            transform.SetPositionAndRotation(position, rotation);
        }
    }

    private void HandleNicknameChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
    {
        RefreshInfoLabel();
    }

    private void HandleHealthChanged(int previousValue, int newValue)
    {
        RefreshInfoLabel();

        if (!IsServer || newValue > 0 || !isAlive.Value)
        {
            return;
        }

        HandleDeathOnServer();
    }

    private void HandleAliveChanged(bool previousValue, bool newValue)
    {
        ApplyAliveState(newValue);
        RefreshInfoLabel();
    }

    private void HandleRespawnEndTimeChanged(double previousValue, double newValue)
    {
        RefreshInfoLabel();
    }

    private void HandleDeathOnServer()
    {
        respawnEndTime.Value = NetworkManager.ServerTime.Time + respawnDelay;
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
        respawnEndTime.Value = 0d;
        isAlive.Value = true;
        respawnCoroutine = null;
    }

    private void TeleportForRespawn(Vector3 position, Quaternion rotation)
    {
        playerController?.SetNetworkTransform(position, rotation, true);
        playerController?.ResetMotionState();

        if (ownerNetworkTransform != null && ownerNetworkTransform.CanCommitToTransform)
        {
            ownerNetworkTransform.TeleportTo(position, rotation);
            return;
        }

        transform.SetPositionAndRotation(position, rotation);

        if (!IsServer || OwnerClientId == NetworkManager.ServerClientId)
        {
            return;
        }

        ClientRpcParams rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };

        ApplyRespawnTransformClientRpc(position, rotation, rpcParams);
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
        bool canControl = IsOwner && IsAlive;
        bool shouldEnableController = IsAlive && (IsServer || IsOwner);

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

        Debug.Log(
            $"NetworkPlayer.SetOwnershipState owner={OwnerClientId} local={NetworkManager.LocalClientId} " +
            $"isOwner={IsOwner} isAlive={IsAlive} transform={transform.position} " +
            $"controllerEnabled={(playerController != null && playerController.IsControllerEnabled)}");
    }

    private void RefreshInfoLabel()
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
        if (IsOwner)
        {
            if (IsAlive)
            {
                return playerShooting != null
                    ? $"HP: {CurrentHealth} | Ammo: {playerShooting.CurrentAmmo}/{playerShooting.MaxAmmo}"
                    : $"HP: {CurrentHealth}";
            }

            double remainingSeconds = Mathf.Max(0f, (float)(respawnEndTime.Value - NetworkManager.ServerTime.Time));
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

        GameObject labelObject = new GameObject("PlayerInfoLabel");
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

        if (playerController != null)
        {
            playerController.AssignCamera(mainCamera);
        }

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
            if (otherPlayer == this || otherPlayer.OwnerClientId == OwnerClientId || !otherPlayer.IsAlive)
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

    private void SetNicknameInternal(FixedString64Bytes requestedNickname)
    {
        string nicknameValue = requestedNickname.ToString().Trim();
        nickname.Value = string.IsNullOrWhiteSpace(nicknameValue)
            ? new FixedString64Bytes(GetDefaultNickname())
            : new FixedString64Bytes(nicknameValue);
    }

    private string GetDefaultNickname()
    {
        return $"Player {OwnerClientId + 1}";
    }
}
