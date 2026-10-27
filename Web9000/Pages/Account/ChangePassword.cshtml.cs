using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Web9000.Data;
using Web9000.Utilities;
using System;
using System.Threading.Tasks;

namespace Web9000.Pages.Account
{
    public class ChangePasswordModel : BasePageModel
    {
        private readonly IDbConnectionFactory _dbFactory;
        private readonly PasswordHasher<object> _passwordHasher;

        public ChangePasswordModel(IDbConnectionFactory dbFactory)
        {
            _dbFactory = dbFactory;
            _passwordHasher = new PasswordHasher<object>();
        }

        [BindProperty]
        public string OldPassword { get; set; } = string.Empty;

        [BindProperty]
        public string NewPassword { get; set; } = string.Empty;

        [BindProperty]
        public string ConfirmPassword { get; set; } = string.Empty;

        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }

        public void OnGet()
        {
            // 頁面載入時不需要特別處理
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // 檢查必填欄位
            if (string.IsNullOrWhiteSpace(OldPassword))
            {
                ErrorMessage = "請輸入原始密碼";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(NewPassword))
            {
                ErrorMessage = "請輸入新密碼";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(ConfirmPassword))
            {
                ErrorMessage = "請輸入確認密碼";
                return Page();
            }

            // 檢查新密碼與確認密碼是否一致
            if (NewPassword != ConfirmPassword)
            {
                ErrorMessage = "新密碼與確認密碼不一致";
                return Page();
            }

            // 檢查新密碼是否與舊密碼相同
            if (OldPassword == NewPassword)
            {
                ErrorMessage = "新密碼不可與原始密碼相同";
                return Page();
            }

            try
            {
                using var conn = _dbFactory.GetWeb9000Connection();
                conn.Open();

                // 取得目前使用者的密碼
                var currentPasswordHash = await conn.QuerySingleOrDefaultAsync<string>(
                    "SELECT LOGIN_PASSWORD FROM [W9K_EMPLOYEE] WHERE EMPLOYEE_ID = @EmployeeId",
                    new { EmployeeId = CurrentUser?.EmployeeId });

                if (string.IsNullOrEmpty(currentPasswordHash))
                {
                    ErrorMessage = "查無使用者資料";
                    return Page();
                }

                // 驗證原始密碼
                var verifyResult = _passwordHasher.VerifyHashedPassword(null, currentPasswordHash, OldPassword);
                if (verifyResult == PasswordVerificationResult.Failed)
                {
                    ErrorMessage = "原始密碼錯誤";
                    return Page();
                }

                // 產生新密碼雜湊
                var newPasswordHash = _passwordHasher.HashPassword(null, NewPassword);

                // 更新密碼
                var updateResult = await conn.ExecuteAsync(
                    @"UPDATE [W9K_EMPLOYEE] 
                      SET LOGIN_PASSWORD = @NewPasswordHash,
                          UPDATE_EMPLOYEE_ID = @UpdateEmployeeId,
                          UPDATE_DATE = @UpdateDate
                      WHERE EMPLOYEE_ID = @EmployeeId",
                    new
                    {
                        NewPasswordHash = newPasswordHash,
                        UpdateEmployeeId = CurrentUser?.EmployeeId,
                        UpdateDate = DateTime.Now,
                        EmployeeId = CurrentUser?.EmployeeId
                    });

                if (updateResult > 0)
                {
                    SuccessMessage = "密碼修改成功";
                    // 清空輸入欄位
                    OldPassword = string.Empty;
                    NewPassword = string.Empty;
                    ConfirmPassword = string.Empty;
                }
                else
                {
                    ErrorMessage = "密碼修改失敗";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"密碼修改發生錯誤：{ex.Message}";
            }

            return Page();
        }
    }
}
