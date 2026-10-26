// Shared by the solo test tools.
static class ToolUtil
{
    // The game's model hash (Jenkins one-at-a-time over the lower-case name).
    public static uint Joaat(string name)
    {
        uint h = 0;
        foreach (char c in name.ToLowerInvariant())
        {
            h += c;
            h += h << 10;
            h ^= h >> 6;
        }
        h += h << 3;
        h ^= h >> 11;
        h += h << 15;
        return h;
    }
}
