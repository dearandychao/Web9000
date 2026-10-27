using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Web9000.Data;
using Web9000.Utilities;

namespace Web9000.Pages.Records
{
    public class QueryModel : BasePageModel
    {
        private readonly IDbConnectionFactory _dbFactory;

        [BindProperty]
        public int? SelectedDepartmentId { get; set; }

        [BindProperty]
        public string? SelectedEmployeeNo { get; set; }

        [BindProperty]
        public DateTime DateFrom { get; set; } = DateTime.Today;

        [BindProperty]
        public DateTime DateTo { get; set; } = DateTime.Today;

        [BindProperty]
        public int CurrentPage { get; set; } = 1;

        [BindProperty]
        public string SortBy { get; set; } = "DateTime";

        [BindProperty]
        public string SortOrder { get; set; } = "DESC";

        public List<SelectListItem> DepartmentList { get; set; } = new();
        public List<SelectListItem> EmployeeList { get; set; } = new();
        public List<AttendanceRecord> Records { get; set; } = new();
        public int TotalRecords { get; set; }
        public int TotalPages { get; set; }
        public string? ErrorMessage { get; set; }
        public bool HasSearched { get; set; } = false;

        public QueryModel(IDbConnectionFactory dbFactory, IConfiguration configuration)
        {
            _dbFactory = dbFactory;
            Configuration = configuration;
        }

        public async Task OnGetAsync()
        {
            await LoadDepartmentsAsync();

            // 初始化時預設選擇第一個部門
            if (!SelectedDepartmentId.HasValue && DepartmentList.Count > 1)
            {
                var firstDept = DepartmentList.FirstOrDefault(d => !string.IsNullOrEmpty(d.Value));
                if (firstDept != null)
                {
                    SelectedDepartmentId = int.Parse(firstDept.Value);
                }
            }

            await LoadEmployeesAsync();
        }

        public async Task<IActionResult> OnPostQueryAsync()
        {
            HasSearched = true;
            await LoadDepartmentsAsync();
            await LoadEmployeesAsync();

            // 驗證日期區間
            if (DateFrom > DateTo)
            {
                ErrorMessage = "起始日期不可大於結束日期";
                return Page();
            }

            try
            {
                using var conn = _dbFactory.GetGDTekConnection();
                conn.Open();

                var pageSize = GetPageSize();
                var offset = (CurrentPage - 1) * pageSize;

                // 建立查詢條件
                var whereConditions = new List<string> { "1=1" };
                var parameters = new DynamicParameters();

                // 部門條件
                if (SelectedDepartmentId.HasValue)
                {
                    // 從 Web9000 查詢該部門的員工編號
                    using var web9000Conn = _dbFactory.GetWeb9000Connection();
                    web9000Conn.Open();
                    var employeeNos = await web9000Conn.QueryAsync<string>(
                        "SELECT EMPLOYEE_NO FROM [W9K_EMPLOYEE] WHERE DEPARTMENT_ID = @DeptId",
                        new { DeptId = SelectedDepartmentId.Value });

                    if (employeeNos.Any())
                    {
                        whereConditions.Add("dl.EmployeeID IN @EmployeeNos");
                        parameters.Add("EmployeeNos", employeeNos);
                    }
                    else
                    {
                        // 該部門沒有員工，直接返回空結果
                        TotalRecords = 0;
                        TotalPages = 0;
                        Records = new List<AttendanceRecord>();
                        return Page();
                    }
                }

                // 員工條件
                if (!string.IsNullOrEmpty(SelectedEmployeeNo))
                {
                    if (SelectedEmployeeNo != "ALL")
                    {
                        whereConditions.Add("dl.EmployeeID = @EmployeeNo");
                        parameters.Add("EmployeeNo", SelectedEmployeeNo);
                    }
                }

                // 日期條件
                whereConditions.Add("CAST(dl.DateTime AS DATE) BETWEEN @DateFrom AND @DateTo");
                parameters.Add("DateFrom", DateFrom.Date);
                parameters.Add("DateTo", DateTo.Date);

                var whereClause = string.Join(" AND ", whereConditions);

                // 查詢總筆數
                var countSql = $@"
                    SELECT COUNT(*)
                    FROM [{GDTekDbName}].[dbo].[DoorLog] dl
                    WHERE {whereClause}";

                TotalRecords = await conn.ExecuteScalarAsync<int>(countSql, parameters);
                TotalPages = (int)Math.Ceiling(TotalRecords / (double)pageSize);

                // 查詢分頁資料
                var dataSql = $@"
                    SELECT 
                        dl.EmployeeID,
                        p.Name AS EmployeeName,
                        dl.CardNo,
                        dl.DateTime,
                        t.Name AS TerminalName,
                        dl.EventAlarmCode,
                        dl.VerificationSource,
                        CASE dl.FKeyID
                            WHEN 0 THEN '簽到'
                            WHEN 1 THEN '上班'
                            WHEN 101 THEN '下班'
                            WHEN 201 THEN '加班起'
                            WHEN 301 THEN '加班止'
                            ELSE '未知'
                        END AS FKeyDescription
                    FROM [{GDTekDbName}].[dbo].[DoorLog] dl
                    LEFT JOIN [{GDTekDbName}].[dbo].[Person] p ON dl.EmployeeID = p.EmployeeID
                    LEFT JOIN [{GDTekDbName}].[dbo].[Terminal] t ON dl.TerminalID = t.TerminalID
                    WHERE {whereClause}
                    ORDER BY {GetOrderByClause()}
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                parameters.Add("Offset", offset);
                parameters.Add("PageSize", pageSize);

                var results = await conn.QueryAsync<dynamic>(dataSql, parameters);
                Records = results.Select(r => new AttendanceRecord
                {
                    EmployeeId = r.EmployeeID ?? "",
                    EmployeeName = r.EmployeeName ?? "",
                    CardNo = r.CardNo ?? "",
                    DateTime = r.DateTime,
                    TerminalName = r.TerminalName ?? "",
                    EventAlarmCode = r.EventAlarmCode ?? "",
                    VerificationSource = r.VerificationSource ?? "",
                    FKeyDescription = r.FKeyDescription ?? ""
                }).ToList();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"查詢發生錯誤：{ex.Message}";
            }

            return Page();
        }

