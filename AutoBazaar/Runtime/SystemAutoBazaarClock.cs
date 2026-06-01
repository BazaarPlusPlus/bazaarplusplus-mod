#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;

namespace BazaarPlusPlus.AutoBazaar;

public sealed class SystemAutoBazaarClock : IAutoBazaarClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public double NowSeconds => _stopwatch.Elapsed.TotalSeconds;

    public string UtcNowIsoString() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
}
