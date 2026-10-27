-- ========================================
-- 建立預設系統管理員帳戶
-- ========================================
-- 帳號：web9000
-- 密碼：Web9000
-- 權限：系統管理者 (EMPLOYEE_TYPE = 1)
-- ========================================

-- 先檢查是否已存在，避免重複建立
IF NOT EXISTS (SELECT 1 FROM [dbo].[W9K_EMPLOYEE] WHERE [LOGIN_NAME] = 'web9000')
BEGIN
    INSERT INTO [dbo].[W9K_EMPLOYEE]
    (
        [EMPLOYEE_NAME],
        [EMPLOYEE_NO],
        [DEPARTMENT_ID],
        [LOGIN_NAME],
        [LOGIN_PASSWORD],
        [EMPLOYEE_TYPE],
        [CARD_NO],
        [CREATE_EMPLOYEE_ID],
        [CREATE_DATE],
        [UPDATE_EMPLOYEE_ID],
        [UPDATE_DATE]
    )
    VALUES
    (
        N'系統管理員',                                                                                                 -- EMPLOYEE_NAME
        'admin',                                                                                                      -- EMPLOYEE_NO
        NULL,                                                                                                         -- DEPARTMENT_ID (系統管理員不屬於特定部門)
        'web9000',                                                                                                    -- LOGIN_NAME
        'AQAAAAIAAYagAAAAEFTR1Jkc0xsGtiYwfs39KfRcCeb/Kd6naHg6SXZUAstbcIi6Y67JHMaIlm47W3Xyuw==',                    -- LOGIN_PASSWORD (Web9000 的 PBKDF2 雜湊)
        1,                                                                                                            -- EMPLOYEE_TYPE (1 = 系統管理者)
        NULL,                                                                                                         -- CARD_NO
        NULL,                                                                                                         -- CREATE_EMPLOYEE_ID
        GETUTCDATE(),                                                                                                 -- CREATE_DATE
        NULL,                                                                                                         -- UPDATE_EMPLOYEE_ID
        GETUTCDATE()                                                                                                  -- UPDATE_DATE
    );

    PRINT '✓ 預設系統管理員帳戶已建立成功';
    PRINT '  - 登入帳號: web9000';
    PRINT '  - 預設密碼: Web9000';
    PRINT '  - 權限等級: 系統管理者';
END
ELSE
BEGIN
    PRINT '⚠ 系統管理員帳戶 (web9000) 已存在，跳過建立';
END
GO
