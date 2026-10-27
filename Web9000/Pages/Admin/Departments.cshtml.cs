using Dapper;
using Microsoft.AspNetCore.Mvc;
using Web9000.Data;

namespace Web9000.Pages.Admin
{
    public class DepartmentsModel : BasePageModel
    {
        private readonly IDbConnectionFactory _dbFactory;

        [BindProperty]
        public int? SelectedDepartmentId { get; set; }

        [BindProperty]
        public string DepartmentName { get; set; } = string.Empty;

        [BindProperty]
        public List<int> SelectedDepartmentIds { get; set; } = new();

        [BindProperty]
        public int CurrentPage { get; set; } = 1;

        public List<DepartmentDto> Departments { get; set; } = new();
        public int TotalRecords { get; set; }
        public int TotalPages { get; set; }
        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }

        public DepartmentsModel(IDbConnectionFactory dbFactory, IConfiguration configuration)
        {
            _dbFactory = dbFactory;
            Configuration = configuration;
        }

        protected override bool RequireAdmin() => true;

        public async Task OnGetAsync()
        {
            await LoadDepartmentsAsync();
        }

        public async Task<IActionResult> OnPostSaveAsync()
        {
            // 驗證必填欄位
            if (string.IsNullOrWhiteSpace(DepartmentName))
            {
                ErrorMessage = "請輸入部門名稱";
                await LoadDepartmentsAsync();
                return Page();
            }

            try
            {
                using var conn = _dbFactory.GetWeb9000Connection();
                conn.Open();

                // 檢查部門名稱是否重複（排除自己）
                string checkSql;
                object checkParams;

                if (SelectedDepartmentId.HasValue)
                {
                    checkSql = @"SELECT DEPARTMENT_ID 
                                 FROM [W9K_DEPARTMENT] 
                                 WHERE DEPARTMENT_NAME = @Name AND DEPARTMENT_ID != @Id";
                    checkParams = new { Name = DepartmentName, Id = SelectedDepartmentId.Value };
                }
                else
                {
                    checkSql = @"SELECT DEPARTMENT_ID 
                                 FROM [W9K_DEPARTMENT] 
                                 WHERE DEPARTMENT_NAME = @Name";
                    checkParams = new { Name = DepartmentName };
                }

                var duplicate = await conn.QueryFirstOrDefaultAsync<int?>(checkSql, checkParams);

                if (duplicate.HasValue)
                {
                    ErrorMessage = $"部門名稱 '{DepartmentName}' 已存在";
                    await LoadDepartmentsAsync();
                    return Page();
                }

                // 新增或更新
                if (SelectedDepartmentId.HasValue)
                {
                    // 更新
                    await conn.ExecuteAsync(
                        @"UPDATE [W9K_DEPARTMENT]
                          SET DEPARTMENT_NAME = @Name,
                              UPDATE_EMPLOYEE_ID = @UpdateEmpId,
                              UPDATE_DATE = GETUTCDATE()
                          WHERE DEPARTMENT_ID = @Id",
                        new 
                        { 
                            Name = DepartmentName, 
                            Id = SelectedDepartmentId.Value,
                            UpdateEmpId = CurrentUser?.EmployeeId
                        });

                    SuccessMessage = "部門已更新";
                }
                else
                {
                    // 新增
                    await conn.ExecuteAsync(
                        @"INSERT INTO [W9K_DEPARTMENT]
                          (DEPARTMENT_NAME, CREATE_EMPLOYEE_ID, CREATE_DATE, UPDATE_DATE)
                          VALUES (@Name, @CreateEmpId, GETUTCDATE(), GETUTCDATE())",
                        new 
                        { 
                            Name = DepartmentName,
                            CreateEmpId = CurrentUser?.EmployeeId
                        });

                    SuccessMessage = "部門已新增";
                }

                // 清空表單
                SelectedDepartmentId = null;
                DepartmentName = string.Empty;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"儲存失敗：{ex.Message}";
            }

            await LoadDepartmentsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync()
        {
            if (SelectedDepartmentIds == null || !SelectedDepartmentIds.Any())
            {
                ErrorMessage = "請勾選要刪除的部門";
                await LoadDepartmentsAsync();
                return Page();
            }

            try
            {
                using var conn = _dbFactory.GetWeb9000Connection();
                conn.Open();

                // 檢查是否有員工使用這些部門
                var employeeCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM [W9K_EMPLOYEE] WHERE DEPARTMENT_ID IN @Ids",
                    new { Ids = SelectedDepartmentIds });

                if (employeeCount > 0)
                {
                    ErrorMessage = $"無法刪除：有 {employeeCount} 位員工隸屬於選取的部門";
                    await LoadDepartmentsAsync();
                    return Page();
                }

                // 刪除部門（W9K_MANAGER 的關聯會自動處理）
                await conn.ExecuteAsync(
                    "DELETE FROM [W9K_DEPARTMENT] WHERE DEPARTMENT_ID IN @Ids",
                    new { Ids = SelectedDepartmentIds });

                SuccessMessage = $"已刪除 {SelectedDepartmentIds.Count} 個部門";
                SelectedDepartmentIds = new List<int>();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"刪除失敗：{ex.Message}";
            }

            await LoadDepartmentsAsync();
            return Page();
        }

        public async Task<JsonResult> OnGetDepartmentAsync(int id)
        {
            try
            {
                using var conn = _dbFactory.GetWeb9000Connection();
                conn.Open();

                var department = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    @"SELECT DEPARTMENT_ID, DEPARTMENT_NAME
                      FROM [W9K_DEPARTMENT]
                      WHERE DEPARTMENT_ID = @Id",
                    new { Id = id });

                if (department == null)
                    return new JsonResult(new { success = false, message = "找不到部門" });

                return new JsonResult(new
                {
                    success = true,
                    data = new
                    {
                        departmentId = (int)department.DEPARTMENT_ID,
                        departmentName = department.DEPARTMENT_NAME ?? ""
                    }
                });
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

                var pageSize = GetPageSize();
                var offset = (CurrentPage - 1) * pageSize;

                // 查詢總筆數
                TotalRecords = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM [W9K_DEPARTMENT]");

                TotalPages = (int)Math.Ceiling(TotalRecords / (double)pageSize);

                // 查詢分頁資料（包含使用此部門的員工數）
                var dataSql = @"
                    SELECT 
                        d.DEPARTMENT_ID,
                        d.DEPARTMENT_NAME,
                        COUNT(e.EMPLOYEE_ID) AS EmployeeCount
                    FROM [W9K_DEPARTMENT] d
                    LEFT JOIN [W9K_EMPLOYEE] e ON d.DEPARTMENT_ID = e.DEPARTMENT_ID
                    GROUP BY d.DEPARTMENT_ID, d.DEPARTMENT_NAME
                    ORDER BY d.DEPARTMENT_NAME
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                var results = await conn.QueryAsync<dynamic>(dataSql, new { Offset = offset, PageSize = pageSize });
                Departments = results.Select(r => new DepartmentDto
                {
                    DepartmentId = r.DEPARTMENT_ID,
                    DepartmentName = r.DEPARTMENT_NAME ?? "",
                    EmployeeCount = r.EmployeeCount ?? 0
                }).ToList();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"載入部門清單失敗：{ex.Message}";
                Departments = new List<DepartmentDto>();
            }
        }
    }

    public class DepartmentDto
    {
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public int EmployeeCount { get; set; }
    }
}
