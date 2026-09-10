using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// GroundItemBobManager — Anime le flottement de TOUS les items au sol
/// dans un seul Update() au lieu d'une coroutine infinie PAR item.
///
/// AVANT : 50 items au sol = 50 coroutines permanentes (overhead enumerator + scheduler)
/// APRÈS : 50 items = 1 seul Update, une boucle simple.
///
/// SETUP : Créer un GameObject "GroundItemBobManager" dans la scène avec ce script.
///         (Auto-créé par GroundItem si absent — aucun setup obligatoire.)
/// </summary>
public class GroundItemBobManager : MonoBehaviour
{
    public static GroundItemBobManager Instance { get; private set; }

    private struct BobEntry
    {
        public Transform tr;
        public Vector3 startPos;
        public float phase;
        public float speed;
        public float amplitude;
    }

    private readonly List<BobEntry> _entries = new List<BobEntry>(64);
    // Index rapide transform → position dans la liste (pour Unregister O(1))
    private readonly Dictionary<Transform, int> _indexOf = new Dictionary<Transform, int>(64);

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Crée le manager à la volée si absent de la scène.</summary>
    public static GroundItemBobManager GetOrCreate()
    {
        if (Instance == null)
        {
            var go = new GameObject("GroundItemBobManager");
            Instance = go.AddComponent<GroundItemBobManager>();
        }
        return Instance;
    }

    public void Register(Transform tr, Vector3 startPos, float speed, float amplitude)
    {
        if (tr == null || _indexOf.ContainsKey(tr)) return;
        _indexOf[tr] = _entries.Count;
        _entries.Add(new BobEntry
        {
            tr = tr,
            startPos = startPos,
            phase = Random.Range(0f, Mathf.PI * 2f),
            speed = speed,
            amplitude = amplitude
        });
    }

    public void Unregister(Transform tr)
    {
        if (tr == null || !_indexOf.TryGetValue(tr, out int idx)) return;

        // Swap-remove O(1) : le dernier prend la place du retiré
        int last = _entries.Count - 1;
        if (idx != last)
        {
            _entries[idx] = _entries[last];
            _indexOf[_entries[idx].tr] = idx;
        }
        _entries.RemoveAt(last);
        _indexOf.Remove(tr);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var e = _entries[i];
            if (e.tr == null)
            {
                // L'objet a été détruit sans Unregister — nettoyage
                _indexOf.Remove(e.tr);
                int last = _entries.Count - 1;
                if (i != last)
                {
                    _entries[i] = _entries[last];
                    if (_entries[i].tr != null) _indexOf[_entries[i].tr] = i;
                }
                _entries.RemoveAt(last);
                continue;
            }

            e.phase += dt * e.speed;
            e.tr.position = e.startPos
                + new Vector3(0f, Mathf.Sin(e.phase) * e.amplitude, 0f);
            _entries[i] = e;
        }
    }
}