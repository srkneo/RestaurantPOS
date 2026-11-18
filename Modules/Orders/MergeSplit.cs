using System.Collections.Generic;

namespace POSSystem.Orders
{
    // Merge: another table's open order joins this one (one bill). Split: some items leave for a new order with its own bill.
    public record MergeOrderRequest(int FromOrderId, int Version, int FromVersion);

    public record SplitLine(int OrderItemId, int Quantity);

    public record SplitOrderRequest(List<SplitLine>? Lines, int Version);
}
