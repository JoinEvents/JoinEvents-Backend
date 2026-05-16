using System;

namespace EventEase.Core.Entities
{
    public class PackageImage
    {
        public string Id { get; set; } = "img_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        public Guid PackageId { get; set; }
        public string Url { get; set; } = string.Empty;
        public bool IsMain { get; set; }
        
        public Package Package { get; set; }
    }
}
