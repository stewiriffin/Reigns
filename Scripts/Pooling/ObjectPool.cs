using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight GameObject pool. Prefer <see cref="Get"/> / <see cref="Release"/> over
/// Instantiate/Destroy on Android hot paths.
/// </summary>
public sealed class ObjectPool
{
    private readonly Stack<GameObject> available;
    private readonly HashSet<GameObject> leased;
    private readonly GameObject prefab;
    private readonly Transform parent;
    private readonly Action<GameObject> onGet;
    private readonly Action<GameObject> onRelease;
    private readonly bool worldPositionStays;

    public int AvailableCount => available.Count;
    public int LeasedCount => leased.Count;

    public ObjectPool(
        GameObject prefab,
        Transform parent,
        int prewarmCount = 0,
        Action<GameObject> onGet = null,
        Action<GameObject> onRelease = null,
        bool worldPositionStays = false)
    {
        this.prefab = prefab;
        this.parent = parent;
        this.onGet = onGet;
        this.onRelease = onRelease;
        this.worldPositionStays = worldPositionStays;
        available = new Stack<GameObject>(Mathf.Max(4, prewarmCount));
        leased = new HashSet<GameObject>();

        for (int i = 0; i < prewarmCount; i++)
            available.Push(CreateInstance(inactive: true));
    }

    public GameObject Get()
    {
        GameObject instance = available.Count > 0 ? available.Pop() : CreateInstance(inactive: false);
        leased.Add(instance);

        if (parent != null && instance.transform.parent != parent)
            instance.transform.SetParent(parent, worldPositionStays);

        instance.SetActive(true);
        onGet?.Invoke(instance);
        return instance;
    }

    public T Get<T>() where T : Component
    {
        GameObject go = Get();
        return go.GetComponent<T>();
    }

    public void Release(GameObject instance)
    {
        if (instance == null)
            return;

        if (!leased.Remove(instance))
        {
            // Already released or never leased — still deactivate safely.
            instance.SetActive(false);
            return;
        }

        onRelease?.Invoke(instance);
        instance.SetActive(false);

        if (parent != null && instance.transform.parent != parent)
            instance.transform.SetParent(parent, worldPositionStays);

        available.Push(instance);
    }

    public void ReleaseAllLeased()
    {
        if (leased.Count == 0)
            return;

        var snapshot = new List<GameObject>(leased);
        for (int i = 0; i < snapshot.Count; i++)
            Release(snapshot[i]);
    }

    private GameObject CreateInstance(bool startInactive)
    {
        GameObject instance;
        if (prefab != null)
        {
            instance = UnityEngine.Object.Instantiate(prefab, parent);
        }
        else
        {
            instance = new GameObject("PooledObject");
            if (parent != null)
                instance.transform.SetParent(parent, worldPositionStays);
        }

        instance.SetActive(!startInactive);
        return instance;
    }
}
