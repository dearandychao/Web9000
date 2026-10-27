## :bust_in_silhouette:  (2026/10/14 下午 08:19)
我們要將一個舊版的差勤查詢網站（Classic ASP + VB6 + Access）重構為現代化的網站。

請仔細閱讀 Web9000.md，並在後續的所有開發對話中，**嚴格遵守以下技術與架構限制（絕對不可違反）**：

### 1. 核心技術限制 (Hard Constraints)
* **專案框架**：ASP.NET Core 8.0 Razor Pages (C#)。
* **資料存取層**：嚴格使用 `Dapper` + `Microsoft.Data.SqlClient` 撰寫原生參數化 T-SQL。**絕對禁止使用 Entity Framework Core (EF Core)**。
* **Excel 報表套件**：統一使用 **`ClosedXML`** 來產生 `.xlsx` 報表（需支援單一檔案包含多個工作表 Multiple Sheets）。**絕對禁止使用 `Microsoft.Office.Interop.Excel` 等任何需要伺服器安裝 Office 的 COM 元件**，也不要引入 EPPlus。
* **前端介面**：使用專案內建的 Bootstrap 5 與原生 JavaScript，保持畫面乾淨直覺，不需引入 Vue/React。

### 2. 資料庫與連線架構
系統面對同一個 SQL Server 執行個體 (`localhost\SQLEXPRESS`) 下的兩個資料庫：
* **指紋機資料庫 (`gdtek`)**：**絕對唯讀**！這是外部指紋機系統的資料庫，我們只能進行 `SELECT` 查詢（或在跨庫查詢時作為來源），絕對不可對其進行任何寫入、修改或結構變更。
* **系統主資料庫 (`Web9000`)**：存放系統自身的使用者帳號、權限設定、以及差勤結算後的實體資料表。所有的 `INSERT/UPDATE/DELETE` 皆在此庫進行。
* **跨庫查詢與型別處理**：請善用 T-SQL 的跨資料庫語法（如 `FROM Web9000.dbo.Users u JOIN gdtek.dbo.Logs g`），並特別注意兩邊欄位的資料型別轉換 (Data Type Casting) 與字串前導零的防呆。

### 3. 使用者初始化與密碼安全規範
* **拋棄舊加密，改用內建標準**：不使用舊系統的 MD5 與 `CHAR` 型別。使用者資料表的密碼欄位請開立為 `VARCHAR(100)`，並統一使用 ASP.NET Core 內建的 `PasswordHasher<object>` (PBKDF2) 進行密碼雜湊與驗證（不需引入額外加密套件）。
* **從 `gdtek` 匯入使用者**：系統需提供初始化或同步功能，從 `gdtek` 讀取員工基本資料（工號、姓名等），在 C# 端將「工號 (EmpId)」透過 `PasswordHasher` 雜湊後作為預設密碼，批次寫入 `Web9000` 的使用者表中（請注意處理重複匯入時的過濾或更新機制）。

### 4. 報表與大量資料結算規範
* **Excel (.xlsx) 匯出**：利用 `ClosedXML` 的 `XLWorkbook` 在記憶體 (`MemoryStream`) 中動態建立報表並回傳下載，不在伺服器硬碟留下暫存檔。針對工號或卡號等有前導零的字串（如 `00123`），寫入儲存格時必須明確指定為文字格式（`XLDataType.Text`），避免被 Excel 自動轉為純數字。
* **實體資料整併**：若規格書中涉及差勤結算或大筆資料彙整，除了一般查詢外，請規劃將計算結果直接寫入或更新為 `Web9000` 內的實體彙整紀錄（而非每次都用龐大的動態視圖重算）。

---

### 🎯 第一階段任務（請在這次回覆中完成以下 4 件事即可，先不要寫各別頁面的程式碼）：

1. **確認理解與盤點**：簡短列出你從規格書中梳理出的「功能頁面清單（Razor Pages 路由規劃）」。
2. **資料庫建置腳本 (DDL)**：根據規格書，幫我產出 `Web9000` 所需的 `CREATE TABLE` T-SQL 腳本（含適當的 Index 規劃，密碼欄位請用 `VARCHAR(100)`）。【若你有舊版 gdtek 的資料表結構，可在這裡貼給它：附註 gdtek 的資料表結構為：...】
3. **基礎建設與員工同步服務**：請提供 `appsettings.json`、`IDbConnectionFactory`、`Program.cs` 註冊程式碼，以及一個負責「從 `gdtek` 讀取員工、將工號雜湊為預設密碼並寫入 `Web9000`」的服務方法範例。
4. **Excel 匯出共用模組**：使用 `ClosedXML` 寫一個共用且彈性的 `ExcelExportHelper.cs` 類別，必須支援「在同一份 `.xlsx` 中建立多個 Sheet」，同時處理好文字型別防呆與 `MemoryStream` 輸出。

準備好後，我會再請你一頁一頁撰寫 Razor Pages 的 `.cshtml` 與 `.cshtml.cs` 程式碼。
## :speech_balloon: Anthropic
我來仔細閱讀 `Web9000.md` 並完成第一階段的四項任務。
1. Read: Web9000.md :white_check_mark:
完美。我已讀取規格書。現在依照指示完成第一階段的 4 項任務。

---

## ✅ 第一階段任務完成

### 1️⃣ **確認理解與盤點 - Razor Pages 路由規劃**

根據規格書，我梳理出以下功能頁面與路由：

| 路由名稱 | 功能說明 | 權限要求 |
|---------|--------|--------|
| `/` (或 `/Login`) | 登入首頁 | 無 |
| `/Dashboard` | 主功能區（根據權限條件渲染菜單） | 已登入 |
| `/Records/Query` | 紀錄查詢 | 一般使用者+ |
| `/Reports/Daily` | 日報表 | 一般使用者+ |
| `/Reports/Monthly` | 月報表 | 一般使用者+ |
| `/Account/ChangePassword` | 修改密碼 | 已登入 |
| `/Admin/Users` | 使用者管理 | 系統管理者 |
| `/Admin/Departments` | 部門管理 | 系統管理者 |

---

### 2️⃣ **資料庫建置腳本 (DDL) - Web9000 資料庫**
```sql
-- ========================================
-- Web9000 資料庫初始化腳本
-- ========================================

-- 1. 部門表
CREATE TABLE [dbo].[W9K_DEPARTMENT]
(
    [DEPARTMENT_ID] INT IDENTITY(1,1) PRIMARY KEY,
    [DEPARTMENT_NAME] VARCHAR(100) NOT NULL,
    [CREATE_EMPLOYEE_ID] INT NULL,
    [CREATE_DATE] DATETIME NOT NULL DEFAULT GETUTCDATE(),
    [UPDATE_EMPLOYEE_ID] INT NULL,
    [UPDATE_DATE] DATETIME NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [UK_DEPT_NAME] UNIQUE ([DEPARTMENT_NAME])
);

CREATE INDEX [IX_DEPT_CREATEDATE] ON [dbo].[W9K_DEPARTMENT]([CREATE_DATE]);
CREATE INDEX [IX_DEPT_UPDATEDATE] ON [dbo].[W9K_DEPARTMENT]([UPDATE_DATE]);

-- 2. 員工表
CREATE TABLE [dbo].[W9K_EMPLOYEE]
(
    [EMPLOYEE_ID] INT IDENTITY(1,1) PRIMARY KEY,
    [EMPLOYEE_NAME] VARCHAR(100) NOT NULL,
    [EMPLOYEE_NO] VARCHAR(20) NOT NULL,
    [DEPARTMENT_ID] INT NULL,
    [LOGIN_NAME] VARCHAR(50) NOT NULL,
    [LOGIN_PASSWORD] VARCHAR(100) NOT NULL,
    [EMPLOYEE_TYPE] INT DEFAULT 0,  -- 0=一般使用者, 1=系統管理者
    [CARD_NO] VARCHAR(20) NULL,
    [CREATE_EMPLOYEE_ID] INT NULL,
    [CREATE_DATE] DATETIME NOT NULL DEFAULT GETUTCDATE(),
    [UPDATE_EMPLOYEE_ID] INT NULL,
    [UPDATE_DATE] DATETIME NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_EMP_DEPT] FOREIGN KEY ([DEPARTMENT_ID]) 
        REFERENCES [dbo].[W9K_DEPARTMENT]([DEPARTMENT_ID]),
    CONSTRAINT [UK_EMP_NO] UNIQUE ([EMPLOYEE_NO]),
    CONSTRAINT [UK_LOGIN_NAME] UNIQUE ([LOGIN_NAME])
);

CREATE INDEX [IX_EMP_DEPTID] ON [dbo].[W9K_EMPLOYEE]([DEPARTMENT_ID]);
CREATE INDEX [IX_EMP_NO] ON [dbo].[W9K_EMPLOYEE]([EMPLOYEE_NO]);
CREATE INDEX [IX_EMP_CREATEDATE] ON [dbo].[W9K_EMPLOYEE]([CREATE_DATE]);
CREATE INDEX [IX_EMP_UPDATEDATE] ON [dbo].[W9K_EMPLOYEE]([UPDATE_DATE]);
CREATE INDEX [IX_EMP_LOGINNAME] ON [dbo].[W9K_EMPLOYEE]([LOGIN_NAME]);

-- 3. 經理管理表 (員工可管理的部門)
CREATE TABLE [dbo].[W9K_MANAGER]
(
    [MANAGER_ID] INT IDENTITY(1,1) PRIMARY KEY,
    [EMPLOYEE_ID] INT NOT NULL,
    [DEPARTMENT_ID] INT NOT NULL,
    [CREATE_EMPLOYEE_ID] INT NULL,
    [CREATE_DATE] DATETIME NOT NULL DEFAULT GETUTCDATE(),
    [UPDATE_EMPLOYEE_ID] INT NULL,
    [UPDATE_DATE] DATETIME NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_MGR_EMP] FOREIGN KEY ([EMPLOYEE_ID]) 
        REFERENCES [dbo].[W9K_EMPLOYEE]([EMPLOYEE_ID]) ON DELETE CASCADE,
    CONSTRAINT [FK_MGR_DEPT] FOREIGN KEY ([DEPARTMENT_ID]) 
        REFERENCES [dbo].[W9K_DEPARTMENT]([DEPARTMENT_ID]),
    CONSTRAINT [UK_MGR_EMP_DEPT] UNIQUE ([EMPLOYEE_ID], [DEPARTMENT_ID])
);

CREATE INDEX [IX_MGR_EMPID] ON [dbo].[W9K_MANAGER]([EMPLOYEE_ID]);
CREATE INDEX [IX_MGR_DEPTID] ON [dbo].[W9K_MANAGER]([DEPARTMENT_ID]);
```

**gdtek 資料庫結構（唯讀參考，無需修改）：**
```sql
-- gdtek.dbo.Person (員工基本資料)
-- EmployeeID VARCHAR, Name VARCHAR, CardNo VARCHAR

-- gdtek.dbo.DoorLog (刷卡日誌)
-- EmployeeID VARCHAR, DateTime DATETIME, TerminalID INT, CardNo VARCHAR
-- EventAlarmCode VARCHAR, VerificationSource VARCHAR, FKeyID INT

-- gdtek.dbo.Terminal (指紋機設定)
-- TerminalID INT, Name VARCHAR
```

---

### 3️⃣ **基礎建設與員工同步服務**

#### **appsettings.json**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "ConnectionStrings": {
    "Web9000": "Server=localhost\\SQLEXPRESS;Database=Web9000;Trusted_Connection=true;Encrypt=false;",
    "GDTek": "Server=localhost\\SQLEXPRESS;Database=gdtek;Trusted_Connection=true;Encrypt=false;"
  },
  "Web9000Settings": {
    "CompanyName": "公司名稱",
    "CompanyLogo": "/images/logo.png",
    "PageSize": 20,
    "SessionTimeoutMinutes": 30
  },
  "AllowedHosts": "*"
}
```

#### **Data/IDbConnectionFactory.cs**
```csharp
using Microsoft.Data.SqlClient;
using System.Data;

