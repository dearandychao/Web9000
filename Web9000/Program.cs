using Web9000.Data;
using Web9000.Models;
using Web9000.Services;

var builder = WebApplication.CreateBuilder(args);

// 註冊資料庫名稱設定
builder.Services.Configure<DatabaseSettings>(
    builder.Configuration.GetSection("DatabaseNames"));

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