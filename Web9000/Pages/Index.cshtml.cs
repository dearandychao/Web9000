using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Web9000.Utilities;

namespace Web9000.Pages
{
    public class IndexModel : PageModel
    {
        public IActionResult OnGet()
        {
            // 檢查是否已登入
            var currentUser = SessionHelper.GetCurrentUser(HttpContext.Session);

            if (currentUser != null)
            {
                // 已登入，導向紀錄查詢
                return RedirectToPage("/Records/Query");
            }
            else
            {
                // 未登入，導向登入頁面
                return RedirectToPage("/Login");
            }
        }
    }
}
