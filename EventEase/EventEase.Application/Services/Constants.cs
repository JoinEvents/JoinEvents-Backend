using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Application.Services
{
    public class Constants
    {
        public enum ServiceStatus
        {
            Pending = 0,
            Active = 1,
            Rejected = 2,
            Paused = 3
        }
    }
}
