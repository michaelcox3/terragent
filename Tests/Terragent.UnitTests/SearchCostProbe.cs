using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Xunit.Abstractions;

namespace Terragent.UnitTests;

/// <summary>Temporary: measures what one node expansion costs. Delete after reading.</summary>
public class SearchCostProbe(ITestOutputHelper output)
{
    private static readonly Leap Jump = Leap.Of(15, 5.01f, 0.4f, 0.08f, 3f);
    private static readonly Costs Prices = new(4f, 45f, 30f, 10f, 1.5f, 1f);

    [Fact]
    public void MeasureNodeCost()
    {
        // Open rock with a corridor along the top, which is the underground shape the
        // run was searching when it took a third of a second.
        const int Wide = 120;
        const int Tall = 60;
        List<string> rows = [];
        for (int y = 0; y < Tall; y++)
        {
            rows.Add(y < 3 ? new string('.', Wide) : new string('#', Wide));
        }

        Grid grid = new(border: false, [.. rows]);
        Point from = new(2, 3);
        Point to = new(Wide - 4, Tall - 4);

        // Warm the code paths so the reading is not the first call's jitting.
        new Navigator(grid).FindRoute(Prices, 35, Jump, from, to, blocks: 0);

        long before = GC.GetTotalAllocatedBytes(precise: true);
        Stopwatch clock = Stopwatch.StartNew();
        Route? route = new Navigator(grid).FindRoute(Prices, 35, Jump, from, to, blocks: 0);
        double spent = clock.Elapsed.TotalMilliseconds;
        long bytes = GC.GetTotalAllocatedBytes(precise: true) - before;

        int expanded = route?.Examined ?? 0;
        output.WriteLine($"expanded {expanded} in {spent:0.0}ms");
        output.WriteLine($"per node {spent * 1000 / Math.Max(1, expanded):0.0}us");
        output.WriteLine($"allocated {bytes / 1024.0:0}KB, {bytes / Math.Max(1, expanded)}B per node");
    }
}
