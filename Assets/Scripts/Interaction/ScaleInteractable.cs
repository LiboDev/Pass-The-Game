using UnityEngine;

/// <summary>
/// Scales other objects between the local scale they hold at scene start (scale 1) and that scale
/// multiplied by <see cref="scaleMultiplier"/> (scale 2). Put it on the collider's GameObject
/// tagged Interactable; the targets are separate objects wired in the Inspector.
/// </summary>
[DisallowMultipleComponent]
public class ScaleInteractable : MonoBehaviour, IInteractable
{
    [Header("Targets")]
    [SerializeField, Tooltip("Objects to scale. Their scale at scene start is scale 1.")]
    private Transform[] targets;

    [Header("Tuning")]
    [SerializeField, Tooltip("Each target's start scale is multiplied by this to get scale 2")]
    private Vector3 scaleMultiplier = new Vector3(1.5f, 1.5f, 1.5f);
    [SerializeField, Min(0f)] private float unitsPerSecond = 2f;

    private Vector3[] startScales;

    public bool IsToggled { get; private set; }

    private void Awake()
    {
        startScales = new Vector3[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;
            startScales[i] = targets[i].localScale;
        }
    }

    private void Update()
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;
            Vector3 target = IsToggled ? Vector3.Scale(startScales[i], scaleMultiplier) : startScales[i];
            targets[i].localScale = Vector3.MoveTowards(
                targets[i].localScale, target, unitsPerSecond * Time.deltaTime);
        }
    }

    public void Interact()
    {
        IsToggled = !IsToggled;
    }
}
