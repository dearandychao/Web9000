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
    public class DailyModel : BasePageModel
    {
        private readonly IDbConnectionFactory _dbFactory;

        public DailyModel(IDbConnectionFactory dbFactory)
        {
            _dbFactory = dbFactory;
        }

        /// <summary>
        /// 要求管理者或部門管理者權限
        /// </summary>
        protected override bool RequireAdminOrDepartmentManager() => true;

        [BindProperty]
        public DateTime ReportDate { get; set; } = DateTime.Today;

        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            // 頁面載入時不需要特別處理
        }

        public async Task<IActionResult> OnPostExportAsync()
        {
            try
            {
                // 先從 Web9000 取得要匯出的員工清單
                using var web9000Conn = _dbFactory.GetWeb9000Connection();
                web9000Conn.Open();

                IEnumerable<dynamic> employees;

                if (IsAdmin)
                {
                    // 系統管理者：取得全體員工
                    employees = await web9000Conn.QueryAsync<dynamic>(
                        @"SELECT e.EMPLOYEE_NO, e.EMPLOYEE_NAME, d.DEPARTMENT_NAME
                          FROM [W9K_EMPLOYEE] e
                          LEFT JOIN [W9K_DEPARTMENT] d ON e.DEPARTMENT_ID = d.DEPARTMENT_ID
                          WHERE e.EMPLOYEE_NO != 'admin'
                          ORDER BY e.EMPLOYEE_NO");
                }
                else
                {
                    // 部門管理者：取得可管理部門的員工
                    var managedDeptIds = CurrentUser?.ManagedDepartmentIds ?? new List<int>();
                    if (!managedDeptIds.Any())
                    {
                        ErrorMessage = "您沒有可管理的部門";
                        return Page();
                    }

                    employees = await web9000Conn.QueryAsync<dynamic>(
                        @"SELECT e.EMPLOYEE_NO, e.EMPLOYEE_NAME, d.DEPARTMENT_NAME
                          FROM [W9K_EMPLOYEE] e
                          LEFT JOIN [W9K_DEPARTMENT] d ON e.DEPARTMENT_ID = d.DEPARTMENT_ID
                          WHERE e.DEPARTMENT_ID IN @DeptIds AND e.EMPLOYEE_NO != 'admin'
                          ORDER BY e.EMPLOYEE_NO",
                        new { DeptIds = managedDeptIds });
                }

                var employeeList = employees.ToList();
                if (!employeeList.Any())
                {
                    ErrorMessage = "無員工資料";
                    return Page();
                }

                // 查詢當日打卡資料
                using var gdtekConn = _dbFactory.GetGDTekConnection();
                gdtekConn.Open();

                var employeeNos = employeeList.Select(e => (string)e.EMPLOYEE_NO).ToList();

                var dataSql = $@"
                    SELECT 
                        dl.EmployeeID,
                        dl.DateTime
                    FROM [{GDTekDbName}].[dbo].[DoorLog] dl
                    WHERE dl.EventAlarmCode = N'正常'
                      AND CONVERT(DATE, dl.DateTime) = @ReportDate
                      AND dl.EmployeeID IN @EmployeeIds
                    ORDER BY dl.EmployeeID, dl.DateTime";

                var parameters = new DynamicParameters();
                parameters.Add("ReportDate", ReportDate.Date);
                parameters.Add("EmployeeIds", employeeNos);

                var records = (await gdtekConn.QueryAsync<dynamic>(dataSql, parameters)).ToList();

                // 將打卡資料群組
                var attendanceDict = records
                    .GroupBy(r => (string)r.EmployeeID)
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderBy(r => (DateTime)r.DateTime).Select(r => (DateTime)r.DateTime).ToList()
                    );

                var headers = new List<string> 
                { 
                    "工號", "部門", "姓名", "簽到進-1", "簽到進-2", "簽到出-2", "簽到出", "上班時數", "備註" 
                };

                var rows = new List<List<object>>();

                // 遍歷所有員工，無論是否有打卡資料
                foreach (var emp in employeeList)
                {
                    var empNo = (string)emp.EMPLOYEE_NO;
                    var empName = (string)emp.EMPLOYEE_NAME;
                    var deptName = emp.DEPARTMENT_NAME != null ? (string)emp.DEPARTMENT_NAME : "";

                    // 取得該員工的打卡資料
                    var timeList = attendanceDict.ContainsKey(empNo) ? attendanceDict[empNo] : new List<DateTime>();
                    var count = timeList.Count;

                    DateTime? signIn1 = count >= 1 ? timeList[0] : null;
                    DateTime? signIn2 = count > 3 ? timeList[count - 3] : null;
                    DateTime? signOut2 = count > 2 ? timeList[count - 2] : null;
                    DateTime? signOut = count > 1 ? timeList[count - 1] : null;

                    // 計算上班時數
                    string workHours = "";
                    if (signIn1.HasValue && signOut.HasValue)
                    {
                        var duration = signOut.Value - signIn1.Value;
                        workHours = $"{(int)duration.TotalHours}:{duration.Minutes:D2}";
                    }

                    rows.Add(new List<object>
                    {
                        empNo,
                        deptName,
                        empName,
                        signIn1?.ToString("HH:mm") ?? "",
                        signIn2?.ToString("HH:mm") ?? "",
                        signOut2?.ToString("HH:mm") ?? "",
                        signOut?.ToString("HH:mm") ?? "",
                        workHours,
                        ""
                    });
                }

                // 加入報表標題列（B2的匯出人員改到G2）
                var reportHeaders = new List<List<object>>
                {
                    new List<object> { "日報表", "", "", "", "", "", "", "", "" },
                    new List<object> { $"日期：{ReportDate:yyyy-MM-dd}", "", "", "", "", "", $"匯出人員：{CurrentUser?.EmployeeName}", "", "" }
                };

                // 指定工號為文字格式
                var textColumns = new HashSet<int> { 0 };

                var stream = ExcelExportHelper.CreateSingleSheetExcel(
                    "日報表",
                    headers,
                    rows,
                    textColumns,
                    reportHeaders);

                var fileName = $"日報表_{ReportDate:yyyyMMdd}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
                return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"匯出發生錯誤：{ex.Message}";
                return Page();
            }
        }
    }
}
