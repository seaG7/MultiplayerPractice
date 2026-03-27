using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TextCore.Text;

[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayer : NetworkBehaviour
{
    [Header("Stats")]
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private int attackDamage = 20;
    [SerializeField] private float attackRange = 2.25f;
    [SerializeField] private float attackCooldown = 0.6f;

    [Header("Sync")]
    [SerializeField] private float positionThreshold = 0.02f;
    [SerializeField] private float rotationThreshold = 1f;

    [Header("View")]
    [SerializeField] private Vector3 infoLabelOffset = new Vector3(0f, 2.1f, 0f);
    [SerializeField] private Transform cameraTarget;

    private readonly NetworkVariable<FixedString64Bytes> nickname =
        new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> health =
        new(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<Vector3> syncedPosition =
        new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<Quaternion> syncedRotation =
        new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private PlayerController playerController;
    private Attack attack;
    private TextMeshPro infoLabel;
    private double nextServerAttackTime;
    private float nextLocalAttackTime;

    public string Nickname => nickname.Value.ToString();
    public int CurrentHealth => health.Value;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        attack = GetComponent<Attack>();
        EnsureCameraTarget();
        EnsureInfoLabel();
    }

    public override void OnNetworkSpawn()
    {
        nickname.OnValueChanged += HandleNicknameChanged;
        health.OnValueChanged += HandleHealthChanged;
        syncedPosition.OnValueChanged += HandlePositionChanged;
        syncedRotation.OnValueChanged += HandleRotationChanged;

        if (IsServer)
        {
            health.Value = maxHealth;
            if (nickname.Value.Length == 0)
            {
                SetNicknameInternal(default);
            }
        }

        SetOwnershipState();
        RefreshInfoLabel();

        if (IsOwner)
        {
            syncedPosition.Value = transform.position;
            syncedRotation.Value = transform.rotation;
            SubmitNicknameServerRpc(new FixedString64Bytes(LocalPlayerProfile.GetNickname()));
            BindCamera();
        }
    }

    public override void OnNetworkDespawn()
    {
        nickname.OnValueChanged -= HandleNicknameChanged;
        health.OnValueChanged -= HandleHealthChanged;
        syncedPosition.OnValueChanged -= HandlePositionChanged;
        syncedRotation.OnValueChanged -= HandleRotationChanged;

        if (IsOwner)
        {
            ClearCamera();
        }
    }

    private void LateUpdate()
    {
        if (IsSpawned && IsOwner)
        {
            PushTransformState();
        }

        UpdateInfoLabelFacing();
    }

    public bool TryPerformAttack()
    {
        if (!IsSpawned || !IsOwner || attack == null || !attack.CanPlayAttackAnimation())
        {
            return false;
        }

        if (Time.time < nextLocalAttackTime || health.Value <= 0)
        {
            return false;
        }

        nextLocalAttackTime = Time.time + attackCooldown;
        attack.PlayAttackAnimation();
        RequestAttackServerRpc();
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
        if (rpcParams.Receive.SenderClientId != OwnerClientId || health.Value <= 0)
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

        target.ApplyDamageOnServer(attackDamage, transform.position, OwnerClientId);
    }

    [ClientRpc]
    private void PlayAttackClientRpc()
    {
        if (IsOwner || attack == null)
        {
            return;
        }

        attack.PlayAttackAnimation();
    }

    [ClientRpc]
    private void PlayHitClientRpc(Vector3 attackDirection)
    {
        if (playerController != null)
        {
            playerController.ApplyDMG(attackDirection, 250f);
        }
    }

    private void ApplyDamageOnServer(int damage, Vector3 attackerPosition, ulong attackerClientId)
    {
        if (!IsServer || attackerClientId == OwnerClientId || health.Value <= 0)
        {
            return;
        }

        health.Value = Mathf.Max(0, health.Value - damage);

        Vector3 attackDirection = transform.position - attackerPosition;
        if (attackDirection.sqrMagnitude <= 0.001f)
        {
            attackDirection = transform.forward;
        }

        PlayHitClientRpc(attackDirection.normalized);
    }

    private void SetOwnershipState()
    {
        if (playerController != null)
        {
            playerController.SetLocalControl(IsOwner);
        }

        if (attack != null)
        {
            attack.SetLocalInputEnabled(IsOwner);
        }
    }

    private void PushTransformState()
    {
        if (Vector3.Distance(syncedPosition.Value, transform.position) > positionThreshold)
        {
            syncedPosition.Value = transform.position;
        }

        if (Quaternion.Angle(syncedRotation.Value, transform.rotation) > rotationThreshold)
        {
            syncedRotation.Value = transform.rotation;
        }
    }

    private void HandlePositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        if (!IsOwner)
        {
            transform.position = newValue;
        }
    }

    private void HandleRotationChanged(Quaternion previousValue, Quaternion newValue)
    {
        if (!IsOwner)
        {
            transform.rotation = newValue;
        }
    }

    private void ApplyRemoteTransform(Vector3 positionValue, Quaternion rotationValue)
    {
        if (IsOwner)
        {
            return;
        }

        transform.position = positionValue;
        transform.rotation = rotationValue;
    }

    private void HandleNicknameChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
    {
        RefreshInfoLabel();
    }

    private void HandleHealthChanged(int previousValue, int newValue)
    {
        RefreshInfoLabel();
    }

    private void RefreshInfoLabel()
    {
        EnsureInfoLabel();
        if (infoLabel == null)
        {
            return;
        }

        string playerName = string.IsNullOrWhiteSpace(Nickname) ? GetDefaultNickname() : Nickname;
        infoLabel.text = $"{playerName}\nHP: {CurrentHealth}";
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
            labelTransform.rotation = Quaternion.LookRotation(directionToCamera.normalized);
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
            if (otherPlayer == this || otherPlayer.OwnerClientId == OwnerClientId || otherPlayer.CurrentHealth <= 0)
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
