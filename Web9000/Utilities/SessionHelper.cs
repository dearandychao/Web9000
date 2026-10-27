using System.Text.Json;
using Web9000.Models;

namespace Web9000.Utilities
{
    public static class SessionHelper
    {
        private const string SESSION_USER_KEY = "CurrentUser";

        /// <summary>
        /// 設定目前登入使用者資訊至 Session
        /// </summary>
        public static void SetCurrentUser(ISession session, SessionUser user)
        {
            var json = JsonSerializer.Serialize(user);
            session.SetString(SESSION_USER_KEY, json);
        }

        /// <summary>
        /// 從 Session 取得目前登入使用者資訊
        /// </summary>
        public static SessionUser? GetCurrentUser(ISession session)
        {
            var json = session.GetString(SESSION_USER_KEY);
            if (string.IsNullOrEmpty(json))
                return null;

            return JsonSerializer.Deserialize<SessionUser>(json);
        }

        /// <summary>
        /// 清除 Session 中的使用者資訊（登出）
        /// </summary>
        public static void ClearCurrentUser(ISession session)
        {
            session.Remove(SESSION_USER_KEY);
            session.Clear();
        }

        /// <summary>
        /// 檢查是否為系統管理者
        /// </summary>
        public static bool IsAdmin(SessionUser? user)
        {
            return user?.EmployeeType == 1;
        }

        /// <summary>
        /// 檢查使用者是否可以管理指定部門
        /// </summary>
        public static bool CanManageDepartment(SessionUser? user, int departmentId)
        {
            if (user == null) return false;
            if (user.EmployeeType == 1) return true; // 管理者可管理所有部門
            return user.ManagedDepartmentIds.Contains(departmentId);
        }
    }
}
