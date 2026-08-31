using UnityEngine;

/// <summary>
/// A door that swings on this transform. Put it on the hinge-edge object (with the collider and the
/// Interactable tag); the visible panel is a child offset from the hinge.
/// </summary>
[DisallowMultipleComponent]
public class Door : MonoBehaviour, IInteractable
{
    [Header("Tuning")]
    [SerializeField, Range(-180f, 180f), Tooltip("Yaw in degrees when open")] private float openAngle = 100f;
    [SerializeField, Min(0f)] private float degreesPerSecond = 180f;

    public bool IsOpen { get; private set; }

    private void Update()
    {
        Quaternion target = Quaternion.Euler(0f, IsOpen ? openAngle : 0f, 0f);
        transform.localRotation = Quaternion.RotateTowards(
            transform.localRotation, target, degreesPerSecond * Time.deltaTime);
    }

    public void Interact()
    {
        IsOpen = !IsOpen;
    }
}
