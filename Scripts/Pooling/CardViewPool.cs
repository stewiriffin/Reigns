using UnityEngine;

/// <summary>
/// Scene-level pool for card visual GameObjects. Prewarms on Awake; never Destroy at runtime.
/// </summary>
public class CardViewPool : MonoBehaviour
{
    public static CardViewPool Instance { get; private set; }

    [SerializeField] private GameObject cardViewPrefab;
    [SerializeField] private Transform poolRoot;
    [SerializeField] private int prewarmCount = 4;

    private ObjectPool pool;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        if (poolRoot == null)
            poolRoot = transform;

        // Prefab optional: pool still works with empty shells for testing.
        pool = new ObjectPool(
            cardViewPrefab,
            poolRoot,
            prewarmCount,
            onGet: go =>
            {
                var view = go.GetComponent<PooledCardView>();
                if (view != null)
                    view.OnSpawned();
            },
            onRelease: go =>
            {
                var view = go.GetComponent<PooledCardView>();
                if (view != null)
                    view.OnDespawned();
            });
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public GameObject Get()
    {
        EnsurePool();
        return pool.Get();
    }

    public PooledCardView GetView()
    {
        GameObject go = Get();
        var view = go.GetComponent<PooledCardView>();
        if (view == null)
            view = go.AddComponent<PooledCardView>();
        return view;
    }

    public void Release(GameObject instance)
    {
        if (instance == null)
            return;

        EnsurePool();
        pool.Release(instance);
    }

    public void Release(PooledCardView view)
    {
        if (view != null)
            Release(view.gameObject);
    }

    private void EnsurePool()
    {
        if (pool != null)
            return;

        if (poolRoot == null)
            poolRoot = transform;

        pool = new ObjectPool(cardViewPrefab, poolRoot, prewarmCount);
    }
}
