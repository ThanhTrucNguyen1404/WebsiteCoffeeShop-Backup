using WebsiteBanXeMay_Nhom3.Models;

namespace WebsiteBanXeMay_Nhom3.Repositories
{
    public interface IProductRepository
    {
        Task<IEnumerable<Product>> GetAllAsync();
        Task<Product> GetByIdAsync(int id);
        Task AddAsync(Product product);
        Task UpdateAsync(Product product);
        Task DeleteAsync(int id);
        Task<List<Product>> GetPaginatedProductsAsync(int page, int pageSize);
        Task<IEnumerable<Product>> SearchProductsAsync(string keyword);

    }
}
