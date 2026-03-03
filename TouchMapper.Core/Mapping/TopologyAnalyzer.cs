using TouchMapper.Core.Models;

namespace TouchMapper.Core.Mapping;

public static class TopologyAnalyzer
{
    /// <summary>
    /// Groups touch devices that share a common USB ancestor, sorted ascending by hop count.
    /// Returns a list of groups; each group is a list of TouchDeviceInfo ordered fewest→most hops.
    /// </summary>
    public static List<List<TouchDeviceInfo>> GroupByAncestor(List<TouchDeviceInfo> devices)
    {
        if (devices.Count == 0) return [];

        // Build Union-Find on devices that share a USB path prefix
        var parent = Enumerable.Range(0, devices.Count).ToArray();

        for (var i = 0; i < devices.Count; i++)
        {
            for (var j = i + 1; j < devices.Count; j++)
            {
                if (ShareUsbAncestor(devices[i].UsbLocationPath, devices[j].UsbLocationPath))
                    Union(parent, i, j);
            }
        }

        // Collect groups
        var groupMap = new Dictionary<int, List<TouchDeviceInfo>>();
        for (var i = 0; i < devices.Count; i++)
        {
            var root = Find(parent, i);
            if (!groupMap.TryGetValue(root, out var list))
                groupMap[root] = list = [];
            list.Add(devices[i]);
        }

        // Sort each group by hop count ascending
        return groupMap.Values
            .Select(g => g.OrderBy(d => d.HopCount).ToList())
            .ToList();
    }

    /// <summary>
    /// Returns true if two USB location paths share a meaningful common prefix
    /// (at least one common USB hub segment beyond the host controller).
    /// </summary>
    private static bool ShareUsbAncestor(string pathA, string pathB)
    {
        // Paths look like: "PCIROOT(0)#PCI(1400)#USB(1)#USB(1)#USB(3)#USB(1)#USB(2)"
        // Split on '#' and compare segments
        var segsA = pathA.Split('#');
        var segsB = pathB.Split('#');

        var commonDepth = 0;
        var minLen = Math.Min(segsA.Length, segsB.Length);
        for (var i = 0; i < minLen; i++)
        {
            if (!string.Equals(segsA[i], segsB[i], StringComparison.OrdinalIgnoreCase))
                break;
            commonDepth++;
        }

        // Require at least one common USB hub segment (not just PCI root + bridge)
        // and the paths must diverge (they're different devices, different lengths)
        var segsCommon = segsA.Take(commonDepth);
        var hasSharedUsbHub = segsCommon.Any(s => s.StartsWith("USB(", StringComparison.OrdinalIgnoreCase));
        return hasSharedUsbHub && segsA.Length != segsB.Length;
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]]; // path compression
            i = parent[i];
        }
        return i;
    }

    private static void Union(int[] parent, int a, int b)
    {
        parent[Find(parent, a)] = Find(parent, b);
    }
}
