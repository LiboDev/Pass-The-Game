using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A billboarded sprite pet that drifts after the player until it is hovering a set distance away,
/// keeping whatever side it drifted to (the player turning does not swing it around), and always
/// facing them. Interacting with it prints its next line to a HUD label - once through the list, no
/// looping - and it only cycles through its frames while that line is up.
/// Put this on a scene object with a SpriteRenderer, a collider and the Interactable tag.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class PetFollower : MonoBehaviour, IInteractable
{
    [Header("References")]
    [SerializeField, Tooltip("Followed and faced — the player body or the camera pivot")]
    private Transform target;
    [SerializeField, Tooltip("HUD text the pet speaks through; switched off while it is quiet")]
    private Text speechLabel;

    [Header("Float position")]
    [SerializeField, Tooltip("Metres above the target it hovers")] private float hoverHeight = 1.4f;
    [SerializeField, Min(0f), Tooltip("Metres it keeps between itself and the target, closing in from further out")]
    private float stopDistance = 1.2f;
    [SerializeField, Min(0.01f), Tooltip("Seconds it takes to catch up — bigger lags further behind")]
    private float followLag = 0.6f;
    [SerializeField, Min(0f), Tooltip("Metres of idle bob, 0 = none")] private float bobAmplitude = 0.1f;
    [SerializeField, Min(0f)] private float bobCyclesPerSecond = 0.5f;

    [Header("Animation")]
    [SerializeField, Tooltip("Frames in play order")] private Sprite[] frames;
    [SerializeField, Min(1), Tooltip("How many of the frames above to cycle through")]
    private int frameCount = 4;
    [SerializeField, Min(0.01f)] private float secondsPerFrame = 0.15f;

    [Header("Speech")]
    [SerializeField, TextArea, Tooltip("Said one per interaction, in order, then back to the first")]
    private string[] lines;
    [SerializeField, Min(0.1f), Tooltip("Seconds a line stays on screen")] private float secondsOnScreen = 3f;

    private const string UntaggedTag = "Untagged";

    private SpriteRenderer spriteRenderer;
    private Vector3 followVelocity;
    private float frameTimer;
    private int frameIndex;
    private int lineIndex;
    private string interactableTag; // the tag the pet started with, restored when it gets new lines
    private float hideAt;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        interactableTag = gameObject.tag;
        if (!target || !speechLabel || frames == null || frames.Length == 0)
        {
            Debug.LogError($"[{nameof(PetFollower)}] needs target, speechLabel and at least one frame assigned", this);
            enabled = false;
            return;
        }

        transform.position = FloatPoint();
        speechLabel.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        frameIndex = 0;
        frameTimer = 0f;
        spriteRenderer.sprite = frames[0];
    }

    private void LateUpdate()
    {
        transform.position = Vector3.SmoothDamp(
            transform.position, FloatPoint(), ref followVelocity, followLag);

        Vector3 away = transform.position - target.position;
        if (away.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(away);
        }

        if (speechLabel.gameObject.activeSelf && Time.time >= hideAt)
        {
            speechLabel.gameObject.SetActive(false);
        }

        if (speechLabel.gameObject.activeSelf)
        {
            Animate();
        }
        else
        {
            RestIdle();
        }
    }

    private void OnDisable()
    {
        if (speechLabel)
        {
            speechLabel.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Replaces what the pet says, starting again from the first line, and always re-tags it so it can
    /// be talked to again even if it had run dry.
    /// </summary>
    public void SetLines(string[] newLines)
    {
        lines = newLines;
        lineIndex = 0;
        gameObject.tag = interactableTag;
    }

    /// <summary>
    /// Says the next line. Once the list runs out the pet drops its interactable tag, so the
    /// Interactor stops matching it and the prompt no longer appears.
    /// </summary>
    public void Interact()
    {
        if (lines != null && lineIndex < lines.Length)
        {
            speechLabel.text = lines[lineIndex];
            speechLabel.gameObject.SetActive(true);
            hideAt = Time.time + secondsOnScreen;
            lineIndex++;
        }

        if (lines == null || lineIndex >= lines.Length)
        {
            gameObject.tag = UntaggedTag;
        }
    }

    /// <summary>
    /// Hover point: stopDistance out from the target along the bearing the pet already sits on, at
    /// hoverHeight, plus the idle bob. Taking the bearing from the pet rather than from the target's
    /// forward is what keeps the player's turning from dragging it around.
    /// </summary>
    private Vector3 FloatPoint()
    {
        Vector3 anchor = target.position + Vector3.up * hoverHeight;
        Vector3 bearing = transform.position - anchor;
        bearing.y = 0f;
        bearing = bearing.sqrMagnitude > 0.0001f ? bearing.normalized : Vector3.forward;

        Vector3 point = anchor + bearing * stopDistance;
        point.y += bobAmplitude * Mathf.Sin(Time.time * bobCyclesPerSecond * 2f * Mathf.PI);
        return point;
    }

    /// <summary>Back to the first frame between lines - the pet only animates while it is talking.</summary>
    private void RestIdle()
    {
        if (frameIndex == 0)
        {
            return;
        }

        frameIndex = 0;
        frameTimer = 0f;
        spriteRenderer.sprite = frames[0];
    }

    private void Animate()
    {
        int count = Mathf.Min(frameCount, frames.Length);
        if (count < 2)
        {
            return;
        }

        frameTimer += Time.deltaTime;
        while (frameTimer >= secondsPerFrame)
        {
            frameTimer -= secondsPerFrame;
            frameIndex = (frameIndex + 1) % count;
            spriteRenderer.sprite = frames[frameIndex];
        }
    }
}
