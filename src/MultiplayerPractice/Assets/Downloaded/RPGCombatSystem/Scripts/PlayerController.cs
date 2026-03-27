using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [HideInInspector] public CharacterController charCont;
    [HideInInspector] public Animator anim;
    public GameObject childPlayer;
    public Camera cam;
    public GameObject movIndicator;
    [HideInInspector] public SoundManager soundMan;

    public float speed = 6.0f;
    public float jumpSpeed = 8.0f;
    public float gravity = 20.0f;
    public bool airControl = true;
    public float maxDashTime = 0.25f;
    public float dashSpeed = 20f;
    [HideInInspector] public bool canMove = true;

    private bool canJump = true;
    private bool wasGrounded;
    private readonly float mass = 60.0f;
    private Vector3 impact = Vector3.zero;
    private float fallTime;
    private float currentDashTime;
    private Vector3 dashDir;
    private bool canDash = true;
    private Vector3 moveDirection = Vector3.zero;
    private float distToGround;
    private Vector3 groundNormal;
    private bool hit;
    private bool localInputEnabled = true;

    public Vector3 FacingForward => childPlayer != null ? childPlayer.transform.forward : transform.forward;

    private void Awake()
    {
        charCont = GetComponent<CharacterController>();
        soundMan = GetComponent<SoundManager>();
        anim = GetComponentInChildren<Animator>();
        currentDashTime = maxDashTime;
        distToGround = charCont.bounds.extents.y;
    }

    private void Update()
    {
        UpdateGroundState();

        if (!localInputEnabled || !canMove)
        {
            UpdateHitRecovery();
            return;
        }

        if (charCont.isGrounded)
        {
            HandleGroundMovement();
        }
        else
        {
            HandleAirMovement();
        }

        moveDirection.y -= gravity * Time.deltaTime;
        charCont.Move(moveDirection * Time.deltaTime);
    }

    private void UpdateGroundState()
    {
        if (charCont.isGrounded)
        {
            if (!wasGrounded)
            {
                canJump = true;
                anim.SetBool("Jump", false);
                if (fallTime > 0.2f)
                {
                    soundMan.PlaySound("Land");
                    if (!hit)
                    {
                        anim.CrossFade("FallingEnd", 0.1f);
                    }
                }

                fallTime = 0f;
            }
        }
        else
        {
            anim.SetFloat("SpeedY", charCont.velocity.y);
            if (wasGrounded && canJump && DistToGround() > 0.3f)
            {
                moveDirection.y = 0f;
                wasGrounded = false;
                anim.SetBool("Jump", true);
                anim.CrossFade("Falling", 0.2f);
            }

            if (charCont.velocity.y < 0f)
            {
                fallTime += Time.deltaTime;
            }
        }

        wasGrounded = charCont.isGrounded;
    }

    private void UpdateHitRecovery()
    {
        if (!hit)
        {
            return;
        }

        moveDirection.y -= gravity * Time.deltaTime;
        Vector3 impactWithGravity = new Vector3(impact.x, impact.y + moveDirection.y, impact.z);
        if (impact.magnitude > 0.2f || !charCont.isGrounded)
        {
            charCont.Move(impactWithGravity * Time.deltaTime);
        }

        impact = Vector3.Lerp(impact, Vector3.zero, 5f * Time.deltaTime);
        if (!charCont.isGrounded || impact.magnitude > 0.2f)
        {
            return;
        }

        hit = false;
        canMove = true;
        anim.Play("Idle");
    }

    private void HandleGroundMovement()
    {
        moveDirection = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
        if (moveDirection.magnitude < 0.1f)
        {
            moveDirection = Vector3.zero;
        }

        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);
        anim.SetFloat("Speed", moveDirection.magnitude);

        if (movIndicator != null)
        {
            movIndicator.transform.localPosition = moveDirection;
        }

        HandleDash();
        if (currentDashTime < maxDashTime)
        {
            return;
        }

        if (moveDirection.magnitude > 0f)
        {
            AlignToCamera();
            RotateVisualTowardsIndicator();
        }

        moveDirection = transform.TransformDirection(moveDirection) * speed;
        moveDirection.y = -10f;

        if (!IsGrounded())
        {
            moveDirection.x += (1f - groundNormal.y) * groundNormal.x;
            moveDirection.z += (1f - groundNormal.y) * groundNormal.z;
        }

        if (Input.GetButtonDown("Jump") && canJump)
        {
            moveDirection.y = jumpSpeed;
            canJump = false;
            anim.SetFloat("SpeedY", moveDirection.y);
            anim.Play("Falling");
            anim.SetBool("Jump", true);
            soundMan.PlaySound("Jump");
        }
    }

    private void HandleAirMovement()
    {
        if (currentDashTime < maxDashTime)
        {
            currentDashTime = maxDashTime;
            canDash = true;
        }

        if (!airControl)
        {
            return;
        }

        Vector3 moveDirectionTemp = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
        moveDirectionTemp = Vector3.ClampMagnitude(moveDirectionTemp, 1f);
        moveDirection = new Vector3(moveDirectionTemp.x, moveDirection.y, moveDirectionTemp.z);

        if (movIndicator != null)
        {
            movIndicator.transform.localPosition = new Vector3(moveDirection.x, 0f, moveDirection.z);
        }

        if (moveDirectionTemp.magnitude > 0f)
        {
            AlignToCamera();
            RotateVisualTowardsIndicator();
        }

        moveDirection = transform.TransformDirection(moveDirection);
        moveDirection = new Vector3(moveDirection.x * speed * 0.8f, moveDirection.y, moveDirection.z * speed * 0.8f);
    }

    private void HandleDash()
    {
        if (Input.GetButtonDown("Dash") && canDash)
        {
            currentDashTime = 0f;
            canDash = false;
            anim.Play("Slide");
            soundMan.PlaySound("Dash");

            if (moveDirection != Vector3.zero)
            {
                dashDir = transform.TransformDirection(moveDirection).normalized;
                RotateVisualTowardsIndicator(true);
            }
            else
            {
                dashDir = FacingForward;
            }
        }

        if (currentDashTime >= maxDashTime)
        {
            canDash = true;
            return;
        }

        dashDir.y = -10f;
        currentDashTime += Time.deltaTime;
        charCont.Move(dashDir * Time.deltaTime * dashSpeed);
    }

    private void AlignToCamera()
    {
        if (cam == null)
        {
            return;
        }

        Vector3 eulerAngles = charCont.transform.eulerAngles;
        charCont.transform.rotation = Quaternion.Euler(eulerAngles.x, cam.transform.eulerAngles.y, eulerAngles.z);
    }

    private void RotateVisualTowardsIndicator(bool snap = false)
    {
        if (childPlayer == null || movIndicator == null)
        {
            return;
        }

        Vector3 targetPosition = new Vector3(
            movIndicator.transform.position.x,
            childPlayer.transform.position.y,
            movIndicator.transform.position.z);

        Vector3 lookDirection = targetPosition - childPlayer.transform.position;
        if (lookDirection.sqrMagnitude <= 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
        childPlayer.transform.rotation = snap
            ? targetRotation
            : Quaternion.Slerp(childPlayer.transform.rotation, targetRotation, Time.deltaTime * 10f);
    }

    public void AddImpact(Vector3 dir, float force)
    {
        moveDirection = Vector3.zero;
        anim.Play("Hit");

        dir.Normalize();
        if (dir.y < 0f)
        {
            dir.y = -dir.y;
        }

        impact += dir.normalized * force / mass;
    }

    public void ApplyDMG(Vector3 dir, float force)
    {
        if (hit)
        {
            return;
        }

        hit = true;
        canMove = false;
        soundMan.PlaySound("Hit");
        currentDashTime = maxDashTime;
        anim.SetFloat("Speed", 0f);
        anim.GetComponent<AnimatorEvents>().DisableWeaponColl();
        AddImpact(dir, force);
    }

    public void SetLocalControl(bool enabled)
    {
        localInputEnabled = enabled;
        if (enabled)
        {
            return;
        }

        moveDirection = Vector3.zero;
        anim.SetFloat("Speed", 0f);
        currentDashTime = maxDashTime;
        canDash = true;
    }

    public void AssignCamera(Camera targetCamera)
    {
        cam = targetCamera;
    }

    public bool CanStartAttack()
    {
        return localInputEnabled && canMove && charCont.isGrounded && !IsDashing();
    }

    public void PlaySound(string soundName)
    {
        soundMan.PlaySound(soundName);
    }

    private float DistToGround()
    {
        if (Physics.Raycast(transform.position, -Vector3.up, out RaycastHit hitInfo, distToGround + 999f))
        {
            return hitInfo.distance - distToGround;
        }

        return 999f;
    }

    private bool IsGrounded()
    {
        return Physics.Raycast(transform.position, -Vector3.up, distToGround + 0.1f);
    }

    private void OnControllerColliderHit(ControllerColliderHit hitInfo)
    {
        groundNormal = hitInfo.normal;
    }

    public bool IsDashing()
    {
        return currentDashTime < maxDashTime;
    }

    public void EnableMove(bool camMoveT)
    {
        if (!hit)
        {
            canMove = camMoveT;
        }
    }
}
