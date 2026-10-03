using UnityEngine;
using UnityEngine.SceneManagement;
using PassTheGame.WeaponsEnemies;

/// <summary>
/// Minimal health so Enemy attacks have something to hit. No respawn or game-over flow - that is a
/// design decision for whoever owns the game loop, not the Weapons And Enemies package.
/// </summary>
[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [SerializeField, Min(1f)] private float maxHealth = 100f;
    [SerializeField, Min(0f)] private float restartDelay = 1f;

    public float Current { get; private set; }

    private void Awake() => Current = maxHealth;

    public void TakeDamage(float amount, GameObject source)
    {
        if (Current <= 0f) return;

        Current = Mathf.Max(0f, Current - amount);
        Debug.Log($"[{nameof(PlayerHealth)}] took {amount} damage from "
            + $"{(source ? source.name : "unknown")}, {Current}/{maxHealth} left", this);

        if (Current <= 0f)
        {
            Debug.Log($"[{nameof(PlayerHealth)}] died", this);
            Invoke(nameof(RestartScene), restartDelay);
        }
    }

    public void Kill(GameObject source) => TakeDamage(Current, source);

    private static void RestartScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.buildIndex);
    }
}
