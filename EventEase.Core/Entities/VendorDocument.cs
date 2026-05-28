using System;

namespace EventEase.Core.Entities
{
    public class VendorDocument
    {
        public Guid Id { get; set; }
        public Guid VendorId { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileUrl { get; set; } = string.Empty;
        public string Status { get; set; } = "pending"; // pending, approved, rejected
        public string? RejectionReason { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public Guid? AuditedBy { get; set; }
    }
}
