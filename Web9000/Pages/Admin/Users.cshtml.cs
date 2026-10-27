using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Web9000.Data;
using Web9000.Services;

namespace Web9000.Pages.Admin
{
    public class UsersModel : BasePageModel
    {
        private readonly IDbConnectionFactory _dbFactory;
        private readonly IEmployeeSyncService _employeeSyncService;

        [BindProperty]
        public int? SelectedUserId { get; set; }

        [BindProperty]
        public string SearchKeyword { get; set; } = string.Empty;

        [BindProperty]
        public UserEditDto EditUser { get; set; } = new();

        [BindProperty]
        public List<int> SelectedUserIds { get; set; } = new();

        [BindProperty]
        public int CurrentPage { get; set; } = 1;

        public List<SelectListItem> DepartmentList { get; set; } = new();
        public List<UserListDto> Users { get; set; } = new();
        public int TotalRecords { get; set; }
        public int TotalPages { get; set; }
        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }

        public UsersModel(
            IDbConnectionFactory dbFactory, 
            IConfiguration configuration,
            IEmployeeSyncService employeeSyncService)
        {
            _dbFactory = dbFactory;
            Configuration = configuration;
            _employeeSyncService = employeeSyncService;
        }

        protected override bool RequireAdmin() => true;

        public async Task OnGetAsync()
        {
            await LoadDepartmentsAsync();
            await LoadUsersAsync();
        }

