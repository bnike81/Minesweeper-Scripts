using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// WaitCache — Cache de WaitForSeconds pour éviter les allocations GC.
/// new WaitForSeconds(t) alloue à chaque appel — cette classe réutilise les instances.
/// Usage : yield return WaitCache.Get(0.1f); au lieu de yield return new WaitForSeconds(0.1f);
/// </summary>
public static class WaitCache
{
    private static readonly Dictionary<float, WaitForSeconds> _cache
        = new Dictionary<float, WaitForSeconds>(32);

    private static readonly WaitForEndOfFrame _endOfFrame = new WaitForEndOfFrame();
    private static readonly WaitForFixedUpdate _fixedUpdate = new WaitForFixedUpdate();

    /// <summary>Retourne un WaitForSeconds caché — zéro allocation.</summary>
    public static WaitForSeconds Get(float seconds)
    {
        // Arrondir à 3 décimales pour éviter les doublons flottants
        float key = Mathf.Round(seconds * 1000f) / 1000f;
        if (!_cache.TryGetValue(key, out var wait))
        {
            wait = new WaitForSeconds(key);
            _cache[key] = wait;
        }
        return wait;
    }

    public static WaitForEndOfFrame EndOfFrame => _endOfFrame;
    public static WaitForFixedUpdate FixedUpdate => _fixedUpdate;
}