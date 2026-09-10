using System.Collections.Generic;

/// <summary>
/// EnumNameCache — Cache les résultats de Enum.ToString() pour éviter les allocations.
/// Enum.ToString() alloue une nouvelle string à chaque appel.
/// Usage : EnumNameCache.Get(myEnum) au lieu de myEnum.ToString()
/// </summary>
public static class EnumNameCache
{
    private static readonly Dictionary<System.Enum, string> _cache
        = new Dictionary<System.Enum, string>(64);

    public static string Get(System.Enum value)
    {
        if (!_cache.TryGetValue(value, out var name))
        {
            name = value.ToString();
            _cache[value] = name;
        }
        return name;
    }
}