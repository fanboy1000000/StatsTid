using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// S145 / TASK-14515 (R60, C-R4) — the TEST-LOCAL ECHO RULE ENGINE used by every replay fact.
///
/// <para>
/// <b>Plain language.</b> A replay is only proven if "the replay gave the same answer" can also
/// fail. The default test stand-in for the Rule Engine (<see cref="TestFixtures.DefaultRuleEngineHandler"/>)
/// answers with one line per hour entry and looks at nothing else: not the part-time fractions, not
/// the settlement cutoff, not the context days. With it, a replay that quietly used today's fraction
/// instead of the frozen one, or today's employment end date instead of the frozen cutoff, would
/// still return an answer identical to the original, so the comparison could neither pass nor prove
/// anything. This stand-in instead ECHOES what payroll sent it, as line items, so that every input
/// the replay contract freezes shows up in the result being compared.
/// </para>
///
/// <para>
/// <b>What it encodes</b> (all as <c>CalculationLineItem</c>s whose <c>TimeType</c> is a
/// self-describing string; the time types are deliberately unmapped, so payroll's wage-type mapping
/// skips them and only the rule results carry the echo):
/// </para>
/// <list type="bullet">
///   <item><description><c>POST /api/rules/evaluate-timeline</c>: three results in the fixed order
///   OVERTIME_CALC, NORM_CHECK_37H, FLEX_BALANCE. OVERTIME_CALC carries one
///   <c>ECHO_PIECE|from|to|fraction|agreement|position</c> line per request piece and one
///   <c>ECHO_CUTOFF|date-or-NULL</c> line (null is encoded as the word NULL, distinct from any
///   date). NORM_CHECK_37H carries one <c>ECHO_ENTRY|date|task|start|end</c> line per request entry,
///   CONTEXT DAYS INCLUDED, with the entry's hours in <c>Hours</c>, and one
///   <c>ECHO_ABSENCE|date|type</c> line per request absence. FLEX_BALANCE carries
///   <c>ECHO_FLEX|opening-balance</c>. Lines within a result are sorted ordinally, so the echo is
///   independent of the order payroll happened to list rows in.</description></item>
///   <item><description><c>POST /api/rules/evaluate</c>: one <c>ECHO_PROFILE|ruleId|fraction|agreement|position</c>
///   line, read from the request's <c>profile</c> (the per-segment lane's profile).</description></item>
///   <item><description>Every other path: exactly what <see cref="TestFixtures.DefaultRuleEngineHandler"/>
///   answers (unknown paths 404), so a per-segment absence or flex call cannot 404 a fact.</description></item>
/// </list>
///
/// <para>
/// <b>The two variants.</b> The engine returns the config fingerprints and the evaluation-model
/// marker it was constructed with (defaults <c>["stub"]</c> and <c>weekly-timeline-v1</c>). A replay
/// PCS built with <c>new ReplayEchoRuleEngine(fingerprints: new[] { "stub-2" })</c> or
/// <c>new ReplayEchoRuleEngine(evaluationModel: "weekly-timeline-v2")</c> is the refinement's
/// "synthetic config" (rev 3.4 :662): the stored record is never edited, the engine just answers
/// differently at replay time.
/// </para>
///
/// <para>
/// Every request is recorded (<see cref="Requests"/>) so a fact can inspect what payroll sent.
/// </para>
/// </summary>
internal sealed class ReplayEchoRuleEngine
{
    private readonly object _lock = new();
    private readonly List<(string Path, string Body)> _requests = new();
    private readonly IReadOnlyList<string> _fingerprints;
    private readonly string _evaluationModel;

    public ReplayEchoRuleEngine(
        IReadOnlyList<string>? fingerprints = null,
        string evaluationModel = "weekly-timeline-v1")
    {
        _fingerprints = fingerprints ?? new[] { "stub" };
        _evaluationModel = evaluationModel;
    }

    /// <summary>Every (path, body) payroll sent, in arrival order.</summary>
    public IReadOnlyList<(string Path, string Body)> Requests
    {
        get { lock (_lock) return _requests.ToList(); }
    }

    /// <summary>Bodies of the per-segment <c>/api/rules/evaluate</c> calls.</summary>
    public IReadOnlyList<string> EvaluateBodies =>
        Requests.Where(r => r.Path.EndsWith("/api/rules/evaluate", StringComparison.Ordinal))
            .Select(r => r.Body).ToList();

    /// <summary>The handler to pass to the PCS' HTTP client factory.</summary>
    public HttpResponseMessage Handle(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "{}";
        lock (_lock) _requests.Add((path, body));

        if (path.EndsWith("/api/rules/evaluate-timeline", StringComparison.Ordinal))
            return Timeline(body);
        if (path.EndsWith("/api/rules/evaluate", StringComparison.Ordinal))
            return Evaluate(body);

        // Every other path answers as the default stub does (unknown paths 404 there).
        return TestFixtures.DefaultRuleEngineHandler(request);
    }

    // ── /api/rules/evaluate-timeline ─────────────────────────────────────────

