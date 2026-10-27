using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Web9000.Data;
using Web9000.Models;
using Web9000.Utilities;

namespace Web9000.Pages
{
    public class LoginModel : PageModel
    {
        private readonly IDbConnectionFactory _dbFactory;
        private readonly IConfiguration _configuration;

        [BindProperty]
        public string LoginName { get; set; } = string.Empty;

        [BindProperty]
        public string LoginPassword { get; set; } = string.Empty;

        public string? ErrorMessage { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string CompanyLogo { get; set; } = string.Empty;

        public LoginModel(IDbConnectionFactory dbFactory, IConfiguration configuration)
        {
            _dbFactory = dbFactory;
            _configuration = configuration;
        }

        public void OnGet()
        {
            // 如果已登入，導向紀錄查詢頁面
            var currentUser = SessionHelper.GetCurrentUser(HttpContext.Session);
            if (currentUser != null)
            {
                Response.Redirect("/Records/Query");
                return;
            }

            // 載入公司資訊
            CompanyName = _configuration.GetValue<string>("Web9000Settings:CompanyName") ?? "差勤查詢系統";
            CompanyLogo = _configuration.GetValue<string>("Web9000Settings:CompanyLogo") ?? "/images/logo.png";
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // 載入公司資訊
            CompanyName = _configuration.GetValue<string>("Web9000Settings:CompanyName") ?? "差勤查詢系統";
            CompanyLogo = _configuration.GetValue<string>("Web9000Settings:CompanyLogo") ?? "/images/logo.png";

            // 驗證必填欄位
            if (string.IsNullOrWhiteSpace(LoginName) || string.IsNullOrWhiteSpace(LoginPassword))
            {
                ErrorMessage = "請輸入帳號與密碼";
                return Page();
            }

            try
            {
                using var conn = _dbFactory.GetWeb9000Connection();
                conn.Open();

                // 查詢使用者資料
                var user = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    @"SELECT 
                        EMPLOYEE_ID, 
                        EMPLOYEE_NAME, 
                        EMPLOYEE_NO, 
                        DEPARTMENT_ID, 
                        LOGIN_NAME, 
                        LOGIN_PASSWORD, 
                        EMPLOYEE_TYPE
                      FROM [W9K_EMPLOYEE]
                      WHERE LOGIN_NAME = @LoginName",
                    new { LoginName });

                if (user == null)
                {
                    ErrorMessage = "帳號或密碼錯誤";
                    return Page();
                }

                // 驗證密碼
                var passwordHasher = new PasswordHasher<object>();
                var verifyResult = passwordHasher.VerifyHashedPassword(null, user.LOGIN_PASSWORD, LoginPassword);

                if (verifyResult == PasswordVerificationResult.Failed)
                {
                    ErrorMessage = "帳號或密碼錯誤";
                    return Page();
                }

                // 查詢使用者可管理的部門
                var managedDepartments = await conn.QueryAsync<int>(
                    @"SELECT DEPARTMENT_ID 
                      FROM [W9K_MANAGER] 
                      WHERE EMPLOYEE_ID = @EmployeeId",
                    new { EmployeeId = (int)user.EMPLOYEE_ID });

                // 建立 Session
                var sessionUser = new SessionUser
                {
                    EmployeeId = user.EMPLOYEE_ID,
                    EmployeeName = user.EMPLOYEE_NAME ?? "",
                    EmployeeNo = user.EMPLOYEE_NO ?? "",
                    DepartmentId = user.DEPARTMENT_ID,
                    LoginName = user.LOGIN_NAME ?? "",
                    EmployeeType = user.EMPLOYEE_TYPE ?? 0,
                    ManagedDepartmentIds = managedDepartments.ToList()
                };

                SessionHelper.SetCurrentUser(HttpContext.Session, sessionUser);

                // 導向紀錄查詢頁面
                return RedirectToPage("/Records/Query");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"系統錯誤：{ex.Message}";
                return Page();
            }
        }
    }
}
