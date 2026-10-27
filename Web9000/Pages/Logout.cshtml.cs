using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Web9000.Utilities;

namespace Web9000.Pages
{
    public class LogoutModel : PageModel
    {
        public IActionResult OnGet()
        {
            SessionHelper.ClearCurrentUser(HttpContext.Session);
            return RedirectToPage("/Login");
        }
    }
}
