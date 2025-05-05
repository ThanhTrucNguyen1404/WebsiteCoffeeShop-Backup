using WebsiteBanXeMay_Nhom3.Models;

namespace WebsiteBanXeMay_Nhom3.Repositories.DiscountCodeRepositories
{
    public interface IDiscountCodeRepository
    {
        Task<IEnumerable<DiscountCode>> GetAllAsync();
        Task<DiscountCode> GetByIdAsync(int id);
        Task<DiscountCode> GetByCodeAsync(string code);
        Task AddAsync(DiscountCode discountCode);
        Task UpdateAsync(DiscountCode discountCode);
        Task DeleteAsync(int id);
    }
}