    private HttpResponseMessage Timeline(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var employeeId = Str(root, "employeeId") ?? "EMP";
        var periodStart = Str(root, "periodStart") ?? "2026-01-01";
        var cutoff = root.TryGetProperty("settlementCutoff", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()! : "NULL";
        var opening = root.TryGetProperty("previousFlexBalance", out var pf) && pf.ValueKind == JsonValueKind.Number
            ? pf.GetDecimal() : 0m;

        var pieceLines = new List<EchoLine>();
        if (root.TryGetProperty("pieces", out var pieces) && pieces.ValueKind == JsonValueKind.Array)
            foreach (var p in pieces.EnumerateArray())
            {
                var fraction = p.TryGetProperty("partTimeFraction", out var f) && f.ValueKind == JsonValueKind.Number
                    ? Fmt(f.GetDecimal()) : "?";
                pieceLines.Add(Line(
                    $"ECHO_PIECE|{Str(p, "from")}|{Str(p, "to")}|{fraction}|{Str(p, "agreementCode")}|{Str(p, "position") ?? "NULL"}",
                    0m, periodStart));
            }
        pieceLines.Add(Line($"ECHO_CUTOFF|{cutoff}", 0m, periodStart));

        var normLines = new List<EchoLine>();
        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
            foreach (var e in entries.EnumerateArray())
            {
                var hours = e.TryGetProperty("hours", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetDecimal() : 0m;
                normLines.Add(Line(
                    $"ECHO_ENTRY|{Str(e, "date")}|{Str(e, "taskId") ?? "NULL"}|{Str(e, "startTime") ?? "NULL"}|{Str(e, "endTime") ?? "NULL"}",
                    hours, periodStart));
            }
        if (root.TryGetProperty("absences", out var absences) && absences.ValueKind == JsonValueKind.Array)
            foreach (var a in absences.EnumerateArray())
            {
                var hours = a.TryGetProperty("hours", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetDecimal() : 0m;
                normLines.Add(Line($"ECHO_ABSENCE|{Str(a, "date")}|{Str(a, "absenceType")}", hours, periodStart));
            }

        var flexLines = new List<EchoLine> { Line($"ECHO_FLEX|{Fmt(opening)}", 0m, periodStart) };

        var payload = new
        {
            results = new object[]
            {
                Result("OVERTIME_CALC", employeeId, Sorted(pieceLines)),
                Result("NORM_CHECK_37H", employeeId, Sorted(normLines)),
                Result("FLEX_BALANCE", employeeId, Sorted(flexLines)),
            },
            flex = new
            {
                ruleId = "FLEX_BALANCE",
                employeeId,
                success = true,
                lineItems = Array.Empty<object>(),
                previousBalance = opening,
                newBalance = opening,
                delta = 0m,
                workedHours = 0m,
                absenceNormCredits = 0m,
                effectiveNorm = 0m,
                excessForPayout = 0m,
            },
            configFingerprints = _fingerprints,
            evaluationModel = _evaluationModel,
        };
        return Json(payload);
    }

    // ── /api/rules/evaluate (per-segment lane) ───────────────────────────────

    private static HttpResponseMessage Evaluate(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var ruleId = Str(root, "ruleId") ?? "UNKNOWN";
        var employeeId = "EMP";
        var fraction = "?";
        string agreement = "?", position = "NULL";
        if (root.TryGetProperty("profile", out var prof) && prof.ValueKind == JsonValueKind.Object)
        {
            employeeId = Str(prof, "employeeId") ?? "EMP";
            if (prof.TryGetProperty("partTimeFraction", out var f) && f.ValueKind == JsonValueKind.Number)
                fraction = Fmt(f.GetDecimal());
            agreement = Str(prof, "agreementCode") ?? "?";
            position = Str(prof, "position") ?? "NULL";
        }
        var date = Str(root, "periodStart") ?? "2026-01-01";

        var payload = Result(ruleId, employeeId, new List<EchoLine>
        {
            Line($"ECHO_PROFILE|{ruleId}|{fraction}|{agreement}|{position}", 0m, date),
        });
        return Json(payload);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static object Result(string ruleId, string employeeId, List<EchoLine> lineItems) => new
    {
        ruleId,
        employeeId,
        success = true,
        lineItems,
    };

    /// <summary>One echoed line; serializes (camelCase) as the rule-engine line-item wire shape.</summary>
    private sealed record EchoLine(string TimeType, decimal Hours, decimal Rate, string Date);

    private static EchoLine Line(string timeType, decimal hours, string date) => new(timeType, hours, 1.0m, date);

    private static List<EchoLine> Sorted(List<EchoLine> lines) =>
        lines.OrderBy(l => l.TimeType, StringComparer.Ordinal).ThenBy(l => l.Hours).ToList();

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Invariant decimal, value-normalised (no trailing zeros), so 1.0 and 1.000 echo alike.</summary>
    private static string Fmt(decimal d) => d.ToString("0.############################", CultureInfo.InvariantCulture);

    private static HttpResponseMessage Json(object payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            Encoding.UTF8, "application/json"),
    };
}
