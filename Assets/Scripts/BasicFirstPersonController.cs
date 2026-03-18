using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class BasicFirstPersonController : MonoBehaviour
{
    public Camera playerCamera;
    public Transform cameraPivot;
    public Transform startAnchor;
    public Transform startLookTarget;
    public float walkSpeed = 4f;
    public float sprintSpeed = 6.5f;
    public float jumpHeight = 1f;
    public float gravity = -20f;
    public float mouseSensitivity = 2f;
    public bool lockCursorOnStart = true;
    public float spawnSnapHeight = 20f;
    public float spawnSnapDistance = 80f;

    CharacterController _controller;
    float _pitch;
    Vector3 _velocity;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    void Start()
    {
        ApplyStartPose();
        ActivatePlayerView();
        SnapToGround();
        if (lockCursorOnStart)
            SetCursorLocked(true);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            SetCursorLocked(false);
        else if (Input.GetMouseButtonDown(0))
            SetCursorLocked(true);

        HandleLook();
        HandleMovement();
    }

    void HandleLook()
    {
        if (cameraPivot == null || Cursor.lockState != CursorLockMode.Locked)
            return;

        var mouseX = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        var mouseY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

        _pitch = Mathf.Clamp(_pitch - mouseY, -85f, 85f);
        cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }

    void HandleMovement()
    {
        var inputX = Input.GetAxisRaw("Horizontal");
        var inputZ = Input.GetAxisRaw("Vertical");
        var input = (transform.right * inputX + transform.forward * inputZ).normalized;
        var speed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;

        if (_controller.isGrounded && _velocity.y < 0f)
            _velocity.y = -2f;

        if (_controller.isGrounded && Input.GetButtonDown("Jump"))
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

        _controller.Move(input * speed * Time.deltaTime);

        _velocity.y += gravity * Time.deltaTime;
        _controller.Move(_velocity * Time.deltaTime);
    }

    static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void ActivatePlayerView()
    {
        if (playerCamera == null && cameraPivot != null)
            playerCamera = cameraPivot.GetComponentInChildren<Camera>(true);

        var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (var i = 0; i < cameras.Length; i++)
        {
            var camera = cameras[i];
            if (camera == null)
                continue;

            camera.enabled = camera == playerCamera;
        }

        if (playerCamera != null)
        {
            playerCamera.enabled = true;
            playerCamera.tag = "MainCamera";

            var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            for (var i = 0; i < listeners.Length; i++)
                listeners[i].enabled = listeners[i].gameObject == playerCamera.gameObject;
        }
    }

    void ApplyStartPose()
    {
        if (startAnchor != null)
        {
            transform.position = startAnchor.position;
            transform.rotation = startAnchor.rotation;
        }

        if (cameraPivot == null)
            return;

        cameraPivot.localRotation = Quaternion.identity;
        _pitch = 0f;

        if (startLookTarget == null)
            return;

        var toTarget = startLookTarget.position - cameraPivot.position;
        if (toTarget.sqrMagnitude < 0.0001f)
            return;

        var flatForward = toTarget;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(flatForward.normalized, Vector3.up);

        var localTarget = transform.InverseTransformPoint(startLookTarget.position);
        var pitch = Mathf.Atan2(localTarget.y, new Vector2(localTarget.x, localTarget.z).magnitude) * Mathf.Rad2Deg;
        _pitch = Mathf.Clamp(-pitch, -85f, 85f);
        cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    void SnapToGround()
    {
        var rayOrigin = transform.position + Vector3.up * spawnSnapHeight;
        if (!Physics.Raycast(rayOrigin, Vector3.down, out var hit, spawnSnapDistance, ~0, QueryTriggerInteraction.Ignore))
            return;

        var position = transform.position;
        position.y = hit.point.y + _controller.skinWidth;
        transform.position = position;
    }
}