        public async Task<IActionResult> OnPostExportAsync()
        {
            HasSearched = true;

            // 驗證日期區間
            if (DateFrom > DateTo)
            {
                ErrorMessage = "起始日期不可大於結束日期";
                await LoadDepartmentsAsync();
                await LoadEmployeesAsync();
                return Page();
            }

            try
            {
                using var conn = _dbFactory.GetGDTekConnection();
                conn.Open();

                // 建立查詢條件（與查詢邏輯相同）
                var whereConditions = new List<string> { "1=1" };
                var parameters = new DynamicParameters();

                if (SelectedDepartmentId.HasValue)
                {
                    using var web9000Conn = _dbFactory.GetWeb9000Connection();
                    web9000Conn.Open();
                    var employeeNos = await web9000Conn.QueryAsync<string>(
                        "SELECT EMPLOYEE_NO FROM [W9K_EMPLOYEE] WHERE DEPARTMENT_ID = @DeptId",
                        new { DeptId = SelectedDepartmentId.Value });

                    if (employeeNos.Any())
                    {
                        whereConditions.Add("dl.EmployeeID IN @EmployeeNos");
                        parameters.Add("EmployeeNos", employeeNos);
                    }
                }

                if (!string.IsNullOrEmpty(SelectedEmployeeNo) && SelectedEmployeeNo != "ALL")
                {
                    whereConditions.Add("dl.EmployeeID = @EmployeeNo");
                    parameters.Add("EmployeeNo", SelectedEmployeeNo);
                }

                whereConditions.Add("CAST(dl.DateTime AS DATE) BETWEEN @DateFrom AND @DateTo");
                parameters.Add("DateFrom", DateFrom.Date);
                parameters.Add("DateTo", DateTo.Date);

                var whereClause = string.Join(" AND ", whereConditions);

                var dataSql = $@"
                    SELECT 
                        dl.EmployeeID,
                        p.Name AS EmployeeName,
                        dl.CardNo,
                        dl.DateTime,
                        t.Name AS TerminalName,
                        dl.EventAlarmCode,
                        dl.VerificationSource,
                        CASE dl.FKeyID
                            WHEN 0 THEN '簽到'
                            WHEN 1 THEN '上班'
                            WHEN 101 THEN '下班'
                            WHEN 201 THEN '加班起'
                            WHEN 301 THEN '加班止'
                            ELSE '未知'
                        END AS FKeyDescription
                    FROM [{GDTekDbName}].[dbo].[DoorLog] dl
                    LEFT JOIN [{GDTekDbName}].[dbo].[Person] p ON dl.EmployeeID = p.EmployeeID
                    LEFT JOIN [{GDTekDbName}].[dbo].[Terminal] t ON dl.TerminalID = t.TerminalID
                    WHERE {whereClause}
                    ORDER BY dl.EmployeeID, dl.DateTime";

                var results = (await conn.QueryAsync<dynamic>(dataSql, parameters)).ToList();

                // 準備 Excel 資料
                var headers = new List<string> { "員工編號", "員工姓名", "卡號", "刷卡時間", "指紋機位置", "事件描述", "驗證方式", "班別" };
                var rows = results.Select(r => new List<object>
                {
                    r.EmployeeID ?? "",
                    r.EmployeeName ?? "",
                    r.CardNo ?? "",
                    ((DateTime)r.DateTime).ToString("yyyy/MM/dd HH:mm:ss"),
                    r.TerminalName ?? "",
                    r.EventAlarmCode ?? "",
                    r.VerificationSource ?? "",
                    r.FKeyDescription ?? ""
                }).ToList();

                // 指定工號、卡號為文字格式（防止前導零消失）
                var textColumns = new HashSet<int> { 0, 2 };

                var stream = ExcelExportHelper.CreateSingleSheetExcel(
                    "差勤紀錄",
                    headers,
                    rows,
                    textColumns);

                var fileName = $"差勤紀錄_{DateFrom:yyyyMMdd}_{DateTo:yyyyMMdd}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
                return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"匯出發生錯誤：{ex.Message}";
                await LoadDepartmentsAsync();
                await LoadEmployeesAsync();
                return Page();
            }
        }

