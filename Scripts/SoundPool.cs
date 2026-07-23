using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Plays one of several clip variations with slight pitch/volume randomization.
/// Uses a ring of AudioSources so rapid triggers (e.g. card swipes) overlap
/// cleanly instead of cutting each other off or sharing one pitch.
/// </summary>
[System.Serializable]
public class SoundPool
{
    [SerializeField] private string label = "SFX";
    [SerializeField] private AudioClip[] clips;

    [Header("Variation")]
    [SerializeField] private Vector2 pitchRange = new Vector2(0.9f, 1.1f);
    [SerializeField] private Vector2 volumeRange = new Vector2(0.85f, 1f);

    [Header("Voices")]
    [Tooltip("How many overlapping plays this pool can sustain.")]
    [SerializeField] private int voiceCount = 6;

    private AudioSource[] voices;
    private int nextVoice;
    private int lastClipIndex = -1;
    private Transform voiceRoot;
    private AudioMixerGroup mixerGroup;
    private bool initialized;

    public bool HasClips
    {
        get
        {
            if (clips == null || clips.Length == 0)
                return false;

            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                    return true;
            }

            return false;
        }
    }

    public string Label => string.IsNullOrEmpty(label) ? "SFX" : label;

    /// <summary>
    /// Seeds the pool from a single legacy clip when the array is empty.
    /// </summary>
    public void EnsureFallbackClip(AudioClip fallback)
    {
        if (fallback == null || HasClips)
            return;

        clips = new[] { fallback };
    }

    /// <summary>
    /// Builds or refreshes the AudioSource ring under <paramref name="parent"/>.
    /// Safe to call more than once (e.g. after mixer rebinding).
    /// </summary>
    public void Initialize(Transform parent, AudioMixerGroup group = null, string poolName = null)
    {
        if (parent == null)
            return;

        if (!string.IsNullOrWhiteSpace(poolName))
            label = poolName.Trim();

        mixerGroup = group;
        voiceCount = Mathf.Clamp(voiceCount, 2, 16);

        string rootName = $"SoundPool_{Label}";
        if (!initialized || voiceRoot == null)
        {
            Transform existing = parent.Find(rootName);
            if (existing != null)
                voiceRoot = existing;
            else
            {
                var rootGo = new GameObject(rootName);
                rootGo.transform.SetParent(parent, false);
                voiceRoot = rootGo.transform;
            }
        }

        EnsureVoiceArray();
        ApplyMixerGroup();
        initialized = true;
    }

    public void SetMixerGroup(AudioMixerGroup group)
    {
        mixerGroup = group;
        ApplyMixerGroup();
    }

    /// <summary>
    /// Picks a random variation, randomizes pitch/volume, and plays on the next free voice.
    /// </summary>
    /// <param name="busVolume">Master×SFX (or 1 when mixer owns gain).</param>
    public bool Play(float busVolume = 1f)
    {
        if (!HasClips)
            return false;

        if (!initialized || voices == null || voices.Length == 0)
            return false;

        AudioClip clip = PickClip();
        if (clip == null)
            return false;

        AudioSource voice = AcquireVoice();
        if (voice == null)
            return false;

        float pitchMin = Mathf.Min(pitchRange.x, pitchRange.y);
        float pitchMax = Mathf.Max(pitchRange.x, pitchRange.y);
        float volMin = Mathf.Clamp01(Mathf.Min(volumeRange.x, volumeRange.y));
        float volMax = Mathf.Clamp01(Mathf.Max(volumeRange.x, volumeRange.y));

        float pitch = Random.Range(pitchMin, pitchMax);
        float variation = Random.Range(volMin, volMax);
        float output = Mathf.Clamp01(busVolume) * variation;

        voice.Stop();
        voice.clip = clip;
        voice.pitch = pitch;
        voice.volume = output;
        voice.Play();
        return true;
    }

    private AudioClip PickClip()
    {
        int count = clips.Length;
        if (count == 1)
            return clips[0];

        // Prefer a different variation than last play when possible.
        int attempts = 0;
        int index = 0;
        while (attempts < 8)
        {
            index = Random.Range(0, count);
            if (clips[index] != null && (count <= 1 || index != lastClipIndex))
                break;
            attempts++;
        }

        if (clips[index] == null)
        {
            for (int i = 0; i < count; i++)
            {
                if (clips[i] != null)
                {
                    index = i;
                    break;
                }
            }
        }

        lastClipIndex = index;
        return clips[index];
    }

    private AudioSource AcquireVoice()
    {
        // Prefer a source that is idle so active plays keep ringing out.
        for (int i = 0; i < voices.Length; i++)
        {
            int index = (nextVoice + i) % voices.Length;
            AudioSource candidate = voices[index];
            if (candidate == null)
                continue;

            if (!candidate.isPlaying)
            {
                nextVoice = (index + 1) % voices.Length;
                return candidate;
            }
        }

        // All busy (very rapid spam): steal the next slot in the ring.
        AudioSource stolen = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;
        return stolen;
    }

    private void EnsureVoiceArray()
    {
        if (voices != null && voices.Length == voiceCount)
        {
            for (int i = 0; i < voices.Length; i++)
            {
                if (voices[i] == null)
                    voices[i] = CreateVoice(i);
            }

            return;
        }

        // Tear down extras if voiceCount changed.
        if (voiceRoot != null)
        {
            for (int i = voiceRoot.childCount - 1; i >= 0; i--)
                Object.Destroy(voiceRoot.GetChild(i).gameObject);
        }

        voices = new AudioSource[voiceCount];
        for (int i = 0; i < voiceCount; i++)
            voices[i] = CreateVoice(i);

        nextVoice = 0;
    }

    private AudioSource CreateVoice(int index)
    {
        var go = new GameObject($"Voice_{index}");
        go.transform.SetParent(voiceRoot, false);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.outputAudioMixerGroup = mixerGroup;
        return source;
    }

    private void ApplyMixerGroup()
    {
        if (voices == null)
            return;

        for (int i = 0; i < voices.Length; i++)
        {
            if (voices[i] != null)
                voices[i].outputAudioMixerGroup = mixerGroup;
        }
    }
}