namespace Web9000.Data
{
    public interface IDbConnectionFactory
    {
        IDbConnection GetWeb9000Connection();
        IDbConnection GetGDTekConnection();
    }

    public class DbConnectionFactory : IDbConnectionFactory
    {
        private readonly IConfiguration _configuration;

        public DbConnectionFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IDbConnection GetWeb9000Connection()
        {
            return new SqlConnection(_configuration.GetConnectionString("Web9000"));
        }

        public IDbConnection GetGDTekConnection()
        {
            return new SqlConnection(_configuration.GetConnectionString("GDTek"));
        }
    }
}
```

#### **Services/EmployeeSyncService.cs**
```csharp
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
                                // 新增員工：預設密碼為工號的雜湊
                                var hashedPassword = passwordHasher.HashPassword(null, employeeNo);
                                string loginName = $"emp_{employeeNo}";

                                await web9000Conn.ExecuteAsync(
                                    @"INSERT INTO [W9K_EMPLOYEE] 
                                      (EMPLOYEE_NAME, EMPLOYEE_NO, LOGIN_NAME, LOGIN_PASSWORD, EMPLOYEE_TYPE, CARD_NO, CREATE_DATE, UPDATE_DATE)
                                      VALUES (@Name, @No, @Login, @Password, 0, @CardNo, GETUTCDATE(), GETUTCDATE())",
                                    new
                                    {
                                        Name = employeeName,
                                        No = employeeNo,
                                        Login = loginName,
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
```

#### **Program.cs 註冊**
```csharp
using Web9000.Data;
using Web9000.Services;

var builder = WebApplication.CreateBuilder(args);

// 註冊資料庫連線工廠
builder.Services.AddScoped<IDbConnectionFactory, DbConnectionFactory>();

// 註冊員工同步服務
builder.Services.AddScoped<IEmployeeSyncService, EmployeeSyncService>();

// 新增 Razor Pages 支援
builder.Services.AddRazorPages();

// 新增 Session 支援
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(
        builder.Configuration.GetValue<int>("Web9000Settings:SessionTimeoutMinutes", 30));
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();

app.MapRazorPages();
app.Run();
```

---

### 4️⃣ **Excel 匯出共用模組 - ExcelExportHelper.cs**
```csharp
using ClosedXML.Excel;

namespace Web9000.Utilities
{
    public class ExcelExportHelper
    {
        /// <summary>
        /// 建立多工作表 Excel 檔案
        /// </summary>
        /// <param name="sheetDataList">工作表清單，每個元素包含 Sheet 名稱與資料</param>
        /// <returns>MemoryStream，可直接用於下載回應</returns>
        public static MemoryStream CreateMultiSheetExcel(List<ExcelSheetData> sheetDataList)
        {
            var workbook = new XLWorkbook();

            foreach (var sheetData in sheetDataList)
            {
                var worksheet = workbook.Worksheets.Add(sheetData.SheetName);
                
                // 寫入標題列
                if (sheetData.Headers.Any())
                {
                    for (int colIdx = 0; colIdx < sheetData.Headers.Count; colIdx++)
                    {
                        var cell = worksheet.Cell(1, colIdx + 1);
                        cell.Value = sheetData.Headers[colIdx];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.LightGray;
                    }
                }

                // 寫入資料列
                if (sheetData.Rows.Any())
                {
                    for (int rowIdx = 0; rowIdx < sheetData.Rows.Count; rowIdx++)
                    {
                        var rowData = sheetData.Rows[rowIdx];
                        for (int colIdx = 0; colIdx < rowData.Count; colIdx++)
                        {
                            var cell = worksheet.Cell(rowIdx + 2, colIdx + 1);
                            var value = rowData[colIdx];

                            // 判斷資料型別並設定儲存格格式
                            if (value is null)
                            {
                                cell.Value = "";
                            }
                            else if (sheetData.TextColumns?.Contains(colIdx) == true)
                            {
                                // 強制為文字格式（防止前導零消失）
                                cell.Value = value.ToString();
                                cell.DataType = XLDataType.Text;
                            }
                            else if (value is DateTime dt)
                            {
                                cell.Value = dt;
                                cell.DataType = XLDataType.DateTime;
                                cell.Style.DateFormat.Format = "yyyy/mm/dd hh:mm:ss";
                            }
                            else if (value is decimal || value is double || value is float || value is int)
                            {
                                cell.Value = value;
                                cell.DataType = XLDataType.Number;
                            }
                            else
                            {
                                cell.Value = value.ToString();
                            }
                        }
                    }
                }

                // 自動調整欄寬
                worksheet.Columns().AdjustToContents();
            }

            var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;
            return stream;
        }

        /// <summary>
        /// 建立單個工作表 Excel 檔案
        /// </summary>
        public static MemoryStream CreateSingleSheetExcel(
            string sheetName,
            List<string> headers,
            List<List<object>> rows,
            HashSet<int>? textColumns = null)
        {
            var sheetData = new ExcelSheetData
            {
                SheetName = sheetName,
                Headers = headers,
                Rows = rows,
                TextColumns = textColumns
            };

            return CreateMultiSheetExcel(new List<ExcelSheetData> { sheetData });
        }
    }

    /// <summary>
    /// Excel 工作表資料結構
    /// </summary>
    public class ExcelSheetData
    {
        /// <summary>
        /// Sheet 名稱
        /// </summary>
        public string SheetName { get; set; } = "Sheet1";

        /// <summary>
        /// 標題列
        /// </summary>
        public List<string> Headers { get; set; } = new();

        /// <summary>
        /// 資料列（每一列是一個 List<object>）
        /// </summary>
        public List<List<object>> Rows { get; set; } = new();

        /// <summary>
        /// 指定哪些欄位要強制為文字格式（用於防止前導零消失）
        /// 例：textColumns = new HashSet<int> { 0, 2 } 表示第 0、2 欄強制為文字
        /// </summary>
        public HashSet<int>? TextColumns { get; set; }
    }
}
```

---

## 📋 Summary

✅ **已完成第一階段 4 項任務：**

1. **Razor Pages 路由規劃** - 8 個頁面（登入、紀錄查詢、日報表、月報表、密碼修改、使用者管理、部門管理）
2. **Web9000 DDL 腳本** - 3 張表（部門、員工、經理管理）+ 適當 Index 與 FK 約束
3. **基礎建設程式碼** - appsettings.json、IDbConnectionFactory、EmployeeSyncService、Program.cs 註冊
4. **ExcelExportHelper** - 支援多 Sheet、文字格式防呆、MemoryStream 輸出

**準備好開始撰寫各頁面程式碼！** 請告訴我從哪個頁面開始。
