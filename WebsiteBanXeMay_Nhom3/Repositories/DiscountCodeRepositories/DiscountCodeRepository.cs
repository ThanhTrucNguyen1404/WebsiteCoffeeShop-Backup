using WebsiteBanXeMay_Nhom3.Models;
using Microsoft.EntityFrameworkCore;

namespace WebsiteBanXeMay_Nhom3.Repositories.DiscountCodeRepositories
{
    public class DiscountCodeRepository : IDiscountCodeRepository
    {
        private readonly ApplicationDbContext _context;

        public DiscountCodeRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<DiscountCode>> GetAllAsync()
            => await _context.DiscountCodes.ToListAsync();

        public async Task<DiscountCode> GetByIdAsync(int id)
            => await _context.DiscountCodes.FindAsync(id);

        public async Task<DiscountCode> GetByCodeAsync(string code)
            => await _context.DiscountCodes.FirstOrDefaultAsync(x => x.Code == code);

        public async Task AddAsync(DiscountCode discountCode)
        {
            _context.DiscountCodes.Add(discountCode);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(DiscountCode discountCode)
        {
            _context.DiscountCodes.Update(discountCode);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var discount = await GetByIdAsync(id);
            if (discount != null)
            {
                _context.DiscountCodes.Remove(discount);
                await _context.SaveChangesAsync();
            }
        }
    }

}
