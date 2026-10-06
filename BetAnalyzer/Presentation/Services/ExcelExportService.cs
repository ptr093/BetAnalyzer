using ClosedXML.Excel;
using BetAnalyzer.Presentation.ViewModels;

namespace BetAnalyzer.Presentation.Services;

public sealed class ExcelExportService
{
    public void Export(
        IReadOnlyCollection<AnalysisResultViewModel> results,
        string filePath)
    {
        using var workbook =
            new XLWorkbook();

        var worksheet =
            workbook.Worksheets.Add(
                "Analiza");

        AddTitle(
            worksheet,
            results.Count);

        AddHeaders(worksheet);

        AddRows(
            worksheet,
            results);

        FormatWorksheet(
            worksheet,
            results.Count);

        AddLegend(worksheet);

        workbook.SaveAs(filePath);
    }

    private static void AddTitle(
        IXLWorksheet worksheet,
        int resultCount)
    {
        worksheet.Range("A1:Q1").Merge();

        worksheet.Cell("A1").Value =
            "BetAnalyzer - raport analizy typów";

        worksheet.Cell("A1")
            .Style.Font.SetBold();

        worksheet.Cell("A1")
            .Style.Font.SetFontSize(16);

        worksheet.Cell("A1")
            .Style.Font.SetFontColor(
                XLColor.White);

        worksheet.Cell("A1")
            .Style.Fill.SetBackgroundColor(
                XLColor.FromHtml("#172B4D"));

        worksheet.Cell("A1")
            .Style.Alignment.SetHorizontal(
                XLAlignmentHorizontalValues.Center);

        worksheet.Cell("A2").Value =
            $"Data wygenerowania: " +
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}";

        worksheet.Cell("E2").Value =
            $"Liczba wyników: {resultCount}";

        worksheet.Range("A2:Q2")
            .Style.Font.SetItalic();

