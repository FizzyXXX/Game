using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleRigidbodyController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float jumpForce = 10f;

    [Header("Mouse")]
    public float mouseSensitivity = 0.15f;

    [Header("Camera")]
    public Transform cameraTransform;

    private Rigidbody rb;

    private float yaw;
    private float pitch;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // Physics can NEVER rotate the player.
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void Start()
    {
        yaw = transform.eulerAngles.y;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        Look();
        Jump();

        // Press Escape to unlock mouse.
        if (Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void Look()
    {
        if (Mouse.current == null)
            return;

        Vector2 mouse = Mouse.current.delta.ReadValue();

        yaw += mouse.x * mouseSensitivity;
        pitch -= mouse.y * mouseSensitivity;

        pitch = Mathf.Clamp(pitch, -89f, 89f);

        // Only change the player's Y rotation.
        rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));

        // Camera handles looking up/down.
        cameraTransform.localRotation =
            Quaternion.Euler(pitch, 0f, 0f);
    }

    void FixedUpdate()
    {
        Move();
    }

    void Move()
    {
        if (Keyboard.current == null)
            return;

        Vector2 input = Vector2.zero;

        if (Keyboard.current.wKey.isPressed)
            input.y += 1;

        if (Keyboard.current.sKey.isPressed)
            input.y -= 1;

        if (Keyboard.current.dKey.isPressed)
            input.x += 1;

        if (Keyboard.current.aKey.isPressed)
            input.x -= 1;

        input = Vector2.ClampMagnitude(input, 1f);

        Vector3 direction =
            transform.forward * input.y +
            transform.right * input.x;

        Vector3 velocity = direction * moveSpeed;

        // Keep physics gravity/jumping.
        velocity.y = rb.linearVelocity.y;

        rb.linearVelocity = velocity;
    }

    void Jump()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.spaceKey.wasPressedThisFrame &&
            IsGrounded())
        {
            // Give the player a strong, clean upward velocity.
            rb.linearVelocity = new Vector3(
                rb.linearVelocity.x,
                jumpForce,
                rb.linearVelocity.z
            );
        }
    }

    bool IsGrounded()
    {
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();

        float rayLength = capsule.bounds.extents.y + 0.15f;

        return Physics.Raycast(
            transform.position,
            Vector3.down,
            rayLength
        );
    }
}