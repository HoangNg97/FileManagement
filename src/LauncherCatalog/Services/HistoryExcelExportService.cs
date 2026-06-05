using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using LauncherCatalog.Models;

namespace LauncherCatalog.Services;

public sealed class HistoryExcelExportService
{
    public async Task ExportAsync(
        string outputPath,
        HistoryFileItem historyFile,
        IReadOnlyList<HistoryEventItem> events)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        var outputFolder = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
        }

        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        await using var stream = File.Open(outputPath, FileMode.CreateNew, FileAccess.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        await WriteEntryAsync(archive, "[Content_Types].xml", BuildContentTypesXml());
        await WriteEntryAsync(archive, "_rels/.rels", BuildPackageRelsXml());
        await WriteEntryAsync(archive, "xl/workbook.xml", BuildWorkbookXml(historyFile.Name));
        await WriteEntryAsync(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelsXml());
        await WriteEntryAsync(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(events));
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
    }

    private static string BuildContentTypesXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """;
    }

    private static string BuildPackageRelsXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """;
    }

    private static string BuildWorkbookXml(string historyFileName)
    {
        var sheetName = EscapeAttribute(SanitizeSheetName(historyFileName));
        return $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="{{sheetName}}" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """;
    }

    private static string BuildWorkbookRelsXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """;
    }

    private static string BuildWorksheetXml(IReadOnlyList<HistoryEventItem> events)
    {
        var rows = new List<string>
        {
            BuildRow(1, ["Event Date", "Note", "Created At", "Updated At"])
        };

        var rowIndex = 2;
        foreach (var historyEvent in events.OrderBy(item => item.EventDate).ThenBy(item => item.CreatedAt))
        {
            rows.Add(BuildRow(rowIndex++,
            [
                historyEvent.EventDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                historyEvent.Note,
                historyEvent.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                historyEvent.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            ]));
        }

        return $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetViews>
                <sheetView workbookViewId="0"/>
              </sheetViews>
              <sheetData>
                {{string.Join(Environment.NewLine, rows)}}
              </sheetData>
            </worksheet>
            """;
    }

    private static string BuildRow(int rowIndex, IReadOnlyList<string> values)
    {
        var cells = values
            .Select((value, index) => BuildInlineStringCell(rowIndex, index, value))
            .ToArray();

        return $"<row r=\"{rowIndex}\">{string.Join(string.Empty, cells)}</row>";
    }

    private static string BuildInlineStringCell(int rowIndex, int columnIndex, string value)
    {
        var reference = $"{GetColumnName(columnIndex)}{rowIndex}";
        return $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(value) ?? string.Empty}</t></is></c>";
    }

    private static string GetColumnName(int index)
    {
        var dividend = index + 1;
        var columnName = string.Empty;

        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar('A' + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }

        return columnName;
    }

    private static string SanitizeSheetName(string value)
    {
        var invalidChars = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "History";
        }

        return sanitized.Length <= 31 ? sanitized : sanitized[..31];
    }

    private static string EscapeAttribute(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }
}
