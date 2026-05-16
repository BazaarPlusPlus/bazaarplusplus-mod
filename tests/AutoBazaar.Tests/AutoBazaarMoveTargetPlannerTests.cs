using System.Collections.Generic;
using Xunit;
using BazaarPlusPlus.Game.AutoBazaar;

public class AutoBazaarMoveTargetPlannerTests
{
    [Fact]
    public void Size1_NoOccupancy_Returns10Placements()
    {
        var result = AutoBazaarMoveTargetPlanner.Enumerate(
            itemSize: 1,
            capacity: 10,
            occupiedSockets: new HashSet<int>());

        Assert.Equal(10, result.Count);
        for (int i = 0; i < 10; i++)
        {
            Assert.Single(result[i]);
            Assert.Equal($"Socket_{i}", result[i][0]);
        }
    }

    [Fact]
    public void Size2_NoOccupancy_Returns9Placements()
    {
        var result = AutoBazaarMoveTargetPlanner.Enumerate(
            itemSize: 2,
            capacity: 10,
            occupiedSockets: new HashSet<int>());

        Assert.Equal(9, result.Count);
        // First placement: Socket_0, Socket_1
        Assert.Equal("Socket_0", result[0][0]);
        Assert.Equal("Socket_1", result[0][1]);
        // Last placement: Socket_8, Socket_9
        Assert.Equal("Socket_8", result[8][0]);
        Assert.Equal("Socket_9", result[8][1]);
    }

    [Fact]
    public void Size3_Occupied3And4_Returns4Placements()
    {
        // Occupied: {3,4}
        // Valid starts for size-3: 0(0,1,2), 5(5,6,7), 6(6,7,8), 7(7,8,9) = 4 placements
        // Start 1 → 1,2,3 blocked by 3
        // Start 2 → 2,3,4 blocked by 3
        // Start 3 → 3,4,5 blocked by 3
        // Start 4 → 4,5,6 blocked by 4
        var result = AutoBazaarMoveTargetPlanner.Enumerate(
            itemSize: 3,
            capacity: 10,
            occupiedSockets: new HashSet<int> { 3, 4 });

        Assert.Equal(4, result.Count);
        Assert.Equal(new[] { "Socket_0", "Socket_1", "Socket_2" }, result[0]);
        Assert.Equal(new[] { "Socket_5", "Socket_6", "Socket_7" }, result[1]);
        Assert.Equal(new[] { "Socket_6", "Socket_7", "Socket_8" }, result[2]);
        Assert.Equal(new[] { "Socket_7", "Socket_8", "Socket_9" }, result[3]);
    }

    [Fact]
    public void Size3_ExcludeOwnSlots2to4_TreatsThemAsVacant()
    {
        // Simulate a size-3 item sitting at Socket_2,3,4 that we want to move.
        // Without excluding, those sockets would block placement there.
        // With exclude range [2, count=3], they are treated as vacant.
        var result = AutoBazaarMoveTargetPlanner.Enumerate(
            itemSize: 3,
            capacity: 10,
            occupiedSockets: new HashSet<int> { 2, 3, 4 },
            excludeStartIndexInclusive: 2,
            excludeCountInclusive: 3);

        // All 8 valid starts (capacity - itemSize + 1 = 8)
        Assert.Equal(8, result.Count);
        Assert.Equal(new[] { "Socket_0", "Socket_1", "Socket_2" }, result[0]);
        Assert.Equal(new[] { "Socket_2", "Socket_3", "Socket_4" }, result[2]);
    }

    [Fact]
    public void SizeGreaterThanCapacity_ReturnsEmpty()
    {
        var result = AutoBazaarMoveTargetPlanner.Enumerate(
            itemSize: 11,
            capacity: 10,
            occupiedSockets: new HashSet<int>());

        Assert.Empty(result);
    }

    [Fact]
    public void SizeEqualsCapacity_NoOccupancy_ReturnsOnePlacement()
    {
        var result = AutoBazaarMoveTargetPlanner.Enumerate(
            itemSize: 10,
            capacity: 10,
            occupiedSockets: new HashSet<int>());

        Assert.Single(result);
        Assert.Equal(10, result[0].Count);
        Assert.Equal("Socket_0", result[0][0]);
        Assert.Equal("Socket_9", result[0][9]);
    }
}
