using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The one place volumes are read and written. Levels live in PlayerPrefs, are pushed to the
/// mixer's exposed parameters, and every write raises <see cref="VolumesChanged"/> so any
/// settings page on screen redraws from here rather than from whatever it was built with.
///
/// Survives scene loads, so a level chosen in the menu is still the level the game runs at.
/// </summary>
[DisallowMultipleComponent]
public class AudioController : MonoBehaviour
{
    // Names as exposed on GameAudioMixer. Renaming one there means renaming it here.
    private const string MasterParameter = "MasterVolume";
    private const string MusicParameter = "MusicVolume";
    private const string SfxParameter = "SFXVolume";

    private const string MasterPreference = "Audio.MasterVolume";
    private const string MusicPreference = "Audio.MusicVolume";
    private const string SfxPreference = "Audio.SFXVolume";

    // The mixer's own floor. At or below this a level reads back as silence rather than as the
    // very small fraction the decibel curve would otherwise turn it into.
    private const float MinimumDecibels = -80f;

    [SerializeField] private AudioMixer mixer;

    /// <summary>Raised on every volume write, so open settings pages resync.</summary>
    public static event Action VolumesChanged;

    private static AudioController instance;

    // PlayerPrefs is the store, not the mixer. Reading levels back off an AudioMixer looked like
    // the more honest answer, but a mixer is an output: a parameter that was never written is
    // simply absent and answers 0 dB, and the editor bakes runtime writes into the asset's
    // snapshot. Sliders redraw from these, so either way they would snap to a level nobody chose.
    public static float MasterVolume => Load(MasterPreference);
    public static float MusicVolume => Load(MusicPreference);
    public static float SfxVolume => Load(SfxPreference);

    public static void SetMasterVolume(float volume) => Set(MasterPreference, MasterParameter, volume);
    public static void SetMusicVolume(float volume) => Set(MusicPreference, MusicParameter, volume);
    public static void SetSfxVolume(float volume) => Set(SfxPreference, SfxParameter, volume);

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        ApplyAll();
    }

    /// <summary>
    /// The mixer brings its start snapshot up after Awake has run, and that snapshot carries its
    /// own values for the exposed volumes, so the levels written during Awake are quietly undone.
    /// Asserted again here and once more a frame later, because the snapshot lands on the audio
    /// system's schedule rather than ours and Start is not reliably after it.
    /// </summary>
    private IEnumerator Start()
    {
        ApplyAll();
        yield return null;
        ApplyAll();
        VolumesChanged?.Invoke();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void ApplyAll()
    {
        Apply(MasterParameter, MasterVolume);
        Apply(MusicParameter, MusicVolume);
        Apply(SfxParameter, SfxVolume);
    }

    private static float Load(string preference)
    {
        return Mathf.Clamp01(PlayerPrefs.GetFloat(preference, 1f));
    }

    /// <summary>
    /// Saves first, then applies. A scene with no AudioController in it used to drop the change on
    /// the floor, so the slider sprang back to its old value the next time the page was opened.
    /// </summary>
    private static void Set(string preference, string parameter, float volume)
    {
        float clamped = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(preference, clamped);
        PlayerPrefs.Save();

        if (instance != null)
        {
            instance.Apply(parameter, clamped);
        }

        VolumesChanged?.Invoke();
    }

    private void Apply(string parameter, float normalized)
    {
        if (mixer == null)
        {
            Debug.LogWarning("AudioController needs its Audio Mixer assigned.", this);
            return;
        }

        float decibels = normalized <= 0f ? MinimumDecibels : Mathf.Log10(normalized) * 20f;
        if (!mixer.SetFloat(parameter, decibels))
        {
            Debug.LogWarning($"Audio Mixer parameter '{parameter}' is not exposed.", this);
        }
    }
}
