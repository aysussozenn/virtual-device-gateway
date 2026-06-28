using System.Text.Json;
using System.Xml.Linq;

namespace Gateway.Icd;

/// <summary>
/// Renders a <see cref="ConformanceRecorder"/> into machine-readable reports for CI:
/// a structured JSON document and a JUnit XML file (consumed natively by most CI systems,
/// which then show each conformance verdict as a test case). The console report stays on
/// the recorder; this is the artifact side.
/// </summary>
public static class ReportWriter
{
    public static string ToJson(ConformanceRecorder rec, string scenario)
    {
        var payload = new
        {
            scenario,
            passed = rec.Passed,
            warnings = rec.Warnings,
            failed = rec.Failed,
            results = rec.Results.Select(r => new
            {
                severity = r.Severity.ToString(),
                rule = r.RuleId,
                message = r.Message,
                messageName = r.MessageName,
                sequence = r.Sequence,
                timestampMs = r.TimestampMs,
                expected = r.Expected,
                actual = r.Actual
            })
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string ToJUnit(ConformanceRecorder rec, string scenario)
    {
        var suite = new XElement("testsuite",
            new XAttribute("name", scenario),
            new XAttribute("tests", rec.Results.Count),
            new XAttribute("failures", rec.Failed));

        var i = 0;
        foreach (var r in rec.Results)
        {
            var testcase = new XElement("testcase",
                new XAttribute("classname", $"{scenario}.{r.RuleId}"),
                new XAttribute("name", $"[{i++}] {r.RuleId} {r.MessageName} @{r.TimestampMs}ms".Trim()));

            if (r.IsFailure)
            {
                var detail = r.Expected is null && r.Actual is null ? "" : $"expected {r.Expected}, actual {r.Actual}";
                testcase.Add(new XElement("failure", new XAttribute("message", r.Message), detail));
            }
            suite.Add(testcase);
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("testsuites", suite)).ToString();
    }
}
