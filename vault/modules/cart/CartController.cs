using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BelovedApp.Controllers
{
    [ApiController]
    [Route("api/cart")]
    public class CartController : ControllerBase
    {
        [HttpPost("checkout")]
        public IActionResult ProcessCheckout([FromBody] CheckoutRequest request)
        {
            if (request.Subtotal < 0)
            {
                return BadRequest(new { message = "Subtotal cannot be negative." });
            }

            var tax = request.Subtotal * 0.1;
            var total = request.Subtotal + tax;

            return Ok(new
            {
                orderId = System.Guid.NewGuid().ToString("N"),
                subtotal = request.Subtotal,
                tax = tax,
                total = total,
                status = "Processed"
            });
        }
    }

    public class CheckoutRequest
    {
        [Required]
        public double Subtotal { get; set; }

        public List<int> ItemIds { get; set; } = new();
    }
}