        public async Task<IActionResult> OnPostSearchAsync()
        {
            await LoadDepartmentsAsync();
            await LoadUsersAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostSaveAsync()
        {
            await LoadDepartmentsAsync();

            // 驗證必填欄位
            if (string.IsNullOrWhiteSpace(EditUser.LoginName))
            {
                ErrorMessage = "請輸入登入帳號";
                await LoadUsersAsync();
                return Page();
            }

            // 如果有輸入密碼，檢查密碼一致性
            if (!string.IsNullOrWhiteSpace(EditUser.LoginPassword))
            {
                if (EditUser.LoginPassword != EditUser.ConfirmPassword)
                {
                    ErrorMessage = "密碼與確認密碼不一致";
                    await LoadUsersAsync();
                    return Page();
                }
            }

            try
            {
                using var conn = _dbFactory.GetWeb9000Connection();
                conn.Open();

                if (!SelectedUserId.HasValue)
                {
                    ErrorMessage = "請從列表中選擇要編輯的使用者";
                    await LoadUsersAsync();
                    return Page();
                }

                // 檢查登入帳號是否重複（排除自己）
                var duplicate = await conn.QueryFirstOrDefaultAsync<int?>(
                    @"SELECT EMPLOYEE_ID 
                      FROM [W9K_EMPLOYEE] 
                      WHERE LOGIN_NAME = @LoginName AND EMPLOYEE_ID != @EmployeeId",
                    new { LoginName = EditUser.LoginName, EmployeeId = SelectedUserId.Value });

                if (duplicate.HasValue)
                {
                    ErrorMessage = $"登入帳號 '{EditUser.LoginName}' 已被使用";
                    await LoadUsersAsync();
                    return Page();
                }

                // 準備更新參數
                var updateParams = new DynamicParameters();
                updateParams.Add("EmployeeId", SelectedUserId.Value);
                updateParams.Add("DepartmentId", EditUser.DepartmentId);
                updateParams.Add("LoginName", EditUser.LoginName);
                updateParams.Add("EmployeeType", EditUser.EmployeeType);
                updateParams.Add("UpdateEmployeeId", CurrentUser?.EmployeeId);

                string updateSql;

                // 如果有輸入密碼，則更新密碼
                if (!string.IsNullOrWhiteSpace(EditUser.LoginPassword))
                {
                    var passwordHasher = new PasswordHasher<object>();
                    var hashedPassword = passwordHasher.HashPassword(null, EditUser.LoginPassword);
                    updateParams.Add("LoginPassword", hashedPassword);

                    updateSql = @"
                        UPDATE [W9K_EMPLOYEE]
                        SET DEPARTMENT_ID = @DepartmentId,
                            LOGIN_NAME = @LoginName,
                            LOGIN_PASSWORD = @LoginPassword,
                            EMPLOYEE_TYPE = @EmployeeType,
                            UPDATE_EMPLOYEE_ID = @UpdateEmployeeId,
                            UPDATE_DATE = GETUTCDATE()
                        WHERE EMPLOYEE_ID = @EmployeeId";
                }
                else
                {
                    updateSql = @"
                        UPDATE [W9K_EMPLOYEE]
                        SET DEPARTMENT_ID = @DepartmentId,
                            LOGIN_NAME = @LoginName,
                            EMPLOYEE_TYPE = @EmployeeType,
                            UPDATE_EMPLOYEE_ID = @UpdateEmployeeId,
                            UPDATE_DATE = GETUTCDATE()
                        WHERE EMPLOYEE_ID = @EmployeeId";
                }

                await conn.ExecuteAsync(updateSql, updateParams);

                SuccessMessage = "使用者資料已更新";
                SelectedUserId = null;
                EditUser = new UserEditDto();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"儲存失敗：{ex.Message}";
            }

            await LoadUsersAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync()
        {
            if (SelectedUserIds == null || !SelectedUserIds.Any())
            {
                ErrorMessage = "請勾選要刪除的使用者";
                await LoadDepartmentsAsync();
                await LoadUsersAsync();
                return Page();
            }

            try
            {
                using var conn = _dbFactory.GetWeb9000Connection();
                conn.Open();

                // 刪除使用者（W9K_MANAGER 會因為 CASCADE 自動刪除）
                await conn.ExecuteAsync(
                    "DELETE FROM [W9K_EMPLOYEE] WHERE EMPLOYEE_ID IN @Ids",
                    new { Ids = SelectedUserIds });

                SuccessMessage = $"已刪除 {SelectedUserIds.Count} 位使用者";
                SelectedUserIds = new List<int>();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"刪除失敗：{ex.Message}";
            }

            await LoadDepartmentsAsync();
            await LoadUsersAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostImportAsync()
        {
            try
            {
                var count = await _employeeSyncService.SyncEmployeesFromGDTekAsync();
                SuccessMessage = $"匯入完成，共處理 {count} 位員工";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"匯入失敗：{ex.Message}";
            }

            await LoadDepartmentsAsync();
            await LoadUsersAsync();
            return Page();
        }

        public async Task<JsonResult> OnGetUserAsync(int id)
        {
            try
            {
                using var conn = _dbFactory.GetWeb9000Connection();
                conn.Open();

                var user = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    @"SELECT 
                        EMPLOYEE_ID,
                        EMPLOYEE_NAME,
                        EMPLOYEE_NO,
                        DEPARTMENT_ID,
                        LOGIN_NAME,
                        EMPLOYEE_TYPE
                      FROM [W9K_EMPLOYEE]
                      WHERE EMPLOYEE_ID = @Id",
                    new { Id = id });

                if (user == null)
                    return new JsonResult(new { success = false, message = "找不到使用者" });

                return new JsonResult(new
                {
                    success = true,
                    data = new
                    {
                        employeeId = (int)user.EMPLOYEE_ID,
                        employeeName = user.EMPLOYEE_NAME ?? "",
                        employeeNo = user.EMPLOYEE_NO ?? "",
                        departmentId = user.DEPARTMENT_ID,
                        loginName = user.LOGIN_NAME ?? "",
                        employeeType = user.EMPLOYEE_TYPE ?? 0
                    }
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public async Task<JsonResult> OnGetManagedDepartmentsAsync(int employeeId)
        {
            try
            {
                using var conn = _dbFactory.GetWeb9000Connection();
                conn.Open();

                var departmentIds = await conn.QueryAsync<int>(
                    "SELECT DEPARTMENT_ID FROM [W9K_MANAGER] WHERE EMPLOYEE_ID = @EmployeeId",
                    new { EmployeeId = employeeId });

                return new JsonResult(new { success = true, data = departmentIds.ToList() });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public async Task<JsonResult> OnPostSaveManagersAsync(int employeeId, List<int> departmentIds)
        {
            try
            {
                using var conn = _dbFactory.GetWeb9000Connection();
                conn.Open();

                using var transaction = conn.BeginTransaction();

                // 刪除舊的管理部門設定
                await conn.ExecuteAsync(
                    "DELETE FROM [W9K_MANAGER] WHERE EMPLOYEE_ID = @EmployeeId",
                    new { EmployeeId = employeeId },
                    transaction);

                // 新增新的管理部門設定
                if (departmentIds != null && departmentIds.Any())
                {
                    foreach (var deptId in departmentIds)
                    {
                        await conn.ExecuteAsync(
                            @"INSERT INTO [W9K_MANAGER] 
                              (EMPLOYEE_ID, DEPARTMENT_ID, CREATE_EMPLOYEE_ID, CREATE_DATE, UPDATE_DATE)
                              VALUES (@EmployeeId, @DeptId, @CreateEmpId, GETUTCDATE(), GETUTCDATE())",
                            new 
                            { 
                                EmployeeId = employeeId, 
                                DeptId = deptId, 
                                CreateEmpId = CurrentUser?.EmployeeId 
                            },
                            transaction);
                    }
                }

                transaction.Commit();

                return new JsonResult(new { success = true, message = "管理部門已更新" });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        private async Task LoadDepartmentsAsync()
        {
            try
            {
                using var conn = _dbFactory.GetWeb9000Connection();
                conn.Open();

                var departments = await conn.QueryAsync<dynamic>(
                    "SELECT DEPARTMENT_ID, DEPARTMENT_NAME FROM [W9K_DEPARTMENT] ORDER BY DEPARTMENT_NAME");

                DepartmentList = departments.Select(d => new SelectListItem
                {
                    Value = d.DEPARTMENT_ID.ToString(),
                    Text = d.DEPARTMENT_NAME
                }).ToList();

                DepartmentList.Insert(0, new SelectListItem { Value = "", Text = "請選擇" });
            }
            catch
            {
                DepartmentList = new List<SelectListItem> { new SelectListItem { Value = "", Text = "請選擇" } };
            }
        }

        private async Task LoadUsersAsync()
        {
            try
            {
                using var conn = _dbFactory.GetWeb9000Connection();
                conn.Open();

                var pageSize = GetPageSize();
                var offset = (CurrentPage - 1) * pageSize;

                // 建立查詢條件：排除 admin 帳號
                var whereClause = "e.EMPLOYEE_NO != 'admin'";
                var parameters = new DynamicParameters();

                if (!string.IsNullOrWhiteSpace(SearchKeyword))
                {
                    whereClause += " AND (e.EMPLOYEE_NO LIKE @Keyword OR e.EMPLOYEE_NAME LIKE @Keyword)";
                    parameters.Add("Keyword", $"%{SearchKeyword}%");
                }

                // 查詢總筆數
                var countSql = $@"
                    SELECT COUNT(*)
                    FROM [W9K_EMPLOYEE] e
                    WHERE {whereClause}";

                TotalRecords = await conn.ExecuteScalarAsync<int>(countSql, parameters);
                TotalPages = (int)Math.Ceiling(TotalRecords / (double)pageSize);

                // 查詢分頁資料
                var dataSql = $@"
                    SELECT 
                        e.EMPLOYEE_ID,
                        e.EMPLOYEE_NAME,
                        e.EMPLOYEE_NO,
                        e.DEPARTMENT_ID,
                        d.DEPARTMENT_NAME,
                        e.LOGIN_NAME,
                        e.EMPLOYEE_TYPE,
                        e.CARD_NO
                    FROM [W9K_EMPLOYEE] e
                    LEFT JOIN [W9K_DEPARTMENT] d ON e.DEPARTMENT_ID = d.DEPARTMENT_ID
                    WHERE {whereClause}
                    ORDER BY e.EMPLOYEE_NO
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                parameters.Add("Offset", offset);
                parameters.Add("PageSize", pageSize);

                var results = await conn.QueryAsync<dynamic>(dataSql, parameters);
                Users = results.Select(r => new UserListDto
                {
                    EmployeeId = r.EMPLOYEE_ID,
                    EmployeeName = r.EMPLOYEE_NAME ?? "",
                    EmployeeNo = r.EMPLOYEE_NO ?? "",
                    DepartmentId = r.DEPARTMENT_ID,
                    DepartmentName = r.DEPARTMENT_NAME ?? "",
                    LoginName = r.LOGIN_NAME ?? "",
                    EmployeeType = r.EMPLOYEE_TYPE ?? 0,
                    CardNo = r.CARD_NO ?? ""
                }).ToList();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"載入使用者清單失敗：{ex.Message}";
                Users = new List<UserListDto>();
            }
        }
    }

    public class UserListDto
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeNo { get; set; } = string.Empty;
        public int? DepartmentId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public string LoginName { get; set; } = string.Empty;
        public int EmployeeType { get; set; }
        public string CardNo { get; set; } = string.Empty;
    }

    public class UserEditDto
    {
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeNo { get; set; } = string.Empty;
        public int? DepartmentId { get; set; }
        public string LoginName { get; set; } = string.Empty;
        public string LoginPassword { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
        public int EmployeeType { get; set; }
    }
}
