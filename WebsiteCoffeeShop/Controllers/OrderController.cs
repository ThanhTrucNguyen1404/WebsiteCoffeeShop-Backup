using WebsiteCoffeeShop.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using WebsiteCoffeeShop.Extensions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using WebsiteCoffeeShop.IRepository;
using WebsiteCoffeeShop.Context;

namespace WebsiteCoffeeShop.Controllers
{
    public class OrderController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IOrderRepository _orderRepository;

        public OrderController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IOrderRepository orderRepository)
        {
            _context = context;
            _userManager = userManager;
            _orderRepository = orderRepository;
        }

        public IActionResult Checkout()
        {
            var cart = HttpContext.Session.GetObjectFromJson<ShoppingCart>("Cart");
            if (cart == null || !cart.Items.Any())
            {
                TempData["ErrorMessage"] = "Giỏ hàng của bạn đang trống!";
                return RedirectToAction("Index", "ShoppingCart");
            }

            var order = new Order
            {
                TotalPrice = cart.Items.Sum(i => i.Price * i.Quantity)
            };

            return View(order);
        }

        [HttpPost]
        public async Task<IActionResult> ProcessPayment(Order order)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var cart = HttpContext.Session.GetObjectFromJson<ShoppingCart>("Cart");
            if (cart == null || !cart.Items.Any())
            {
                TempData["ErrorMessage"] = "Giỏ hàng trống, không thể đặt hàng!";
                return RedirectToAction("Index", "ShoppingCart");
            }

            // Gán thông tin đơn hàng
            order.UserId = user.Id;
            order.OrderDate = DateTime.Now;
            order.Status = "Pending";
            order.TotalPrice = cart.Items.Sum(i => i.Price * i.Quantity);
            order.OrderDetails = cart.Items.Select(i => new OrderDetail
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                Price = i.Price
            }).ToList();

            // Áp dụng mã giảm giá nếu có
            if (order.DiscountCodeId.HasValue)
            {
                var discount = await _context.DiscountCodes.FindAsync(order.DiscountCodeId.Value);
                if (discount != null && discount.IsActive && discount.ExpiryDate >= DateTime.Now && discount.UsageLimit > 0)
                {
                    decimal discountAmount = 0;
                    if (discount.IsPercentage)
                    {
                        discountAmount = order.TotalPrice * (decimal)discount.DiscountPercent / 100;
                    }
                    else
                    {
                        discountAmount = (decimal)discount.DiscountAmount;
                    }

                    // Đảm bảo không giảm quá tổng tiền
                    if (discountAmount > order.TotalPrice)
                        discountAmount = order.TotalPrice;

                    order.TotalPrice -= discountAmount;
                    if (order.TotalPrice < 0) order.TotalPrice = 0;

                    // Giảm số lượt sử dụng của mã giảm giá
                    discount.UsageLimit--;
                    _context.Update(discount);
                }
                else
                {
                    // Nếu mã giảm giá không hợp lệ, đặt lại về null
                    order.DiscountCodeId = null;
                }
            }

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Cộng điểm thưởng (1% tổng giá trị đơn hàng)
            int pointsEarned = (int)(order.TotalPrice * 0.01m);
            user.RewardPoints += pointsEarned;
            await _userManager.UpdateAsync(user);

            // Xóa giỏ hàng và mã giảm giá sau khi đặt hàng thành công
            HttpContext.Session.Remove("Cart");
            HttpContext.Session.Remove("AppliedDiscountCode");
            HttpContext.Session.Remove("AppliedDiscountId");

            if (order.PaymentMethod == "COD")
            {
                return RedirectToAction("OrderCompleted", new { id = order.Id });
            }
            else if (order.PaymentMethod == "BankTransfer")
            {
                return RedirectToAction("BankTransferInstructions", new { id = order.Id });
            }
            else if (order.PaymentMethod == "VNPAY")
            {
                return RedirectToAction("CreatePaymentUrl", "PaymentController", new { amount = order.TotalPrice, orderId = order.Id });
            }

            TempData["ErrorMessage"] = "Phương thức thanh toán không hợp lệ!";
            return RedirectToAction("Checkout");
        }

        public async Task<IActionResult> OrderCompleted(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
                .Include(o => o.DiscountCode)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy đơn hàng!";
                return RedirectToAction("Index", "Home");
            }

            // Tính toán và truyền thông tin mã giảm giá vào ViewData
            if (order.DiscountCode != null)
            {
                decimal originalTotal = order.OrderDetails.Sum(od => od.Price * od.Quantity);
                decimal discountAmount = 0;

                if (order.DiscountCode.IsPercentage)
                {
                    discountAmount = originalTotal * (order.DiscountCode.DiscountPercent / 100m);
                }
                else
                {
                    discountAmount = (decimal)order.DiscountCode.DiscountAmount;
                }

                // Đảm bảo không giảm quá tổng tiền gốc
                if (discountAmount > originalTotal)
                    discountAmount = originalTotal;

                ViewData["DiscountCodeAmount"] = discountAmount;
                ViewData["DiscountCodeName"] = order.DiscountCode.Code;
                ViewData["OriginalTotal"] = originalTotal;
                ViewData["FinalTotal"] = order.TotalPrice;
            }

            // Tạo CartItems từ OrderDetails để hiển thị
            var cartItems = order.OrderDetails.Select(od => new CartItem
            {
                ProductId = od.ProductId,
                Name = od.Product?.Name ?? "Unknown Product",
                Price = od.Price,
                Quantity = od.Quantity,
                ImageUrl = od.Product?.ImageUrl ?? ""
            }).ToList();

            ViewData["CartItems"] = cartItems;

            return View("Completed", order);
        }

        [HttpGet]
        public async Task<IActionResult> PrintInvoice(int id)
        {
            var order = await _orderRepository.GetOrderByIdToPrint(id);
            if (order == null || order.ApplicationUser == null || order.OrderDetails == null || !order.OrderDetails.Any())
            {
                return NotFound("Dữ liệu đơn hàng không đầy đủ.");
            }

            var pdfBytes = InvoiceGenerator.GenerateInvoicePdf(order);
            return File(pdfBytes, "application/pdf", $"HoaDon-{order.Id}.pdf");
        }

        public async Task<IActionResult> BankTransferInstructions(int id)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy đơn hàng!";
                return RedirectToAction("Index", "Home");
            }

            return View(order);
        }

        [Authorize]
        public async Task<IActionResult> History()
        {
            var userId = _userManager.GetUserId(User);

            var orders = await _context.Orders
                .Where(o => o.UserId == userId)
                .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Product)
                .Include(o => o.DiscountCode)
                .OrderByDescending(o => o.OrderDate)
                .AsNoTracking()
                .ToListAsync();

            return View(orders);
        }

        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
                .Include(o => o.DiscountCode)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                return NotFound();
            }

            // Tính toán và truyền thông tin mã giảm giá vào ViewData
            if (order.DiscountCode != null && order.DiscountCode.IsActive && order.DiscountCode.ExpiryDate >= DateTime.Now)
            {
                decimal originalTotal = order.OrderDetails.Sum(od => od.Price * od.Quantity);
                decimal discountAmount = 0;

                if (order.DiscountCode.IsPercentage)
                {
                    discountAmount = originalTotal * (order.DiscountCode.DiscountPercent / 100m);
                }
                else
                {
                    discountAmount = (decimal)order.DiscountCode.DiscountAmount;
                }

                if (discountAmount > originalTotal)
                    discountAmount = originalTotal;

                ViewData["DiscountCodeAmount"] = discountAmount;
                ViewData["DiscountCodeName"] = order.DiscountCode.Code;
            }

            return View(order);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Product)
                .Include(o => o.DiscountCode)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                return NotFound();
            }

            return View(order);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Edit(Order order)
        {
            if (!ModelState.IsValid)
            {
                return View(order);
            }

            var existingOrder = await _context.Orders
                .Include(o => o.OrderDetails)
                .Include(o => o.DiscountCode)
                .FirstOrDefaultAsync(o => o.Id == order.Id);

            if (existingOrder == null)
            {
                TempData["ErrorMessage"] = "❌ Không tìm thấy đơn hàng!";
                return NotFound();
            }

            // Cập nhật dữ liệu đơn hàng
            existingOrder.ShippingAddress = order.ShippingAddress;
            existingOrder.Notes = order.Notes;
            existingOrder.Status = order.Status;
            existingOrder.TotalPrice = order.TotalPrice;
            existingOrder.PaymentMethod = order.PaymentMethod;

            try
            {
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "✅ Cập nhật đơn hàng thành công!";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi khi lưu: {ex.Message}");
                TempData["ErrorMessage"] = "❌ Lỗi hệ thống khi lưu đơn hàng!";
            }

            return RedirectToAction("History");
        }

        [Authorize]
        public async Task<IActionResult> RemoveItem(int orderId, int productId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .Include(o => o.DiscountCode)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

            if (order == null || order.Status != "Chờ xác nhận")
            {
                return NotFound();
            }

            var itemToRemove = order.OrderDetails.FirstOrDefault(d => d.ProductId == productId);
            if (itemToRemove != null)
            {
                order.OrderDetails.Remove(itemToRemove);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Edit", new { id = orderId });
        }

        [Authorize]
        public async Task<IActionResult> ConfirmOrder(int orderId)
        {
            var order = await _context.Orders
                .Include(o => o.ApplicationUser)
                .Include(o => o.DiscountCode)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return NotFound();
            if (order.ApplicationUser == null) return BadRequest("Người dùng không tồn tại.");

            // Áp dụng mã giảm giá nếu có
            if (order.DiscountCode != null && order.DiscountCode.IsActive && order.DiscountCode.ExpiryDate >= DateTime.Now)
            {
                decimal discountAmount = 0;
                if (order.DiscountCode.IsPercentage)
                {
                    discountAmount = order.TotalPrice * (order.DiscountCode.DiscountPercent / 100m);
                }
                else
                {
                    discountAmount = (decimal)order.DiscountCode.DiscountAmount;
                }

                if (discountAmount > order.TotalPrice)
                    discountAmount = order.TotalPrice;

                order.TotalPrice -= discountAmount;
                if (order.TotalPrice < 0) order.TotalPrice = 0;
            }

            // Cộng điểm thưởng từ giá trị đơn hàng
            int rewardPoints = (int)(order.TotalPrice / 1000); // 1,000 VND = 1 điểm
            order.RewardPointsEarned = rewardPoints;
            order.ApplicationUser.RewardPoints += rewardPoints;

            _context.Update(order);
            _context.Update(order.ApplicationUser);
            await _context.SaveChangesAsync();

            // Cập nhật Claim mới
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var existingClaim = claimsIdentity.FindFirst("RewardPoints");

            if (existingClaim != null)
                claimsIdentity.RemoveClaim(existingClaim);

            claimsIdentity.AddClaim(new Claim("RewardPoints", order.ApplicationUser.RewardPoints.ToString()));

            var authProperties = new AuthenticationProperties();
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity), authProperties);

            return RedirectToAction("OrderHistory");
        }
    }
}