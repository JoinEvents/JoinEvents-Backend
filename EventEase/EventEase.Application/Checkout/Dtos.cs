using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventEase.Application.Checkout
{
    public class Dtos
    {
        public record CartItem(Guid ServiceId, int Qty);
        public record CartRequest(List<CartItem> Items);
        public record CartPreview(decimal Total, List<(string name, int qty, decimal price)> Lines);
        public record InitiatePaymentRequest(Guid BookingId, string PaymentMethod, string? CouponCode);
    }
}