        public async Task<JsonResult> OnGetEmployeesAsync(int? departmentId)
        {
            var employees = new List<SelectListItem>();

            try
            {
                using var conn = _dbFactory.GetWeb9000Connection();
                conn.Open();

                string sql;
                object parameters;

                if (IsAdmin)
                {
                    // 管理者：列出所有員工或指定部門員工
                    if (departmentId.HasValue)
                    {
                        sql = @"SELECT EMPLOYEE_NO, EMPLOYEE_NAME 
                                FROM [W9K_EMPLOYEE] 
                                WHERE DEPARTMENT_ID = @DeptId
                                ORDER BY EMPLOYEE_NO";
                        parameters = new { DeptId = departmentId.Value };
                    }
                    else
                    {
                        sql = @"SELECT EMPLOYEE_NO, EMPLOYEE_NAME 
                                FROM [W9K_EMPLOYEE] 
                                ORDER BY EMPLOYEE_NO";
                        parameters = new { };
                    }
                }
                else
                {
                    // 一般使用者：只列出自己或可管理部門的員工
                    if (departmentId.HasValue && CurrentUser != null)
                    {
                        if (SessionHelper.CanManageDepartment(CurrentUser, departmentId.Value))
                        {
                            sql = @"SELECT EMPLOYEE_NO, EMPLOYEE_NAME 
                                    FROM [W9K_EMPLOYEE] 
                                    WHERE DEPARTMENT_ID = @DeptId
                                    ORDER BY EMPLOYEE_NO";
                            parameters = new { DeptId = departmentId.Value };
                        }
                        else
                        {
                            sql = @"SELECT EMPLOYEE_NO, EMPLOYEE_NAME 
                                    FROM [W9K_EMPLOYEE] 
                                    WHERE EMPLOYEE_ID = @EmpId";
                            parameters = new { EmpId = CurrentUser.EmployeeId };
                        }
                    }
                    else
                    {
                        sql = @"SELECT EMPLOYEE_NO, EMPLOYEE_NAME 
                                FROM [W9K_EMPLOYEE] 
                                WHERE EMPLOYEE_ID = @EmpId";
                        parameters = new { EmpId = CurrentUser?.EmployeeId };
                    }
                }

                var results = await conn.QueryAsync<dynamic>(sql, parameters);
                employees = results.Select(e => new SelectListItem
                {
                    Value = e.EMPLOYEE_NO,
                    Text = $"{e.EMPLOYEE_NO} - {e.EMPLOYEE_NAME}"
                }).ToList();

                if (employees.Count > 1)
                {
                    employees.Insert(0, new SelectListItem { Value = "ALL", Text = "全體員工" });
                }
            }
            catch
            {
                // 發生錯誤時返回空清單
            }

            return new JsonResult(employees);
        }

        private async Task LoadDepartmentsAsync()
        {
            try
            {
                using var conn = _dbFactory.GetWeb9000Connection();
                conn.Open();

                IEnumerable<dynamic> departments;

                if (IsAdmin)
                {
                    // 管理者：列出所有部門
                    departments = await conn.QueryAsync<dynamic>(
                        "SELECT DEPARTMENT_ID, DEPARTMENT_NAME FROM [W9K_DEPARTMENT] ORDER BY DEPARTMENT_NAME");
                }
                else if (CurrentUser != null)
                {
                    // 部門管理者或一般使用者：列出可管理部門+自己所屬部門
                    var deptIds = new List<int>();
                    if (CurrentUser.DepartmentId.HasValue)
                        deptIds.Add(CurrentUser.DepartmentId.Value);
                    deptIds.AddRange(CurrentUser.ManagedDepartmentIds);

                    if (deptIds.Any())
                    {
                        departments = await conn.QueryAsync<dynamic>(
                            "SELECT DEPARTMENT_ID, DEPARTMENT_NAME FROM [W9K_DEPARTMENT] WHERE DEPARTMENT_ID IN @DeptIds ORDER BY DEPARTMENT_NAME",
                            new { DeptIds = deptIds.Distinct() });
                    }
                    else
                    {
                        departments = new List<dynamic>();
                    }
                }
                else
                {
                    departments = new List<dynamic>();
                }

                DepartmentList = departments.Select(d => new SelectListItem
                {
                    Value = d.DEPARTMENT_ID.ToString(),
                    Text = d.DEPARTMENT_NAME
                }).ToList();

                // 不加入 "請選擇" 選項，預設為第一個部門
            }
            catch
            {
                DepartmentList = new List<SelectListItem> { new SelectListItem { Value = "", Text = "請選擇" } };
            }
        }

