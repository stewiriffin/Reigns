using UnityEngine;

/// <summary>
/// Plays a character's speaking clip when a card is drawn.
/// Randomizes pitch slightly so repeated lines feel like conversational gibberish.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class CardVoicePlayer : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] [Range(0.5f, 1f)] private float minPitch = 0.8f;
    [SerializeField] [Range(1f, 1.5f)] private float maxPitch = 1.2f;
    [SerializeField] [Range(0f, 1f)] private float volume = 1f;

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.loop = false;
    }

    /// <summary>
    /// Plays the card's speaking sound with a randomized pitch in [minPitch, maxPitch].
    /// </summary>
    public void PlayCardVoice(Card card)
    {
        if (audioSource == null || card == null || card.speakingSound == null)
            return;

        audioSource.Stop();
        audioSource.clip = card.speakingSound;
        audioSource.pitch = Random.Range(minPitch, maxPitch);
        audioSource.volume = volume;
        audioSource.Play();
    }

    public void StopVoice()
    {
        if (audioSource != null)
            audioSource.Stop();
    }
}
