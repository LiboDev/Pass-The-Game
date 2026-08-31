using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Looks along the eye each frame. A collider within range that carries the interactable tag and an
/// IInteractable shows the prompt; the Interact action (E) calls it.
/// </summary>
[DisallowMultipleComponent]
public class Interactor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform eye;
    [SerializeField, Tooltip("HUD object switched on while an interactable is in view")] private GameObject prompt;
    [SerializeField] private InputActionReference interact;

    [Header("Tuning")]
    [SerializeField, Min(0f)] private float range = 5f;
    [SerializeField] private string interactableTag = "Interactable";

    private void Awake()
    {
        if (!eye || !prompt || !interact)
        {
            Debug.LogError("Interactor needs eye, prompt and interact assigned", this);
            enabled = false;
        }
    }

    private void Update()
    {
        IInteractable target = null;
        if (Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, range,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && hit.collider.CompareTag(interactableTag))
        {
            hit.collider.TryGetComponent(out target);
        }

        prompt.SetActive(target != null);
        if (target != null && interact.action.WasPressedThisFrame())
        {
            target.Interact();
        }
    }

    private void OnDisable()
    {
        if (prompt)
        {
            prompt.SetActive(false);
        }
    }
}
