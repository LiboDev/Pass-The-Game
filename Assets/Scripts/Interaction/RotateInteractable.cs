using UnityEngine;

/// <summary>
/// Twists other objects between the local rotation they hold at scene start (position 1) and that
/// rotation plus <see cref="rotationOffset"/> (position 2). Put it on the collider's GameObject
/// tagged Interactable; the targets are separate objects wired in the Inspector.
/// </summary>
[DisallowMultipleComponent]
public class RotateInteractable : MonoBehaviour, IInteractable
{
    [Header("Targets")]
    [SerializeField, Tooltip("Objects to twist. Their rotation at scene start is position 1.")]
    private Transform[] targets;

    [Header("Tuning")]
    [SerializeField, Tooltip("Euler degrees added to each target's start rotation to get position 2")]
    private Vector3 rotationOffset = new Vector3(0f, 100f, 0f);
    [SerializeField, Min(0f)] private float degreesPerSecond = 180f;

    private Quaternion[] startRotations;

    public bool IsToggled { get; private set; }

    private void Awake()
    {
        startRotations = new Quaternion[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;
            startRotations[i] = targets[i].localRotation;
        }
    }

    private void Update()
    {
        Quaternion offset = Quaternion.Euler(rotationOffset);
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;
            Quaternion target = IsToggled ? startRotations[i] * offset : startRotations[i];
            targets[i].localRotation = Quaternion.RotateTowards(
                targets[i].localRotation, target, degreesPerSecond * Time.deltaTime);
        }
    }

    public void Interact()
    {
        IsToggled = !IsToggled;
    }
}
