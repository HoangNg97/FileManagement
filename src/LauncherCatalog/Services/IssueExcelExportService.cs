using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using LauncherCatalog.Models;

namespace LauncherCatalog.Services;

/// <summary>
/// Exports one or more <see cref="Issue"/> threads to a styled <c>.xlsx</c> workbook using a vertical,
/// Jira-style layout: each issue is a two-column block (narrow label column + wide content column) with
/// a title banner, a metadata section (created by / created date / message count), a "Conversation
/// history" band, and then each message stacked top-to-bottom as a shaded header row followed by a
/// wrapped body row.
///
/// When several issues are exported they are laid out side by side on a single sheet and separated by a
/// thick vertical divider border (no spacer column). Rows 1-5 (title + metadata + section band) are
/// frozen so they stay visible while the thread scrolls. The file is written as raw OOXML (no external
/// library) with a full <c>styles.xml</c> for bold/shaded headers, thin borders, wrapped text, capped
/// column widths, adjusted row heights, formatted dates, and a consistent Calibri 11 font. No AutoFilter
/// is applied because a vertical layout has no repeating column headers.
/// </summary>
public sealed class IssueExcelExportService
{
    private const int LabelWidthChars = 18;
    private const int ContentWidthChars = 58;
    private const int FrozenRowCount = 5;
    private const double MaxRowHeight = 409d; // Excel's maximum row height in points.

    // Cell style indices into the cellXfs table declared in BuildStylesXml (must stay in sync).
    private const int StyleTitle = 1;
    private const int StyleTitleDivider = 2;
    private const int StyleMetaLabel = 3;
    private const int StyleMetaLabelDivider = 4;
    private const int StyleMetaValue = 5;
    private const int StyleMetaValueDivider = 6;
    private const int StyleMetaDate = 7;
    private const int StyleMetaDateDivider = 8;
    private const int StyleSection = 9;
    private const int StyleSectionDivider = 10;
    private const int StyleMsgHeader = 11;
    private const int StyleMsgHeaderDivider = 12;
    private const int StyleMsgBody = 13;
    private const int StyleMsgBodyDivider = 14;
    private const int StyleBlankThin = 15;
    private const int StyleBlankLeftDivider = 16;

    private enum RowKind
    {
        Title,
        Metadata,
        MetadataDate,
        MetadataNumber,
        Section,
        MessageHeader,
        MessageBody
    }

    private sealed record RowSpec(RowKind Kind, string Label, string Text, double Number, DateTime Date);

