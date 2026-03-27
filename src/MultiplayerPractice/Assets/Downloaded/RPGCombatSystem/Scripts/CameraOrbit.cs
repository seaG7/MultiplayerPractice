using UnityEngine;

public class CameraOrbit : MonoBehaviour
{
    public Transform refCamera;
    public Vector3 offsetCamera = new Vector3(0f, 0.5f, 0f);
    public float distance = 2.5f;
    public float xSpeed = 400f;
    public float ySpeed = 80f;
    public float yMinLimit = -20f;
    public float yMaxLimit = 80f;
    public bool zoom;
    public float zoomSpeed = 120f;

    private float x;
    private float y;
    private bool cursorLocked;

    private void Start()
    {
        Vector3 angles = transform.eulerAngles;
        x = angles.y;
        y = angles.x;
        ApplyCursorState(refCamera != null);
    }

    private void LateUpdate()
    {
        if (refCamera == null)
        {
            ApplyCursorState(false);
            return;
        }

        ApplyCursorState(true);

        x += Input.GetAxis("Mouse X") * xSpeed * Time.deltaTime;
        if (zoom)
        {
            distance += Input.GetAxis("Mouse Y");
        }
        else
        {
            y -= Input.GetAxis("Mouse Y") * zoomSpeed * Time.deltaTime;
        }

        y = ClampAngle(y, yMinLimit, yMaxLimit);

        Quaternion rotation = Quaternion.Euler(y, x, 0f);
        Vector3 targetPosition = refCamera.position + offsetCamera;
        Vector3 desiredPosition = rotation * new Vector3(0f, 0f, -distance) + targetPosition;

        transform.rotation = rotation;
        transform.position = desiredPosition;

        float rayDistance = distance + 1f;
        Ray ray = new Ray(targetPosition, transform.position - targetPosition);
        if (Physics.Raycast(ray, out RaycastHit hitInfo, rayDistance)
            && !hitInfo.transform.CompareTag("Player")
            && !hitInfo.transform.CompareTag("Weapon")
            && !hitInfo.transform.CompareTag("Enemy"))
        {
            rayDistance = hitInfo.distance - 1f;
        }

        rayDistance = Mathf.Clamp(rayDistance, 0f, distance);
        transform.position = ray.GetPoint(rayDistance);
    }

    public void SetTarget(Transform target)
    {
        refCamera = target;
        ApplyCursorState(refCamera != null);
    }

    public void ClearTarget()
    {
        refCamera = null;
        ApplyCursorState(false);
    }

    private void ApplyCursorState(bool shouldLock)
    {
        if (cursorLocked == shouldLock)
        {
            return;
        }

        cursorLocked = shouldLock;
        Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !shouldLock;
    }

    private float ClampAngle(float angle, float min, float max)
    {
        if (angle < -360f)
        {
            angle += 360f;
        }
        if (angle > 360f)
        {
            angle -= 360f;
        }

        return Mathf.Clamp(angle, min, max);
    }
}
