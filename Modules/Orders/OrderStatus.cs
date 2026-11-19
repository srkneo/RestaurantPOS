namespace POSSystem.Models
{
    public enum OrderStatus
    {
        Received,
        Preparing,
        Ready,
        Completed,
        Cancelled,
        // Dine-in food is at the table; the table stays busy until the bill is paid (then Completed).
        Served
    }
}
