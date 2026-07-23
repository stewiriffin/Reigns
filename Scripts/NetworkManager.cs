using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Downloads remote event-card JSON at startup (GitHub Gist / CDN / your server)
/// and merges new cards into the offline Resources deck — no Play Store update required.
/// Offline: fails silently and leaves the local deck untouched.
/// </summary>
public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }

    [Header("Remote card feed")]
    [Tooltip("Raw JSON URL (Gist raw, S3, your API). Same schema as Resources/Cards/event_cards.json.")]
    [SerializeField] private string remoteCardsUrl =
        "https://gist.githubusercontent.com/YOUR_USER/YOUR_GIST_ID/raw/event_cards_remote.json";

    [SerializeField] private bool fetchOnStartup = true;
    [SerializeField] private int timeoutSeconds = 8;
    [Tooltip("If true, previously downloaded remote cards load from disk when offline.")]
    [SerializeField] private bool useDiskCacheWhenOffline = true;

    /// <summary>Remote cards successfully downloaded or loaded from cache this session.</summary>
    public CardDatabase RemoteDatabase { get; private set; }

    /// <summary>True after the startup fetch attempt finishes (success or fail).</summary>
    public bool StartupFetchCompleted { get; private set; }

    /// <summary>True when RemoteDatabase has at least one card.</summary>
    public bool HasRemoteCards =>
        RemoteDatabase != null &&
        ((RemoteDatabase.baseDeck != null && RemoteDatabase.baseDeck.Length > 0) ||
         (RemoteDatabase.unlockablePools != null && RemoteDatabase.unlockablePools.Length > 0));

    /// <summary>Fired when remote (or cache) cards are ready to merge. Arg = success.</summary>
    public event Action<bool> OnRemoteCardsReady;

    private const string CacheFileName = "remote_event_cards.json";
    private string CachePath => Path.Combine(Application.persistentDataPath, CacheFileName);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (fetchOnStartup)
            StartCoroutine(FetchRemoteCardsRoutine());
        else
            StartupFetchCompleted = true;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Manual refresh (e.g. debug / settings).</summary>
    public void RefreshRemoteCards()
    {
        StopAllCoroutines();
        StartupFetchCompleted = false;
        StartCoroutine(FetchRemoteCardsRoutine());
    }

    /// <summary>
    /// Loads local Resources deck, then appends any remote/cached cards (deduped by id).
    /// </summary>
    public CardDatabase LoadMergedDatabase(string localResourcePath)
    {
        CardDatabase local = CardLoader.LoadDatabase(localResourcePath);
        if (!HasRemoteCards)
            return local;

        return CardLoader.MergeDatabases(local, RemoteDatabase);
    }

    private IEnumerator FetchRemoteCardsRoutine()
    {
        bool online = Application.internetReachability != NetworkReachability.NotReachable;

        if (!online)
        {
            if (useDiskCacheWhenOffline && TryLoadCache(out CardDatabase cached))
            {
                RemoteDatabase = cached;
                CardLoader.ResolveAllAssets(RemoteDatabase);
                Debug.Log($"NetworkManager: Offline — using {CountCards(RemoteDatabase)} cached remote card(s).");
                CompleteFetch(success: true);
            }
            else
            {
                // Fail silently — GameManager keeps the baked-in Resources deck.
                RemoteDatabase = null;
                CompleteFetch(success: false);
            }

            yield break;
        }

        if (string.IsNullOrWhiteSpace(remoteCardsUrl) ||
            remoteCardsUrl.Contains("YOUR_USER") ||
            remoteCardsUrl.Contains("YOUR_GIST"))
        {
            // No real URL configured — try cache, otherwise silent local-only.
            if (TryLoadCache(out CardDatabase cachedPlaceholder))
            {
                RemoteDatabase = cachedPlaceholder;
                CardLoader.ResolveAllAssets(RemoteDatabase);
                CompleteFetch(success: true);
            }
            else
            {
                RemoteDatabase = null;
                CompleteFetch(success: false);
            }

            yield break;
        }

        using (UnityWebRequest request = UnityWebRequest.Get(remoteCardsUrl))
        {
            request.timeout = Mathf.Max(3, timeoutSeconds);
            yield return request.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool failed = request.result != UnityWebRequest.Result.Success;
#else
            bool failed = request.isNetworkError || request.isHttpError;
#endif
            if (failed)
            {
                Debug.Log($"NetworkManager: Remote fetch failed ({request.error}) — using local deck.");
                if (useDiskCacheWhenOffline && TryLoadCache(out CardDatabase cachedAfterFail))
                {
                    RemoteDatabase = cachedAfterFail;
                    CardLoader.ResolveAllAssets(RemoteDatabase);
                    CompleteFetch(success: true);
                }
                else
                {
                    RemoteDatabase = null;
                    CompleteFetch(success: false);
                }

                yield break;
            }

            string json = request.downloadHandler != null ? request.downloadHandler.text : null;
            if (string.IsNullOrWhiteSpace(json))
            {
                RemoteDatabase = null;
                CompleteFetch(success: false);
                yield break;
            }

            CardDatabase remote = null;
            try
            {
                remote = CardLoader.ParseDatabase(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("NetworkManager: Failed to parse remote JSON — " + e.Message);
                RemoteDatabase = null;
                CompleteFetch(success: false);
                yield break;
            }

            if (remote == null || CountCards(remote) == 0)
            {
                RemoteDatabase = null;
                CompleteFetch(success: false);
                yield break;
            }

            RemoteDatabase = remote;
            CardLoader.ResolveAllAssets(RemoteDatabase);
            WriteCache(json);
            Debug.Log($"NetworkManager: Downloaded {CountCards(RemoteDatabase)} remote card(s).");
            CompleteFetch(success: true);
        }
    }

    private void CompleteFetch(bool success)
    {
        StartupFetchCompleted = true;
        OnRemoteCardsReady?.Invoke(success);
    }

    private bool TryLoadCache(out CardDatabase database)
    {
        database = null;
        try
        {
            if (!File.Exists(CachePath))
                return false;

            string json = File.ReadAllText(CachePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
                return false;

            database = CardLoader.ParseDatabase(json);
            return database != null && CountCards(database) > 0;
        }
        catch (Exception e)
        {
            Debug.LogWarning("NetworkManager: Cache read failed — " + e.Message);
            database = null;
            return false;
        }
    }

    private void WriteCache(string json)
    {
        try
        {
            File.WriteAllText(CachePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception e)
        {
            Debug.LogWarning("NetworkManager: Cache write failed — " + e.Message);
        }
    }

    private static int CountCards(CardDatabase db)
    {
        if (db == null)
            return 0;

        int count = db.baseDeck != null ? db.baseDeck.Length : 0;
        if (db.unlockablePools == null)
            return count;

        for (int i = 0; i < db.unlockablePools.Length; i++)
        {
            UnlockableCardPool pool = db.unlockablePools[i];
            if (pool?.cards != null)
                count += pool.cards.Length;
        }

        return count;
    }
}
