using UnityEngine;

/// <summary>
/// Slides other objects between the local position they hold at scene start (position 1) and that
/// position plus <see cref="positionOffset"/> (position 2). Put it on the collider's GameObject
/// tagged Interactable; the targets are separate objects wired in the Inspector.
/// </summary>
[DisallowMultipleComponent]
public class MoveInteractable : MonoBehaviour, IInteractable
{
    [Header("Targets")]
    [SerializeField, Tooltip("Objects to move. Their position at scene start is position 1.")]
    private Transform[] targets;

    [Header("Tuning")]
    [SerializeField, Tooltip("Local-space offset added to each target's start position to get position 2")]
    private Vector3 positionOffset = new Vector3(0f, 3f, 0f);
    [SerializeField, Min(0f)] private float unitsPerSecond = 2f;

    private Vector3[] startPositions;

    public bool IsToggled { get; private set; }

    private void Awake()
    {
        startPositions = new Vector3[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;
            startPositions[i] = targets[i].localPosition;
        }
    }

    private void Update()
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;
            Vector3 target = IsToggled ? startPositions[i] + positionOffset : startPositions[i];
            targets[i].localPosition = Vector3.MoveTowards(
                targets[i].localPosition, target, unitsPerSecond * Time.deltaTime);
        }
    }

    public void Interact()
    {
        IsToggled = !IsToggled;
    }
}