        worksheet.Range("A2:Q2")
            .Style.Font.SetFontColor(
                XLColor.FromHtml("#667085"));
    }

    private static void AddHeaders(
        IXLWorksheet worksheet)
    {
        var headers =
            new[]
            {
                "Mecz",
                "Rynek",
                "Okres",
                "Zakres",
                "Typ",
                "Linia",
                "Kurs",
                "Szansa modelu",
                "Szansa z kursu",
                "Edge",
                "EV",
                "Kelly",
                "Sugerowana stawka",
                "Pewność",
                "Kryterium historii",
                "Rekomendacja",
                "Szczegóły"
            };

        for (var i = 0; i < headers.Length; i++)
        {
            var cell =
                worksheet.Cell(4, i + 1);

            cell.Value =
                headers[i];

            cell.Style.Font.SetBold();

            cell.Style.Font.SetFontColor(
                XLColor.White);

            cell.Style.Fill.SetBackgroundColor(
                XLColor.FromHtml("#2563EB"));

            cell.Style.Alignment.SetHorizontal(
                XLAlignmentHorizontalValues.Center);

            cell.Style.Alignment.SetVertical(
                XLAlignmentVerticalValues.Center);

            cell.Style.Alignment.SetWrapText();

            cell.Style.Border.SetOutsideBorder(
                XLBorderStyleValues.Thin);

            cell.Style.Border.SetInsideBorder(
                XLBorderStyleValues.Thin);
        }
    }

    private static void AddRows(
        IXLWorksheet worksheet,
        IReadOnlyCollection<AnalysisResultViewModel> results)
    {
        var ordered =
            results
                .OrderByDescending(
                    x => x.ExpectedValue)
                .ThenByDescending(
                    x => x.Edge)
                .ThenByDescending(
                    x => x.Probability)
                .ToList();

        var row = 5;

        foreach (var item in ordered)
        {
            worksheet.Cell(row, 1).Value =
                item.Match;

            worksheet.Cell(row, 2).Value =
                item.Market;

            worksheet.Cell(row, 3).Value =
                item.Period;

            worksheet.Cell(row, 4).Value =
                item.Scope;

            worksheet.Cell(row, 5).Value =
                item.Selection;

            if (item.Line.HasValue)
            {
                worksheet.Cell(row, 6).Value =
                    item.Line.Value;
            }

            worksheet.Cell(row, 7).Value =
                item.Odd;

            worksheet.Cell(row, 8).Value =
                item.Probability;

            worksheet.Cell(row, 9).Value =
                item.ImpliedProbability;

            worksheet.Cell(row, 10).Value =
                item.Edge;

            worksheet.Cell(row, 11).Value =
                item.ExpectedValue;

            worksheet.Cell(row, 12).Value =
                item.KellyFraction;

            worksheet.Cell(row, 13).Value =
                item.SuggestedStakePercent / 100d;

            worksheet.Cell(row, 14).Value =
                item.ConfidenceLevel;

            worksheet.Cell(row, 15).Value =
                item.FiveMatchesCriterion;

            worksheet.Cell(row, 16).Value =
                item.Recommendation;

            worksheet.Cell(row, 17).Value =
                item.Details;

            ApplyRowColor(
                worksheet,
                row,
                item);

            row++;
        }
    }

    private static void ApplyRowColor(
        IXLWorksheet worksheet,
        int row,
        AnalysisResultViewModel item)
    {
        var range =
            worksheet.Range(
                row,
                1,
                row,
                17);

        if (item.Recommendation == "Odrzucone" ||
            item.ExpectedValue <= 0d)
        {
            range.Style.Fill.SetBackgroundColor(
                XLColor.FromHtml("#FCE4D6"));

            return;
        }

        if (item.ConfidenceLevel == "Bardzo wysokie")
        {
            range.Style.Fill.SetBackgroundColor(
                XLColor.FromHtml("#C6E0B4"));

            return;
        }

        if (item.ConfidenceLevel == "Wysokie")
        {
            range.Style.Fill.SetBackgroundColor(
                XLColor.FromHtml("#E2F0D9"));

            return;
        }

        if (item.ConfidenceLevel == "Umiarkowane")
        {
            range.Style.Fill.SetBackgroundColor(
                XLColor.FromHtml("#FFF2CC"));

            return;
        }

        range.Style.Fill.SetBackgroundColor(
            XLColor.FromHtml("#FCE4D6"));
    }

    private static void FormatWorksheet(
        IXLWorksheet worksheet,
        int resultCount)
    {
        var lastRow =
            Math.Max(
                5,
                resultCount + 4);

        var tableRange =
            worksheet.Range(
                4,
                1,
                lastRow,
                17);

        tableRange.Style.Border.SetOutsideBorder(
            XLBorderStyleValues.Thin);

        tableRange.Style.Border.SetInsideBorder(
            XLBorderStyleValues.Hair);

        worksheet.Range(
            $"F5:F{lastRow}")
            .Style.NumberFormat.Format =
            "0.0";

        worksheet.Range(
            $"G5:G{lastRow}")
            .Style.NumberFormat.Format =
            "0.00";

        worksheet.Range(
            $"H5:M{lastRow}")
            .Style.NumberFormat.Format =
            "0.00%";

        worksheet.Range(
            $"H5:H{lastRow}")
            .AddConditionalFormat()
            .ColorScale()
            .LowestValue(
                XLColor.FromHtml("#F8696B"))
            .Midpoint(
                XLCFContentType.Percentile,
                50,
                XLColor.FromHtml("#FFEB84"))
            .HighestValue(
                XLColor.FromHtml("#63BE7B"));

        worksheet.Range(
            $"J5:J{lastRow}")
            .AddConditionalFormat()
            .ColorScale()
            .LowestValue(
                XLColor.FromHtml("#F8696B"))
            .Midpoint(
                XLCFContentType.Number,
                0,
                XLColor.White)
            .HighestValue(
                XLColor.FromHtml("#63BE7B"));

        worksheet.Range(
            $"K5:K{lastRow}")
            .AddConditionalFormat()
            .ColorScale()
            .LowestValue(
                XLColor.FromHtml("#F8696B"))
            .Midpoint(
                XLCFContentType.Number,
                0,
                XLColor.White)
            .HighestValue(
                XLColor.FromHtml("#63BE7B"));

        worksheet.Range(
            $"M5:M{lastRow}")
            .AddConditionalFormat()
            .ColorScale()
            .LowestValue(
                XLColor.White)
            .HighestValue(
                XLColor.FromHtml("#63BE7B"));

        worksheet.Range(
            $"A4:Q{lastRow}")
            .SetAutoFilter();

        worksheet.SheetView.FreezeRows(4);

        worksheet.Column(1).Width = 32;
        worksheet.Column(2).Width = 18;
        worksheet.Column(3).Width = 14;
        worksheet.Column(4).Width = 28;
        worksheet.Column(5).Width = 12;
        worksheet.Column(6).Width = 10;
        worksheet.Column(7).Width = 10;
        worksheet.Column(8).Width = 16;
        worksheet.Column(9).Width = 16;
        worksheet.Column(10).Width = 12;
        worksheet.Column(11).Width = 12;
        worksheet.Column(12).Width = 12;
        worksheet.Column(13).Width = 18;
        worksheet.Column(14).Width = 18;
        worksheet.Column(15).Width = 34;
        worksheet.Column(16).Width = 16;
        worksheet.Column(17).Width = 55;

        worksheet.Column(15)
            .Style.Alignment.SetWrapText();

        worksheet.Column(17)
            .Style.Alignment.SetWrapText();

        worksheet.Range(
            $"G5:M{lastRow}")
            .Style.Alignment.SetHorizontal(
                XLAlignmentHorizontalValues.Center);

        worksheet.Range(
            $"N5:N{lastRow}")
            .Style.Alignment.SetHorizontal(
                XLAlignmentHorizontalValues.Center);

        worksheet.Range(
            $"P5:P{lastRow}")
            .Style.Alignment.SetHorizontal(
                XLAlignmentHorizontalValues.Center);

        worksheet.Rows()
            .AdjustToContents();
    }

    private static void AddLegend(
        IXLWorksheet worksheet)
    {
        worksheet.Range("A3:Q3").Merge();

        worksheet.Cell("A3").Value =
            "Kolory: ciemna zieleń = bardzo wysoka pewność, " +
            "jasna zieleń = wysoka, żółty = umiarkowana, " +
            "czerwony = brak dodatniego EV albo typ odrzucony.";

        worksheet.Cell("A3")
            .Style.Font.SetItalic();

        worksheet.Cell("A3")
            .Style.Font.SetFontColor(
                XLColor.FromHtml("#667085"));
    }
}