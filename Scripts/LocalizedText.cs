using TMPro;
using UnityEngine;

/// <summary>
/// Binds a TextMeshPro label to a localization key and refreshes when the language changes.
/// </summary>
[RequireComponent(typeof(TextMeshProUGUI))]
public class LocalizedText : MonoBehaviour
{
    [SerializeField] private string localizationKey;
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private bool useFallbackTextAsDefault;
    [SerializeField] private string fallbackText;

    private void Awake()
    {
        if (label == null)
            label = GetComponent<TextMeshProUGUI>();

        if (useFallbackTextAsDefault && string.IsNullOrEmpty(fallbackText) && label != null)
            fallbackText = label.text;
    }

    private void OnEnable()
    {
        LocalizationManager.OnLanguageChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= Refresh;
    }

    public void SetKey(string key, bool refreshNow = true)
    {
        localizationKey = key;
        if (refreshNow)
            Refresh();
    }

    public void Refresh()
    {
        if (label == null)
            return;

        label.text = LocalizationManager.Get(localizationKey, fallbackText);
    }
}
