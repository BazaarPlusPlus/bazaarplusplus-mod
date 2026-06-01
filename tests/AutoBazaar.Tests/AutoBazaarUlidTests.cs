using System;
using System.Collections.Generic;
using System.Threading;
using BazaarPlusPlus.AutoBazaar;
using Xunit;

public class AutoBazaarUlidTests
{
    [Fact]
    public void New_ReturnsLength26()
    {
        var u = AutoBazaarUlid.New();
        Assert.Equal(26, u.Length);
    }

    [Fact]
    public void New_UsesOnlyCrockfordAlphabet()
    {
        const string alpha = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var u = AutoBazaarUlid.New();
        foreach (var c in u)
            Assert.Contains(c, alpha);
    }

    [Fact]
    public void New_IsMonotonicOver10000Iterations()
    {
        var prev = AutoBazaarUlid.New();
        for (var i = 0; i < 10_000; i++)
        {
            var next = AutoBazaarUlid.New();
            Assert.True(
                string.CompareOrdinal(prev, next) < 0,
                $"non-monotonic at iteration {i}: prev={prev} next={next}"
            );
            prev = next;
        }
    }

    [Fact]
    public void New_RemainsMonotonic_AcrossThreads()
    {
        const int threadCount = 8;
        const int perThread = 1000;
        var bag = new System.Collections.Concurrent.ConcurrentBag<string>();
        var threads = new List<Thread>();
        for (var t = 0; t < threadCount; t++)
        {
            var th = new Thread(() =>
            {
                for (var i = 0; i < perThread; i++)
                    bag.Add(AutoBazaarUlid.New());
            });
            threads.Add(th);
            th.Start();
        }
        foreach (var th in threads)
            th.Join();
        var all = new List<string>(bag);
        all.Sort(StringComparer.Ordinal);
        var distinct = new HashSet<string>(all, StringComparer.Ordinal);
        Assert.Equal(all.Count, distinct.Count); // no duplicates
    }
}
