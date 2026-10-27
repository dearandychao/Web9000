namespace Web9000.Models
{
    /// <summary>
    /// Session 中儲存的使用者資訊
    /// </summary>
    public class SessionUser
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeNo { get; set; } = string.Empty;
        public int? DepartmentId { get; set; }
        public string LoginName { get; set; } = string.Empty;
        public int EmployeeType { get; set; } // 0=一般使用者, 1=系統管理者
        public List<int> ManagedDepartmentIds { get; set; } = new();
    }
}
