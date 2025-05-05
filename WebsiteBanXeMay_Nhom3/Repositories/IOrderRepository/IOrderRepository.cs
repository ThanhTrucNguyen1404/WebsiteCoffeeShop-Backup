using WebsiteBanXeMay_Nhom3.Models;

namespace WebsiteBanXeMay_Nhom3.Repositories
{
    public interface IOrderRepository
    {
        Task<List<Order>> GetOrdersByUserAsync(string userId); // Lấy đơn hàng của người dùng
        Task<Order> GetByIdAsync(int id); // Lấy đơn hàng theo ID
        Task AddAsync(Order order); // Thêm đơn hàng mới
        Task UpdateAsync(Order order); // Cập nhật đơn hàng
        Task DeleteAsync(int id); // Xóa đơn hàng
    }
}
