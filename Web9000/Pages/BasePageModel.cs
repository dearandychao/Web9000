using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Web9000.Models;
using Web9000.Utilities;

namespace Web9000.Pages
{
    /// <summary>
    /// 所有需要登入的頁面基底類別
    /// </summary>
    public abstract class BasePageModel : PageModel
    {
        protected SessionUser? CurrentUser => SessionHelper.GetCurrentUser(HttpContext.Session);
        public bool IsAdmin => SessionHelper.IsAdmin(CurrentUser);
        public bool IsDepartmentManager => CurrentUser?.ManagedDepartmentIds?.Any() == true;
        public bool IsAdminOrDepartmentManager => IsAdmin || IsDepartmentManager;

        // 資料庫名稱設定
        protected string Web9000DbName { get; private set; } = "Web9000";
        protected string GDTekDbName { get; private set; } = "gdtek";

        public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context)
        {
            base.OnPageHandlerExecuting(context);

            // 載入資料庫名稱設定
            var dbSettings = HttpContext.RequestServices.GetService<IOptions<DatabaseSettings>>();
            if (dbSettings?.Value != null)
            {
                Web9000DbName = dbSettings.Value.Web9000Database;
                GDTekDbName = dbSettings.Value.GDTekDatabase;
            }

            // 檢查是否已登入
            if (CurrentUser == null)
            {
                context.Result = new RedirectToPageResult("/Login");
                return;
            }

            // 檢查是否為管理頁面
            if (RequireAdmin() && !IsAdmin)
            {
                context.Result = new RedirectToPageResult("/AccessDenied");
                return;
            }

            // 檢查是否需要管理者或部門管理者權限
            if (RequireAdminOrDepartmentManager() && !IsAdminOrDepartmentManager)
            {
                context.Result = new RedirectToPageResult("/AccessDenied");
                return;
            }
        }

        /// <summary>
        /// 子類別覆寫此方法以要求管理者權限
        /// </summary>
        protected virtual bool RequireAdmin() => false;

        /// <summary>
        /// 子類別覆寫此方法以要求管理者或部門管理者權限
        /// </summary>
        protected virtual bool RequireAdminOrDepartmentManager() => false;

        /// <summary>
        /// 取得分頁設定
        /// </summary>
        protected int GetPageSize()
        {
            return Configuration?.GetValue<int>("Web9000Settings:PageSize") ?? 20;
        }

        protected IConfiguration? Configuration { get; set; }
    }
}
