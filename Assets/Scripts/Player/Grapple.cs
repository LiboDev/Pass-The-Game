using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Two independent tethers, fired from the camera and anchored to whatever they hit. Each is locked to
/// the length it was made at: you can move anywhere inside that radius, but not past it, so gravity
/// turns a taut line into a swing. Both can be out at once, and the pair of overlapping limits is what
/// makes swinging on two lines read differently from swinging on one.
///
/// Each hook's key toggles it: press to fire, press again to let go. Holding the launch modifier while
/// letting go throws the player at the anchor first - the momentum move.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(FirstPersonController))]
public class Grapple : MonoBehaviour
{
    [Serializable]
    private class Hook
    {
        [Tooltip("Toggles this hook: fires it, then releases it")]
        public InputActionReference fire;
        [Tooltip("Where the line is drawn from - a shoulder, not the camera")]
        public Transform muzzle;
        public LineRenderer line;

        [NonSerialized] public ConfigurableJoint joint;
        [NonSerialized] public Vector3 anchor;

        public bool Attached => joint != null;
    }

    [Header("References")]
    [SerializeField, Tooltip("Aiming comes from here - the camera rig, not the muzzles")]
    private Transform eye;
    [SerializeField, Tooltip("Held while pressing a hook's key to be launched at it instead of just released")]
    private InputActionReference launch;
    [SerializeField] private Hook left = new Hook();
    [SerializeField] private Hook right = new Hook();

    [Header("Firing")]
    [SerializeField, Min(1f), Tooltip("How far a hook reaches")] private float maxRange = 40f;
    [SerializeField, Tooltip("What a hook can bite into. Must exclude the player's own layer")]
    private LayerMask grappleMask = ~0;

    [Header("Tether")]
    [SerializeField, Min(0f), Tooltip("Stiffness at the end of the line. Higher is more rigid; too high buzzes")]
    private float limitSpring = 8000f;
    [SerializeField, Min(0f), Tooltip("Takes the ring out of the line snapping taut")]
    private float limitDamper = 200f;

    [Header("Launch")]
    [SerializeField, Min(0f), Tooltip("m/s thrown towards the anchor on a launch release. A speed, not a force: mass does not change it")]
    private float launchSpeed = 18f;

    private Rigidbody body;
    private FirstPersonController controller;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        controller = GetComponent<FirstPersonController>();
        if (!eye || !launch || !Valid(left) || !Valid(right))
        {
            Debug.LogError("Grapple needs eye, launch, and a fire action, muzzle and line on both hooks", this);
            enabled = false;
            return;
        }

        if ((grappleMask.value & (1 << gameObject.layer)) != 0)
        {
            Debug.LogWarning("Grapple Mask includes the player's own layer; hooks will anchor to the "
                + "player instead of the world.", this);
        }

        Stow(left);
        Stow(right);
    }

    private static bool Valid(Hook hook) => hook.fire && hook.muzzle && hook.line;

    private void Update()
    {
        Toggle(left);
        Toggle(right);

        // The controller reads this to swap its airborne movement model; see FirstPersonController.Move.
        controller.Tethered = left.Attached || right.Attached;

        Draw(left);
        Draw(right);
    }

    private void Toggle(Hook hook)
    {
        if (!hook.fire.action.WasPressedThisFrame())
        {
            return;
        }

        if (hook.Attached)
        {
            Release(hook, launch.action.IsPressed());
        }
        else
        {
            Fire(hook);
        }
    }

    /// <summary>
    /// Aims from the camera, not the muzzle: the muzzle is only where the line is drawn from, and firing
    /// from it would send hooks somewhere other than the crosshair. A miss does nothing at all.
    /// </summary>
    private void Fire(Hook hook)
    {
        if (!Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, maxRange, grappleMask,
                QueryTriggerInteraction.Ignore))
        {
            return;
        }

        hook.anchor = hit.point;
        hook.joint = Attach(hit.point);
        hook.line.enabled = true;
    }

    /// <summary>
    /// A joint anchored to a fixed point in the world rather than to another body, limiting the player to
    /// a sphere of the radius they fired at. The limit is sprung rather than solid, so hitting the end of
    /// the line at speed loads up instead of slamming into a wall.
    /// </summary>
    private ConfigurableJoint Attach(Vector3 worldAnchor)
    {
        ConfigurableJoint joint = gameObject.AddComponent<ConfigurableJoint>();
        joint.autoConfigureConnectedAnchor = false;
        joint.connectedBody = null;               // null connected body means connectedAnchor is world space
        joint.anchor = Vector3.zero;
        joint.connectedAnchor = worldAnchor;

        // Limited on every axis makes the limit a sphere around the anchor: free inside it, held at its
        // surface. Angular motion is left free - the tether must never try to turn the player.
        joint.xMotion = ConfigurableJointMotion.Limited;
        joint.yMotion = ConfigurableJointMotion.Limited;
        joint.zMotion = ConfigurableJointMotion.Limited;
        joint.angularXMotion = ConfigurableJointMotion.Free;
        joint.angularYMotion = ConfigurableJointMotion.Free;
        joint.angularZMotion = ConfigurableJointMotion.Free;

        joint.linearLimit = new SoftJointLimit
        {
            limit = Vector3.Distance(transform.position, worldAnchor), // the length it was made at
        };
        joint.linearLimitSpring = new SoftJointLimitSpring
        {
            spring = limitSpring,
            damper = limitDamper,
        };
        return joint;
    }

    private void Release(Hook hook, bool withLaunch)
    {
        if (withLaunch)
        {
            // VelocityChange, so launchSpeed is honestly the m/s it adds and retuning the player's mass
            // never silently changes how far a launch throws you.
            Vector3 direction = (hook.anchor - transform.position).normalized;
            body.AddForce(direction * launchSpeed, ForceMode.VelocityChange);
        }

        Stow(hook);
    }

    private void Stow(Hook hook)
    {
        if (hook.joint)
        {
            Destroy(hook.joint);
            hook.joint = null;
        }

        if (hook.line) // a failed Awake disables the component, and OnDisable lands back here
        {
            hook.line.enabled = false;
        }
    }

    /// <summary>
    /// Drops both lines. Anything that moves the player without physics has to call this first: a tether
    /// that survives a teleport snaps the player straight back across the level, and one that survives
    /// getting into a vehicle is a joint on a body that has just gone kinematic.
    /// </summary>
    public void ReleaseAll()
    {
        Stow(left);
        Stow(right);
        controller.Tethered = false;
    }

    private void Draw(Hook hook)
    {
        if (!hook.Attached)
        {
            return;
        }

        hook.line.positionCount = 2;
        hook.line.SetPosition(0, hook.muzzle.position);
        hook.line.SetPosition(1, hook.anchor);
    }

    private void OnDisable()
    {
        ReleaseAll();
    }
}
