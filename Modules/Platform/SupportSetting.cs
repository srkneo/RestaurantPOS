using System;

namespace POSSystem.Models
{
    // Who restaurants call for help (one row, Id 1). The platform owner sets it on the cloud on /platform/support;
    // shop PCs get a copy with every licence renewal, so the footer and About page are right there too.
    public class SupportSetting
    {
        public int Id { get; set; }
        public string CompanyName { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string? Phone { get; set; }
        public string? WhatsApp { get; set; }
        public string? Email { get; set; }
        public string? Hours { get; set; }
        public string? Website { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
