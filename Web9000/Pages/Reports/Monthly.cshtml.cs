using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Web9000.Data;
using Web9000.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Web9000.Pages.Reports
{
    public class MonthlyModel : BasePageModel
    {
        private readonly IDbConnectionFactory _dbFactory;

        public MonthlyModel(IDbConnectionFactory dbFactory)
        {
            _dbFactory = dbFactory;
        }

        /// <summary>
        /// 要求管理者或部門管理者權限
        /// </summary>
        protected override bool RequireAdminOrDepartmentManager() => true;

        [BindProperty]
        public int ReportYear { get; set; } = DateTime.Now.Year;

        [BindProperty]
        public int ReportMonth { get; set; } = DateTime.Now.Month;

        public List<SelectListItem> YearList { get; set; } = new();
        public List<SelectListItem> MonthList { get; set; } = new();
        public List<SelectListItem> DepartmentList { get; set; } = new();
        public List<SelectListItem> EmployeeList { get; set; } = new();

        [BindProperty]
        public int? SelectedDepartmentId { get; set; }

        [BindProperty]
        public string? SelectedEmployeeNo { get; set; }

        public string? ErrorMessage { get; set; }

        public async Task OnGetAsync()
        {
            LoadYearMonthLists();
            await LoadDepartmentsAsync();

            // 初始化時預設選擇第一個部門
            if (!SelectedDepartmentId.HasValue && DepartmentList.Count > 0)
            {
                var firstDept = DepartmentList.FirstOrDefault(d => !string.IsNullOrEmpty(d.Value));
                if (firstDept != null)
                {
                    SelectedDepartmentId = int.Parse(firstDept.Value);
                }
            }

            await LoadEmployeesAsync();
        }

        public async Task<IActionResult> OnPostExportAsync()
        {
            LoadYearMonthLists();

            try
            {
                using var conn = _dbFactory.GetGDTekConnection();
                conn.Open();

                var startDate = new DateTime(ReportYear, ReportMonth, 1);
                var endDate = startDate.AddMonths(1).AddDays(-1);

                // 建立查詢條件
                var whereConditions = new List<string> 
                { 
                    "dl.EventAlarmCode = N'正常'",
                    "dl.DateTime >= @StartDate",
                    "dl.DateTime < @EndDate"
                };
                var parameters = new DynamicParameters();
                parameters.Add("StartDate", startDate);
                parameters.Add("EndDate", endDate.AddDays(1));

                // 取得要匯出的員工清單
                var employeeNos = new List<string>();

                if (!string.IsNullOrEmpty(SelectedEmployeeNo) && SelectedEmployeeNo != "ALL")
                {
                    employeeNos.Add(SelectedEmployeeNo);
                }
                else if (SelectedDepartmentId.HasValue)
                {
                    using var web9000Conn = _dbFactory.GetWeb9000Connection();
                    web9000Conn.Open();
                    var deptEmployees = await web9000Conn.QueryAsync<string>(
                        "SELECT EMPLOYEE_NO FROM [W9K_EMPLOYEE] WHERE DEPARTMENT_ID = @DeptId",
                        new { DeptId = SelectedDepartmentId.Value });
                    employeeNos = deptEmployees.ToList();
                }

                if (!employeeNos.Any())
                {
                    ErrorMessage = "無可匯出的員工資料";
                    await LoadDepartmentsAsync();
                    await LoadEmployeesAsync();
                    return Page();
                }

                whereConditions.Add("dl.EmployeeID IN @EmployeeIds");
                parameters.Add("EmployeeIds", employeeNos);

                var whereClause = string.Join(" AND ", whereConditions);

                // 查詢資料
                var dataSql = $@"
                    SELECT 
                        dl.EmployeeID,
                        p.Name AS EmployeeName,
                        dl.DateTime
                    FROM [{GDTekDbName}].[dbo].[DoorLog] dl
                    LEFT JOIN [{GDTekDbName}].[dbo].[Person] p ON dl.EmployeeID = p.EmployeeID
                    WHERE {whereClause}
                    ORDER BY dl.EmployeeID, dl.DateTime";

                var records = (await conn.QueryAsync<dynamic>(dataSql, parameters)).ToList();

                if (!records.Any())
                {
                    ErrorMessage = "查詢月份無差勤資料";
                    await LoadDepartmentsAsync();
                    await LoadEmployeesAsync();
                    return Page();
                }

                // 取得員工部門資料
                using var web9000ConnForDept = _dbFactory.GetWeb9000Connection();
                web9000ConnForDept.Open();
                var employeeInfo = (await web9000ConnForDept.QueryAsync<dynamic>(
                    @"SELECT e.EMPLOYEE_NO, e.EMPLOYEE_NAME, d.DEPARTMENT_NAME 
                      FROM [W9K_EMPLOYEE] e 
                      LEFT JOIN [W9K_DEPARTMENT] d ON e.DEPARTMENT_ID = d.DEPARTMENT_ID
                      WHERE e.EMPLOYEE_NO IN @EmployeeIds",
                    new { EmployeeIds = employeeNos })).ToDictionary(x => (string)x.EMPLOYEE_NO);

                // 群組資料（依員工）
                var employeeGroups = records.GroupBy(r => (string)r.EmployeeID);

                var sheets = new List<ExcelSheetData>();

                foreach (var empGroup in employeeGroups.OrderBy(g => g.Key))
                {
                    var empNo = empGroup.Key;
                    var empName = empGroup.FirstOrDefault()?.EmployeeName ?? empNo;
                    var deptName = employeeInfo.ContainsKey(empNo) ? (employeeInfo[empNo].DEPARTMENT_NAME ?? "") : "";

                    // 建立該員工整個月的打卡資料字典
                    var attendanceByDate = empGroup
                        .GroupBy(r => ((DateTime)r.DateTime).Date)
                        .ToDictionary(
                            g => g.Key,
                            g => g.OrderBy(r => (DateTime)r.DateTime).Select(r => (DateTime)r.DateTime).ToList()
                        );

                    var headers = new List<string> 
                    { 
                        "日期", "上班", "簽到進-2", "簽到出-2", "下班", "上班時數", "時數審核", "備註" 
                    };

                    var rows = new List<List<object>>();

                    // 產生整個月的所有日期
                    var daysInMonth = DateTime.DaysInMonth(ReportYear, ReportMonth);
                    for (int day = 1; day <= daysInMonth; day++)
                    {
                        var date = new DateTime(ReportYear, ReportMonth, day);

                        // 取得該日期的打卡資料
                        var timeList = attendanceByDate.ContainsKey(date) ? attendanceByDate[date] : new List<DateTime>();
                        var count = timeList.Count;

                        DateTime? signIn = count >= 1 ? timeList[0] : null;
                        DateTime? signIn2 = count > 3 ? timeList[count - 3] : null;
                        DateTime? signOut2 = count > 2 ? timeList[count - 2] : null;
                        DateTime? signOut = count > 1 ? timeList[count - 1] : null;

                        // 計算上班時數
                        string workHours = "";
                        if (signIn.HasValue && signOut.HasValue)
                        {
                            var duration = signOut.Value - signIn.Value;
                            workHours = $"{(int)duration.TotalHours}:{duration.Minutes:D2}";
                        }

                        rows.Add(new List<object>
                        {
                            date.ToString("yyyy-MM-dd"),
                            signIn?.ToString("HH:mm") ?? "",
                            signIn2?.ToString("HH:mm") ?? "",
                            signOut2?.ToString("HH:mm") ?? "",
                            signOut?.ToString("HH:mm") ?? "",
                            workHours,
                            "",
                            ""
                        });
                    }

                    // 加入報表標題列（B1的列印日期改到F1，B2的姓名改到C2，C2的部門改到F2）
                    var reportHeaders = new List<List<object>>
                    {
                        new List<object> { $"報表年月：{ReportYear}/{ReportMonth:D2}", "", "", "", "", $"列印日期：{DateTime.Now:yyyy-MM-dd}", "", "" },
                        new List<object> { $"編號：{empNo}", "", $"姓名：{empName}", "", "", $"部門：{deptName}", "", "" }
                    };

                    sheets.Add(new ExcelSheetData
                    {
                        SheetName = $"{empNo}_{empName}",
                        Headers = headers,
                        Rows = rows,
                        TextColumns = new HashSet<int>(), // 日期已是字串格式，不需特殊處理
                        ReportHeaders = reportHeaders
                    });
                }

                var stream = ExcelExportHelper.CreateMultiSheetExcel(sheets);

                var fileName = $"月報表_{ReportYear}{ReportMonth:D2}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
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

        private void LoadYearMonthLists()
        {
            // 年份：目前年度前後各 2 年
            var currentYear = DateTime.Now.Year;
            YearList = Enumerable.Range(currentYear - 2, 5)
                .Select(y => new SelectListItem { Value = y.ToString(), Text = y.ToString() })
                .ToList();

            // 月份：1-12 月
            MonthList = Enumerable.Range(1, 12)
                .Select(m => new SelectListItem { Value = m.ToString(), Text = $"{m:D2}月" })
                .ToList();
        }

        private async Task LoadDepartmentsAsync()
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
            else if (CurrentUser != null && CurrentUser.ManagedDepartmentIds.Any())
            {
                // 部門管理者：只列出可管理部門（不包含自己所屬部門）
                var deptIds = CurrentUser.ManagedDepartmentIds.ToList();

                departments = await conn.QueryAsync<dynamic>(
                    "SELECT DEPARTMENT_ID, DEPARTMENT_NAME FROM [W9K_DEPARTMENT] WHERE DEPARTMENT_ID IN @DeptIds ORDER BY DEPARTMENT_NAME",
                    new { DeptIds = deptIds });
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
        }

        private async Task LoadEmployeesAsync()
        {
            if (!SelectedDepartmentId.HasValue)
            {
                EmployeeList = new List<SelectListItem>();
                return;
            }

            using var conn = _dbFactory.GetWeb9000Connection();
            conn.Open();

            IEnumerable<dynamic> employees;

            if (IsAdmin)
            {
                // 管理者：列出 "全體員工" + 所選部門的員工
                employees = await conn.QueryAsync<dynamic>(
                    "SELECT EMPLOYEE_NO, EMPLOYEE_NAME FROM [W9K_EMPLOYEE] WHERE DEPARTMENT_ID = @DeptId ORDER BY EMPLOYEE_NO",
                    new { DeptId = SelectedDepartmentId.Value });
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
    }
}
