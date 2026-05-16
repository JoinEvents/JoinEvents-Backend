using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Core.Entities
{
    public class RefreshToken
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Token { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool Revoked { get; set; }
    }
}
