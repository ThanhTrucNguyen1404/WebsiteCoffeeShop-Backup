using WebsiteCoffeeShop.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebsiteCoffeeShop.Context;

namespace WebsiteCoffeeShop.Controllers
{
    public class DiscountCodeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DiscountCodeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // API để lấy danh sách mã giảm giá đang hoạt động
        [HttpGet]
        public async Task<IActionResult> GetAvailableDiscountCodes()
        {
            var discountCodes = await _context.DiscountCodes
                .Where(d => d.IsActive && d.ExpiryDate >= DateTime.Now && d.UsageLimit > 0)
                .Select(d => new {
                    d.Id,
                    d.Code,
                    d.Description,
                    d.IsPercentage,
                    d.DiscountPercent,
                    d.DiscountAmount,
                    d.ExpiryDate,
                    d.UsageLimit
                })
                .OrderBy(d => d.Code)
                .ToListAsync();

            return Json(discountCodes);
        }

        // API để kiểm tra và lấy chi tiết mã giảm giá
        [HttpGet]
        public async Task<IActionResult> VerifyDiscountCode(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return Json(new
                {
                    success = false,
                    message = "Mã giảm giá không được để trống!"
                });
            }

            var discountCode = await _context.DiscountCodes
                .Where(d => d.Code.ToUpper() == code.ToUpper() && d.IsActive && d.ExpiryDate >= DateTime.Now)
                .FirstOrDefaultAsync();

            if (discountCode == null)
            {
                return Json(new
                {
                    success = false,
                    message = "Mã giảm giá không tồn tại hoặc đã hết hạn!"
                });
            }

            if (discountCode.UsageLimit <= 0)
            {
                return Json(new
                {
                    success = false,
                    message = "Mã giảm giá đã hết lượt sử dụng!"
                });
            }

            // Trả về thông tin mã giảm giá hợp lệ
            var result = new
            {
                Id = discountCode.Id,
                Code = discountCode.Code,
                Description = discountCode.Description,
                IsPercentage = discountCode.IsPercentage,
                DiscountPercent = discountCode.DiscountPercent,
                DiscountAmount = discountCode.DiscountAmount,
                ExpiryDate = discountCode.ExpiryDate,
                UsageLimit = discountCode.UsageLimit
            };

            return Json(new
            {
                success = true,
                message = "Mã giảm giá hợp lệ!",
                data = result
            });
        }

        // API để áp dụng mã giảm giá (lưu vào session)
        [HttpPost]
        public async Task<IActionResult> ApplyDiscountCode([FromBody] ApplyDiscountRequest request)
        {
            if (string.IsNullOrEmpty(request.Code))
            {
                return Json(new
                {
                    success = false,
                    message = "Mã giảm giá không được để trống!"
                });
            }

            var discountCode = await _context.DiscountCodes
                .Where(d => d.Code.ToUpper() == request.Code.ToUpper() && d.IsActive && d.ExpiryDate >= DateTime.Now)
                .FirstOrDefaultAsync();

            if (discountCode == null)
            {
                return Json(new
                {
                    success = false,
                    message = "Mã giảm giá không tồn tại hoặc đã hết hạn!"
                });
            }

            if (discountCode.UsageLimit <= 0)
            {
                return Json(new
                {
                    success = false,
                    message = "Mã giảm giá đã hết lượt sử dụng!"
                });
            }

            // Kiểm tra xem người dùng đã sử dụng mã này chưa
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                var hasUsedCode = await _context.Orders
                    .AnyAsync(o => o.UserId == userId && o.DiscountCodeId == discountCode.Id);
                
                if (hasUsedCode)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Bạn đã sử dụng mã giảm giá này trước đó!"
                    });
                }
            }

            // Lưu mã giảm giá vào session
            HttpContext.Session.SetString("AppliedDiscountCode", discountCode.Code);
            HttpContext.Session.SetInt32("AppliedDiscountId", discountCode.Id);

            // Tính toán giá trị giảm giá
            decimal discountAmount = 0;
            if (request.TotalAmount > 0)
            {
                if (discountCode.IsPercentage)
                {
                    discountAmount = request.TotalAmount * (discountCode.DiscountPercent / 100m);
                }
                else
                {
                    discountAmount = (decimal)discountCode.DiscountAmount;
                }

                // Đảm bảo số tiền giảm không vượt quá tổng tiền
                if (discountAmount > request.TotalAmount)
                {
                    discountAmount = request.TotalAmount;
                }
            }

            return Json(new
            {
                success = true,
                message = "Áp dụng mã giảm giá thành công!",
                discountAmount = discountAmount,
                finalAmount = Math.Max(0, request.TotalAmount - discountAmount),
                discountInfo = new
                {
                    Code = discountCode.Code,
                    Description = discountCode.Description,
                    IsPercentage = discountCode.IsPercentage,
                    DiscountPercent = discountCode.DiscountPercent,
                    DiscountAmount = discountCode.DiscountAmount,
                    UsageLimit = discountCode.UsageLimit
                }
            });
        }

        // API để xóa mã giảm giá đã áp dụng
        [HttpPost]
        public IActionResult RemoveDiscountCode()
        {
            HttpContext.Session.Remove("AppliedDiscountCode");
            HttpContext.Session.Remove("AppliedDiscountId");

            return Json(new
            {
                success = true,
                message = "Đã xóa mã giảm giá!"
            });
        }

        // API để lấy thông tin mã giảm giá hiện tại từ session
        [HttpGet]
        public async Task<IActionResult> GetCurrentDiscountCode()
        {
            var discountCode = HttpContext.Session.GetString("AppliedDiscountCode");

            if (string.IsNullOrEmpty(discountCode))
            {
                return Json(new
                {
                    success = false,
                    message = "Chưa áp dụng mã giảm giá nào!"
                });
            }

            var discount = await _context.DiscountCodes
                .Where(d => d.Code == discountCode && d.IsActive && d.ExpiryDate >= DateTime.Now)
                .FirstOrDefaultAsync();

            if (discount == null)
            {
                // Xóa mã giảm giá không hợp lệ khỏi session
                HttpContext.Session.Remove("AppliedDiscountCode");
                HttpContext.Session.Remove("AppliedDiscountId");

                return Json(new
                {
                    success = false,
                    message = "Mã giảm giá đã hết hạn!"
                });
            }

            return Json(new
            {
                success = true,
                data = new
                {
                    Code = discount.Code,
                    Description = discount.Description,
                    IsPercentage = discount.IsPercentage,
                    DiscountPercent = discount.DiscountPercent,
                    DiscountAmount = discount.DiscountAmount
                }
            });
        }
    }

    // Model cho request áp dụng mã giảm giá
    public class ApplyDiscountRequest
    {
        public string Code { get; set; }
        public decimal TotalAmount { get; set; }
    }
}