using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Swaps the three menu pages and carries the settings page's wiring. Every button and slider in
/// the scene points at a method here through its own persistent listener, so nothing about this
/// menu is assembled at runtime.
/// </summary>
public class MainMenuNavigation : MonoBehaviour
{
    [Header("Pages")]
    [SerializeField] private GameObject mainPage;
    [SerializeField] private GameObject settingsPage;
    [SerializeField] private GameObject creditsPage;

    [Header("Play")]
    [Tooltip("Scene the Play button loads. Must be in File > Build Profiles > Scene List.")]
    [SerializeField] private string gameSceneName = "game";

    [Header("Settings Rows")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Text masterValueLabel;
    [SerializeField] private Text musicValueLabel;
    [SerializeField] private Text sfxValueLabel;

    private void Start()
    {
        AudioController.VolumesChanged += RefreshVolumes;
        RefreshVolumes();
        ShowMain();
    }

    private void OnDestroy()
    {
        AudioController.VolumesChanged -= RefreshVolumes;
    }

    public void ShowMain() => ShowPage(mainPage);

    public void ShowSettings()
    {
        ShowPage(settingsPage);

        // Refreshed after the page is up: a Slider rebuilds its handle and fill from its own
        // stored value when it is enabled, which would undo a refresh applied while hidden.
        RefreshVolumes();
    }

    public void ShowCredits() => ShowPage(creditsPage);

    public void Play() => SceneManager.LoadScene(gameSceneName);

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // None of these touches its own row. The write raises the change event, and the refresh that
    // follows redraws every handle and readout from the saved level, so a level the controller
    // clamps or refuses shows up on the row rather than only where the handle was dropped.
    public void SetMasterVolume(float volume) => AudioController.SetMasterVolume(volume);
    public void SetMusicVolume(float volume) => AudioController.SetMusicVolume(volume);
    public void SetSfxVolume(float volume) => AudioController.SetSfxVolume(volume);

    private void RefreshVolumes()
    {
        Sync(masterSlider, masterValueLabel, AudioController.MasterVolume);
        Sync(musicSlider, musicValueLabel, AudioController.MusicVolume);
        Sync(sfxSlider, sfxValueLabel, AudioController.SfxVolume);
    }

    private static void Sync(Slider slider, Text valueLabel, float volume)
    {
        if (slider != null)
        {
            slider.SetValueWithoutNotify(volume);
        }

        if (valueLabel != null)
        {
            valueLabel.text = Mathf.RoundToInt(volume * 100f) + "%";
        }
    }

    private void ShowPage(GameObject pageToShow)
    {
        mainPage.SetActive(pageToShow == mainPage);
        settingsPage.SetActive(pageToShow == settingsPage);
        creditsPage.SetActive(pageToShow == creditsPage);
    }
}