    public async Task ExportAsync(string outputPath, IReadOnlyList<Issue> issues)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        if (issues is null || issues.Count == 0)
        {
            throw new ArgumentException("At least one issue is required.", nameof(issues));
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

        var sheetName = issues.Count == 1 ? SanitizeSheetName(issues[0].DisplayTitle) : "Issues";

        await using var stream = File.Open(outputPath, FileMode.CreateNew, FileAccess.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        await WriteEntryAsync(archive, "[Content_Types].xml", BuildContentTypesXml());
        await WriteEntryAsync(archive, "_rels/.rels", BuildPackageRelsXml());
        await WriteEntryAsync(archive, "xl/workbook.xml", BuildWorkbookXml(sheetName));
        await WriteEntryAsync(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelsXml());
        await WriteEntryAsync(archive, "xl/styles.xml", BuildStylesXml());
        await WriteEntryAsync(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(issues));
    }

    private static string BuildWorksheetXml(IReadOnlyList<Issue> issues)
    {
        // Build the per-issue vertical layout: each issue owns a fixed list of physical rows starting
        // at row 1 (title -> metadata -> section -> message header/body pairs). Because issues sit side
        // by side, a given sheet row maps to a different row kind for each issue depending on how many
        // messages it has, so we index everything by absolute row number.
        var layouts = issues.Select(BuildRowSpecs).ToList();
        var maxRow = layouts.Max(rows => rows.Count);

        var cols = BuildColumns(issues.Count);
        var merges = new List<string>();
        var rowXml = new StringBuilder();

        for (var rowIndex = 1; rowIndex <= maxRow; rowIndex++)
        {
            var cells = new StringBuilder();
            double rowHeight = 15d;

            for (var issueIndex = 0; issueIndex < issues.Count; issueIndex++)
            {
                var divider = issueIndex > 0;
                var labelCol = issueIndex * 2;
                var contentCol = labelCol + 1;
                var specs = layouts[issueIndex];

                if (rowIndex > specs.Count)
                {
                    // Filler row: keep the vertical divider running full height on later issues.
                    if (divider)
                    {
                        cells.Append(BlankCell(labelCol, rowIndex, StyleBlankLeftDivider));
                    }

                    continue;
                }

                var spec = specs[rowIndex - 1];
                rowHeight = Math.Max(rowHeight, EstimateRowHeight(spec));

                switch (spec.Kind)
                {
                    case RowKind.Metadata:
                    case RowKind.MetadataDate:
                    case RowKind.MetadataNumber:
                        cells.Append(StringCell(labelCol, rowIndex, divider ? StyleMetaLabelDivider : StyleMetaLabel, spec.Label));
                        cells.Append(spec.Kind switch
                        {
                            RowKind.MetadataDate => DateCell(contentCol, rowIndex, StyleMetaDate, spec.Date),
                            RowKind.MetadataNumber => NumberCell(contentCol, rowIndex, StyleMetaValue, spec.Number),
                            _ => StringCell(contentCol, rowIndex, StyleMetaValue, spec.Text)
                        });
                        break;

                    default:
                        // Merged banner row spanning the issue's label + content columns.
                        var anchorStyle = spec.Kind switch
                        {
                            RowKind.Title => divider ? StyleTitleDivider : StyleTitle,
                            RowKind.Section => divider ? StyleSectionDivider : StyleSection,
                            RowKind.MessageHeader => divider ? StyleMsgHeaderDivider : StyleMsgHeader,
                            _ => divider ? StyleMsgBodyDivider : StyleMsgBody
                        };

                        cells.Append(StringCell(labelCol, rowIndex, anchorStyle, spec.Text));
                        cells.Append(BlankCell(contentCol, rowIndex, StyleBlankThin));
                        merges.Add($"{ColumnName(labelCol)}{rowIndex}:{ColumnName(contentCol)}{rowIndex}");
                        break;
                }
            }

            rowHeight = Math.Min(rowHeight, MaxRowHeight);
            rowXml.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowIndex}\" ht=\"{rowHeight.ToString("0.##", CultureInfo.InvariantCulture)}\" customHeight=\"1\">");
            rowXml.Append(cells);
            rowXml.Append("</row>");
        }

        var mergeXml = merges.Count == 0
            ? string.Empty
            : $"<mergeCells count=\"{merges.Count}\">{string.Concat(merges.Select(reference => $"<mergeCell ref=\"{reference}\"/>"))}</mergeCells>";

        return $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetViews>
                <sheetView workbookViewId="0">
                  <pane ySplit="{{FrozenRowCount}}" topLeftCell="A{{FrozenRowCount + 1}}" activePane="bottomLeft" state="frozen"/>
                  <selection pane="bottomLeft" activeCell="A{{FrozenRowCount + 1}}" sqref="A{{FrozenRowCount + 1}}"/>
                </sheetView>
              </sheetViews>
              <sheetFormatPr defaultRowHeight="15"/>
              {{cols}}
              <sheetData>
                {{rowXml}}
              </sheetData>
              {{mergeXml}}
            </worksheet>
            """;
    }

    private static List<RowSpec> BuildRowSpecs(Issue issue)
    {
        var specs = new List<RowSpec>
        {
            new(RowKind.Title, string.Empty, issue.DisplayTitle, 0, default),
            new(RowKind.Metadata, "Note", string.IsNullOrWhiteSpace(issue.Note) ? "-" : issue.Note.Trim(), 0, default),
            new(RowKind.MetadataDate, "Created date", string.Empty, 0, issue.CreatedDate),
            new(RowKind.MetadataNumber, "Messages", string.Empty, issue.Messages.Count, default),
            new(RowKind.Section, string.Empty, "Conversation history", 0, default)
        };

        if (issue.Messages.Count == 0)
        {
            specs.Add(new RowSpec(RowKind.MessageBody, string.Empty, "No messages yet.", 0, default));
            return specs;
        }

        foreach (var message in issue.Messages)
        {
            var author = string.IsNullOrWhiteSpace(message.CreatedBy) ? "Unknown" : message.CreatedBy.Trim();
            var timestamp = message.CreatedDate.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
            var exportHeader = $"{author} \u00B7 {timestamp}";
            specs.Add(new RowSpec(RowKind.MessageHeader, string.Empty, exportHeader, 0, default));
            specs.Add(new RowSpec(RowKind.MessageBody, string.Empty, message.Content, 0, default));
        }

        return specs;
    }

    private static string BuildColumns(int issueCount)
    {
        var builder = new StringBuilder("<cols>");
        for (var issueIndex = 0; issueIndex < issueCount; issueIndex++)
        {
            var labelCol = issueIndex * 2 + 1; // 1-based for <col>.
            var contentCol = labelCol + 1;
            builder.Append(CultureInfo.InvariantCulture, $"<col min=\"{labelCol}\" max=\"{labelCol}\" width=\"{LabelWidthChars}\" customWidth=\"1\"/>");
            builder.Append(CultureInfo.InvariantCulture, $"<col min=\"{contentCol}\" max=\"{contentCol}\" width=\"{ContentWidthChars}\" customWidth=\"1\"/>");
        }

        builder.Append("</cols>");
        return builder.ToString();
    }

    private static double EstimateRowHeight(RowSpec spec)
    {
        return spec.Kind switch
        {
            RowKind.Title => 26d,
            RowKind.Section => 20d,
            RowKind.Metadata => WrapHeight(spec.Text, ContentWidthChars, 16d),
            RowKind.MetadataDate => 16d,
            RowKind.MetadataNumber => 16d,
            RowKind.MessageHeader => WrapHeight(spec.Text, LabelWidthChars + ContentWidthChars, 18d),
            _ => WrapHeight(spec.Text, LabelWidthChars + ContentWidthChars, 15d)
        };
    }

    private static double WrapHeight(string text, int charsPerLine, double minHeight)
    {
        if (string.IsNullOrEmpty(text) || charsPerLine <= 0)
        {
            return minHeight;
        }

        var lines = 0;
        foreach (var segment in text.Split('\n'))
        {
            var length = segment.TrimEnd('\r').Length;
            lines += Math.Max(1, (int)Math.Ceiling(length / (double)charsPerLine));
        }

        return Math.Max(minHeight, lines * 15d + 4d);
    }

    private static string StringCell(int columnIndex, int rowIndex, int styleIndex, string value)
    {
        var reference = $"{ColumnName(columnIndex)}{rowIndex}";
        var text = SecurityElement.Escape(value) ?? string.Empty;
        return $"<c r=\"{reference}\" s=\"{styleIndex}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{text}</t></is></c>";
    }

    private static string NumberCell(int columnIndex, int rowIndex, int styleIndex, double value)
    {
        var reference = $"{ColumnName(columnIndex)}{rowIndex}";
        return $"<c r=\"{reference}\" s=\"{styleIndex}\"><v>{value.ToString(CultureInfo.InvariantCulture)}</v></c>";
    }

    private static string DateCell(int columnIndex, int rowIndex, int styleIndex, DateTime value)
    {
        var reference = $"{ColumnName(columnIndex)}{rowIndex}";
        var serial = ToExcelSerial(value);
        return $"<c r=\"{reference}\" s=\"{styleIndex}\"><v>{serial.ToString(CultureInfo.InvariantCulture)}</v></c>";
    }

    private static string BlankCell(int columnIndex, int rowIndex, int styleIndex)
    {
        return $"<c r=\"{ColumnName(columnIndex)}{rowIndex}\" s=\"{styleIndex}\"/>";
    }

    private static double ToExcelSerial(DateTime value)
    {
        // Excel's day 0 is 1899-12-30 (its 1900 leap-year bug baked in).
        var epoch = new DateTime(1899, 12, 30);
        return (value - epoch).TotalDays;
    }

    private static string ColumnName(int index)
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

    private static async Task WriteEntryAsync(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
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
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
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

    private static string BuildWorkbookXml(string sheetName)
    {
        var escaped = EscapeAttribute(sheetName);
        return $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="{{escaped}}" sheetId="1" r:id="rId1"/>
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
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """;
    }

    private static string BuildStylesXml()
    {
        // Fonts: 0 = base, 1 = bold, 2 = title (bold white). Fills: 0/1 reserved, 2 = title, 3 = section,
        // 4 = message header. Borders: 0 = none, 1 = thin box, 2 = thick divider left + thin box,
        // 3 = thick divider left only (filler). cellXfs indices mirror the Style* constants above.
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <numFmts count="1">
                <numFmt numFmtId="164" formatCode="yyyy\-mm\-dd\ hh:mm"/>
              </numFmts>
              <fonts count="3">
                <font><sz val="11"/><name val="Calibri"/></font>
                <font><b/><sz val="11"/><name val="Calibri"/></font>
                <font><b/><sz val="14"/><color rgb="FFFFFFFF"/><name val="Calibri"/></font>
              </fonts>
              <fills count="5">
                <fill><patternFill patternType="none"/></fill>
                <fill><patternFill patternType="gray125"/></fill>
                <fill><patternFill patternType="solid"><fgColor rgb="FF4472C4"/><bgColor indexed="64"/></patternFill></fill>
                <fill><patternFill patternType="solid"><fgColor rgb="FFD9E1F2"/><bgColor indexed="64"/></patternFill></fill>
                <fill><patternFill patternType="solid"><fgColor rgb="FFF2F2F2"/><bgColor indexed="64"/></patternFill></fill>
              </fills>
              <borders count="4">
                <border><left/><right/><top/><bottom/><diagonal/></border>
                <border>
                  <left style="thin"><color rgb="FFBFBFBF"/></left>
                  <right style="thin"><color rgb="FFBFBFBF"/></right>
                  <top style="thin"><color rgb="FFBFBFBF"/></top>
                  <bottom style="thin"><color rgb="FFBFBFBF"/></bottom>
                  <diagonal/>
                </border>
                <border>
                  <left style="thick"><color rgb="FF000000"/></left>
                  <right style="thin"><color rgb="FFBFBFBF"/></right>
                  <top style="thin"><color rgb="FFBFBFBF"/></top>
                  <bottom style="thin"><color rgb="FFBFBFBF"/></bottom>
                  <diagonal/>
                </border>
                <border>
                  <left style="thick"><color rgb="FF000000"/></left>
                  <right/><top/><bottom/><diagonal/>
                </border>
              </borders>
              <cellStyleXfs count="1">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
              </cellStyleXfs>
              <cellXfs count="17">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                <xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
                <xf numFmtId="0" fontId="2" fillId="2" borderId="2" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
                <xf numFmtId="0" fontId="1" fillId="0" borderId="1" xfId="0" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>
                <xf numFmtId="0" fontId="1" fillId="0" borderId="2" xfId="0" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>
                <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center" wrapText="1"/></xf>
                <xf numFmtId="0" fontId="0" fillId="0" borderId="2" xfId="0" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center" wrapText="1"/></xf>
                <xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>
                <xf numFmtId="164" fontId="0" fillId="0" borderId="2" xfId="0" applyNumberFormat="1" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>
                <xf numFmtId="0" fontId="1" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>
                <xf numFmtId="0" fontId="1" fillId="3" borderId="2" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>
                <xf numFmtId="0" fontId="1" fillId="4" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center" wrapText="1"/></xf>
                <xf numFmtId="0" fontId="1" fillId="4" borderId="2" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center" wrapText="1"/></xf>
                <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="top" wrapText="1"/></xf>
                <xf numFmtId="0" fontId="0" fillId="0" borderId="2" xfId="0" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="top" wrapText="1"/></xf>
                <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyFont="1" applyBorder="1"/>
                <xf numFmtId="0" fontId="0" fillId="0" borderId="3" xfId="0" applyFont="1" applyBorder="1"/>
              </cellXfs>
              <cellStyles count="1">
                <cellStyle name="Normal" xfId="0" builtinId="0"/>
              </cellStyles>
            </styleSheet>
            """;
    }

    private static string SanitizeSheetName(string value)
    {
        var invalidChars = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Issue";
        }

        return sanitized.Length <= 31 ? sanitized : sanitized[..31];
    }

    private static string EscapeAttribute(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }
}
