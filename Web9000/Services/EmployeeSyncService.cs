using Dapper;
using Microsoft.AspNetCore.Identity;
using Web9000.Data;
using Web9000.Models;

namespace Web9000.Services
{
    public interface IEmployeeSyncService
    {
        Task<int> SyncEmployeesFromGDTekAsync();
    }

    public class EmployeeSyncService : IEmployeeSyncService
    {
        private readonly IDbConnectionFactory _dbFactory;
        private readonly ILogger<EmployeeSyncService> _logger;

        public EmployeeSyncService(IDbConnectionFactory dbFactory, ILogger<EmployeeSyncService> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        /// <summary>
        /// 從 gdtek 匯入員工，若員工編號不存在則新增（預設密碼為工號），存在則僅更新姓名與卡號
        /// </summary>
        public async Task<int> SyncEmployeesFromGDTekAsync()
        {
            int count = 0;
            var passwordHasher = new PasswordHasher<object>();

            try
            {
                // 步驟 1: 從 gdtek 讀取所有員工
                using (var gdtekConn = _dbFactory.GetGDTekConnection())
                {
                    gdtekConn.Open();
                    var sourceEmployees = (await gdtekConn.QueryAsync<dynamic>(
                        "SELECT EmployeeID, Name, CardNo FROM [Person]")).ToList();

                    if (!sourceEmployees.Any())
                    {
                        _logger.LogWarning("No employees found in gdtek.Person");
                        return 0;
                    }

                    // 步驟 2: 同步到 Web9000
                    using (var web9000Conn = _dbFactory.GetWeb9000Connection())
                    {
                        web9000Conn.Open();

                        foreach (var emp in sourceEmployees)
                        {
                            string employeeNo = emp.EmployeeID?.ToString() ?? "";
                            string employeeName = emp.Name?.ToString() ?? "";
                            string cardNo = emp.CardNo?.ToString() ?? "";

                            // 檢查員工是否已存在
                            var existing = await web9000Conn.QueryFirstOrDefaultAsync<dynamic>(
                                "SELECT EMPLOYEE_ID FROM [W9K_EMPLOYEE] WHERE EMPLOYEE_NO = @EmployeeNo",
                                new { EmployeeNo = employeeNo });

                            if (existing == null)
                            {
                                // 新增員工：登入帳號與密碼都設為工號的雜湊
                                var hashedPassword = passwordHasher.HashPassword(null, employeeNo);

                                await web9000Conn.ExecuteAsync(
                                    @"INSERT INTO [W9K_EMPLOYEE] 
                                      (EMPLOYEE_NAME, EMPLOYEE_NO, LOGIN_NAME, LOGIN_PASSWORD, EMPLOYEE_TYPE, CARD_NO, CREATE_DATE, UPDATE_DATE)
                                      VALUES (@Name, @No, @LoginName, @Password, 0, @CardNo, GETUTCDATE(), GETUTCDATE())",
                                    new
                                    {
                                        Name = employeeName,
                                        No = employeeNo,
                                        LoginName = employeeNo,  // 登入帳號設為工號
                                        Password = hashedPassword,
                                        CardNo = cardNo
                                    });
                                count++;
                            }
                            else
                            {
                                // 更新現有員工：只更新姓名與卡號
                                await web9000Conn.ExecuteAsync(
                                    @"UPDATE [W9K_EMPLOYEE] 
                                      SET EMPLOYEE_NAME = @Name, CARD_NO = @CardNo, UPDATE_DATE = GETUTCDATE()
                                      WHERE EMPLOYEE_NO = @No",
                                    new
                                    {
                                        Name = employeeName,
                                        CardNo = cardNo,
                                        No = employeeNo
                                    });
                            }
                        }
                    }
                }

                _logger.LogInformation($"Employee sync completed. {count} new employees added/updated.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during employee sync from gdtek");
                throw;
            }

            return count;
        }
    }
}