        private async Task LoadEmployeesAsync()
        {
            try
            {
                using var conn = _dbFactory.GetWeb9000Connection();
                conn.Open();

                IEnumerable<dynamic> employees;

                if (IsAdmin)
                {
                    // 管理者：列出 "全體員工" + 所選部門的員工
                    if (SelectedDepartmentId.HasValue)
                    {
                        employees = await conn.QueryAsync<dynamic>(
                            "SELECT EMPLOYEE_NO, EMPLOYEE_NAME FROM [W9K_EMPLOYEE] WHERE DEPARTMENT_ID = @DeptId ORDER BY EMPLOYEE_NO",
                            new { DeptId = SelectedDepartmentId.Value });
                    }
                    else
                    {
                        employees = await conn.QueryAsync<dynamic>(
                            "SELECT EMPLOYEE_NO, EMPLOYEE_NAME FROM [W9K_EMPLOYEE] ORDER BY EMPLOYEE_NO");
                    }
                }
                else if (CurrentUser != null)
                {
                    // 判斷是否為部門管理者
                    bool isDeptManager = CurrentUser.ManagedDepartmentIds.Any();
                    bool canManageSelectedDept = SelectedDepartmentId.HasValue && 
                                                 (CurrentUser.ManagedDepartmentIds.Contains(SelectedDepartmentId.Value) || 
                                                  CurrentUser.DepartmentId == SelectedDepartmentId.Value);

                    if (isDeptManager && canManageSelectedDept)
                    {
                        // 部門管理者：列出 "全體員工" + 所選部門的員工
                        employees = await conn.QueryAsync<dynamic>(
                            "SELECT EMPLOYEE_NO, EMPLOYEE_NAME FROM [W9K_EMPLOYEE] WHERE DEPARTMENT_ID = @DeptId ORDER BY EMPLOYEE_NO",
                            new { DeptId = SelectedDepartmentId.Value });
                    }
                    else
                    {
                        // 一般使用者：只列出自己
                        employees = await conn.QueryAsync<dynamic>(
                            "SELECT EMPLOYEE_NO, EMPLOYEE_NAME FROM [W9K_EMPLOYEE] WHERE EMPLOYEE_ID = @EmpId",
                            new { EmpId = CurrentUser.EmployeeId });
                    }
                }
                else
                {
                    employees = new List<dynamic>();
                }

                EmployeeList = employees.Select(e => new SelectListItem
                {
                    Value = e.EMPLOYEE_NO,
                    Text = $"{e.EMPLOYEE_NO} - {e.EMPLOYEE_NAME}"
                }).ToList();

                // 管理者或部門管理者才加入 "全體員工" 選項
                if (EmployeeList.Count > 1 && (IsAdmin || (CurrentUser != null && CurrentUser.ManagedDepartmentIds.Any())))
                {
                    EmployeeList.Insert(0, new SelectListItem { Value = "ALL", Text = "全體員工" });
                }
            }
            catch
            {
                EmployeeList = new List<SelectListItem> { new SelectListItem { Value = "ALL", Text = "全體員工" } };
            }
        }

        private string GetOrderByClause()
        {
            var orderDirection = SortOrder?.ToUpper() == "ASC" ? "ASC" : "DESC";

            return SortBy switch
            {
                "EmployeeID" => $"dl.EmployeeID {orderDirection}",
                "EmployeeName" => $"p.Name {orderDirection}",
                "DateTime" => $"dl.DateTime {orderDirection}",
                "TerminalName" => $"t.Name {orderDirection}",
                _ => $"dl.DateTime {orderDirection}"
            };
        }
    }

    public class AttendanceRecord
    {
        public string EmployeeId { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string CardNo { get; set; } = string.Empty;
        public DateTime DateTime { get; set; }
        public string TerminalName { get; set; } = string.Empty;
        public string EventAlarmCode { get; set; } = string.Empty;
        public string VerificationSource { get; set; } = string.Empty;
        public string FKeyDescription { get; set; } = string.Empty;
    }
}
