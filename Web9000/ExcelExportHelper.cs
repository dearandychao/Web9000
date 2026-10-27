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
                int currentRow = 1;

                // 寫入報表標題列（在表頭之前）
                if (sheetData.ReportHeaders?.Any() == true)
                {
                    foreach (var titleRow in sheetData.ReportHeaders)
                    {
                        for (int colIdx = 0; colIdx < titleRow.Count; colIdx++)
                        {
                            var cell = worksheet.Cell(currentRow, colIdx + 1);
                            cell.Value = titleRow[colIdx]?.ToString() ?? "";
                            cell.Style.Font.Bold = true;
                        }
                        currentRow++;
                    }
                    // 標題列後空一行
                    currentRow++;
                }

                // 寫入標題列
                int headerRow = currentRow;
                if (sheetData.Headers.Any())
                {
                    for (int colIdx = 0; colIdx < sheetData.Headers.Count; colIdx++)
                    {
                        var cell = worksheet.Cell(currentRow, colIdx + 1);
                        cell.Value = sheetData.Headers[colIdx];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.LightGray;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    }
                    currentRow++;
                }

                // 寫入資料列
                int dataStartRow = currentRow;
                if (sheetData.Rows.Any())
                {
                    for (int rowIdx = 0; rowIdx < sheetData.Rows.Count; rowIdx++)
                    {
                        var rowData = sheetData.Rows[rowIdx];
                        for (int colIdx = 0; colIdx < rowData.Count; colIdx++)
                        {
                            var cell = worksheet.Cell(currentRow + rowIdx, colIdx + 1);
                            var value = rowData[colIdx];

                            // 判斷資料型別並設定儲存格格式
                            if (value is null)
                            {
                                cell.Value = "";
                            }
                            else if (sheetData.TextColumns?.Contains(colIdx) == true)
                            {
                                // 強制為文字格式（防止前導零消失）
                                cell.SetValue(value.ToString());
                                cell.Style.NumberFormat.Format = "@";  // @ 是文字格式
                            }
                            else if (value is DateTime dt)
                            {
                                cell.Value = dt;
                                cell.Style.DateFormat.Format = "yyyy/mm/dd hh:mm:ss";
                            }
                            else if (value is decimal || value is double || value is float || value is int)
                            {
                                cell.Value = (XLCellValue)value;
                            }
                            else
                            {
                                cell.Value = value.ToString();
                            }

                            // 加上邊框
                            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            cell.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        }
                    }

                    // 為整個表格區域設定邊框
                    if (sheetData.Headers.Any())
                    {
                        var dataRange = worksheet.Range(headerRow, 1, currentRow + sheetData.Rows.Count - 1, sheetData.Headers.Count);
                        dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
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
            HashSet<int>? textColumns = null,
            List<List<object>>? reportHeaders = null)
        {
            var sheetData = new ExcelSheetData
            {
                SheetName = sheetName,
                Headers = headers,
                Rows = rows,
                TextColumns = textColumns,
                ReportHeaders = reportHeaders
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

        /// <summary>
        /// 報表標題列（在表頭之前的額外說明列）
        /// 例如：日報表標題、日期、匯出人員等資訊
        /// </summary>
        public List<List<object>>? ReportHeaders { get; set; }
    }
}