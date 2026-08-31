/// <summary>
/// Implement on a component that sits on a collider's GameObject tagged Interactable. The player's
/// Interactor calls Interact() when the object is looked at and the Interact action fires.
/// </summary>
public interface IInteractable
{
    void Interact();
}
