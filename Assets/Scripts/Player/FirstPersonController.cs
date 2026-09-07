using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Walk, jump and crouch on a Rigidbody. Looking is not done here - <see cref="PlayerLook"/>
/// on a detached camera rig owns that, and writes the yaw onto <see cref="orientation"/>, which is the
/// basis every move direction is built from. This body's own rotation is frozen and never read.
///
/// Movement is a target-velocity solve rather than raw AddForce: each step works out the planar
/// velocity the input asks for and accelerates towards it. Stopping falls out of the same maths, so
/// there is no drag to tune against - the Rigidbody's linear damping stays at zero.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class FirstPersonController : MonoBehaviour
{
    [Header("References")]
    [SerializeField, Tooltip("Yaw-only transform PlayerLook drives; move directions are built from it")]
    private Transform orientation;
    [SerializeField, Tooltip("Where the camera rig sits; it drops as the capsule shrinks")]
    private Transform eyeTarget;
    [SerializeField] private InputActionReference move;
    [SerializeField] private InputActionReference jump;
    [SerializeField] private InputActionReference crouch;
    [SerializeField, Tooltip("Optional capsule mesh kept scaled to the CapsuleCollider (see FitCapsule)")]
    private Transform bodyVisual;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float walkSpeed = 4f;
    [SerializeField, Min(0f), Tooltip("m/s² towards the wanted velocity on the ground; this is also the braking rate")]
    private float groundAcceleration = 60f;
    [SerializeField, Min(0f), Tooltip("Much lower, so a jump keeps the speed it left the ground with")]
    private float airAcceleration = 12f;
    [SerializeField, Min(0f), Tooltip("m/s² of steering while on a tether; nothing here brakes, so a swing keeps its speed")]
    private float tetheredAirAcceleration = 14f;

    [Header("Jump")]
    [SerializeField, Min(0f), Tooltip("Metres the jump peaks at")] private float jumpHeight = 1.2f;
    [SerializeField, Tooltip("Negative = down, m/s². Applied by hand; the Rigidbody's own gravity is off")]
    private float gravity = -20f;
    [SerializeField, Min(0f), Tooltip("Seconds after walking off a ledge a jump still works")]
    private float coyoteTime = 0.12f;
    [SerializeField, Min(0f), Tooltip("Seconds before landing a jump press is still remembered")]
    private float jumpBuffer = 0.12f;

    [Header("Crouch")]
    [SerializeField, Min(0.1f), Tooltip("Capsule height while crouched; standing height is taken from the CapsuleCollider")]
    private float crouchHeight = 1f;
    [SerializeField, Range(0f, 1f)] private float crouchSpeedMultiplier = 0.5f;
    [SerializeField, Min(0f), Tooltip("Metres per second the capsule shrinks or grows")]
    private float crouchTransitionSpeed = 8f;

    [Header("Ground")]
    [SerializeField, Tooltip("What counts as standable. Must exclude the player's own layer")]
    private LayerMask groundMask = ~0;
    [SerializeField, Min(0f), Tooltip("How far below the feet the ground probe reaches")]
    private float groundProbeDistance = 0.3f;
    [SerializeField, Range(0f, 89f), Tooltip("Steeper than this is not ground: you slide off it")]
    private float maxSlopeAngle = 50f;
    [SerializeField, Min(0f), Tooltip("Downward acceleration holding the feet on slopes and steps")]
    private float groundStick = 25f;

    [Header("Steps")]
    [SerializeField, Min(0f), Tooltip("Tallest ledge walked over rather than into. 0 disables step assist")]
    private float stepHeight = 0.35f;
    [SerializeField, Min(0f), Tooltip("Metres per second the body is lifted over a step")]
    private float stepSpeed = 3f;

    private Rigidbody body;
    private CapsuleCollider capsule;
    private float standHeight;
    private float standEyeHeight;

    private bool grounded;
    private Vector3 groundNormal = Vector3.up;
    private float? lastGroundedTime;
    private float? jumpPressedTime;
    private float groundProbeSuppressedUntil;

    public bool IsCrouching => capsule.height < standHeight - 0.01f;

    /// <summary>
    /// Set while something else owns the player's position - riding in a vehicle, say. Looking is a
    /// separate component on a separate object, so it keeps working; walking, crouching, gravity and
    /// the ground probe all stop, leaving the Rigidbody to whatever put it there.
    /// </summary>
    public bool LookOnly { get; set; }

    /// <summary>
    /// Set by <see cref="Grapple"/> while a tether is attached. It swaps the airborne movement model:
    /// see <see cref="Move"/> for why the normal one cannot be used on a swing.
    /// </summary>
    public bool Tethered { get; set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        if (!orientation || !eyeTarget || !move || !jump || !crouch)
        {
            Debug.LogError("FirstPersonController needs orientation, eyeTarget, move, jump and crouch "
                + "assigned", this);
            enabled = false;
            return;
        }

        // Rotation is PlayerLook's job, gravity and damping are applied by hand below, and interpolation
        // is what lets the camera rig follow this body without stutter.
        body.freezeRotation = true;
        body.useGravity = false;
        body.linearDamping = 0f;
        body.angularDamping = 0f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // The probe below starts inside this capsule, so a mask containing the player hits itself every
        // step and nothing ever reads as grounded. Worth saying out loud - the symptom looks like broken
        // gravity, not like a layer mistake.
        if ((groundMask.value & (1 << gameObject.layer)) != 0)
        {
            Debug.LogWarning("Ground Mask includes the player's own layer ("
                + LayerMask.LayerToName(gameObject.layer) + "); the ground probe will hit the player. "
                + "Run Tools > Wire Player In Open Scene, or clear that layer from the mask.", this);
        }

        standHeight = capsule.height;
        standEyeHeight = eyeTarget.localPosition.y;
        if (bodyVisual)
        {
            FitCapsule(bodyVisual, capsule);
        }
    }

    /// <summary>Scales a Unity capsule primitive (height 2, radius 0.5) onto the collider's capsule.</summary>
    public static void FitCapsule(Transform mesh, CapsuleCollider capsule)
    {
        mesh.localPosition = capsule.center;
        mesh.localScale = new Vector3(capsule.radius * 2f, capsule.height * 0.5f, capsule.radius * 2f);
    }

    private void Update()
    {
        if (LookOnly)
        {
            return;
        }

        // Held rather than pressed, so landing with the key down jumps again; FixedUpdate spends it.
        if (jump.action.IsPressed())
        {
            jumpPressedTime = Time.time;
        }

        Crouch();
    }

    private void FixedUpdate()
    {
        if (LookOnly)
        {
            return;
        }

        ProbeGround();
        Move();
        Jump();
    }

    /// <summary>
    /// Sphere cast straight down from the bottom of the capsule. Unlike a CharacterController's
    /// isGrounded this hands back the ground normal, which the slope handling below needs. Ground too
    /// steep to stand on is reported as no ground at all, so gravity takes over and you slide off it.
    /// </summary>
    private void ProbeGround()
    {
        grounded = false;
        groundNormal = Vector3.up;

        if (Time.time < groundProbeSuppressedUntil)
        {
            return;
        }

        float radius = capsule.radius * 0.95f;
        Vector3 origin = transform.position + Vector3.up * capsule.radius;
        if (Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit,
                groundProbeDistance, groundMask, QueryTriggerInteraction.Ignore)
            && Vector3.Angle(hit.normal, Vector3.up) <= maxSlopeAngle)
        {
            grounded = true;
            groundNormal = hit.normal;
            lastGroundedTime = Time.time;
        }
    }

    private void Move()
    {
        Vector2 input = move.action.ReadValue<Vector2>();
        Vector3 wishDirection = orientation.forward * input.y + orientation.right * input.x;
        if (wishDirection.sqrMagnitude > 1f)
        {
            wishDirection.Normalize(); // a diagonal must not be faster than a straight line
        }

        float speed = IsCrouching ? walkSpeed * crouchSpeedMultiplier : walkSpeed;

        if (!grounded)
        {
            AirMove(wishDirection, speed);
            body.AddForce(Vector3.up * gravity, ForceMode.Acceleration);
            return;
        }

        // On a slope the wanted velocity runs along the ground rather than through it, so walking up a
        // ramp neither digs in nor launches off the top.
        Vector3 wishVelocity = Vector3.ProjectOnPlane(wishDirection, groundNormal).normalized
            * (wishDirection.magnitude * speed);
        Vector3 planar = Vector3.ProjectOnPlane(body.linearVelocity, groundNormal);

        // The ground movement model: accelerate towards the velocity the stick is asking for. With no
        // input the target is zero, so this is also the brake, and no drag is needed to stop cleanly.
        body.AddForce(Vector3.ClampMagnitude(wishVelocity - planar, speed) * groundAcceleration,
            ForceMode.Acceleration);

        // Gravity off, pressed into the slope instead: this is what stops a frictionless capsule
        // creeping down every ramp it stands on.
        body.AddForce(-groundNormal * groundStick, ForceMode.Acceleration);
        StepUp(wishDirection);
    }

    /// <summary>
    /// Air control that can only ever top up the speed along the direction being asked for, never
    /// subtract from it. Anything already faster than walking pace - a swing, a tether launch, a long
    /// fall - is simply left alone.
    ///
    /// The ground model cannot be reused here. It solves towards walking pace from both sides, so it
    /// reads the speed a swing carries as something to brake off, at roughly 5g, and every momentum
    /// move dies a few frames after it starts.
    /// </summary>
    private void AirMove(Vector3 wishDirection, float speed)
    {
        float magnitude = wishDirection.magnitude;
        if (magnitude < 0.01f)
        {
            return;
        }

        Vector3 direction = wishDirection / magnitude;
        Vector3 planar = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z);
        float headroom = speed * magnitude - Vector3.Dot(planar, direction);
        if (headroom <= 0f)
        {
            return; // already going that way faster than walking pace: leave the momentum be
        }

        float acceleration = Tethered ? tetheredAirAcceleration : airAcceleration;
        body.AddForce(direction * Mathf.Min(acceleration * Time.fixedDeltaTime, headroom),
            ForceMode.VelocityChange);
    }

    /// <summary>
    /// What a CharacterController gave away free as stepOffset. A ledge blocking the feet but not the
    /// knees, with nothing walkable about its face, gets climbed by lifting the body over it.
    /// </summary>
    private void StepUp(Vector3 wishDirection)
    {
        if (stepHeight <= 0f || wishDirection.sqrMagnitude < 0.01f)
        {
            return;
        }

        float reach = capsule.radius + 0.1f;
        Vector3 direction = wishDirection.normalized;
        if (!Physics.Raycast(transform.position + Vector3.up * 0.05f, direction, out RaycastHit obstacle,
                reach, groundMask, QueryTriggerInteraction.Ignore))
        {
            return; // nothing at foot height
        }

        if (Vector3.Angle(obstacle.normal, Vector3.up) <= maxSlopeAngle)
        {
            return; // a ramp, not a step: ordinary slope movement already handles it
        }

        if (Physics.Raycast(transform.position + Vector3.up * (stepHeight + 0.05f), direction,
                reach, groundMask, QueryTriggerInteraction.Ignore))
        {
            return; // still blocked higher up, so it is a wall
        }

        body.position += Vector3.up * (stepSpeed * Time.fixedDeltaTime);
    }

    /// <summary>
    /// Coyote time and jump buffering, both measured against the same press. Either alone leaves the
    /// jump feeling like it drops inputs; together the edge of a ledge and the moment before landing
    /// both count.
    /// </summary>
    private void Jump()
    {
        if (!lastGroundedTime.HasValue || !jumpPressedTime.HasValue
            || Time.time - lastGroundedTime.Value > coyoteTime
            || Time.time - jumpPressedTime.Value > jumpBuffer)
        {
            return;
        }

        Vector3 velocity = body.linearVelocity;
        velocity.y = 0f; // so jumping while already falling reaches the same height every time
        body.linearVelocity = velocity;
        body.AddForce(Vector3.up * Mathf.Sqrt(jumpHeight * -2f * gravity), ForceMode.VelocityChange);

        jumpPressedTime = null;
        lastGroundedTime = null;
        grounded = false;
        // The probe would still find the floor on the frame after take-off and eat the jump.
        groundProbeSuppressedUntil = Time.time + 0.1f;
    }

    /// <summary>Held crouch. Standing back up waits until nothing is above the head.</summary>
    private void Crouch()
    {
        float targetHeight = crouch.action.IsPressed() || !HasHeadroom() ? crouchHeight : standHeight;
        float height = Mathf.MoveTowards(capsule.height, targetHeight, crouchTransitionSpeed * Time.deltaTime);
        if (Mathf.Approximately(height, capsule.height))
        {
            return;
        }

        // Centre follows height so the capsule shrinks from the top and the feet stay put.
        capsule.height = height;
        capsule.center = new Vector3(0f, height * 0.5f, 0f);
        eyeTarget.localPosition = new Vector3(0f, standEyeHeight - (standHeight - height), 0f);
        if (bodyVisual)
        {
            FitCapsule(bodyVisual, capsule);
        }
    }

    /// <summary>Sweeps the capsule's top sphere up to standing height; the player's own capsule overlaps at the start and is ignored.</summary>
    private bool HasHeadroom()
    {
        if (capsule.height >= standHeight)
        {
            return true;
        }

        float radius = capsule.radius;
        Vector3 topSphere = transform.position + Vector3.up * (capsule.height - radius);
        return !Physics.SphereCast(topSphere, radius * 0.9f, Vector3.up, out _, standHeight - capsule.height,
            groundMask, QueryTriggerInteraction.Ignore);
    }
}
