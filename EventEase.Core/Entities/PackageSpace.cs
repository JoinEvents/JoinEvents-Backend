using System;

namespace EventEase.Core.Entities
{
    public class PackageSpace
    {
        public Guid PackageId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int SeatingCapacity { get; set; }
        public int FloatingCapacity { get; set; }
    }
}
